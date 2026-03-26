using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// 会话统计信息
    /// </summary>
    public sealed class SessionStatistics
    {
        /// <summary>
        /// 发送消息数量
        /// </summary>
        public long MessagesSent { get; internal set; }

        /// <summary>
        /// 接收消息数量
        /// </summary>
        public long MessagesReceived { get; internal set; }

        /// <summary>
        /// 发送字节数
        /// </summary>
        public long BytesSent { get; internal set; }

        /// <summary>
        /// 接收字节数
        /// </summary>
        public long BytesReceived { get; internal set; }

        /// <summary>
        /// 发送错误次数
        /// </summary>
        public long SendErrors { get; internal set; }

        /// <summary>
        /// 接收错误次数
        /// </summary>
        public long ReceiveErrors { get; internal set; }

        /// <summary>
        /// 平均往返时间（RTT）
        /// </summary>
        public double AverageRtt { get; internal set; }

        /// <summary>
        /// 最后发送时间
        /// </summary>
        public DateTime LastSendTime { get; internal set; }

        /// <summary>
        /// 最后接收时间
        /// </summary>
        public DateTime LastReceiveTime { get; internal set; }

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            MessagesSent = 0;
            MessagesReceived = 0;
            BytesSent = 0;
            BytesReceived = 0;
            SendErrors = 0;
            ReceiveErrors = 0;
            AverageRtt = 0;
            LastSendTime = DateTime.MinValue;
            LastReceiveTime = DateTime.MinValue;
        }

        /// <summary>
        /// 转换为字符串
        /// </summary>
        public override string ToString()
        {
            return $"Sent: {MessagesSent} msgs ({BytesSent} bytes), Received: {MessagesReceived} msgs ({BytesReceived} bytes), RTT: {AverageRtt:F2}ms";
        }
    }

    /// <summary>
    /// 抽象会话类
    /// </summary>
    public abstract class KcpSession : IDisposable
    {
        private readonly object _logicalStateLock = new object();
        private object? _logicalState;
        private bool _disposed;

        /// <summary>
        /// 初始化会话
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <param name="transport">传输层</param>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        protected KcpSession(long sessionId, EndPoint remoteEndPoint, IKcpTransport transport, KcpOptions options, ILogger logger)
        {
            SessionId = sessionId;
            RemoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Logger = logger ?? NullLogger.Instance;
            Statistics = new SessionStatistics();
            LastActivity = DateTime.UtcNow;
        }

        /// <summary>
        /// 会话ID
        /// </summary>
        public long SessionId { get; }

        /// <summary>
        /// 远程终结点
        /// </summary>
        public EndPoint RemoteEndPoint { get; }

        /// <summary>
        /// 最后活动时间
        /// </summary>
        public DateTime LastActivity { get; private set; }

        /// <summary>
        /// 传输层
        /// </summary>
        protected IKcpTransport Transport { get; }

        /// <summary>
        /// 配置选项
        /// </summary>
        protected KcpOptions Options { get; }

        /// <summary>
        /// 日志记录器
        /// </summary>
        protected ILogger Logger { get; }

        /// <summary>
        /// 会话统计信息
        /// </summary>
        public SessionStatistics Statistics { get; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => Transport.IsConnected;

        /// <summary>
        /// 逻辑状态（用户自定义数据）
        /// </summary>
        public object? LogicalState
        {
            get
            {
                lock (_logicalStateLock)
                {
                    return _logicalState;
                }
            }
        }

        /// <summary>
        /// 消息接收事件
        /// </summary>
        public event EventHandler<IMessage>? MessageReceived;

        /// <summary>
        /// 断开连接事件
        /// </summary>
        public event EventHandler<SessionDisconnectedEventArgs>? Disconnected;

        /// <summary>
        /// 错误事件
        /// </summary>
        public event EventHandler<Exception>? Error;

        /// <summary>
        /// 异步发送消息
        /// </summary>
        /// <param name="message">消息</param>
        /// <param name="flags">消息标志</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>发送任务</returns>
        public virtual async Task SendAsync(IMessage message, MessageFlags flags = MessageFlags.None, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpSession));
            if (message == null) throw new ArgumentNullException(nameof(message));

            try
            {
                var messageId = MessageTypeRegistry.GetMessageId(message.GetType());
                if (!messageId.HasValue)
                {
                    throw new InvalidOperationException($"Message type not registered: {message.GetType().Name}");
                }

                var data = MessageCodec.EncodeToArray(messageId.Value, message, flags);
                var bytesSent = await Transport.SendAsync(data, cancellationToken).ConfigureAwait(false);

                // 更新统计
                Statistics.MessagesSent++;
                Statistics.BytesSent += bytesSent;
                Statistics.LastSendTime = DateTime.UtcNow;

                Logger.LogDebug($"Sent message: {message.GetType().Name} ({bytesSent} bytes)");
            }
            catch (Exception ex)
            {
                Statistics.SendErrors++;
                Logger.LogError(ex, $"Failed to send message: {message.GetType().Name}");
                OnError(ex);
                throw;
            }
        }

        /// <summary>
        /// 异步接收消息
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>接收到的消息</returns>
        public virtual async Task<IMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(KcpSession));

            try
            {
                var data = await Transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                if (data == null || data.Length == 0) return null;

                var (messageId, message) = MessageCodec.Decode(data);

                // 更新统计
                Statistics.MessagesReceived++;
                Statistics.BytesReceived += data.Length;
                Statistics.LastReceiveTime = DateTime.UtcNow;
                UpdateLastActivity();

                Logger.LogDebug($"Received message: {message.GetType().Name} ({data.Length} bytes)");

                // 触发事件
                OnMessageReceived(message);

                return message;
            }
            catch (Exception ex)
            {
                Statistics.ReceiveErrors++;
                Logger.LogError(ex, "Failed to receive message");
                OnError(ex);
                throw;
            }
        }

        /// <summary>
        /// 异步关闭会话
        /// </summary>
        /// <param name="reason">关闭原因</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>关闭任务</returns>
        public virtual async Task CloseAsync(string reason = "Normal", CancellationToken cancellationToken = default)
        {
            if (_disposed) return;

            try
            {
                Logger.LogInformation($"Closing session {SessionId}: {reason}");
                await Transport.CloseAsync(cancellationToken).ConfigureAwait(false);
                OnDisconnected(new SessionDisconnectedEventArgs(SessionId, reason, false));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error while closing session {SessionId}");
                OnDisconnected(new SessionDisconnectedEventArgs(SessionId, $"Error: {ex.Message}", true));
                throw;
            }
        }

        /// <summary>
        /// 强制关闭会话
        /// </summary>
        public virtual void ForceClose()
        {
            if (_disposed) return;

            try
            {
                Logger.LogWarning($"Force closing session {SessionId}");
                Transport.ForceClose();
                OnDisconnected(new SessionDisconnectedEventArgs(SessionId, "ForceClosed", true));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error while force closing session {SessionId}");
            }
        }

        /// <summary>
        /// 更新最后活动时间
        /// </summary>
        public void UpdateLastActivity()
        {
            LastActivity = DateTime.UtcNow;
        }

        /// <summary>
        /// 尝试绑定逻辑状态
        /// </summary>
        /// <param name="state">逻辑状态</param>
        /// <returns>是否绑定成功</returns>
        public bool TryBindLogicalState(object state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            lock (_logicalStateLock)
            {
                if (_logicalState != null) return false;
                _logicalState = state;
                return true;
            }
        }

        /// <summary>
        /// 尝试获取逻辑状态
        /// </summary>
        /// <typeparam name="T">状态类型</typeparam>
        /// <param name="state">状态值</param>
        /// <returns>是否获取成功</returns>
        public bool TryGetLogicalState<T>(out T? state)
        {
            lock (_logicalStateLock)
            {
                if (_logicalState is T typedState)
                {
                    state = typedState;
                    return true;
                }

                state = default;
                return false;
            }
        }

        /// <summary>
        /// 清除逻辑状态
        /// </summary>
        public void ClearLogicalState()
        {
            lock (_logicalStateLock)
            {
                _logicalState = null;
            }
        }

        /// <summary>
        /// 触发消息接收事件
        /// </summary>
        /// <param name="message">消息</param>
        protected virtual void OnMessageReceived(IMessage message)
        {
            MessageReceived?.Invoke(this, message);
        }

        /// <summary>
        /// 触发断开连接事件
        /// </summary>
        /// <param name="e">事件参数</param>
        protected virtual void OnDisconnected(SessionDisconnectedEventArgs e)
        {
            Disconnected?.Invoke(this, e);
        }

        /// <summary>
        /// 触发错误事件
        /// </summary>
        /// <param name="ex">异常</param>
        protected virtual void OnError(Exception ex)
        {
            Error?.Invoke(this, ex);
        }

        /// <summary>
        /// 开始接收消息循环
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>接收循环任务</returns>
        protected virtual async Task StartReceiveLoopAsync(CancellationToken cancellationToken = default)
        {
            Logger.LogInformation($"Starting receive loop for session {SessionId}");

            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                try
                {
                    var message = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    if (message == null)
                    {
                        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Error in receive loop for session {SessionId}");
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }

            Logger.LogInformation($"Receive loop stopped for session {SessionId}");
        }

        /// <inheritdoc/>
        public virtual void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            ForceClose();
            Transport.Dispose();

            Logger.LogInformation($"Session {SessionId} disposed");
        }
    }

    /// <summary>
    /// 会话断开连接事件参数
    /// </summary>
    public sealed class SessionDisconnectedEventArgs : EventArgs
    {
        /// <summary>
        /// 初始化事件参数
        /// </summary>
        public SessionDisconnectedEventArgs(long sessionId, string reason, bool isError)
        {
            SessionId = sessionId;
            Reason = reason;
            IsError = isError;
            Timestamp = DateTime.UtcNow;
        }

        /// <summary>
        /// 会话ID
        /// </summary>
        public long SessionId { get; }

        /// <summary>
        /// 断开原因
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// 是否是错误断开
        /// </summary>
        public bool IsError { get; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; }
    }
}