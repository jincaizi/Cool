// Task 9：测**真实的传输量与首帧可用时长**（不是估算）。
//
// 口径（这是"测量"的全部要点——数字只有配上口径才有意义）：
//   * 带宽用 CDP 的 `Network.emulateNetworkConditions` 模拟，加在**浏览器这一侧**，
//     所以测出来的是"面试官的网络 + 这个包"的联合结果，与服务端是不是本机无关。
//   * 传输量取 `Network.loadingFinished.encodedDataLength` 之和 —— 这是**上线字节数**
//     （含响应头、且已计入 Content-Encoding 后的长度），不是磁盘上的文件大小。
//   * "首帧可用" = `#unity-loading-bar` 被置为 display:none 的时刻。
//     那是 index.html 里 `createUnityInstance(...).then()` 的回调，意味着
//     wasm 已编译、资源已解包、Unity 已经出了第一帧（页面上那一瞬就是可点的游戏画面）。
//     用 MutationObserver 抓，精度不受轮询间隔影响。
//   * 时间零点 = `performance.timeOrigin`（导航开始），都是页面内的 performance.now()。
//
// 用法（零依赖：Node 内建 fetch/WebSocket，不装 puppeteer）：
//   node t9-measure-load.js <url> --label gzip-5mbps --mbps 5 --rtt-ms 200 --out out.json
//   node t9-measure-load.js <url> --label loopback --no-throttle
//
// 5 Mbps 按十进制算：5_000_000 bit/s ÷ 8 = 625_000 B/s。
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');

const CDP_HTTP = process.env.T9_CDP || 'http://127.0.0.1:9222';
const CHROME = process.env.T9_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';

function argValue(name, dflt) {
  const i = process.argv.indexOf(name);
  return i >= 0 && process.argv[i + 1] && !process.argv[i + 1].startsWith('--') ? process.argv[i + 1] : dflt;
}
const URL_ = process.argv[2];
const LABEL = argValue('--label', 'run');
const MBPS = Number(argValue('--mbps', '5'));
const RTT = Number(argValue('--rtt-ms', '200'));
const NO_THROTTLE = process.argv.includes('--no-throttle');
const OUT = argValue('--out', '');
const TIMEOUT_MS = Number(argValue('--timeout-ms', '600000'));
const PROFILE = path.join(os.tmpdir(), 't9-chrome-profile');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function cdpTarget() {
  for (let i = 0; i < 40; i++) {
    try {
      const list = await (await fetch(CDP_HTTP + '/json/list')).json();
      const page = list.find((t) => t.type === 'page');
      if (page) return page;
    } catch { /* Chrome 还没起来 */ }
    await sleep(500);
  }
  throw new Error('no CDP page target on ' + CDP_HTTP);
}

async function ensureChrome() {
  try {
    await fetch(CDP_HTTP + '/json/version');
    console.log('chrome already up on ' + CDP_HTTP);
    return null;
  } catch { /* 需要自己拉一个 */ }
  const port = new URL(CDP_HTTP).port;
  const args = [
    '--headless=new',
    '--remote-debugging-port=' + port,
    '--user-data-dir=' + PROFILE,
    '--no-first-run', '--no-default-browser-check',
    '--enable-unsafe-swiftshader', '--use-angle=swiftshader',
    '--window-size=1280,800',
    '--disable-background-timer-throttling',
    'about:blank',
  ];
  console.log('launching headless chrome: ' + CHROME);
  const child = spawn(CHROME, args, { detached: true, stdio: 'ignore' });
  child.unref();
  return child;
}

// 页面内探针：必须在任何页面脚本之前注入（Page.addScriptToEvaluateOnNewDocument）。
// 抓 `#unity-loading-bar` 被隐藏的时刻 = 引擎装载完成、首帧已出。
const PROBE = `(() => {
  window.__t9 = { lbHiddenAt: null, progressAt: [], errors: [], startedAt: performance.now() };
  const attach = () => {
    const b = document.querySelector('#unity-loading-bar');
    if (!b) return false;
    if (b.style.display === 'none') window.__t9.lbHiddenAt = performance.now();
    new MutationObserver(() => {
      if (window.__t9.lbHiddenAt === null && b.style.display === 'none') {
        window.__t9.lbHiddenAt = performance.now();
      }
    }).observe(b, { attributes: true, attributeFilter: ['style'] });
    return true;
  };
  if (!attach()) {
    const iv = setInterval(() => { if (attach()) clearInterval(iv); }, 5);
  }
  window.addEventListener('error', (e) => window.__t9.errors.push(String(e.message)));
  let frames = 0;
  const tick = () => { window.__t9.frames = ++frames; requestAnimationFrame(tick); };
  requestAnimationFrame(tick);
})()`;

