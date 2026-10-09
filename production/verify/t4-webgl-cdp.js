// T4 浏览器验证的驱动装置（零依赖：Node 内建 fetch/WebSocket，不装 puppeteer/playwright）。
// 通过 CDP 驱动一个 **headless Chrome** 加载 WebGL 包，执行一串步骤并把浏览器的
// console / 未捕获异常原样带回来 —— 于是"通道通没通"可以脚本化复现，而不是靠人肉点。
//
// 三板斧（三个终端）：
//   1) 静态服务器：  cd Build/WebGL && python -m http.server 8080
//   2) 对手方：      node production/verify/t4-webgl-ws-stub.js --port 8000        # 回 LoginResponse
//                    或 dotnet run --project Server/Server3C.csproj -- 8000 --ws-echo  # 纯字节回吐
//   3) 无头浏览器：  "C:/Program Files/Google/Chrome/Application/chrome.exe" \
//                      --headless=new --remote-debugging-port=9222 \
//                      --user-data-dir=<任意临时目录> --enable-unsafe-swiftshader \
//                      --use-angle=swiftshader --window-size=1280,800 about:blank
//      驱动：      node production/verify/t4-webgl-cdp.js - \
//                    addscriptfile:production/verify/t4-webgl-inject.js \
//                    nav:http://localhost:8080/index.html sleep:42000 \
//                    click:705,510 sleep:1200 click:639,553 sleep:16000 \
//                    eval:"JSON.stringify({states:Object.values(window.__wsnet.sockets).map(s=>s.state)})" \
//                    eval:"JSON.stringify({sent:window.__sentFrames,recv:window.__recvFrames})" \
//                    shot:C:/tmp/t4.png
//
// 步骤语法（step 冒号后是参数；第一个冒号之后全部保留，所以 eval 里可以有冒号/逗号）：
//   nav:<url>            跳转到 url                addscriptfile:<path>  下次加载前注入脚本
//   sleep:<ms>           等待                      eval:<js>             在页面里求值并打印
//   shot:<abs path>      截图落盘                  click:<x>,<y>         真实鼠标点击（含 move）
//   moveto:<x>,<y>       只移动指针
// 第一个位置参数传 "-" 表示"附着到当前页面、不跳转"（用于续跑正在进行的会话）。
//
// 已知边界（照实说）：headless + SwiftShader 是软渲染，帧率低、截图有黑地形伪影；
// 它验证的是"字节与状态"，不是画面质量。
const URL = process.argv[2];
const steps = process.argv.slice(3);
const CDP_HTTP = 'http://127.0.0.1:9222';

function sleep(ms) { return new Promise(r => setTimeout(r, ms)); }

