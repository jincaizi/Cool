using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Cool.Protocol;

namespace KcpServer
{
    /// <summary>
    /// WebSocket 会话 —— IServerSession 的第二实现（KCP 之外）。
    /// 与 KcpServerSession 的关键差别：TCP 自带流控与重传，
    /// 故**不需要** KCP 那套发送窗口 / 丢包计数 / conv 采纳。
    /// Send 保持"同步入队 + 后台 drain"的形状，让主循环永不被网络写阻塞——
    /// 这与 KcpServerSession 的契约一致，是 IServerSession 能同时容纳两者的前提。
    /// </summary>
    public sealed class WebSocketServerSession : IServerSession
    {
        private const int ReceiveBufferSize = 64 * 1024;

        private readonly WebSocket _socket;
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public long SessionId { get; }
        public bool IsConnected => _socket.State == WebSocketState.Open;
        public DateTime LastActivityTime { get; private set; } = DateTime.UtcNow;

        /// <summary>待发队列深度（观测用，与 KcpServerSession.QueuedSendCount 同惯例）</summary>
        public int QueuedSendCount => _sendQueue.Count;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;

        public WebSocketServerSession(long sessionId, WebSocket socket, ILogger logger)
        {
            SessionId = sessionId;
            _socket = socket;
            _logger = logger;
            _ = ReceiveLoopAsync();
            _ = DrainLoopAsync();
        }

        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            if (!IsConnected) return;
            _sendQueue.Enqueue(MessageCodec.EncodeToArray(messageId, message, flags));
        }

        public void UpdateActivity() => LastActivityTime = DateTime.UtcNow;

        public bool IsTimedOut(TimeSpan timeout) => DateTime.UtcNow - LastActivityTime > timeout;

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[ReceiveBufferSize];
            var accumulated = new System.IO.MemoryStream();
            try
            {
                while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Disconnected?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    accumulated.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;      // 分片聚合到一帧

                    var frame = accumulated.ToArray();
                    accumulated.SetLength(0);
                    UpdateActivity();
                    DataReceived?.Invoke(this, frame);       // → Server3C.OnSessionDataReceived → ProcessPacket
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"WebSocket session {SessionId} receive loop failed");
                Error?.Invoke(this, ex);
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task DrainLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (!_sendQueue.TryDequeue(out var frame))
                    {
                        await Task.Delay(2, _cts.Token);
                        continue;
                    }
                    if (_socket.State != WebSocketState.Open) return;
                    await _socket.SendAsync(new ArraySegment<byte>(frame),
                        WebSocketMessageType.Binary, endOfMessage: true, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Error?.Invoke(this, ex); }
        }

        public async Task CloseAsync(string reason = "Normal")
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
            }
            catch { /* 关闭路径的异常不值得上抛 */ }
            finally { _cts.Cancel(); }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            try { _socket.Dispose(); } catch { }
        }
    }
}
