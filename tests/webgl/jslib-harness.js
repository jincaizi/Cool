// 把 .jslib 当普通 JS 载入 Node。
// 加载期只提供它需要的两个全局（mergeInto / LibraryManager）；HEAPU8 与
// UTF8ToString 只在函数体里出现，由测试按需注入——这正是状态机可测的原因。
//
// 注意 sandbox **故意不定义** gameInstance 与 SendMessage：一旦有人把
// JS→C# 回调方向加回来，测试会当场以 ReferenceError 炸掉，而不是静默通过。
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const JSLIB = path.join(__dirname, '..', '..', 'Assets', 'Plugins', 'WebGL', 'WebGLWebSocket.jslib');

function loadJslib(globals) {
  const sandbox = Object.assign({ console: console }, globals || {});

  // 复刻 emscripten 的 mergeInto：$Foo 会被**提升成模块作用域里的 Foo**。
  // 这一步不能省——入口函数里的 `WebSocketNetState.nextId++` 是自由变量引用，
  // 真实构建里它在模块作用域内可以解析，而在这里必须真的挂成全局，
  // 否则一调用就是 "WebSocketNetState is not defined"。
  sandbox.LibraryManager = { library: {} };
  sandbox.mergeInto = function (target, source) {
    Object.keys(source).forEach(function (key) {
      const name = key.charAt(0) === '$' ? key.slice(1) : key;
      target[name] = source[key];
    });
  };

  vm.createContext(sandbox);
  vm.runInContext(fs.readFileSync(JSLIB, 'utf8'), sandbox, { filename: JSLIB });

  const lib = sandbox.LibraryManager.library;
  // 同上：把提升后的名字挂成全局（测试也用同一个名字读 MAX_QUEUED_FRAMES）
  Object.keys(lib).forEach(function (key) {
    if (!(key in sandbox)) sandbox[key] = lib[key];
  });
  return lib;
}

// 够用的假 WebSocket：测试自己决定何时触发 onopen/onmessage/onclose
class FakeWebSocket {
  constructor(url) {
    this.url = url;
    this.binaryType = '';
    this.sent = [];
    this.closedWith = null;
    FakeWebSocket.instances.push(this);
  }
  send(data) { this.sent.push(data); }
  close(code, reason) { this.closedWith = { code: code, reason: reason }; }
}
FakeWebSocket.instances = [];

module.exports = { loadJslib, FakeWebSocket, JSLIB };
