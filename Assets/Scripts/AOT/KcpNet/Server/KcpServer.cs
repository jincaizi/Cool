using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// KCP服务器
    /// </summary>
    public sealed class KcpServer : IDisposable
    {
        private readonly KcpOptions _options;
        private readonly ILogger _logger;
        private readonly MessageDispatcher _messageDispatcher;
        private readonly ConcurrentDictionary<long, KcpServerSession> _sessions = new ConcurrentDictionary<long, KcpServerSession>();
        private readonly ConcurrentDictionary<IPAddress, int> _ipConnectionCounts = new ConcurrentDictionary<IPAddress, int>();
        private readonly ConcurrentDictionary<IPEndPoint, DateTime> _connectionAttempts = new ConcurrentDictionary<IPEndPoint, DateTime>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Socket _listenerSocket;
        private Task? _listenerTask;
        private Task? _maintenanceTask;
        private long _nextSessionId = 1;
        private bool _isRunning;
        private bool _disposed;

        /// <summary>
        /// 初始化KCP服务器
        /// </summary>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        public KcpServer(KcpOptions options, ILogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? NullLogger.Instance;
            _messageDispatcher = new MessageDispatcher(options, logger);

            _listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _listenerSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenerSocket.SendBufferSize = _options.SendBufferSize;
            _listenerSocket.ReceiveBufferSize = _options.ReceiveBufferSize;
        }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 活跃会话数量
        /// </summary>
        public int ActiveSessionCount => _sessions.Count;

        /// <summary>
        /// 消息分发器
        /// </summary>
        public MessageDispatcher MessageDispatcher => _messageDispatcher;

        /// <summary>
        /// 异步启动服务器
        /// </summary>
        /// <param name="port">监听端口</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>启动任务</returns>
        public async Task StartAsync(int port, CancellationToken cancellationToken = default)
        {
            if (_isRunning) throw new InvalidOperationException("Server is already running");
            if (_disposed) throw new ObjectDisposedException(nameof(KcpServer));

            try
            {
                var localEndPoint = new IPEndPoint(_options.BindAddress, port);
                _listenerSocket.Bind(localEndPoint);

                _isRunning = true;
                _listenerTask = Task.Run(() => ListenerLoopAsync(_cts.Token));
                _maintenanceTask = Task.Run(() => MaintenanceLoopAsync(_cts.Token));

                _logger.LogInformation($"KCP server started on port {port}");
                _logger.LogInformation($"Using options: SendWindow={_options.SendWindowSize}, ReceiveWindow={_options.ReceiveWindowSize}, Mtu={_options.Mtu}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to start server on port {port}");
                throw;
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 异步停止服务器
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>停止任务</returns>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!_isRunning) return;

            _logger.LogInformation("Stopping KCP server...");

            _isRunning = false;
            _cts.Cancel();

            // 发送关闭通知到所有会话
            await BroadcastShutdownNotificationAsync().ConfigureAwait(false);

            // 等待任务完成
            try
            {
                if (_listenerTask != null)
                    await _listenerTask.WithTimeout(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

                if (_maintenanceTask != null)
                    await _maintenanceTask.WithTimeout(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while waiting for tasks to complete");
            }

            // 关闭所有会话
            await CloseAllSessionsAsync().ConfigureAwait(false);

            _listenerSocket.Close();
            _messageDispatcher.Dispose();

            _logger.LogInformation("KCP server stopped");
        }

        /// <summary>
        /// 广播消息到所有会话
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="flags">消息标志</param>
        /// <returns>广播任务</returns>
        public async Task BroadcastAsync(IMessage message, MessageFlags flags = MessageFlags.None)
        {
            var tasks = new List<Task>();
            foreach (var session in _sessions.Values.ToList())
            {
                if (session.IsConnected)
                {
                    tasks.Add(session.SendAsync(message, flags));
                }
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                _logger.LogDebug($"Broadcast message {message.GetType().Name} to {tasks.Count} sessions");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during broadcast");
            }
        }

        /// <summary>
        /// 获取会话统计信息
        /// </summary>
        public ServerStatistics GetStatistics()
        {
            return new ServerStatistics
            {
                ActiveSessions = _sessions.Count,
                TotalConnections = _nextSessionId - 1,
                PeakSessions = 0, // 需要跟踪峰值
                MessagesProcessed = _messageDispatcher.GetRegisteredMessageTypes().Sum(t => _messageDispatcher.GetHandlerCount(t)),
                Uptime = DateTime.UtcNow - _startTime
            };
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="handler">处理器</param>
        /// <param name="priority">优先级</param>
        public void RegisterHandler<T>(Action<KcpSession, T, CancellationToken> handler, MessagePriority priority = MessagePriority.Medium) where T : IMessage
        {
            _messageDispatcher.RegisterHandler(handler, priority);
        }

        /// <summary>
        /// 注册异步消息处理器
        /// </summary>
        /// <typeparam name="T">消息类型</typeparam>
        /// <param name="handler">异步处理器</param>
        /// <param name="priority">优先级</param>
        public void RegisterHandler<T>(Func<KcpSession, T, CancellationToken, Task> handler, MessagePriority priority = MessagePriority.Medium) where T : IMessage
        {
            _messageDispatcher.RegisterHandler(handler, priority);
        }

        /// <summary>
        /// 获取所有活跃会话
        /// </summary>
        public IReadOnlyList<KcpServerSession> GetAllSessions()
        {
            return _sessions.Values.ToList();
        }

        /// <summary>
        /// 获取指定IP的活跃会话数
        /// </summary>
        public int GetActiveSessionCountForIp(IPAddress ipAddress)
        {
            if (_ipConnectionCounts.TryGetValue(ipAddress, out var count))
            {
                return count;
            }
            return 0;
        }

        /// <summary>
        /// 监听器循环
        /// </summary>
        private async Task ListenerLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Listener loop started");

            var buffer = new byte[_options.Mtu];
            var remoteEndPoint = (EndPoint)new IPEndPoint(IPAddress.Any, 0);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 接收UDP数据包
                    if (_listenerSocket.Available > 0 || await _listenerSocket.PollAsync(1000, SelectMode.SelectRead, cancellationToken))
                    {
                        var bytesRead = _listenerSocket.ReceiveFrom(buffer, ref remoteEndPoint);
                        if (bytesRead > 0)
                        {
                            var data = new byte[bytesRead];
                            Array.Copy(buffer, 0, data, 0, bytesRead);

                            // 处理数据包
                            await ProcessUdpPacketAsync(data, remoteEndPoint, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in listener loop");
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("Listener loop stopped");
        }

        /// <summary>
        /// 处理UDP数据包
        /// </summary>
        private async Task ProcessUdpPacketAsync(byte[] data, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            var ipEndPoint = remoteEndPoint as IPEndPoint;
            if (ipEndPoint == null) return;

            // 检查IP连接限制
            if (!CheckIpConnectionLimit(ipEndPoint.Address))
            {
                _logger.LogWarning($"IP {ipEndPoint.Address} exceeded connection limit");
                return;
            }

            // 查找或创建会话
            var session = FindOrCreateSession(ipEndPoint, cancellationToken);
            if (session == null) return;

            try
            {
                // 处理数据
                await _messageDispatcher.DispatchAsync(session, data).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing UDP packet from {remoteEndPoint}");
            }
        }

        /// <summary>
        /// 查找或创建会话
        /// </summary>
        private KcpServerSession? FindOrCreateSession(IPEndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            // 首先查找现有会话
            foreach (var se in _sessions.Values)
            {
                if (se.RemoteEndPoint.Equals(remoteEndPoint))
                {
                    return se;
                }
            }

            // 创建新会话
            var sessionId = Interlocked.Increment(ref _nextSessionId);
            var transport = new KcpServerTransport(_listenerSocket, remoteEndPoint, _options, _logger);
            var session = new KcpServerSession(sessionId, remoteEndPoint, transport, _options, _logger);

            if (_sessions.TryAdd(sessionId, session))
            {
                // 更新IP连接计数
                _ipConnectionCounts.AddOrUpdate(remoteEndPoint.Address, 1, (_, count) => count + 1);

                // 设置会话事件处理
                session.Disconnected += OnSessionDisconnected;
                session.Error += OnSessionError;

                _logger.LogInformation($"New session created: {sessionId} from {remoteEndPoint}");
                return session;
            }

            return null;
        }

        /// <summary>
        /// 检查IP连接限制
        /// </summary>
        private bool CheckIpConnectionLimit(IPAddress ipAddress)
        {
            if (_ipConnectionCounts.TryGetValue(ipAddress, out var count))
            {
                if (count >= _options.MaxConnectionsPerIp)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 维护循环
        /// </summary>
        private async Task MaintenanceLoopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Maintenance loop started");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // 清理超时会话
                    CleanupTimeoutSessions();

                    // 清理旧的连接尝试记录
                    CleanupOldConnectionAttempts();

                    // 定期日志
                    if (DateTime.UtcNow.Second % 30 == 0)
                    {
                        _logger.LogInformation($"Server status: {_sessions.Count} active sessions");
                    }

                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in maintenance loop");
                    await Task.Delay(5000, cancellationToken).ConfigureAwait(false);
                }
            }

            _logger.LogInformation("Maintenance loop stopped");
        }

        /// <summary>
        /// 清理超时会话
        /// </summary>
        private void CleanupTimeoutSessions()
        {
            var now = DateTime.UtcNow;
            var timeoutSessions = new List<long>();

            foreach (var kvp in _sessions)
            {
                var session = kvp.Value;
                var inactiveTime = now - session.LastActivity;

                if (inactiveTime.TotalSeconds > _options.SessionTimeout)
                {
                    timeoutSessions.Add(kvp.Key);
                    _logger.LogInformation($"Session {kvp.Key} timed out after {inactiveTime.TotalSeconds:F1}s of inactivity");
                }
                else if (inactiveTime.TotalSeconds > _options.HeartbeatTimeout)
                {
                    // 发送心跳请求或标记为可疑
                    _logger.LogDebug($"Session {kvp.Key} inactive for {inactiveTime.TotalSeconds:F1}s");
                }
            }

            // 移除超时会话
            foreach (var sessionId in timeoutSessions)
            {
                if (_sessions.TryRemove(sessionId, out var session))
                {
                    try
                    {
                        session.ForceClose();
                        DecrementIpConnectionCount(session.RemoteEndPoint as IPEndPoint);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error while cleaning up session {sessionId}");
                    }
                }
            }
        }

        /// <summary>
        /// 清理旧的连接尝试记录
        /// </summary>
        private void CleanupOldConnectionAttempts()
        {
            var now = DateTime.UtcNow;
            var oldAttempts = _connectionAttempts.Where(kvp => (now - kvp.Value).TotalMinutes > 5).ToList();

            foreach (var kvp in oldAttempts)
            {
                _connectionAttempts.TryRemove(kvp.Key, out _);
            }
        }

        /// <summary>
        /// 减少IP连接计数
        /// </summary>
        private void DecrementIpConnectionCount(IPEndPoint? endPoint)
        {
            if (endPoint == null) return;

            _ipConnectionCounts.AddOrUpdate(endPoint.Address, 0, (_, count) => Math.Max(0, count - 1));
        }

        /// <summary>
        /// 会话断开连接事件处理
        /// </summary>
        private void OnSessionDisconnected(object? sender, SessionDisconnectedEventArgs e)
        {
            var session = sender as KcpServerSession;
            if (session == null) return;

            if (_sessions.TryRemove(e.SessionId, out _))
            {
                DecrementIpConnectionCount(session.RemoteEndPoint as IPEndPoint);
                _logger.LogInformation($"Session {e.SessionId} disconnected: {e.Reason}");
            }
        }

        /// <summary>
        /// 会话错误事件处理
        /// </summary>
        private void OnSessionError(object? sender, Exception e)
        {
            var session = sender as KcpServerSession;
            if (session == null) return;

            _logger.LogError(e, $"Session {session.SessionId} error");
        }

        /// <summary>
        /// 广播关闭通知
        /// </summary>
        private async Task BroadcastShutdownNotificationAsync()
        {
            var shutdown = new ShutdownNotification
            {
                DelaySeconds = 3,
                Reason = "ServerShutdown",
                EstimatedRecoveryTime = DateTime.UtcNow.AddMinutes(5).Ticks
            };

            await BroadcastAsync(shutdown, MessageFlags.Reliable).ConfigureAwait(false);
            _logger.LogInformation("Broadcast shutdown notification to all sessions");
        }

        /// <summary>
        /// 关闭所有会话
        /// </summary>
        private async Task CloseAllSessionsAsync()
        {
            var tasks = new List<Task>();
            foreach (var session in _sessions.Values.ToList())
            {
                tasks.Add(session.CloseAsync("ServerShutdown"));
            }

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
                _sessions.Clear();
                _ipConnectionCounts.Clear();
                _logger.LogInformation($"Closed all {tasks.Count} sessions");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while closing sessions");
            }
        }

        /// <summary>
        /// 服务器启动时间
        /// </summary>
        private readonly DateTime _startTime = DateTime.UtcNow;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _ = StopAsync().ConfigureAwait(false);
            _listenerSocket?.Dispose();
            _cts.Dispose();
            _messageDispatcher.Dispose();

            _logger.LogInformation("KCP server disposed");
        }
    }

    /// <summary>
    /// 服务器统计信息
    /// </summary>
    public sealed class ServerStatistics
    {
        /// <summary>
        /// 活跃会话数
        /// </summary>
        public int ActiveSessions { get; internal set; }

        /// <summary>
        /// 总连接数
        /// </summary>
        public long TotalConnections { get; internal set; }

        /// <summary>
        /// 峰值会话数
        /// </summary>
        public int PeakSessions { get; internal set; }

        /// <summary>
        /// 处理的消息数
        /// </summary>
        public int MessagesProcessed { get; internal set; }

        /// <summary>
        /// 运行时间
        /// </summary>
        public TimeSpan Uptime { get; internal set; }

        /// <summary>
        /// 转换为字符串
        /// </summary>
        public override string ToString()
        {
            return $"Sessions: {ActiveSessions} active, {TotalConnections} total, Peak: {PeakSessions}, " +
                   $"Messages: {MessagesProcessed}, Uptime: {Uptime:hh\\:mm\\:ss}";
        }
    }

    /// <summary>
    /// Task扩展方法
    /// </summary>
    internal static class TaskExtensions
    {
        public static async Task WithTimeout(this Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var delayTask = Task.Delay(timeout, cancellationToken);
            var completedTask = await Task.WhenAny(task, delayTask).ConfigureAwait(false);

            if (completedTask == delayTask)
            {
                throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
            }

            await task.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Socket扩展方法
    /// </summary>
    internal static class SocketExtensions
    {
        public static Task<bool> PollAsync(this Socket socket, int microseconds, SelectMode mode, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => socket.Poll(microseconds, mode), cancellationToken);
        }
    }
}