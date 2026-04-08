using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// 服务端KCP传输实现 (Kcp 2.7.0)
    /// </summary>
    public sealed class KcpServerTransport : KcpTransportBase
    {
        private Kcp? _kcp;
        private bool _kcpInitialized;
        private readonly long _sessionId;

        /// <summary>
        /// 初始化服务端传输
        /// </summary>
        /// <param name="socket">UDP套接字</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="sessionId">会话ID</param>
        public KcpServerTransport(Socket socket, EndPoint remoteEndPoint, KcpOptions options, ILogger logger, long sessionId = 0)
            : base(options, logger)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (remoteEndPoint == null) throw new ArgumentNullException(nameof(remoteEndPoint));

            _sessionId = sessionId;

            // 使用传入的socket和远程终结点
            _socket = socket;
            _remoteEndPoint = remoteEndPoint;
            _localEndPoint = socket.LocalEndPoint;
            _state = KcpTransportState.Connected;

            InitializeKcp();
        }

        /// <summary>
        /// 初始化KCP
        /// </summary>
        private void InitializeKcp()
        {
            if (Socket == null || RemoteEndPoint == null)
                throw new InvalidOperationException("Socket or RemoteEndPoint is null");

            _kcp = new Kcp(
                (uint)_sessionId,
                Socket,
                RemoteEndPoint,
                Options.SendWindowSize,
                Options.ReceiveWindowSize,
                Logger
            );

            // 配置 KCP
            _kcp.NoDelay(
                Options.NoDelay ? 1 : 0,
                Options.Interval,
                Options.FastResend,
                Options.NoCongestionControl ? 1 : 0
            );
            _kcp.SetMtu(Options.Mtu);

            _kcpInitialized = true;
            Logger.LogDebug($"KCP initialized for server transport (sessionId={_sessionId}) to {RemoteEndPoint}");
        }

        /// <inheritdoc/>
        protected override Task ConnectInternalAsync(CancellationToken cancellationToken)
        {
            // 服务端传输不需要主动连接
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override Task CloseInternalAsync(CancellationToken cancellationToken)
        {
            _kcpInitialized = false;
            _kcp?.Dispose();
            _kcp = null;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override async Task SendKcpAsync(byte[] data)
        {
            if (!_kcpInitialized || _kcp == null)
                throw new InvalidOperationException("KCP not initialized");

            try
            {
                _kcp.Send(data, 0, data.Length);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to send data via KCP to {RemoteEndPoint}");
                throw;
            }
        }

        /// <inheritdoc/>
        protected override async Task<byte[]?> ReceiveKcpAsync()
        {
            if (!_kcpInitialized || _kcp == null || Socket == null)
                return null;

            try
            {
                // 检查是否有数据可读
                if (Socket.Available > 0)
                {
                    var buffer = new byte[Options.Mtu];
                    EndPoint tempEndPoint = new IPEndPoint(IPAddress.Any, 0);

                    var bytesRead = Socket.ReceiveFrom(buffer, ref tempEndPoint);
                    if (bytesRead > 0 && tempEndPoint.Equals(RemoteEndPoint))
                    {
                        // 输入到KCP
                        _kcp.Input(buffer, 0, bytesRead);

                        // 从KCP读取数据
                        var bufferWriter = new ArrayBufferWriter<byte>(65536);
                        var recvCount = _kcp.TryRecv(bufferWriter);

                        if (recvCount > 0)
                        {
                            return bufferWriter.WrittenSpan.ToArray();
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to receive data via KCP from {RemoteEndPoint}");
                return null;
            }
        }

        /// <inheritdoc/>
        protected override async Task UpdateKcpAsync(TimeSpan elapsed)
        {
            if (!_kcpInitialized || _kcp == null)
                return;

            try
            {
                _kcp.Update(DateTime.UtcNow);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to update KCP for {RemoteEndPoint}");
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected");

            // 轮询接收数据
            while (!cancellationToken.IsCancellationRequested)
            {
                var data = await ReceiveKcpAsync().ConfigureAwait(false);
                if (data != null && data.Length > 0)
                {
                    return data;
                }

                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }

            throw new OperationCanceledException("Receive operation cancelled");
        }

        /// <summary>
        /// 处理接收到的UDP数据包
        /// </summary>
        /// <param name="data">数据</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <returns>是否成功处理</returns>
        public bool HandleUdpPacket(byte[] data, EndPoint remoteEndPoint)
        {
            if (!_kcpInitialized || _kcp == null || Socket == null)
                return false;

            try
            {
                // 验证远程终结点
                if (!remoteEndPoint.Equals(RemoteEndPoint))
                {
                    Logger.LogWarning($"Received packet from unexpected endpoint: {remoteEndPoint}, expected: {RemoteEndPoint}");
                    return false;
                }

                // 输入到KCP
                _kcp.Input(data, 0, data.Length);

                // 从KCP读取数据
                var bufferWriter = new ArrayBufferWriter<byte>(65536);
                var recvCount = _kcp.TryRecv(bufferWriter);

                if (recvCount > 0)
                {
                    OnDataReceived(bufferWriter.WrittenSpan.ToArray());
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to handle UDP packet from {remoteEndPoint}");
                return false;
            }
        }
    }
}