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
        private readonly EndPoint _remoteEndPoint;
        private readonly ILogger _logger;
        private readonly byte[] _buffer;

        /// <summary>发送 socket 提供者（由 KcpServerSession.SetSendSocket 注入）</summary>
        public Func<System.Net.Sockets.Socket?>? SendSocketProvider { get; set; }

        public KcpCallbackHandler(EndPoint remoteEndPoint, ILogger logger, int bufferSize = 65536)
        {
            _remoteEndPoint = remoteEndPoint;
            _logger = logger;
            _buffer = new byte[bufferSize];
        }

        public void Output(IMemoryOwner<byte> buffer, int avalidLength)
        {
            try
            {
                var span = buffer.Memory.Span.Slice(0, avalidLength);
                var socket = SendSocketProvider?.Invoke();
                if (socket != null)
                {
                    socket.SendTo(span.ToArray(), SocketFlags.None, _remoteEndPoint);
                }
                else
                {
                    _logger.LogWarning("KCP output dropped: send socket not injected");
                }
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
        private readonly System.Net.Sockets.Kcp.Kcp<System.Net.Sockets.Kcp.KcpSegment> _kcp;
        private readonly KcpCallbackHandler _callbackHandler;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private KcpSessionState _state = KcpSessionState.Connecting;
        private bool _disposed;
        private DateTime _lastActivityTime;
        private DateTime _lastReceiveTime;
        private Task? _updateTask;
        private readonly object _sendLock = new object();

        // KCP 配置
        private const int SendWindowSize = 32;
        private const int ReceiveWindowSize = 32;
        private const int Interval = 10;
        private const int FastResend = 2;
        private const int Mtu = 1400;

        /// <summary>静默超时：超过该时长未收到客户端任何包即判定断线（UDP 无显式断开通知；
        /// 客户端心跳 5s，连失 3 个心跳窗口判死）</summary>
        private static readonly TimeSpan PeerSilenceTimeout = TimeSpan.FromSeconds(15);

        private volatile Socket? _sendSocket;
        private bool _wired;

        /// <summary>发送 socket（由 SessionManager 注入；会话不持有 socket 生命周期）。只读——注入唯一入口为 SetSendSocket</summary>
        public Socket? SendSocket => _sendSocket;

        public uint KcpConv { get; }

        public long SessionId => _sessionId;
        public EndPoint RemoteEndPoint => _remoteEndPoint;
        public bool IsConnected => _state == KcpSessionState.Connected;
        public DateTime LastActivityTime => _lastActivityTime;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;

        public KcpServerSession(long sessionId, EndPoint remoteEndPoint, uint? kcpConv, ILogger logger)
        {
            _sessionId = sessionId;
            _remoteEndPoint = remoteEndPoint;
            _logger = logger;
            KcpConv = kcpConv ?? 0;

            // 创建回调处理器
            _callbackHandler = new KcpCallbackHandler(remoteEndPoint, logger);

            // 创建 KCP - 使用 Kcp<Segment> API
            // KCP 协议要求双端 conv 一致（KcpCore.Input 严格校验 conv != conv_ 即丢包），
            // 因此必须采用客户端首包中的 conv，而不是本地会话计数
            _kcp = new System.Net.Sockets.Kcp.Kcp<System.Net.Sockets.Kcp.KcpSegment>(kcpConv ?? (uint)sessionId, _callbackHandler, _callbackHandler);
            // Kcp 2.7.0 的基类不会初始化 SegmentManager（为 null），首次 Send/Input 会 NRE；
            // 与库内 SimpleSegManager.Kcp 子类保持一致，显式指定默认段管理器
            _kcp.SegmentManager = System.Net.Sockets.Kcp.SimpleSegManager.Default;

            // 配置 KCP - 使用接口方法
            _kcp.WndSize(SendWindowSize, ReceiveWindowSize);
            _kcp.NoDelay(1, Interval, FastResend, 0);
            _kcp.SetMtu(Mtu);

            _lastActivityTime = DateTime.UtcNow;
            _lastReceiveTime = DateTime.UtcNow;
            _state = KcpSessionState.Connected;

            // 启动 KCP 更新循环
            _updateTask = Task.Run(() => UpdateLoopAsync(_cts.Token));

            _logger.LogInformation($"KcpServerSession created: {_sessionId} from {_remoteEndPoint}");
        }

        /// <summary>唯一注入入口：发送 socket + 回调输出通路</summary>
        public void SetSendSocket(System.Net.Sockets.Socket socket)
        {
            _sendSocket = socket;
            _callbackHandler.SendSocketProvider = () => _sendSocket;
        }

        /// <summary>幂等接线：保证事件订阅先于任何包处理（消除发布-订阅窗口竞态）。
        /// 与 KCP 更新循环同锁（_sendLock），互斥于 _kcp.Update/TryRecv → 接线完成后才可能有 DataReceived</summary>
        public void EnsureWired(Action wireAction)
        {
            lock (_sendLock)
            {
                if (_wired) return;
                _wired = true;
                wireAction();
            }
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
                _lastReceiveTime = DateTime.UtcNow;

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

        /// <summary>成功入队 KCP 发送队列的段数（诊断计数）</summary>
        public long QueuedSendCount;

        /// <summary>因发送队列满被丢弃的段数（诊断计数——见 Send 内注释）</summary>
        public long DroppedSendCount;

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
                    // 发送队列满 = 段被静默丢弃（对端 ACK/窗口补不上时发生）——必须留痕，
                    // 否则表现为"服务器在发但客户端收不到"的隐性吞吐损失（2026-09-13 集成实机定位）
                    if (result < 0)
                    {
                        DroppedSendCount++;
                        if (DroppedSendCount % 100 == 1)
                            _logger.LogWarning($"[KCP] send queue full — dropped segment #{DroppedSendCount} (session {_sessionId})");
                    }
                    else
                    {
                        QueuedSendCount++;
                    }
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
            var data = MessageCodec.EncodeToArray(messageId, message, flags);
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
                    bool silent = false;
                    lock (_sendLock)
                    {
                        _kcp.Update(DateTimeOffset.UtcNow);
                        // 静默检测：仅按收包计时（Send 刷新 activity 会掩盖死端）
                        if (DateTime.UtcNow - _lastReceiveTime > PeerSilenceTimeout)
                            silent = true;
                    }
                    if (silent)
                    {
                        _logger.LogWarning($"Session {_sessionId} silent for {PeerSilenceTimeout.TotalSeconds:0}s - closing");
                        // 本循环内不可 await（CloseAsync 会等待本循环任务）；fire-and-forget 后 break 使任务即刻完成
                        _ = CloseAsync("PeerSilence");
                        break;
                    }
                    // 接收统一走 HandleUdpPacket（主循环线程）——此处不再 ReceiveFromKcp

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