async function main() {
  if (!URL_) throw new Error('usage: node t9-measure-load.js <url> [--label L] [--mbps N] [--rtt-ms N] [--no-throttle]');
  const chrome = await ensureChrome();
  const target = await cdpTarget();

  const ws = new WebSocket(target.webSocketDebuggerUrl);
  let id = 0;
  const pending = new Map();
  const requests = new Map();   // requestId -> { url, status, mimeType, encoded, finishedAt }
  const consoleErrors = [];

  ws.onmessage = (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) reject(new Error(JSON.stringify(msg.error)));
      else resolve(msg.result);
      return;
    }
    const p = msg.params || {};
    if (msg.method === 'Network.responseReceived') {
      requests.set(p.requestId, {
        url: p.response.url,
        status: p.response.status,
        mimeType: p.response.mimeType,
        fromDiskCache: !!p.response.fromDiskCache,
        encoded: 0,
        finishedAt: null,
      });
    } else if (msg.method === 'Network.loadingFinished') {
      const r = requests.get(p.requestId);
      if (r) { r.encoded = p.encodedDataLength; r.finishedAt = Date.now(); }
    } else if (msg.method === 'Network.loadingFailed') {
      const r = requests.get(p.requestId);
      if (r) r.failed = p.errorText;
    } else if (msg.method === 'Runtime.exceptionThrown') {
      const d = p.exceptionDetails || {};
      consoleErrors.push('EXCEPTION ' + ((d.exception && d.exception.description) || d.text));
    } else if (msg.method === 'Runtime.consoleAPICalled' && p.type === 'error') {
      consoleErrors.push('console.error ' + (p.args || []).map((a) => a.value || a.description || '').join(' '));
    }
  };

  const send = (method, params = {}) => new Promise((resolve, reject) => {
    const msgId = ++id;
    pending.set(msgId, { resolve, reject });
    ws.send(JSON.stringify({ id: msgId, method, params }));
  });

  await new Promise((r) => { ws.onopen = r; });
  await send('Runtime.enable');
  await send('Page.enable');
  await send('Network.enable');
  await send('Network.setCacheDisabled', { cacheDisabled: true });
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });

  if (NO_THROTTLE) {
    await send('Network.emulateNetworkConditions', {
      offline: false, latency: 0, downloadThroughput: -1, uploadThroughput: -1,
    });
    console.log('[measure] throttle: OFF (loopback baseline)');
  } else {
    const bytesPerSec = Math.round(MBPS * 1000 * 1000 / 8);
    await send('Network.emulateNetworkConditions', {
      offline: false, latency: RTT, downloadThroughput: bytesPerSec, uploadThroughput: bytesPerSec,
      connectionType: 'cellular3g',
    });
    console.log('[measure] throttle: ' + MBPS + ' Mbps (' + bytesPerSec + ' B/s) / ' + RTT + ' ms RTT');
  }

  await send('Page.addScriptToEvaluateOnNewDocument', { source: PROBE });

  const t0 = Date.now();
  await send('Page.navigate', { url: URL_ });

  // 等到 loading-bar 被隐藏（首帧可用），或超时
  let state = null;
  while (Date.now() - t0 < TIMEOUT_MS) {
    const r = await send('Runtime.evaluate', {
      expression: 'JSON.stringify(window.__t9 || null)', returnByValue: true,
    });
    const v = r.result && r.result.value;
    state = v ? JSON.parse(v) : null;
    if (state && state.lbHiddenAt !== null) break;
    await sleep(250);
  }
  const wallMs = Date.now() - t0;

  // 首帧之后再等一会儿，让尾部请求（TemplateData 之类）结算完
  await sleep(3000);

  const shot = await send('Page.captureScreenshot', { format: 'png' });
  const shotPath = OUT ? OUT.replace(/\.json$/, '') + '.png' : null;
  if (shotPath) fs.writeFileSync(shotPath, Buffer.from(shot.data, 'base64'));

  const rows = [...requests.values()].filter((r) => r.encoded > 0);
  const totalBytes = rows.reduce((a, r) => a + r.encoded, 0);

  // 首屏 = 在"首帧可用"之前完成的那些请求（通常是 index/loader/framework/wasm/data）
  const firstScreenCutoff = (state && state.lbHiddenAt) ? state.lbHiddenAt : Infinity;
  const result = {
    label: LABEL,
    url: URL_,
    throttle: NO_THROTTLE ? 'off' : (MBPS + 'Mbps/' + RTT + 'ms'),
    wallMs: wallMs,
    firstFrameUsableMs: state ? state.lbHiddenAt : null,
    totalTransferredBytes: totalBytes,
    requestCount: rows.length,
    requests: rows.map((r) => ({
      url: r.url.replace(/^https?:\/\/[^/]+/, ''),
      status: r.status,
      mimeType: r.mimeType,
      encodedDataLength: r.encoded,
    })),
    pageErrors: (state ? state.errors : []).concat(consoleErrors),
    screenshot: shotPath,
  };
  console.log(JSON.stringify(result, null, 2));
  if (OUT) fs.writeFileSync(OUT, JSON.stringify(result, null, 2));

  ws.close();
  if (chrome) chrome.kill();
  process.exit(0);
}

main().catch((e) => { console.log('HARNESS FAIL: ' + e.message + '\n' + e.stack); process.exit(1); });
