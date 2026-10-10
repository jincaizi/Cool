// T4 浏览器验证的页面内观测钩子（配合 t4-webgl-cdp.js 的 addscriptfile: 使用）。
// 在页面加载前注入，于是 jslib 创建的 WebSocket 与它收发的每一帧都被记账：
//   __sentFrames / __sentBytes / __sentHead —— C# 经 WebSocketNet_Send 真正发出的帧
//   __recvFrames / __recvBytes / __recvHead —— 服务端发来、进入 jslib onmessage 的帧
//   __ws0                                   —— jslib 创建的那个 WebSocket 实例
//   __unity                                 —— createUnityInstance 的结果（默认模板把它关在闭包里）
// T8 追加（纯新增，不动上面的记账）：
//   __recvHist / __sentHist  —— 按**协议 messageId** 分桶的收发计数。帧头格式是
//     flags(1) + messageId(2, LE) + length(2, LE)，所以 id = b[1] | (b[2]<<8)。
//     这是"某个具体消息类型到底有没有到过浏览器"的直接判据（比问"有没有异常"强得多）。
//   __recvIds / __sentIds     —— 最近 400 条的 id 序列（看时序）。
//   消息号见 Assets/Scripts/AOT/Protocol/MessageId.cs（DamageResult=302, CastBroadcast=301,
//   WorldSnapshot=110, PositionSyncResponse=101, LoginResponse=2）。
// 另：jslib 自己会挂 window.__wsnet（$WebSocketNetState 的 __postset），
//   它给出 sockets[handle].state / queue.length / dropped，是"C# 有没有在轮询"的直接判据。
// T12 追加（纯新增，不动上面的记账）：
//   __rawFrames —— 把 **LoginResponse(2)** 与 **DamageResult(302)** 的原始字节 + 到达时刻留存，
//     供离线解码（玩家 id / 每击的 TargetPlayerId / TargetRemainingHp）。分桶计数只能证明
//     "某类帧到过"，证明不了"这一帧是不是打在本机玩家身上"——B4 那轮的悬案正需要这个。
//     上限 300 帧（大包 ~300 hex 字符，内存可忽略）。
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
      if (u.length >= 3) {
        var id = u[1] | (u[2] << 8);
        window.__sentHist = window.__sentHist || {};
        window.__sentHist[id] = (window.__sentHist[id] || 0) + 1;
        (window.__sentIds = window.__sentIds || []).push(id);
        if (window.__sentIds.length > 400) window.__sentIds.shift();
      }
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
            if (u.length >= 3) {
              var id = u[1] | (u[2] << 8);
              window.__recvHist = window.__recvHist || {};
              window.__recvHist[id] = (window.__recvHist[id] || 0) + 1;
              (window.__recvIds = window.__recvIds || []).push(id);
              if (window.__recvIds.length > 400) window.__recvIds.shift();
              // T12：留原字节（登录响应 2 / 伤害结果 302）——离线解码用
              if (id === 2 || id === 302) {
                window.__rawFrames = window.__rawFrames || [];
                window.__rawFrames.push({
                  id: id,
                  t: Date.now(),
                  hex: Array.prototype.slice.call(u).map(function (b) { return b.toString(16).padStart(2, '0'); }).join('')
                });
                if (window.__rawFrames.length > 300) window.__rawFrames.shift();
              }
            }
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
