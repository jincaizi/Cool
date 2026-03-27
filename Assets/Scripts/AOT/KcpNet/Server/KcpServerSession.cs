using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace KcpNet
{
    /// <summary>
    /// 服务端会话
    /// </summary>
    public sealed class KcpServerSession : KcpSession
    {
        private readonly RateLimiter _rateLimiter;
        private readonly ConnectionStatistics _connectionStats;
        private DateTime _lastRateLimitCheck = DateTime.UtcNow;
        private int _messagesInCurrentSecond;
        private long _bytesInCurrentSecond;

        /// <summary>
        /// 初始化服务端会话
        /// </summary>
        /// <param name="sessionId">会话ID</param>
        /// <param name="remoteEndPoint">远程终结点</param>
        /// <param name="transport">传输层</param>
        /// <param name="options">配置选项</param>
        /// <param name="logger">日志记录器</param>
        public KcpServerSession(long sessionId, EndPoint remoteEndPoint, IKcpTransport transport, KcpOptions options, ILogger logger)
            : base(sessionId, remoteEndPoint, transport, options, logger)
        {
            _rateLimiter = new RateLimiter(options);
            _connectionStats = new ConnectionStatistics();
        }

        /// <summary>
        /// 连接统计信息
        /// </summary>
        public ConnectionStatistics ConnectionStats => _connectionStats;

        /// <summary>
        /// 是否超过速率限制
        /// </summary>
        public bool IsRateLimited => _rateLimiter.IsLimited;

        /// <summary>
        /// 当前每秒消息数
        /// </summary>
        public int CurrentMessagesPerSecond => _messagesInCurrentSecond;

        /// <summary>
        /// 当前每秒字节数
        /// </summary>
        public long CurrentBytesPerSecond => _bytesInCurrentSecond;

        /// <inheritdoc/>
        public override async Task SendAsync(IMessage message, MessageFlags flags = MessageFlags.None, CancellationToken cancellationToken = default)
        {
            // 检查速率限制
            if (_rateLimiter.ShouldLimitMessage())
            {
                Logger.LogWarning($"Rate limit exceeded for session {SessionId}, message dropped");
                _connectionStats.MessagesDroppedDueToRateLimit++;
                return;
            }

            await base.SendAsync(message, flags, cancellationToken).ConfigureAwait(false);
            _connectionStats.MessagesSent++;
        }

        /// <inheritdoc/>
        public override async Task<IMessage?> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            var message = await base.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (message != null)
            {
                UpdateRateLimitStatistics();
                _connectionStats.MessagesReceived++;
            }
            return message;
        }

        /// <summary>
        /// 更新速率限制统计
        /// </summary>
        private void UpdateRateLimitStatistics()
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRateLimitCheck;

            if (elapsed.TotalSeconds >= 1.0)
            {
                _messagesInCurrentSecond = 0;
                _bytesInCurrentSecond = 0;
                _lastRateLimitCheck = now;
            }

            _messagesInCurrentSecond++;
            // 注意：这里需要知道消息大小，但在ReceiveAsync中不知道
            // 可以在基类中跟踪或通过其他方式获取
        }

        /// <summary>
        /// 重置速率限制
        /// </summary>
        public void ResetRateLimit()
        {
            _rateLimiter.Reset();
            _messagesInCurrentSecond = 0;
            _bytesInCurrentSecond = 0;
            _lastRateLimitCheck = DateTime.UtcNow;
        }

        /// <summary>
        /// 获取会话状态摘要
        /// </summary>
        public string GetStatusSummary()
        {
            return $"Session {SessionId}: {RemoteEndPoint}, " +
                   $"Messages: {Statistics.MessagesSent}/{Statistics.MessagesReceived}, " +
                   $"RateLimited: {IsRateLimited}, " +
                   $"Active: {LastActivity:HH:mm:ss}";
        }
    }

    /// <summary>
    /// 连接统计信息
    /// </summary>
    public sealed class ConnectionStatistics
    {
        /// <summary>
        /// 发送的消息数
        /// </summary>
        public long MessagesSent { get; internal set; }

        /// <summary>
        /// 接收的消息数
        /// </summary>
        public long MessagesReceived { get; internal set; }

        /// <summary>
        /// 由于速率限制丢弃的消息数
        /// </summary>
        public long MessagesDroppedDueToRateLimit { get; internal set; }

        /// <summary>
        /// 连接建立时间
        /// </summary>
        public DateTime ConnectionTime { get; } = DateTime.UtcNow;

        /// <summary>
        /// 连接持续时间
        /// </summary>
        public TimeSpan ConnectionDuration => DateTime.UtcNow - ConnectionTime;

        /// <summary>
        /// 重置统计信息
        /// </summary>
        public void Reset()
        {
            MessagesSent = 0;
            MessagesReceived = 0;
            MessagesDroppedDueToRateLimit = 0;
        }
    }

    /// <summary>
    /// 速率限制器
    /// </summary>
    internal sealed class RateLimiter
    {
        private readonly KcpOptions _options;
        private DateTime _windowStart;
        private int _messageCount;
        private long _byteCount;
        private bool _isLimited;

        public RateLimiter(KcpOptions options)
        {
            _options = options;
            _windowStart = DateTime.UtcNow;
        }

        /// <summary>
        /// 是否被限制
        /// </summary>
        public bool IsLimited => _isLimited;

        /// <summary>
        /// 检查是否应该限制消息
        /// </summary>
        public bool ShouldLimitMessage()
        {
            var now = DateTime.UtcNow;
            var windowDuration = now - _windowStart;

            // 每秒重置计数
            if (windowDuration.TotalSeconds >= 1.0)
            {
                _messageCount = 0;
                _byteCount = 0;
                _windowStart = now;
                _isLimited = false;
            }

            // 检查消息数限制
            if (_messageCount >= _options.MaxMessagesPerSecond)
            {
                _isLimited = true;
                return true;
            }

            _messageCount++;
            return false;
        }

        /// <summary>
        /// 检查是否应该限制字节数
        /// </summary>
        public bool ShouldLimitBytes(int bytes)
        {
            var now = DateTime.UtcNow;
            var windowDuration = now - _windowStart;

            // 每秒重置计数
            if (windowDuration.TotalSeconds >= 1.0)
            {
                _messageCount = 0;
                _byteCount = 0;
                _windowStart = now;
                _isLimited = false;
            }

            // 检查字节数限制（这里简化处理，实际需要更复杂的逻辑）
            _byteCount += bytes;
            return false;
        }

        /// <summary>
        /// 重置限制器
        /// </summary>
        public void Reset()
        {
            _messageCount = 0;
            _byteCount = 0;
            _windowStart = DateTime.UtcNow;
            _isLimited = false;
        }
    }
}