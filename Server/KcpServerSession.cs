using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// KCP回调实现 - 实现 IKcpCallback 和 IRentable 接口
    /// </summary>
    public sealed class KcpCallbackHandler : System.Net.Sockets.Kcp.IKcpCallback, System.Net.Sockets.Kcp.IRentable
    {
        private readonly Socket _socket;
        private readonly EndPoint _remoteEndPoint;
        private readonly ILogger _logger;
        private readonly byte[] _buffer;

        public KcpCallbackHandler(Socket socket, EndPoint remoteEndPoint, ILogger logger, int bufferSize = 65536)
        {
            _socket = socket;
            _remoteEndPoint = remoteEndPoint;
            _logger = logger;
            _buffer = new byte[bufferSize];
        }

        public void Output(IMemoryOwner<byte> buffer, int avalidLength)
        {
            try
            {
                var span = buffer.Memory.Span.Slice(0, avalidLength);
                var data = span.ToArray();
                _socket.SendTo(data, SocketFlags.None, _remoteEndPoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KCP output error");
            }
            finally
            {
                buffer.Dispose();
            }
        }

        public IMemoryOwner<byte> RentBuffer(int needSize)
        {
            if (needSize > _buffer.Length)
            {
                return new SimpleMemoryOwner(new byte[needSize]);
            }
            return new SimpleMemoryOwner(_buffer);
        }

        private sealed class SimpleMemoryOwner : IMemoryOwner<byte>
        {
            private readonly byte[] _array;

            public SimpleMemoryOwner(byte[] array)
            {
                _array = array;
            }

            public Memory<byte> Memory => _array.AsMemory();

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// KCP会话状态
    /// </summary>
    public enum KcpSessionState
    {
        None,
        Connecting,
        Connected,
        Closing,
        Closed
    }

    /// <summary>
    /// 服务端KCP会话 - 处理单个客户端的KCP连接
    /// 使用 Kcp 2.7.0 的 Kcp<Segment> API
    /// </summary>
    public sealed class KcpServerSession : IDisposable
    {
        private readonly long _sessionId;
        private readonly EndPoint _remoteEndPoint;
        private readonly Socket _socket;
        private readonly System.Net.Sockets.Kcp.Kcp<System.Net.Sockets.Kcp.KcpSegment> _kcp;
        private readonly KcpCallbackHandler _callbackHandler;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private KcpSessionState _state = KcpSessionState.Connecting;
        private bool _disposed;
        private DateTime _lastActivityTime;
        private Task? _updateTask;
        private readonly object _sendLock = new object();

        // KCP 配置
        private const int SendWindowSize = 32;
        private const int ReceiveWindowSize = 32;
        private const int Interval = 10;
        private const int FastResend = 2;
        private const int Mtu = 1400;

        public long SessionId => _sessionId;
        public EndPoint RemoteEndPoint => _remoteEndPoint;
        public bool IsConnected => _state == KcpSessionState.Connected;
        public DateTime LastActivityTime => _lastActivityTime;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;

        public KcpServerSession(long sessionId, EndPoint remoteEndPoint, Socket socket, ILogger logger)
        {
            _sessionId = sessionId;
            _remoteEndPoint = remoteEndPoint;
            _socket = socket;
            _logger = logger;

            // 创建回调处理器
            _callbackHandler = new KcpCallbackHandler(socket, remoteEndPoint, logger);

            // 创建 KCP - 使用 Kcp<Segment> API
            _kcp = new System.Net.Sockets.Kcp.Kcp<System.Net.Sockets.Kcp.KcpSegment>((uint)sessionId, _callbackHandler, _callbackHandler);

            // 配置 KCP - 使用接口方法
            _kcp.WndSize(SendWindowSize, ReceiveWindowSize);
            _kcp.NoDelay(1, Interval, FastResend, 0);
            _kcp.SetMtu(Mtu);

            _lastActivityTime = DateTime.UtcNow;
            _state = KcpSessionState.Connected;

            // 启动 KCP 更新循环
            _updateTask = Task.Run(() => UpdateLoopAsync(_cts.Token));

            _logger.LogInformation($"KcpServerSession created: {_sessionId} from {_remoteEndPoint}");
        }

        /// <summary>
        /// 处理接收到的 UDP 数据包
        /// </summary>
        public void HandleUdpPacket(byte[] data, int length)
        {
            if (_disposed || _state != KcpSessionState.Connected) return;

            try
            {
                // 输入到 KCP
                _kcp.Input(new ReadOnlySpan<byte>(data, 0, length));
                _lastActivityTime = DateTime.UtcNow;

                // 尝试接收 KCP 数据
                ReceiveFromKcp();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling UDP packet for session {_sessionId}");
                Error?.Invoke(this, ex);
            }
        }

        /// <summary>
        /// 从 KCP 接收数据
        /// </summary>
        private void ReceiveFromKcp()
        {
            var bufferWriter = new ArrayBufferWriter<byte>(65536);
            while (true)
            {
                bufferWriter.Clear();
                var result = _kcp.TryRecv(bufferWriter);
                if (result <= 0) break;

                var data = bufferWriter.WrittenSpan.ToArray();
                if (data.Length <= 0) break;

                _lastActivityTime = DateTime.UtcNow;
                DataReceived?.Invoke(this, data);
            }
        }

        /// <summary>
        /// 发送数据
        /// </summary>
        public int Send(byte[] data, int offset, int length)
        {
            if (_disposed || _state != KcpSessionState.Connected) return 0;

            try
            {
                lock (_sendLock)
                {
                    var result = _kcp.Send(new ReadOnlySpan<byte>(data, offset, length), null);
                    _lastActivityTime = DateTime.UtcNow;
                    return result >= 0 ? length : result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending data for session {_sessionId}");
                Error?.Invoke(this, ex);
                return 0;
            }
        }

        /// <summary>
        /// 发送消息
        /// </summary>
        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            var data = MessageCodec.Encode(messageId, message, flags);
            Send(data, 0, data.Length);
        }

        /// <summary>
        /// KCP 更新循环
        /// </summary>
        private async Task UpdateLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug($"KCP update loop started for session {_sessionId}");

            while (!cancellationToken.IsCancellationRequested && _state == KcpSessionState.Connected)
            {
                try
                {
                    lock (_sendLock)
                    {
                        _kcp.Update(DateTimeOffset.UtcNow);
                    }

                    // 尝试接收数据
                    ReceiveFromKcp();

                    await Task.Delay(Interval, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in KCP update loop for session {_sessionId}");
                    await Task.Delay(1000, cancellationToken);
                }
            }

            _logger.LogDebug($"KCP update loop stopped for session {_sessionId}");
        }

        /// <summary>
        /// 更新最后活动时间
        /// </summary>
        public void UpdateActivity()
        {
            _lastActivityTime = DateTime.UtcNow;
        }

        /// <summary>
        /// 检查是否超时
        /// </summary>
        public bool IsTimedOut(TimeSpan timeout)
        {
            return DateTime.UtcNow - _lastActivityTime > timeout;
        }

        /// <summary>
        /// 异步关闭会话
        /// </summary>
        public async Task CloseAsync(string reason = "Normal")
        {
            if (_disposed) return;

            _logger.LogInformation($"Closing KcpServerSession {_sessionId}: {reason}");
            _state = KcpSessionState.Closing;

            _cts.Cancel();

            if (_updateTask != null)
            {
                try
                {
                    await _updateTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning($"Timeout waiting for update task to stop for session {_sessionId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error waiting for update task for session {_sessionId}");
                }
            }

            _state = KcpSessionState.Closed;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 强制关闭会话
        /// </summary>
        public void ForceClose()
        {
            if (_disposed) return;

            _logger.LogWarning($"Force closing KcpServerSession {_sessionId}");
            _state = KcpSessionState.Closed;
            _cts.Cancel();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
            _kcp.Dispose();

            _logger.LogInformation($"KcpServerSession {_sessionId} disposed");
        }
    }
}