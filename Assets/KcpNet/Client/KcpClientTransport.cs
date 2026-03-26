using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// 客户端KCP传输实现
    /// </summary>
    public sealed class KcpClientTransport : KcpTransportBase
    {
        private Kcp? _kcp;
        private bool _kcpInitialized;

        /// <summary>
        /// 初始化客户端传输
        /// </summary>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        public KcpClientTransport(KcpOptions options, ILogger logger)
            : base(options, logger)
        {
        }

        /// <inheritdoc/>
        protected override async Task ConnectInternalAsync(CancellationToken cancellationToken)
        {
            if (Socket == null) throw new InvalidOperationException("Socket not initialized");

            // 初始化KCP
            _kcp = new Kcp((uint)Options.SendWindowSize, (uint)Options.ReceiveWindowSize);
            _kcp.NoDelay(Options.NoDelay ? 1 : 0, Options.Interval, Options.FastResend, Options.NoCongestionControl);
            _kcp.SetMtu(Options.Mtu);

            // 设置输出回调
            _kcp.Output = (byte[] data, int length) =>
            {
                if (Socket != null && Socket.Connected)
                {
                    Socket.SendTo(data, 0, length, SocketFlags.None, RemoteEndPoint!);
                }
                return length;
            };

            _kcpInitialized = true;
            Logger.LogInformation("KCP initialized for client transport");
        }

        /// <inheritdoc/>
        protected override async Task CloseInternalAsync(CancellationToken cancellationToken)
        {
            _kcpInitialized = false;
            _kcp = null;
            await Task.CompletedTask;
        }

        /// <inheritdoc/>
        protected override async Task SendKcpAsync(byte[] data)
        {
            if (!_kcpInitialized || _kcp == null)
                throw new InvalidOperationException("KCP not initialized");

            try
            {
                _kcp.Send(data, 0, data.Length);
                _kcp.Flush();
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
                // 接收UDP数据
                var buffer = new byte[Options.Mtu];
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                if (Socket.Available > 0)
                {
                    var bytesRead = Socket.ReceiveFrom(buffer, ref remoteEndPoint);
                    if (bytesRead > 0)
                    {
                        var data = new byte[bytesRead];
                        Array.Copy(buffer, 0, data, 0, bytesRead);

                        // 输入到KCP
                        _kcp.Input(data, 0, data.Length);

                        // 从KCP读取数据
                        var recvSize = _kcp.PeekSize();
                        if (recvSize > 0)
                        {
                            var recvBuffer = new byte[recvSize];
                            var actualSize = _kcp.Recv(recvBuffer, 0, recvSize);
                            if (actualSize > 0)
                            {
                                var result = new byte[actualSize];
                                Array.Copy(recvBuffer, 0, result, 0, actualSize);
                                return result;
                            }
                        }
                    }
                }

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
    }
}