using System;
using System.Net;
using System.Threading.Tasks;
using Cool.Protocol;

namespace KcpNet
{
    /// <summary>
    /// INetworkClient 的 KCP 实现 —— 封装 KcpClient：
    /// 唯一 conv（时间戳派生，P0 MUST-DO-4）；消息解码后经 MessageId 事件上抛。
    /// 回调线程：DirectMessageExecutor 直发（P1 测试期）——回调发生在网络驱动线程，
    /// 涉及 UnityEngine API 的消费方须自行封送主线程（T7 起换 UnityMainThreadExecutor）。
    /// </summary>
    public sealed class KcpNetClientAdapter : INetworkClient
    {
        private readonly KcpOptions _options;
        private readonly ILogger _logger;
        private KcpClient? _client;

        public bool IsConnected => _client?.IsConnected ?? false;

        public event Action<MessageId, IMessage>? MessageReceived;
        public event Action<string>? Disconnected;
        public event Action<Exception>? Error;

        public KcpNetClientAdapter(KcpOptions? options = null, ILogger? logger = null)
        {
            _options = options ?? CreateDefaultOptions();
            _logger = logger ?? ConsoleLogger.Instance;
        }

        public async Task<bool> ConnectAsync(string host, int port)
        {
            // 先释放旧连接（重连路径：防双连接并发投递 + 驱动循环泄漏）
            if (_client != null)
            {
                _client.MessageReceived -= OnMessageReceived;
                _client.Disconnected -= OnDisconnected;
                _client.Error -= OnError;
                _client.Dispose();
                _client = null;
            }

            try
            {
                // 唯一 conv：时间戳截断（与服务器首包采纳机制配合）
                uint conv = (uint)(DateTime.UtcNow.Ticks % int.MaxValue);
                _client = new KcpClient(
                    (long)conv,
                    new IPEndPoint(IPAddress.Parse(host), port),
                    new KcpClientTransport(_options, _logger, conv),
                    _options,
                    _logger,
                    DirectMessageExecutor.Instance);

                _client.MessageReceived += OnMessageReceived;
                _client.Disconnected += OnDisconnected;
                _client.Error += OnError;

                await _client.ConnectAsync(host, port);
                return _client.IsConnected;
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                return false;
            }
        }

        public Task DisconnectAsync(string reason)
        {
            if (_client == null) return Task.CompletedTask;
            return _client.CloseAsync(reason);
        }

        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            if (_client == null || !_client.IsConnected)
            {
                _logger.LogDebug("Send dropped: not connected");
                return;
            }
            // fire-and-forget：不在调用线程同步等待（Unity 主线程 SyncContext 下
            // GetResult() 有死锁风险；KCP 发送本身同步完成，异步包装无长等待）
            _ = _client.SendAsync(message, flags);
        }

        private void OnMessageReceived(object? sender, IMessage message)
        {
            var id = MessageTypeRegistry.GetMessageId(message.GetType());
            if (id.HasValue)
            {
                MessageReceived?.Invoke(id.Value, message);
            }
        }

        private void OnDisconnected(object? sender, SessionDisconnectedEventArgs e)
        {
            Disconnected?.Invoke(e.Reason);
        }

        private void OnError(object? sender, Exception ex)
        {
            Error?.Invoke(ex);
        }

        public void Dispose()
        {
            if (_client != null)
            {
                _client.MessageReceived -= OnMessageReceived;
                _client.Disconnected -= OnDisconnected;
                _client.Error -= OnError;
                _client.Dispose();
                _client = null;
            }
        }

        private static KcpOptions CreateDefaultOptions()
        {
            return new KcpOptions
            {
                SendWindowSize = 32,
                ReceiveWindowSize = 32,
                NoDelay = true,
                Interval = 10,
                Mtu = 1400,
                ConnectionTimeout = 30,
                HeartbeatInterval = 5,
                HeartbeatTimeout = 30,
                MaxReconnectAttempts = 3,
                BaseReconnectDelay = 1,
                MaxReconnectDelay = 30,
                UseExponentialBackoff = true,
                SendBufferSize = 65536,
                ReceiveBufferSize = 65536
            };
        }
    }
}
