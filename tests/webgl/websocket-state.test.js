// 运行：node --test tests/webgl/websocket-state.test.js
// （**必须写全文件名**：`node --test tests/webgl/` 目录形式在 Node v22.22.1 上
// 会把目录当模块解析、报 MODULE_NOT_FOUND，一个测试都不跑。）
const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const { loadJslib, FakeWebSocket, JSLIB } = require('./jslib-harness');

const HEAP_SIZE = 4096;

function setup() {
  FakeWebSocket.instances = [];
  const heap = new Uint8Array(HEAP_SIZE);
  const lib = loadJslib({
    WebSocket: FakeWebSocket,
    HEAPU8: heap,
    UTF8ToString: function () { return 'ws://127.0.0.1:8000/'; }
  });
  return { lib: lib, heap: heap };
}

function frame(bytes) { return Uint8Array.from(bytes); }

test('回调期不碰 Unity：jslib 不引用任何 JS→C# 机制', () => {
  const src = fs.readFileSync(JSLIB, 'utf8');
  assert.ok(!/SendMessage/.test(src), 'jslib 不得使用 SendMessage');
  assert.ok(!/gameInstance/.test(src), 'gameInstance 在 Unity 2020.1 已被移除');
  assert.ok(!/dynCall/.test(src), 'dynCall 是刻意避开的易错方向');
});

test('Connect 返回句柄，onopen 前状态是 connecting', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  assert.ok(id > 0, 'handle 必须为正数');
  assert.strictEqual(s.lib.WebSocketNet_GetState(id), 0);
});

test('onopen 后状态变 open，且能发送', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  const ws = FakeWebSocket.instances[0];
  ws.onopen();

  assert.strictEqual(s.lib.WebSocketNet_GetState(id), 1);
  const payload = new Uint8Array([1, 2, 3]);
  s.heap.set(payload, 64);
  assert.strictEqual(s.lib.WebSocketNet_Send(id, 64, 3), 1);
  assert.strictEqual(ws.sent.length, 1);
});

test('未 open 时 Send 返回 0 且不写 socket', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  const ws = FakeWebSocket.instances[0];
  s.heap.set(new Uint8Array([9]), 64);

  assert.strictEqual(s.lib.WebSocketNet_Send(id, 64, 1), 0);
  assert.strictEqual(ws.sent.length, 0);
});

test('Poll 在队列空时返回 0', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 0, HEAP_SIZE), 0);
});

test('onmessage 入队，Poll 按序取出并把字节写进 heap', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  const ws = FakeWebSocket.instances[0];
  ws.onmessage({ data: frame([10, 11]).buffer });
  ws.onmessage({ data: frame([20]).buffer });

  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 128, HEAP_SIZE), 2);
  assert.deepStrictEqual(Array.from(s.heap.slice(128, 130)), [10, 11]);
  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 128, HEAP_SIZE), 1);
  assert.deepStrictEqual(Array.from(s.heap.slice(128, 129)), [20]);
  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 128, HEAP_SIZE), 0, '取完应为空');
});

test('文本帧被丢弃，不会变成空数组污染队列', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  FakeWebSocket.instances[0].onmessage({ data: 'a text frame' });
  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 0, HEAP_SIZE), 0);
});

test('帧大于缓冲时返回 -1 且不出队，扩容后可重取', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  FakeWebSocket.instances[0].onmessage({ data: frame([1, 2, 3, 4, 5]).buffer });

  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 0, 4), -1, '太小应拒绝');
  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 0, 5), 5, '扩容后应成功');
  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 0, 5), 0, '成功后应出队');
});

test('队列超过上限时丢最旧的，并累加 dropped', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  const ws = FakeWebSocket.instances[0];
  const limit = s.lib.WebSocketNetState.MAX_QUEUED_FRAMES;

  for (let i = 0; i <= limit + 9; i++) ws.onmessage({ data: frame([i & 0xff]).buffer });

  assert.strictEqual(s.lib.WebSocketNet_GetDropped(id), 10);
  assert.strictEqual(s.lib.WebSocketNet_Poll(id, 0, HEAP_SIZE), 1);
  assert.strictEqual(s.heap[0], 10, '队首应是被保留的第 10 条（前 10 条被丢）');
});

test('onclose 后状态变 closed 且 closeCode 透出', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  FakeWebSocket.instances[0].onclose({ code: 1006 });

  assert.strictEqual(s.lib.WebSocketNet_GetState(id), 2);
  assert.strictEqual(s.lib.WebSocketNet_GetCloseCode(id), 1006);
});

test('Close 之后再查句柄按已关闭处理，不会读到野数据', () => {
  const s = setup();
  const id = s.lib.WebSocketNet_Connect(0);
  const ws = FakeWebSocket.instances[0];
  ws.onopen();
  s.lib.WebSocketNet_Close(id);

  assert.deepStrictEqual(ws.closedWith, { code: 1000, reason: 'normal' });
  assert.strictEqual(s.lib.WebSocketNet_GetState(id), 2);
  assert.strictEqual(s.lib.WebSocketNet_Send(id, 0, 0), 0);
});

// —— T4 出包实测补的回归测试：初版 jslib 没有 __deps，emscripten 把 $WebSocketNetState
//    整个丢出构建（产物里 11 处引用、0 处声明），构建期静默通过，运行时首次 Connect 抛
//    ReferenceError。Node 沙箱复刻 mergeInto 但不模拟依赖解析，所以只能在源码层守。
test('每个入口都声明 $WebSocketNetState 依赖，且自检钩子经 __postset 发出', () => {
  const src = fs.readFileSync(JSLIB, 'utf8');
  const entries = [
    'WebSocketNet_Connect', 'WebSocketNet_Poll', 'WebSocketNet_Send',
    'WebSocketNet_GetState', 'WebSocketNet_GetCloseCode',
    'WebSocketNet_GetDropped', 'WebSocketNet_Close'
  ];
  for (const name of entries) {
    const dep = new RegExp(name + "__deps\\s*:\\s*\\[\\s*'\\$WebSocketNetState'\\s*\\]");
    assert.ok(dep.test(src), name + " 必须声明 __deps: ['$WebSocketNetState']");
  }
  // mergeInto 之外的顶层语句会被 jslib 管线丢弃 —— __wsnet 只能走 postset
  assert.ok(/\$WebSocketNetState__postset\s*:/.test(src),
    '__wsnet 自检钩子必须经 $WebSocketNetState__postset 发出');
});