async function main() {
  let target = null;
  for (let i = 0; i < 40; i++) {
    try {
      const list = await (await fetch(CDP_HTTP + '/json/list')).json();
      target = list.find(t => t.type === 'page');
      if (target) break;
    } catch (e) { /* Chrome 还没起来 */ }
    await sleep(500);
  }
  if (!target) { console.log('FAIL: no CDP page target on ' + CDP_HTTP); process.exit(1); }
  console.log('target url =', target.url);

  const ws = new WebSocket(target.webSocketDebuggerUrl);
  let id = 0;
  const pending = new Map();
  const consoleLines = [];

  ws.onmessage = (ev) => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) reject(new Error(JSON.stringify(msg.error)));
      else resolve(msg.result);
      return;
    }
    if (msg.method === 'Runtime.consoleAPICalled') {
      const text = (msg.params.args || []).map(a => a.value !== undefined ? String(a.value) : (a.description || a.type)).join(' ');
      consoleLines.push('[console.' + msg.params.type + '] ' + text);
      // 实时只回有信息量的行，避免 Unity 启动日志把输出刷没
      if (/WebSocketNet|NetHost|__wsnet|rror|xception/i.test(text)) console.log('  << ' + text.slice(0, 400));
    } else if (msg.method === 'Runtime.exceptionThrown') {
      const d = msg.params.exceptionDetails || {};
      const text = (d.exception && (d.exception.description || d.exception.value)) || d.text;
      consoleLines.push('[exception] ' + text);
      console.log('  << [EXCEPTION] ' + String(text).slice(0, 500)
        + ' @url=' + (d.url || '?') + ' line=' + (d.lineNumber != null ? d.lineNumber : '?'));
    } else if (msg.method === 'Log.entryAdded') {
      const e = msg.params.entry;
      consoleLines.push('[' + e.level + '] ' + e.text);
    }
  };

  function send(method, params = {}) {
    return new Promise((resolve, reject) => {
      const msgId = ++id;
      pending.set(msgId, { resolve, reject });
      ws.send(JSON.stringify({ id: msgId, method, params }));
    });
  }

  await new Promise((r) => { ws.onopen = r; });
  await send('Runtime.enable');
  await send('Log.enable');
  await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 800, deviceScaleFactor: 1, mobile: false });

  if (URL !== '-') {
    console.log('navigating to', URL);
    await send('Page.navigate', { url: URL });
  } else {
    console.log('attaching to current page (no navigation)');
  }

  async function evaluate(expr) {
    const r = await send('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) {
      return 'EXCEPTION: ' + JSON.stringify((r.exceptionDetails.exception && r.exceptionDetails.exception.description) || r.exceptionDetails.text);
    }
    return r.result && r.result.value;
  }

  for (const step of steps) {
    const cut = step.indexOf(':');
    const kind = cut < 0 ? step : step.slice(0, cut);
    const arg = cut < 0 ? '' : step.slice(cut + 1);
    if (kind === 'sleep') {
      console.log('... sleep ' + arg + 'ms');
      await sleep(Number(arg));
    } else if (kind === 'eval') {
      console.log('EVAL ' + arg + '\n  => ' + JSON.stringify(await evaluate(arg)));
    } else if (kind === 'shot') {
      const r = await send('Page.captureScreenshot', { format: 'png' });
      require('fs').writeFileSync(arg, Buffer.from(r.data, 'base64'));
      console.log('SHOT written ' + arg);
    } else if (kind === 'click') {
      const [x, y] = arg.split(',').map(Number);
      await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, button: 'none', buttons: 0, pointerType: 'mouse' });
      await sleep(120);
      await send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1, pointerType: 'mouse' });
      await sleep(120);
      await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', buttons: 0, clickCount: 1, pointerType: 'mouse' });
      console.log('CLICK ' + x + ',' + y);
    } else if (kind === 'moveto') {
      const [x, y] = arg.split(',').map(Number);
      await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, button: 'none', buttons: 0, pointerType: 'mouse' });
      console.log('MOVETO ' + x + ',' + y);
    } else if (kind === 'addscriptfile') {
      const src = require('fs').readFileSync(arg, 'utf8');
      await send('Page.addScriptToEvaluateOnNewDocument', { source: src });
      console.log('ADDSCRIPTFILE ok (' + src.length + ' bytes)');
    } else if (kind === 'nav') {
      console.log('NAV ' + arg);
      await send('Page.navigate', { url: arg });
    } else {
      console.log('unknown step ' + step);
    }
  }

  console.log('--- console lines IN ORDER (启动噪声已滤) ---');
  consoleLines
    .filter(l => !/memorysetup-|UnityMemory|mediaDevices support|EXT_|GLES:|OpenGL LOG|Version:|Input (Manager|System)|New input system|UnloadTime|Unloading|FindLiveObjects|Total: |Unused Serialized/.test(l))
    .slice(0, 60)
    .forEach(l => console.log('  | ' + l.slice(0, 400)));

  console.log('--- console lines (deduped) ---');
  const counts = new Map();
  consoleLines
    .filter(l => !/memorysetup-|UnityMemory|mediaDevices support/.test(l))
    .forEach(l => counts.set(l, (counts.get(l) || 0) + 1));
  let printed = 0;
  for (const [line, n] of counts) {
    console.log((n > 1 ? '[x' + n + '] ' : '') + line.slice(0, 600));
    if (++printed >= 200) { console.log('... (' + counts.size + ' distinct lines total)'); break; }
  }

  const errors = consoleLines.filter(l => /^\[console\.error\]|^\[exception\]/.test(l));
  console.log('--- console error/exception count: ' + errors.length + ' ---');
  errors.forEach(l => console.log('  !! ' + l.slice(0, 600)));
  process.exit(0);
}

main().catch(e => { console.log('HARNESS FAIL:', e.message); process.exit(1); });
