using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// 客户端KCP传输实现 (Kcp 2.7.0)
    /// </summary>
    public sealed class KcpClientTransport : KcpTransportBase
    {
        private Kcp? _kcp;
        private bool _kcpInitialized;
        private readonly long _sessionId;

        /// <summary>
        /// 初始化客户端传输
        /// </summary>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="sessionId">会话ID</param>
        public KcpClientTransport(KcpOptions options, ILogger logger, long sessionId = 0)
            : base(options, logger)
        {
            _sessionId = sessionId;
        }

        /// <inheritdoc/>
        protected override Task ConnectInternalAsync(CancellationToken cancellationToken)
        {
            if (Socket == null) throw new InvalidOperationException("Socket not initialized");

            // 初始化KCP
            _kcp = new Kcp(
                (uint)_sessionId,
                Socket,
                RemoteEndPoint!,
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
            Logger.LogInformation($"KCP initialized for client transport (sessionId={_sessionId})");
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
                Logger.LogError(ex, "Failed to send data via KCP");
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
                // 先喂入所有可用 UDP 包（KCP input），再独立尝试取应用消息。
                // 2026-09-13 集成实机定位：旧实现把 TryRecv 放在 "Socket.Available > 0" 分支内——
                // 无新包到达时 KCP 接收队列里**已就绪**的应用消息不会被取走，接收窗不释放，
                // 对端发送因此受阻（实测服务器发 15 msg/s、客户端仅收 7.8/s，且 socket 无积压）。
                while (Socket.Available > 0)
                {
                    var buffer = new byte[Options.Mtu];
                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    var bytesRead = Socket.ReceiveFrom(buffer, ref remoteEndPoint);
                    if (bytesRead <= 0) break;

                    // 输入到KCP
                    _kcp.Input(buffer, 0, bytesRead);
                }

                // 从KCP读取数据（与是否收到新包无关——保证已就绪消息被取走）
                var bufferWriter = new ArrayBufferWriter<byte>(65536);
                var recvCount = _kcp.TryRecv(bufferWriter);

                if (recvCount > 0)
                {
                    return bufferWriter.WrittenSpan.ToArray();
                }

                return null;
            }
            catch (ObjectDisposedException)
            {
                // socket 已释放（Dispose 路径）：静默返回，外层接收循环依 IsConnected 退出，不再空转抛错
                return null;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to receive data via KCP");
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
                Logger.LogError(ex, "Failed to update KCP");
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected");

            // 轮询接收数据（IsConnected：Close/Dispose 后 _state != Connected，循环退出，防止已释放 socket 上空转）
            while (!cancellationToken.IsCancellationRequested && IsConnected)
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
    }
}