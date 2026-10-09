using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Cool.Protocol;
using KcpServer;
using KcpServer.Tests;
using Xunit;

namespace Server.Tests.WebSocketTests
{
    /// <summary>
    /// Task 5：WebSocketServerSession 的契约测试。
    /// 用内存中的假 WebSocket 驱动，不建真连接——
    /// 真连接由 Task 8 的浏览器验收覆盖。
    /// </summary>
    public class WebSocketServerSessionTests
    {
        [Fact]
        public void SessionId_IsPreserved()
        {
            using var session = new WebSocketServerSession(42L, new FakeSocket(), new TestLogger());
            Assert.Equal(42L, session.SessionId);
        }

        [Fact]
        public void Send_IsQueuedNotBlocking()
        {
            var socket = new FakeSocket();
            using var session = new WebSocketServerSession(1L, socket, new TestLogger());

            session.Send(MessageId.EchoResponse, new EchoResponse { ClientTimestamp = 1L });

            // Send 是同步的（IServerSession 契约）：调用后消息应在队列里，
            // 而不是已经同步写完 —— 否则主循环会被网络写阻塞
            Assert.Equal(1, session.QueuedSendCount);
        }

        [Fact]
        public async Task InboundFrame_RaisesDataReceived()
        {
            var socket = new FakeSocket();
            using var session = new WebSocketServerSession(1L, socket, new TestLogger());

            byte[]? received = null;
            session.DataReceived += (_, data) => received = data;

            var frame = MessageCodec.EncodeToArray(
                MessageId.EchoRequest, new EchoRequest { ClientTimestamp = 1L }, MessageFlags.Reliable);
            await socket.PushInboundAsync(frame);

            Assert.NotNull(received);
            Assert.Equal(frame, received);
        }

        [Fact]
        public void IsTimedOut_RespectsLastActivity()
        {
            using var session = new WebSocketServerSession(1L, new FakeSocket(), new TestLogger());
            Assert.False(session.IsTimedOut(TimeSpan.FromSeconds(30)));

            // UpdateActivity 之后同样不该超时（基线语义：活动即续期）
            session.UpdateActivity();
            Assert.False(session.IsTimedOut(TimeSpan.FromSeconds(30)));
        }

        /// <summary>
        /// 内存中的假 WebSocket：ReceiveAsync 按入站队列交付帧，SendAsync 只记账。
        /// 不建 socket、不起连接——被测的是会话自身的收发/事件契约，不是网络。
        /// </summary>
        private sealed class FakeSocket : WebSocket
        {
            private readonly ConcurrentQueue<byte[]> _inbound = new ConcurrentQueue<byte[]>();
            private readonly SemaphoreSlim _available = new SemaphoreSlim(0);

            /// <summary>最近一次 PushInboundAsync 的等待者——在接收循环**处理完**该帧后完成（见 PushInboundAsync）</summary>
            private TaskCompletionSource? _pendingDelivery;

            private WebSocketState _state = WebSocketState.Open;

            public override WebSocketCloseStatus? CloseStatus => null;
            public override string? CloseStatusDescription => null;
            public override string? SubProtocol => null;
            public override WebSocketState State => _state;

            /// <summary>
            /// 压入一帧，并在接收循环**已处理完**该帧之后才完成返回的 Task。
            /// 完成时机刻意不是"帧被取走"而是"循环回到 ReceiveAsync"：
            /// 循环在两次 ReceiveAsync 之间是一段同步直线代码（聚合 → UpdateActivity → DataReceived.Invoke），
            /// 回到 ReceiveAsync 即意味着 DataReceived 已派发完毕 —— 断言因此是确定的，不靠 sleep 碰运气。
            /// </summary>
            public Task PushInboundAsync(byte[] frame)
            {
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingDelivery = tcs;
                _inbound.Enqueue(frame);
                _available.Release();
                return tcs.Task;
            }

            public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                // 上一帧的处理已跑完（循环回到了这里）——放行它的等待者
                var delivered = Interlocked.Exchange(ref _pendingDelivery, null);
                delivered?.TrySetResult();

                await _available.WaitAsync(cancellationToken).ConfigureAwait(false);
                _inbound.TryDequeue(out var frame);
                var length = frame?.Length ?? 0;
                if (length > 0) Array.Copy(frame!, 0, buffer.Array!, buffer.Offset, length);
                return new WebSocketReceiveResult(length, WebSocketMessageType.Binary, endOfMessage: true);
            }

            public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            {
                // 写路径的成功/失败不在本文件的断言口径里（往返字节由真连接覆盖）——收下即可
                return Task.CompletedTask;
            }

            public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                _state = WebSocketState.Closed;
                return Task.CompletedTask;
            }

            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            {
                _state = WebSocketState.CloseSent;
                return Task.CompletedTask;
            }

            public override void Abort() => _state = WebSocketState.Aborted;

            /// <summary>只改状态位：挂起的 ReceiveAsync 留给 GC（本假件不持有真句柄，无需唤醒/释放）</summary>
            public override void Dispose() => _state = WebSocketState.Closed;
        }
    }
}
