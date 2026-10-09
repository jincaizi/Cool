// T4 浏览器验证的"服务端"替身（零依赖：Node 内建 http/crypto，不需要 npm 装 ws）。
//
// 用途：给 WebGL 客户端一个**确定的**对手方，把"通道是否双向通"与"真服务端是否起来"分开。
//   node t4-webgl-ws-stub.js [--port 8000] [--echo]
//     （默认）收到任意二进制帧 → 回一个**合法 LoginResponse**（PlayerId=42）
//              —— 这是强判据：客户端要解得出它才会进世界，解得出即"入站字节逐字节正确"。
//     --echo  收到什么原样回什么 —— 不需要任何协议知识，配合服务端 `--ws-echo` 做纯字节往返。
//
// 为什么不用 npm 的 ws：本装置要能被 T5/T8/T9 与"面试前夜"的任何人直接重放，
// 依赖全局 pm2 目录里的 ws 是不可重放的。帧格式自己实现（~60 行，双侧都实测过）。
//
// 局限（已知，够用就好）：每条 WS 消息只在收满一条完整消息后回一次；不实现 permessage-deflate
// （浏览器默认不开，实测握手无扩展协商）。
const http = require('http');
const crypto = require('crypto');

const WS_GUID = '258EAFA5-E914-47DA-95CA-C5AB0DC85B11';

function argValue(name, dflt) {
  const i = process.argv.indexOf(name);
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1] : dflt;
}
const PORT = Number(argValue('--port', '8000'));
const ECHO = process.argv.includes('--echo');

// ---- 帧编解码 ----------------------------------------------------------------

// 返回 {opcode, payload, totalLength} 或 null（数据还不完整）
function decodeFrame(buf) {
  if (buf.length < 2) return null;
  const fin = (buf[0] & 0x80) !== 0;
  const opcode = buf[0] & 0x0f;
  const masked = (buf[1] & 0x80) !== 0;
  let len = buf[1] & 0x7f;
  let offset = 2;
  if (len === 126) {
    if (buf.length < offset + 2) return null;
    len = buf.readUInt16BE(offset); offset += 2;
  } else if (len === 127) {
    if (buf.length < offset + 8) return null;
    const big = buf.readBigUInt64BE(offset); offset += 8;
    if (big > 64n * 1024n * 1024n) throw new Error('frame too large: ' + big);
    len = Number(big);
  }
  let mask = null;
  if (masked) {
    if (buf.length < offset + 4) return null;
    mask = buf.slice(offset, offset + 4); offset += 4;
  }
  if (buf.length < offset + len) return null;
  const payload = Buffer.from(buf.slice(offset, offset + len));
  if (masked) for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i % 4];
  return { fin: fin, opcode: opcode, payload: payload, totalLength: offset + len };
}

// 服务端发出的帧一律不掩码
function encodeFrame(opcode, payload) {
  const len = payload.length;
  let header;
  if (len < 126) {
    header = Buffer.alloc(2);
    header[1] = len;
  } else if (len < 65536) {
    header = Buffer.alloc(4);
    header[1] = 126;
    header.writeUInt16BE(len, 2);
  } else {
    header = Buffer.alloc(10);
    header[1] = 127;
    header.writeBigUInt64BE(BigInt(len), 2);
  }
  header[0] = 0x80 | opcode;
  return Buffer.concat([header, payload]);
}

// ---- LoginResponse 帧（字段号见 Assets/Scripts/AOT/Protocol/Messages/Login.cs）----

function varint(n) {
  const out = [];
  while (n >= 0x80) { out.push((n & 0x7f) | 0x80); n >>>= 7; }
  out.push(n & 0x7f);
  return out;
}
function ascii(s) { return Array.from(Buffer.from(s, 'ascii')); }
function f32(v) { const b = Buffer.alloc(4); b.writeFloatLE(v, 0); return Array.from(b); }

