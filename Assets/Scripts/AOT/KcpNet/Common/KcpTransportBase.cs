#if !UNITY_WEBGL
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// KCP传输层抽象基类
    /// </summary>
    public abstract class KcpTransportBase : IKcpTransport
    {
        /// <summary>单次网络调度最多处理的接收包数（排空 socket 缓冲；防止单次调度饥饿发送/更新路径）</summary>
        private const int MaxReceiveBatchPerIteration = 64;

        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _sendSemaphore = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _networkDriverTask;
        private readonly ILogger _logger;
        private readonly KcpOptions _options;

        protected volatile KcpTransportState _state = KcpTransportState.None;
        protected EndPoint? _remoteEndPoint;
        protected EndPoint? _localEndPoint;
        protected Socket? _socket;
        private bool _disposed;

        /// <summary>
        /// 初始化传输层基类
        /// </summary>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        protected KcpTransportBase(KcpOptions options, ILogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? NullLogger.Instance;

            if (_options.UseDedicatedNetworkThread)
            {
                _networkDriverTask = Task.Factory.StartNew(
                    NetworkDriverLoop,
                    TaskCreationOptions.LongRunning);
            }
            else
            {
                _networkDriverTask = Task.Run(NetworkDriverLoop);
            }
        }

        /// <inheritdoc/>
        public bool IsConnected => _state == KcpTransportState.Connected;

        /// <inheritdoc/>
        public EndPoint? RemoteEndPoint => _remoteEndPoint;

        /// <inheritdoc/>
        public EndPoint? LocalEndPoint => _localEndPoint;

        /// <inheritdoc/>
        public KcpTransportState State => _state;

        /// <summary>
        /// 配置选项
        /// </summary>
        protected KcpOptions Options => _options;

        /// <summary>
        /// 日志记录器
        /// </summary>
        protected ILogger Logger => _logger;

        /// <summary>
        /// 取消令牌
        /// </summary>
        protected CancellationToken CancellationToken => _cts.Token;

        /// <summary>
        /// 底层Socket
        /// </summary>
        protected Socket? Socket => _socket;

        /// <inheritdoc/>
        public virtual async Task ConnectAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpTransportBase));
            if (_state != KcpTransportState.None && _state != KcpTransportState.Closed)
                throw new InvalidOperationException($"Cannot connect in state {_state}");

            try
            {
                _state = KcpTransportState.Connecting;
                _remoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));

                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _socket.SendBufferSize = _options.SendBufferSize;
                _socket.ReceiveBufferSize = _options.ReceiveBufferSize;

                if (_options.BindAddress != null && _options.BindPort > 0)
                {
                    var localEndPoint = new IPEndPoint(_options.BindAddress, _options.BindPort);
                    _socket.Bind(localEndPoint);
                }
                else if (_options.BindPort > 0)
                {
                    var localEndPoint = new IPEndPoint(IPAddress.Any, _options.BindPort);
                    _socket.Bind(localEndPoint);
                }

                _localEndPoint = _socket.LocalEndPoint;

                await ConnectInternalAsync(cancellationToken).ConfigureAwait(false);

                _state = KcpTransportState.Connected;
                _logger.LogInformation($"Connected to {remoteEndPoint}");
            }
            catch (Exception ex)
            {
                _state = KcpTransportState.Error;
                _logger.LogError(ex, $"Failed to connect to {remoteEndPoint}");
                throw;
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<int> SendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpTransportBase));
            if (_state != KcpTransportState.Connected)
                throw new InvalidOperationException($"Cannot send in state {_state}");

            try
            {
                var data = buffer.ToArray(); // TODO: 使用ArrayPool优化
                _sendQueue.Enqueue(data);
                _sendSemaphore.Release();

                return data.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue send data");
                throw;
            }
        }

        /// <inheritdoc/>
        public virtual async ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpTransportBase));
            if (_state != KcpTransportState.Connected)
                throw new InvalidOperationException($"Cannot receive in state {_state}");

            // 子类应重写此方法以提供具体的接收逻辑
            throw new NotImplementedException("ReceiveAsync must be implemented by derived class");
        }

        /// <inheritdoc/>
        public virtual async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return;

            try
            {
                _state = KcpTransportState.Closing;
                await CloseInternalAsync(cancellationToken).ConfigureAwait(false);
                _state = KcpTransportState.Closed;
                _logger.LogInformation("Connection closed gracefully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while closing connection");
                throw;
            }
        }

        /// <inheritdoc/>
        public virtual void ForceClose()
        {
            if (_disposed) return;

            try
            {
                _state = KcpTransportState.Closing;
                _socket?.Close();
                _state = KcpTransportState.Closed;
                _logger.LogWarning("Connection force closed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while force closing connection");
            }
        }

        /// <inheritdoc/>
        public virtual void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cts.Cancel();

            try
            {
                _networkDriverTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while waiting for network driver task to complete");
            }

            ForceClose();
            _socket?.Dispose();
            _cts.Dispose();
            _sendSemaphore.Dispose();

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 网络驱动循环
        /// </summary>
        private async Task NetworkDriverLoop()
        {
            _logger.LogInformation("Network driver loop started");

            var stopwatch = new Stopwatch();
            var lastUpdateTime = DateTime.UtcNow;

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    // 处理发送队列
                    await ProcessSendQueueAsync().ConfigureAwait(false);

                    // 处理接收
                    await ProcessReceiveAsync().ConfigureAwait(false);

                    // 更新KCP状态
                    var now = DateTime.UtcNow;
                    var elapsed = now - lastUpdateTime;
                    if (elapsed.TotalMilliseconds >= _options.Interval)
                    {
                        await UpdateKcpAsync(elapsed).ConfigureAwait(false);
                        lastUpdateTime = now;
                    }

                    // 休眠以避免CPU占用过高。专用线程模式用同步休眠：Task.Delay 的续体需回线程池
                    // 调度，实测被延迟至 ~190ms/次，把驱动循环（含 KCP Update——ACK 生成与发送窗
                    // 补充）压到 ~5 次/秒，服务器 32 段发送窗因此补不上、快照流滞后无界增长
                    //（2026-09-13 集成实机定位：客户端接收 5~8 包/秒 vs 服务器发 15）。
                    // 专用线程（UseDedicatedNetworkThread，默认 true）下同步休眠不占用池线程。
                    if (_options.UseDedicatedNetworkThread)
                        System.Threading.Thread.Sleep(1);
                    else
                        await Task.Delay(1, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.Token.IsCancellationRequested)
                {
                    // 正常取消
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in network driver loop");
                    await Task.Delay(1000, _cts.Token).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("Network driver loop stopped");
        }

        /// <summary>
        /// 处理发送队列
        /// </summary>
        private async Task ProcessSendQueueAsync()
        {
            while (_sendSemaphore.Wait(0))
            {
                if (_sendQueue.TryDequeue(out var data))
                {
                    try
                    {
                        await SendKcpAsync(data).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send data via KCP");
                    }
                    finally
                    {
                        // TODO: 归还ArrayPool缓冲区
                    }
                }
            }
        }

        /// <summary>
        /// 处理接收——单次调度内循环排空（2026-09-13 集成实机定位：旧实现每调度只读一个包 +
        /// Task.Delay 续体回线程池调度，客户端实际吞吐被压到 ~5 包/秒；服务器 15Hz 快照流因此
        /// 持续积压于 socket 缓冲、滞后无界增长，AOI 远程实体被 1.2s 缺席超时全部清空）。
        /// 上限防单次调度饥饿发送/更新路径（64 包 ≫ 任一带宽下的两 tick 到达量）。
        /// </summary>
        private async Task ProcessReceiveAsync()
        {
            try
            {
                for (int i = 0; i < MaxReceiveBatchPerIteration; i++)
                {
                    var data = await ReceiveKcpAsync().ConfigureAwait(false);
                    if (data == null || data.Length == 0) break;
                    OnDataReceived(data);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while receiving data");
            }
        }

        /// <summary>
        /// 数据接收事件
        /// </summary>
        public event EventHandler<byte[]>? DataReceived;

        /// <summary>
        /// 触发数据接收事件
        /// </summary>
        /// <param name="data">接收到的数据</param>
        protected virtual void OnDataReceived(byte[] data)
        {
            DataReceived?.Invoke(this, data);
        }

        // ========== 抽象方法，由子类实现 ==========

        /// <summary>
        /// 内部连接实现
        /// </summary>
        protected abstract Task ConnectInternalAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 内部关闭实现
        /// </summary>
        protected abstract Task CloseInternalAsync(CancellationToken cancellationToken);

        /// <summary>
        /// 通过KCP发送数据
        /// </summary>
        protected abstract Task SendKcpAsync(byte[] data);

        /// <summary>
        /// 通过KCP接收数据
        /// </summary>
        protected abstract Task<byte[]?> ReceiveKcpAsync();

        /// <summary>
        /// 更新KCP状态
        /// </summary>
        protected abstract Task UpdateKcpAsync(TimeSpan elapsed);
    }
}
#endif