// WebGL 面试演示的静态托管 —— 边传边 gzip，零依赖（只用 Node 内建模块）。
//
//   node static-server.js --root ../../Build/WebGL --port 8080
//   node static-server.js --root . --port 8080 --no-gzip      # 对照组（未压缩）
//
// 为什么在服务器而不是在 Unity 里压缩（Task 9 的压缩路线 A）：
//   * Unity 侧预压缩（WebGLCompressionFormat.Gzip）产出 `WebGL.wasm.gz` 这种**改名文件**，
//     于是服务器必须为每个文件维护"请求 X 就给 X.gz + Content-Encoding"的映射，任何一处
//     对不上就是 404 或者浏览器把 .gz 字节当 wasm 执行；而且 loader.js / framework.js /
//     index.html 这些"小但每次都要"的文件 Unity 不压，正好漏掉了首屏最慢的那部分。
//   * 服务器侧 gzip 的产物形状与 Unity 的设置解耦：`Build/WebGL/` 永远是原文件，
//     压缩只存在于传输层，双重压缩不可能发生（Unity 那边被 WebGLBuildScript 钉死为 Disabled，
//     并且出包后会自查产物里没有 .gz/.br）。
//
// 两个必须做对的细节：
//   ① `Content-Length` 要发**压缩后**的长度。Unity 的 loader 靠它算进度条百分比；
//      用分块传输（不设 Content-Length）进度条会一直停在 0 —— 面试官会以为卡死了。
//      所以这里先把整个文件压出来放进内存，而不是 pipe 一个流。
//   ② `.wasm` 必须回 `application/wasm`。浏览器只有在 MIME 正确时才走
//      `WebAssembly.instantiateStreaming`，否则退化到先下载再编译（慢一截）或直接报错。
//
// 内存与耗时：首屏四个文件压完约 20 MB 常驻（gzip 结果按 mtime+size 缓存），
// 首次压缩对 55 MB 的 wasm 约 1-3 秒 —— 发生在**第一个请求**上，所以别拿它当加载耗时基线
// （measure 之前先跑一次预热）。

const http = require('http');
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

function argValue(name, dflt) {
  const i = process.argv.indexOf(name);
  if (i >= 0 && process.argv[i + 1] && !process.argv[i + 1].startsWith('--')) return process.argv[i + 1];
  return dflt;
}

const ROOT = path.resolve(argValue('--root', '.'));
const PORT = Number(argValue('--port', '8080'));
const BIND = argValue('--bind', '0.0.0.0');
const GZIP = !process.argv.includes('--no-gzip');

const TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.wasm': 'application/wasm',
  '.data': 'application/octet-stream',
  '.ico': 'image/x-icon',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.svg': 'image/svg+xml',
};

// 值得压的类型。图片/图标已经是压缩格式，再 gzip 只浪费 CPU。
const COMPRESSIBLE = new Set(['.html', '.js', '.css', '.json', '.wasm', '.data', '.svg']);

const gzipCache = new Map(); // 绝对路径 -> { key, buf }

function contentType(file) {
  return TYPES[path.extname(file).toLowerCase()] || 'application/octet-stream';
}

// 命中缓存的前提是 mtime + size 都没变（改了包就自动失效，无需重启）
function cacheKey(st) {
  return st.size + ':' + st.mtimeMs;
}

function send(res, status, headers, body, req) {
  res.writeHead(status, headers);
  if (req && req.method === 'HEAD') res.end();
  else res.end(body);
}

function gzipFor(file, st, cb) {
  const key = cacheKey(st);
  const hit = gzipCache.get(file);
  if (hit && hit.key === key) return cb(null, hit.buf);
  zlib.gzip(fs.readFileSync(file), { level: 6 }, (err, buf) => {
    if (err) return cb(err);
    gzipCache.set(file, { key: key, buf: buf });
    cb(null, buf);
  });
}

// 单区间 Range（`bytes=a-b`）。压缩与 Range 天然冲突（没法给压缩流的任意偏移），
// 所以带 Range 的请求一律回原文件 —— 与 nginx 的 gzip 默认行为一致。
function parseRange(header, size) {
  const m = /^bytes=(\d*)-(\d*)$/.exec((header || '').trim());
  if (!m) return null;
  let start = m[1] === '' ? null : Number(m[1]);
  let end = m[2] === '' ? null : Number(m[2]);
  if (start === null && end === null) return null;
  if (start === null) { start = Math.max(0, size - end); end = size - 1; }
  else if (end === null || end >= size) end = size - 1;
  if (start > end || start >= size) return null;
  return { start, end };
}

const server = http.createServer((req, res) => {
  const started = Date.now();
  const urlPath = decodeURIComponent(req.url.split('?')[0]);
  let file = path.join(ROOT, path.normalize(urlPath).replace(/^([/\\])+/, ''));

  // 目录穿越防护：解析后必须仍在 ROOT 里
  if (!path.resolve(file).startsWith(ROOT)) {
    res.writeHead(403).end('forbidden');
    return;
  }
  if (fs.existsSync(file) && fs.statSync(file).isDirectory()) file = path.join(file, 'index.html');

  fs.stat(file, (err, st) => {
    if (err || !st.isFile()) {
      console.log(req.method + ' ' + urlPath + ' -> 404');
      res.writeHead(404, { 'Content-Type': 'text/plain' }).end('404 ' + urlPath);
      return;
    }

    const type = contentType(file);
    const ext = path.extname(file).toLowerCase();
    const range = req.headers['range'] ? parseRange(req.headers['range'], st.size) : null;
    const wantsGzip = GZIP && !range && COMPRESSIBLE.has(ext)
      && /\bgzip\b/.test(req.headers['accept-encoding'] || '');

    if (range) {
      const body = fs.readFileSync(file).slice(range.start, range.end + 1);
      send(res, 206, {
        'Content-Type': type,
        'Content-Length': body.length,
        'Content-Range': 'bytes ' + range.start + '-' + range.end + '/' + st.size,
        'Accept-Ranges': 'bytes',
      }, body, req);
      console.log(req.method + ' ' + urlPath + ' 206 ' + body.length + 'B range');
      return;
    }

    if (!wantsGzip) {
      const body = fs.readFileSync(file);
      send(res, 200, {
        'Content-Type': type,
        'Content-Length': body.length,
        'Accept-Ranges': 'bytes',
      }, body, req);
      console.log(req.method + ' ' + urlPath + ' 200 ' + body.length + 'B plain ' + (Date.now() - started) + 'ms');
      return;
    }

    gzipFor(file, st, (gerr, buf) => {
      if (gerr) {
        res.writeHead(500, { 'Content-Type': 'text/plain' }).end('gzip failed: ' + gerr.message);
        return;
      }
      send(res, 200, {
        'Content-Type': type,
        'Content-Length': buf.length,
        'Content-Encoding': 'gzip',
        'Vary': 'Accept-Encoding',
        'Accept-Ranges': 'bytes',
      }, buf, req);
      console.log(req.method + ' ' + urlPath + ' 200 ' + buf.length + 'B gzip'
        + ' (' + st.size + 'B raw, ' + Math.round(buf.length / st.size * 100) + '%)'
        + ' ' + (Date.now() - started) + 'ms');
    });
  });
});

server.listen(PORT, BIND, () => {
  console.log('static server: http://' + BIND + ':' + PORT + '/  root=' + ROOT
    + '  gzip=' + (GZIP ? 'on' : 'off'));
  console.log('访问 http://<本机IP>:' + PORT + '/index.html');
});
