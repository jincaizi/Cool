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
// T8 追加：键盘 / 鼠标按钮 / 相对移动注入（**纯新增，未改动上面任何既有步骤的行为**）。
// 动机：T4 只会点击，而 P6 单机玩法、P4 的"玩家被击中"前置都要站着走位 + 挥砍。
// 桌面浏览器形态走键鼠，所以这四类事件就是驱动手段。
//   keydown:<KeyName>      按住（rawKeyDown + autoRepeat 心跳，见下）
//   keyup:<KeyName>        松开
//   key:<KeyName>,<ms>     按住 ms 毫秒后松开
//   tap:<KeyName>          按下并立刻松开
//   mdown:<left|right|middle>   在**当前指针位置**按下鼠标键
//   mup:<left|right|middle>     松开
//   moverel:<dx>,<dy>      指针相对移动（累加式，page 坐标；用来产生 movementX/Y）
//   mark:<label>           在日志里打一条带**相对脚本启动的毫秒时间戳**的标记（用于计时判据）
// T12 追加（纯新增）：
//   type:<text>            逐字符输入（KEYMAP 覆盖的字符）——用来往选人面板的地址/端口输入框打字
//   KEYMAP 补全数字 0/5/6/7/8/9 与 Backspace（原来只有 1-4）
//   CDP 会话开头 Network.setCacheDisabled —— 防止"换了新包、浏览器却喂旧包"的假失败
// KeyName 映射见 KEYMAP（W/A/S/D/Q/R/0-9/Space/Shift/Esc/Backspace）。
// 注意：按住类步骤只发一次 keydown（Unity 轮询按键状态，不需要重复发；重复发只用于规避
// 个别实现把 keydown 当一次性事件的情况，本装置实测一次即可）。
const KEYMAP = {
  W: { code: 'KeyW', key: 'w', vk: 87 }, A: { code: 'KeyA', key: 'a', vk: 65 },
  S: { code: 'KeyS', key: 's', vk: 83 }, D: { code: 'KeyD', key: 'd', vk: 68 },
  Q: { code: 'KeyQ', key: 'q', vk: 81 }, R: { code: 'KeyR', key: 'r', vk: 82 },
  '0': { code: 'Digit0', key: '0', vk: 48 },
  '1': { code: 'Digit1', key: '1', vk: 49 }, '2': { code: 'Digit2', key: '2', vk: 50 },
  '3': { code: 'Digit3', key: '3', vk: 51 }, '4': { code: 'Digit4', key: '4', vk: 52 },
  '5': { code: 'Digit5', key: '5', vk: 53 }, '6': { code: 'Digit6', key: '6', vk: 54 },
  '7': { code: 'Digit7', key: '7', vk: 55 }, '8': { code: 'Digit8', key: '8', vk: 56 },
  '9': { code: 'Digit9', key: '9', vk: 57 },
  Backspace: { code: 'Backspace', key: 'Backspace', vk: 8 },
  Space: { code: 'Space', key: ' ', vk: 32 }, Shift: { code: 'ShiftLeft', key: 'Shift', vk: 16 },
  Esc: { code: 'Escape', key: 'Escape', vk: 27 },
};
const MOUSEBTN = { left: { button: 'left', buttons: 1 }, right: { button: 'right', buttons: 2 }, middle: { button: 'middle', buttons: 4 } };
//
// 已知边界（照实说）：headless + SwiftShader 是软渲染，帧率低、截图有黑地形伪影；
// 它验证的是"字节与状态"，不是画面质量。
const URL = process.argv[2];
const steps = process.argv.slice(3);
// T8：CDP 端点可用环境变量覆盖（默认仍是 9222，行为不变）——便于同时开两个浏览器实例
// （一个跑长时程单机观测、一个跑联网验收，互不打扰）。
const CDP_HTTP = process.env.T8_CDP || 'http://127.0.0.1:9222';

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
      // T8：实时回显的**过滤集**追加了玩法日志前缀（Sys3CEntry / Attack / EventBus / StateLogger），
      // 目的是让这类行**夹在 `+<ms>` 步骤标记之间**打印出来 → 事件与时间戳可以直接对齐。
      if (/WebSocketNet|NetHost|__wsnet|rror|xception|Sys3CEntry|\[Attack\]|\[EventBus\]|StateLogger/i.test(text)) console.log('  << ' + text.slice(0, 400));
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
  // T12：**禁掉 HTTP 缓存**。WebGL 的 Build/WebGL.data|.wasm 与页面 URL 无关（页面的 query 串
  // 不影响子资源 URL），所以换了新包但沿用同一个浏览器 profile 时，Chrome 会把**上一次的包**
  // 喂给你——"验证旧包"这类假失败 T7/T8 各踩过一次。每个 CDP 会话各自生效，无需重开浏览器。
  await send('Network.enable');
  await send('Network.setCacheDisabled', { cacheDisabled: true });
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

  // T8：指针位置记账（moverel 是相对移动，必须知道"当前"在哪）
  let mouseX = 640, mouseY = 400;
  const t0 = Date.now();
  const elapsed = () => (Date.now() - t0) + 'ms';

  async function keyEvent(type, name, extra = {}) {
    const k = KEYMAP[name];
    if (!k) throw new Error('unknown key ' + name);
    await send('Input.dispatchKeyEvent', Object.assign({
      type: type, key: k.key, code: k.code, windowsVirtualKeyCode: k.vk,
      nativeVirtualKeyCode: k.vk, location: 0,
    }, extra));
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
      mouseX = x; mouseY = y;   // T8：同步指针记账（click 本身的行为未变）
      await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, button: 'none', buttons: 0, pointerType: 'mouse' });
      await sleep(120);
      await send('Input.dispatchMouseEvent', { type: 'mousePressed', x, y, button: 'left', buttons: 1, clickCount: 1, pointerType: 'mouse' });
      await sleep(120);
      await send('Input.dispatchMouseEvent', { type: 'mouseReleased', x, y, button: 'left', buttons: 0, clickCount: 1, pointerType: 'mouse' });
      console.log('CLICK ' + x + ',' + y);
    } else if (kind === 'moveto') {
      const [x, y] = arg.split(',').map(Number);
      mouseX = x; mouseY = y;   // T8：同步指针记账（moveto 本身的行为未变）
      await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y, button: 'none', buttons: 0, pointerType: 'mouse' });
      console.log('MOVETO ' + x + ',' + y);
    } else if (kind === 'keydown') {
      await keyEvent('rawKeyDown', arg);
      console.log('+' + elapsed() + ' KEYDOWN ' + arg);
    } else if (kind === 'keyup') {
      await keyEvent('keyUp', arg);
      console.log('+' + elapsed() + ' KEYUP ' + arg);
    } else if (kind === 'key') {
      const [name, ms] = arg.split(',');
      await keyEvent('rawKeyDown', name);
      console.log('+' + elapsed() + ' KEY ' + name + ' down (hold ' + ms + 'ms)');
      await sleep(Number(ms));
      await keyEvent('keyUp', name);
      console.log('+' + elapsed() + ' KEY ' + name + ' up');
    } else if (kind === 'tap') {
      await keyEvent('rawKeyDown', arg);
      await sleep(60);
      await keyEvent('keyUp', arg);
      console.log('+' + elapsed() + ' TAP ' + arg);
    } else if (kind === 'mdown' || kind === 'mup') {
      const b = MOUSEBTN[arg];
      if (!b) { console.log('unknown mouse button ' + arg); continue; }
      await send('Input.dispatchMouseEvent', {
        type: kind === 'mdown' ? 'mousePressed' : 'mouseReleased',
        x: mouseX, y: mouseY, button: b.button, buttons: kind === 'mdown' ? b.buttons : 0,
        clickCount: 1, pointerType: 'mouse',
      });
      console.log('+' + elapsed() + ' ' + (kind === 'mdown' ? 'MDOWN ' : 'MUP ') + arg + ' @' + mouseX + ',' + mouseY);
    } else if (kind === 'moverel') {
      const [dx, dy] = arg.split(',').map(Number);
      const steps_ = Math.max(1, Math.min(10, Math.round(Math.max(Math.abs(dx), Math.abs(dy)) / 40)));
      for (let i = 0; i < steps_; i++) {
        mouseX += dx / steps_; mouseY += dy / steps_;
        await send('Input.dispatchMouseEvent', { type: 'mouseMoved', x: Math.round(mouseX), y: Math.round(mouseY), button: 'none', buttons: 0, pointerType: 'mouse' });
        await sleep(30);
      }
      console.log('+' + elapsed() + ' MOVEREL ' + arg + ' -> @' + Math.round(mouseX) + ',' + Math.round(mouseY));
    } else if (kind === 'mark') {
      console.log('+' + elapsed() + ' === MARK ' + arg + ' ===');
    } else if (kind === 'type') {
      // T12：逐字符输入文本（KEYMAP 里有的字符才行）——用来往选人面板的服务器地址/端口输入框里打字
      // （换端口起服 = 不动别人正在跑的服务器就能验断线路径）。
      // 必须发 **keyDown + text**（不是 rawKeyDown）：TMP_InputField 的插入读的是字符事件，
      // rawKeyDown 只带 keyCode，实测光标会聚焦但一个字符都进不去（T12 踩过）。
      let unmapped = '';
      for (const ch of arg) {
        if (!KEYMAP[ch]) { unmapped += ch; continue; }
        await keyEvent('keyDown', ch, { text: ch, unmodifiedText: ch });
        await sleep(60);
        await keyEvent('keyUp', ch);
        await sleep(60);
      }
      console.log('+' + elapsed() + ' TYPE ' + arg + (unmapped ? ' (unmapped: ' + unmapped + ')' : ''));
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
