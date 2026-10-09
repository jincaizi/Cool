mergeInto(LibraryManager.library, {
  // 全部状态住在 $WebSocketNetState 里。除 7 个入口函数外它不引用任何
  // Unity/emscripten 全局——**heap 由参数传入**，所以状态机可以在 Node 里
  // 直接单测（见 tests/webgl/websocket-state.test.js）。把不可测面积压到
  // 最小的关键就在这一句：状态机不认识 Unity，入口函数不认识逻辑。
  //
  // ⚠️ **每个入口都必须声明 `__deps: ['$WebSocketNetState']`。** emscripten 不扫描
  // 函数体里的自由变量——没有入口依赖的 $ 项**整个不进入构建**，且构建期毫无提示：
  // 产物里只剩 11 处 `WebSocketNetState.xxx` 引用、0 处声明，首次 Connect 即
  // `ReferenceError: WebSocketNetState is not defined`。
  // （T4 实测：初版 jslib 正是这样出的包；单元测试的 Node 沙箱复刻 mergeInto 却
  // 不模拟依赖解析，所以这条只能靠源码级断言守——见测试 12。）
  $WebSocketNetState: {
    sockets: {},
    nextId: 1,
    MAX_QUEUED_FRAMES: 256,

    create: function (id, ws) {
      var entry = {
        ws: ws,
        state: 0,        // 0=connecting 1=open 2=closed 3=failed
        closeCode: 0,
        queue: [],       // 每项一个 Uint8Array —— 一条消息 = 一个 WS 帧（D-W9）
        dropped: 0
      };
      this.sockets[id] = entry;
      return entry;
    },

    get: function (id) {
      return Object.prototype.hasOwnProperty.call(this.sockets, id) ? this.sockets[id] : null;
    },

    enqueue: function (entry, bytes) {
      entry.queue.push(bytes);
      if (entry.queue.length > this.MAX_QUEUED_FRAMES) {
        // 丢最旧的：积压到 256 帧说明 C# 已经很久没轮询，此时新数据才有价值。
        // 丢的是内存，不丢的是"还能继续玩"。
        entry.queue.shift();
        entry.dropped++;
      }
    },

    // 0 = 队列空；>0 = 写入的字节数；-1 = 该帧比缓冲大（**不出队**，C# 扩容后重试）
    poll: function (entry, heap, destPtr, maxLen) {
      if (entry.queue.length === 0) return 0;
      var frame = entry.queue[0];
      if (frame.length > maxLen) return -1;
      heap.set(frame, destPtr);
      entry.queue.shift();
      return frame.length;
    }
  },

  // 浏览器控制台的自检钩子。Unity 的 framework.js 是包在闭包里的，$ 变量默认
  // 从 window 上够不着——没有这一行，"通道到底通没通"就只能靠 Network 面板猜。
  // 排查时：__wsnet.sockets[1].queue.length 在涨 = C# 没在轮询；
  // 恒为 0 且 state 不是 1 = 连接根本没建立。
  //
  // 走 __postset 而不是 mergeInto 之外的顶层语句：**jslib 管线只取 mergeInto 的
  // 对象参数，文件里其余的顶层代码一律丢弃**（T4 实测：初版的顶层赋值在产物里
  // 搜不到一个字符）。postset 随 $WebSocketNetState 一起发出，且发出的位置在
  // 状态对象声明之后，所以 `WebSocketNetState` 在作用域内。
  $WebSocketNetState__postset: 'if (typeof window !== "undefined") { window.__wsnet = WebSocketNetState; }',

  WebSocketNet_Connect__deps: ['$WebSocketNetState'],
  WebSocketNet_Connect: function (urlPtr) {
    var ws;
    try {
      ws = new WebSocket(UTF8ToString(urlPtr));
      ws.binaryType = 'arraybuffer';
    } catch (e) {
      return -1;                       // 非法 URL 等——构造期失败，同步返回
    }
    var id = WebSocketNetState.nextId++;
    var entry = WebSocketNetState.create(id, ws);

    // 三个回调都只改状态/入队，**不回调 C#**（本任务最重要的一条约束）
    ws.onopen = function () { entry.state = 1; };
    ws.onmessage = function (ev) {
      if (typeof ev.data === 'string') return;   // 服务端只发二进制帧；文本帧直接丢
      WebSocketNetState.enqueue(entry, new Uint8Array(ev.data));
    };
    ws.onclose = function (ev) {
      entry.state = 2;
      entry.closeCode = ev.code || 0;
    };
    ws.onerror = function () {
      if (entry.state !== 2) entry.state = 3;
    };

    return id;
  },

  // C# 每帧调用（由 WebGLWebSocketPump 驱动）
  WebSocketNet_Poll__deps: ['$WebSocketNetState'],
  WebSocketNet_Poll: function (handle, destPtr, maxLen) {
    var entry = WebSocketNetState.get(handle);
    if (!entry) return 0;
    return WebSocketNetState.poll(entry, HEAPU8, destPtr, maxLen);
  },

  WebSocketNet_GetState__deps: ['$WebSocketNetState'],
  WebSocketNet_GetState: function (handle) {
    var entry = WebSocketNetState.get(handle);
    return entry ? entry.state : 2;    // 句柄不存在 ⇒ 按已关闭处理
  },

  WebSocketNet_GetCloseCode__deps: ['$WebSocketNetState'],
  WebSocketNet_GetCloseCode: function (handle) {
    var entry = WebSocketNetState.get(handle);
    return entry ? entry.closeCode : 0;
  },

  WebSocketNet_GetDropped__deps: ['$WebSocketNetState'],
  WebSocketNet_GetDropped: function (handle) {
    var entry = WebSocketNetState.get(handle);
    return entry ? entry.dropped : 0;
  },

  WebSocketNet_Send__deps: ['$WebSocketNetState'],
  WebSocketNet_Send: function (handle, dataPtr, length) {
    var entry = WebSocketNetState.get(handle);
    if (!entry || entry.state !== 1) return 0;
    // 必须拷贝：HEAPU8 的视图在 wasm 堆增长后会失效
    var copy = new Uint8Array(length);
    copy.set(HEAPU8.subarray(dataPtr, dataPtr + length));
    entry.ws.send(copy);
    return 1;
  },

  WebSocketNet_Close__deps: ['$WebSocketNetState'],
  WebSocketNet_Close: function (handle) {
    var entry = WebSocketNetState.get(handle);
    if (!entry) return;
    try { entry.ws.close(1000, 'normal'); } catch (e) { /* 已经关了 */ }
    delete WebSocketNetState.sockets[handle];
  }
});
