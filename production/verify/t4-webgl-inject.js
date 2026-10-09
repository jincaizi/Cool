// T4 浏览器验证的页面内观测钩子（配合 t4-webgl-cdp.js 的 addscriptfile: 使用）。
// 在页面加载前注入，于是 jslib 创建的 WebSocket 与它收发的每一帧都被记账：
//   __sentFrames / __sentBytes / __sentHead —— C# 经 WebSocketNet_Send 真正发出的帧
//   __recvFrames / __recvBytes / __recvHead —— 服务端发来、进入 jslib onmessage 的帧
//   __ws0                                   —— jslib 创建的那个 WebSocket 实例
//   __unity                                 —— createUnityInstance 的结果（默认模板把它关在闭包里）
// 另：jslib 自己会挂 window.__wsnet（$WebSocketNetState 的 __postset），
//   它给出 sockets[handle].state / queue.length / dropped，是"C# 有没有在轮询"的直接判据。
(function () {
  window.__t4hook = 'installed';

  var origSend = WebSocket.prototype.send;
  WebSocket.prototype.send = function (data) {
    window.__sentFrames = (window.__sentFrames || 0) + 1;
    window.__ws0 = this;
    try {
      // 浏览器 WebSocket 只接受 ArrayBuffer/TypedArray/Blob/string；这里只记录裸字节形态
      var u = data instanceof Uint8Array
        ? data
        : new Uint8Array(data.buffer || data, data.byteOffset || 0, data.byteLength || data.length);
      window.__sentBytes = (window.__sentBytes || 0) + u.length;
      window.__sentHead = Array.prototype.slice.call(u.slice(0, 8))
        .map(function (b) { return b.toString(16).padStart(2, '0'); }).join(' ');
    } catch (e) { window.__sentErr = String(e); }
    return origSend.call(this, data);
  };

  var desc = Object.getOwnPropertyDescriptor(WebSocket.prototype, 'onmessage');
  if (desc && desc.set) {
    Object.defineProperty(WebSocket.prototype, 'onmessage', {
      configurable: true,
      get: desc.get,
      set: function (fn) {
        var wrapped = function (ev) {
          window.__recvFrames = (window.__recvFrames || 0) + 1;
          try {
            var u = new Uint8Array(ev.data);
            window.__recvBytes = (window.__recvBytes || 0) + u.length;
            window.__recvHead = Array.prototype.slice.call(u.slice(0, 8))
              .map(function (b) { return b.toString(16).padStart(2, '0'); }).join(' ');
          } catch (e) { window.__recvErr = String(e); }
          return fn.apply(this, arguments);
        };
        desc.set.call(this, wrapped);
      }
    });
  }

  var stored;
  Object.defineProperty(window, 'createUnityInstance', {
    configurable: true,
    get: function () { return stored; },
    set: function (fn) {
      stored = function () {
        var p = fn.apply(this, arguments);
        if (p && p.then) p.then(function (inst) { window.__unity = inst; });
        return p;
      };
    }
  });
})();