const TOKEN = 'T4LOCAL01';
const PAYLOAD = [
  0x08, 0x01,                                       // 1: Success = true
  0x18, ...varint(42),                              // 3: PlayerId = 42
  0x22, ...varint(TOKEN.length), ...ascii(TOKEN),   // 4: SessionToken
  0x2A, 0x05, ...ascii('Local'),                    // 5: ZoneId
  0x30, ...varint(1234567),                         // 6: ServerTimestamp
  0x38, 0x32,                                       // 7: ServerTickRateMs = 50
  0x45, ...f32(0), 0x4D, ...f32(0), 0x55, ...f32(0), 0x5D, ...f32(0)  // 8..11: 出生点
];
const LOGIN_RESPONSE = Buffer.from([
  0x04,                                             // flags: Reliable
  0x02, 0x00,                                       // messageId = LoginResponse(2), LE
  PAYLOAD.length & 0xff, (PAYLOAD.length >> 8) & 0xff,
  ...PAYLOAD
]);

// ---- 服务器 ------------------------------------------------------------------

const server = http.createServer((req, res) => {
  res.writeHead(400, { 'Content-Type': 'text/plain' });
  res.end('this endpoint speaks WebSocket only\n');
});

server.on('upgrade', (req, socket) => {
  const key = req.headers['sec-websocket-key'];
  if (!key) { socket.destroy(); return; }
  const accept = crypto.createHash('sha1').update(key + WS_GUID).digest('base64');
  socket.write('HTTP/1.1 101 Switching Protocols\r\n' +
               'Upgrade: websocket\r\n' +
               'Connection: Upgrade\r\n' +
               'Sec-WebSocket-Accept: ' + accept + '\r\n\r\n');
  console.log('CONNECTED (path=' + req.url + ')');

  let pending = Buffer.alloc(0);
  let fragmentOpcode = 0;
  let fragments = [];
  let inFrames = 0;

  socket.on('data', (chunk) => {
    pending = Buffer.concat([pending, chunk]);
    for (;;) {
      let frame;
      try { frame = decodeFrame(pending); }
      catch (e) { console.log('FRAME ERROR ' + e.message); socket.destroy(); return; }
      if (!frame) break;
      pending = pending.slice(frame.totalLength);

      if (frame.opcode === 0x8) { socket.end(encodeFrame(0x8, Buffer.alloc(0))); return; }
      if (frame.opcode === 0x9) { socket.write(encodeFrame(0xA, frame.payload)); continue; }
      if (frame.opcode === 0xA) continue;                  // pong：忽略

      if (frame.opcode === 0x0) {                          // continuation
        fragments.push(frame.payload);
        if (!frame.fin) continue;
        frame = { opcode: fragmentOpcode, payload: Buffer.concat(fragments) };
        fragments = [];
      } else if (!frame.fin) {                             // 分片首帧
        fragmentOpcode = frame.opcode;
        fragments = [frame.payload];
        continue;
      }

      inFrames++;
      // 只打头 8 / 尾 4 字节：够定位帧类型（flags+messageId+length），又不至于刷屏
      console.log('REQ#' + inFrames + ' len=' + frame.payload.length
        + ' head=' + frame.payload.slice(0, 8).toString('hex')
        + (frame.payload.length > 8 ? ' tail=' + frame.payload.slice(-4).toString('hex') : ''));
      const reply = ECHO ? frame.payload : LOGIN_RESPONSE;
      socket.write(encodeFrame(0x2, reply));
    }
  });

  socket.on('error', () => { /* 对端断开是常态 */ });
  socket.on('close', () => console.log('CLOSED'));
});

server.listen(PORT, '127.0.0.1', () => {
  console.log('T4 WS stub listening on 127.0.0.1:' + PORT + ' mode=' + (ECHO ? 'echo' : 'LoginResponse')
    + ' loginResponseFrameLen=' + LOGIN_RESPONSE.length);
});
