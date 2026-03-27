using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// KCP客户端
    /// </summary>
    public sealed class KcpClient : KcpSession
    {
        private readonly IMessageExecutor _messageExecutor;
        private readonly CancellationTokenSource _heartbeatCts = new CancellationTokenSource();
        private Task? _heartbeatTask;
        private Task? _receiveLoopTask;
        private long _heartbeatSequence;
        private bool _isReconnecting;
        private int _reconnectAttempts;

        /// <summary>
        /// 初始化客户端
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <param name="transport">传输层</param>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        /// <param name="messageExecutor">消息执行器</param>
        public KcpClient(long sessionId, EndPoint remoteEndPoint, IKcpTransport transport, KcpOptions options, ILogger logger, IMessageExecutor messageExecutor)
            : base(sessionId, remoteEndPoint, transport, options, logger)
        {
            _messageExecutor = messageExecutor ?? throw new ArgumentNullException(nameof(messageExecutor));
        }

        /// <summary>
        /// 异步连接到服务器
        /// </summary>
        /// <param name="host">主机地址</param>
        /// <param name="port">端口</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>连接任务</returns>
        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            if (IsConnected) throw new InvalidOperationException("Already connected");

            try
            {
                Logger.LogInformation($"Connecting to {host}:{port}...");

                var addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
                if (addresses.Length == 0)
                    throw new InvalidOperationException($"No IP addresses found for host: {host}");

                var remoteEndPoint = new IPEndPoint(addresses[0], port);
                await Transport.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);

                // 开始接收循环
                _receiveLoopTask = StartReceiveLoopAsync(cancellationToken);

                // 开始心跳
                StartHeartbeat();

                Logger.LogInformation($"Connected to {host}:{port} successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to connect to {host}:{port}");
                throw;
            }
        }

        /// <summary>
        /// 开始心跳
        /// </summary>
        public void StartHeartbeat()
        {
            if (_heartbeatTask != null && !_heartbeatTask.IsCompleted)
                return;

            _heartbeatSequence = 0;
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
            Logger.LogInformation("Heartbeat started");
        }

        /// <summary>
        /// 停止心跳
        /// </summary>
        public void StopHeartbeat()
        {
            _heartbeatCts.Cancel();
            _heartbeatTask = null;
            Logger.LogInformation("Heartbeat stopped");
        }

        /// <summary>
        /// 异步重连
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>重连任务</returns>
        public async Task ReconnectAsync(CancellationToken cancellationToken = default)
        {
            if (_isReconnecting) return;

            _isReconnecting = true;
            _reconnectAttempts++;

            try
            {
                // 停止当前连接
                StopHeartbeat();
                await CloseAsync("Reconnecting", cancellationToken).ConfigureAwait(false);

                // 计算退避延迟
                var delay = CalculateReconnectDelay();
                Logger.LogInformation($"Waiting {delay}ms before reconnection attempt {_reconnectAttempts}");

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                // 尝试重连
                if (RemoteEndPoint is IPEndPoint ipEndPoint)
                {
                    await ConnectAsync(ipEndPoint.Address.ToString(), ipEndPoint.Port, cancellationToken).ConfigureAwait(false);
                    _reconnectAttempts = 0; // 重置重连计数
                    Logger.LogInformation("Reconnected successfully");
                }
                else
                {
                    Logger.LogError("Cannot reconnect: RemoteEndPoint is not IPEndPoint");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Reconnection attempt {_reconnectAttempts} failed");
                throw;
            }
            finally
            {
                _isReconnecting = false;
            }
        }

        /// <summary>
        /// 处理服务端消息
        /// </summary>
        /// <param name="message">消息</param>
        public void HandleServerMessage(IMessage message)
        {
            if (message == null) return;

            // 在消息执行器上下文中处理消息
            _messageExecutor.Execute(() =>
            {
                try
                {
                    switch (message)
                    {
                        case Kick kick:
                            HandleKickMessage(kick);
                            break;
                        case ShutdownNotification shutdown:
                            HandleShutdownNotification(shutdown);
                            break;
                        case Heartbeat heartbeat:
                            HandleHeartbeatResponse(heartbeat);
                            break;
                        default:
                            // 其他消息通过事件处理
                            OnMessageReceived(message);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Error handling server message: {message.GetType().Name}");
                }
            });
        }

        /// <inheritdoc/>
        public override async Task SendAsync(IMessage message, MessageFlags flags = MessageFlags.None, CancellationToken cancellationToken = default)
        {
            await base.SendAsync(message, flags, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        protected override void OnMessageReceived(IMessage message)
        {
            // 在消息执行器上下文中触发事件
            _messageExecutor.Execute(() => base.OnMessageReceived(message));
        }

        /// <inheritdoc/>
        protected override void OnDisconnected(SessionDisconnectedEventArgs e)
        {
            // 在消息执行器上下文中触发事件
            _messageExecutor.Execute(() => base.OnDisconnected(e));
        }

        /// <inheritdoc/>
        protected override void OnError(Exception ex)
        {
            // 在消息执行器上下文中触发事件
            _messageExecutor.Execute(() => base.OnError(ex));
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            StopHeartbeat();
            _heartbeatCts.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// 心跳循环
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            Logger.LogDebug("Heartbeat loop started");

            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                try
                {
                    var heartbeat = new Heartbeat
                    {
                        ClientTimestamp = DateTime.UtcNow.Ticks,
                        Sequence = (uint)Interlocked.Increment(ref _heartbeatSequence)
                    };

                    await SendAsync(heartbeat, MessageFlags.Reliable, cancellationToken).ConfigureAwait(false);
                    Logger.LogDebug($"Heartbeat sent: sequence {heartbeat.Sequence}");

                    await Task.Delay(Options.HeartbeatInterval * 1000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in heartbeat loop");
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }

            Logger.LogDebug("Heartbeat loop stopped");
        }

        /// <summary>
        /// 处理踢出消息
        /// </summary>
        private void HandleKickMessage(Kick kick)
        {
            Logger.LogWarning($"Kicked from server: {kick.Reason} (type: {kick.KickType})");

            var args = new SessionDisconnectedEventArgs(SessionId, $"Kicked: {kick.Reason}", true);
            OnDisconnected(args);

            if (kick.AllowReconnectAfter > 0)
            {
                Logger.LogInformation($"Reconnection allowed after {kick.AllowReconnectAfter} seconds");
                // 可以在这里安排重连
            }
        }

        /// <summary>
        /// 处理关闭通知
        /// </summary>
        private void HandleShutdownNotification(ShutdownNotification shutdown)
        {
            Logger.LogInformation($"Server shutdown notification: {shutdown.Reason}, delay: {shutdown.DelaySeconds}s");

            // 可以在这里安排优雅断开
            if (shutdown.DelaySeconds > 0)
            {
                Task.Delay(shutdown.DelaySeconds * 1000).ContinueWith(_ =>
                {
                    CloseAsync("ServerShutdown").ConfigureAwait(false);
                });
            }
            else
            {
                CloseAsync("ServerShutdown").ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 处理心跳响应
        /// </summary>
        private void HandleHeartbeatResponse(Heartbeat heartbeat)
        {
            // 更新RTT统计
            var now = DateTime.UtcNow.Ticks;
            var rtt = (now - heartbeat.ClientTimestamp) / TimeSpan.TicksPerMillisecond;

            // 平滑RTT计算
            if (Statistics.AverageRtt == 0)
            {
                Statistics.AverageRtt = rtt;
            }
            else
            {
                Statistics.AverageRtt = Statistics.AverageRtt * 0.7 + rtt * 0.3;
            }

            Logger.LogDebug($"Heartbeat response received: RTT={rtt:F2}ms, AvgRTT={Statistics.AverageRtt:F2}ms");
        }

        /// <summary>
        /// 计算重连延迟（指数退避）
        /// </summary>
        private int CalculateReconnectDelay()
        {
            if (!Options.UseExponentialBackoff)
                return Options.BaseReconnectDelay * 1000;

            var delay = Options.BaseReconnectDelay * Math.Pow(2, _reconnectAttempts - 1);
            delay = Math.Min(delay, Options.MaxReconnectDelay);

            return (int)delay * 1000;
        }
    }
}