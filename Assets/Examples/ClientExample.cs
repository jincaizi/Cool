using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using KcpNet;

namespace Examples
{
    /// <summary>
    /// KCP客户端使用示例
    /// </summary>
    public class ClientExample : IDisposable
    {
        private KcpClient _client;
        private KcpOptions _options;
        private ILogger _logger;
        private IMessageExecutor _messageExecutor;
        private CancellationTokenSource _cts;
        private bool _disposed;

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _client?.IsConnected ?? false;

        /// <summary>
        /// 会话ID
        /// </summary>
        public long SessionId => _client?.SessionId ?? 0;

        /// <summary>
        /// 初始化客户端示例
        /// </summary>
        public ClientExample()
        {
            // 创建配置选项
            _options = new KcpOptions
            {
                // KCP参数
                SendWindowSize = 32,
                ReceiveWindowSize = 32,
                NoDelay = true,
                Interval = 10,
                Mtu = 1400,

                // 网络参数
                ConnectionTimeout = 30,

                // 心跳与超时
                HeartbeatInterval = 5,
                HeartbeatTimeout = 30,

                // 重连参数
                MaxReconnectAttempts = 3,
                BaseReconnectDelay = 1,
                MaxReconnectDelay = 30,
                UseExponentialBackoff = true,

                // 性能调优
                SendBufferSize = 65536,
                ReceiveBufferSize = 65536
            };

            // 创建日志记录器
            _logger = ConsoleLogger.Instance;

            // 创建消息执行器（直接在当前线程执行）
            _messageExecutor = DirectMessageExecutor.Instance;

            _cts = new CancellationTokenSource();
        }

        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task ConnectAsync(string host = "127.0.0.1", int port = 8888)
        {
            try
            {
                Console.WriteLine($"正在连接到服务器 {host}:{port}...");

                // 创建客户端传输层
                var transport = new KcpClientTransport(_options, _logger);

                // 创建会话ID（实际应用中应该由服务器分配，这里使用临时值）
                long sessionId = DateTime.UtcNow.Ticks;

                // 创建远程终结点
                var remoteEndPoint = new IPEndPoint(IPAddress.Parse(host), port);

                // 创建客户端实例
                _client = new KcpClient(sessionId, remoteEndPoint, transport, _options, _logger, _messageExecutor);

                // 注册事件处理
                _client.MessageReceived += OnMessageReceived;
                _client.Disconnected += OnDisconnected;
                _client.Error += OnError;

                // 连接到服务器
                await _client.ConnectAsync(host, port, _cts.Token);

                Console.WriteLine($"已连接到服务器 {host}:{port}");
                Console.WriteLine($"会话ID: {_client.SessionId}");

                // 启动接收循环
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));

                // 发送登录请求
                await SendLoginRequestAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接服务器失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 发送登录请求
        /// </summary>
        private async Task SendLoginRequestAsync()
        {
            try
            {
                var loginRequest = new LoginRequest
                {
                    Username = $"Player_{DateTime.UtcNow.Ticks % 1000}",
                    PasswordHash = "md5_hash_placeholder", // 实际应用中应该使用真正的密码哈希
                    ClientVersion = "1.0.0",
                    DeviceInfo = "PC"
                };

                await _client.SendAsync(loginRequest, MessageFlags.Reliable, _cts.Token);
                Console.WriteLine($"已发送登录请求: 用户名={loginRequest.Username}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送登录请求失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送聊天消息
        /// </summary>
        public async Task SendChatMessageAsync(string content, int channelType = 0)
        {
            if (!IsConnected)
            {
                Console.WriteLine("未连接到服务器，无法发送消息");
                return;
            }

            try
            {
                var chatMessage = new ChatMessage
                {
                    SenderId = SessionId,
                    SenderName = $"Player_{SessionId % 1000}",
                    Content = content,
                    ChannelType = channelType,
                    Timestamp = DateTime.UtcNow.Ticks
                };

                await _client.SendAsync(chatMessage, MessageFlags.Reliable, _cts.Token);
                Console.WriteLine($"已发送聊天消息: {content}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送聊天消息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发送位置同步请求
        /// </summary>
        public async Task SendPositionUpdateAsync(float x, float y, float z, float rotation, float speed)
        {
            if (!IsConnected) return;

            try
            {
                var positionRequest = new PositionSyncRequest
                {
                    X = x,
                    Y = y,
                    Z = z,
                    Rotation = rotation,
                    Speed = speed,
                    Timestamp = DateTime.UtcNow.Ticks,
                    Sequence = (uint)(DateTime.UtcNow.Ticks % 1000000)
                };

                await _client.SendAsync(positionRequest, MessageFlags.None, _cts.Token);
                Console.WriteLine($"已发送位置更新: ({x:F2}, {y:F2}, {z:F2})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发送位置更新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 接收循环
        /// </summary>
        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("接收循环已启动");

            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                try
                {
                    var message = await _client.ReceiveAsync(cancellationToken);
                    if (message != null)
                    {
                        // 消息已经在事件处理程序中处理，这里只记录
                        Console.WriteLine($"收到消息: {message.GetType().Name}");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"接收消息时出错: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
            }

            Console.WriteLine("接收循环已停止");
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync(string reason = "ClientRequest")
        {
            try
            {
                if (_client != null && _client.IsConnected)
                {
                    // 发送断开连接请求
                    var disconnectRequest = new DisconnectRequest
                    {
                        Reason = reason,
                        Graceful = true
                    };

                    await _client.SendAsync(disconnectRequest, MessageFlags.Reliable, _cts.Token);
                    await Task.Delay(100); // 等待消息发送

                    // 关闭连接
                    await _client.CloseAsync(reason, _cts.Token);
                }

                Console.WriteLine($"已断开连接: {reason}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"断开连接时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 消息接收事件处理
        /// </summary>
        private void OnMessageReceived(object sender, IMessage message)
        {
            // 处理不同类型的消息
            switch (message)
            {
                case LoginResponse loginResponse:
                    HandleLoginResponse(loginResponse);
                    break;

                case ChatMessage chatMessage:
                    HandleChatMessage(chatMessage);
                    break;

                case BroadcastMessage broadcastMessage:
                    HandleBroadcastMessage(broadcastMessage);
                    break;

                case Kick kick:
                    HandleKickMessage(kick);
                    break;

                case ShutdownNotification shutdown:
                    HandleShutdownNotification(shutdown);
                    break;

                case PositionSyncResponse positionResponse:
                    HandlePositionSyncResponse(positionResponse);
                    break;

                default:
                    Console.WriteLine($"收到未处理的消息类型: {message.GetType().Name}");
                    break;
            }
        }

        /// <summary>
        /// 处理登录响应
        /// </summary>
        private void HandleLoginResponse(LoginResponse response)
        {
            Console.WriteLine($"登录响应: {(response.Success ? "成功" : "失败")} - {response.Message}");

            if (response.Success)
            {
                Console.WriteLine($"用户ID: {response.UserId}, 会话令牌: {response.SessionToken}");

                // 登录成功后可以开始发送其他消息
                // 例如：请求玩家列表、发送就绪状态等
            }
            else
            {
                Console.WriteLine("登录失败，请检查用户名和密码");
            }
        }

        /// <summary>
        /// 处理聊天消息
        /// </summary>
        private void HandleChatMessage(ChatMessage message)
        {
            Console.WriteLine($"[{GetChannelName(message.ChannelType)}] {message.SenderName}: {message.Content}");
        }

        /// <summary>
        /// 处理广播消息
        /// </summary>
        private void HandleBroadcastMessage(BroadcastMessage message)
        {
            Console.WriteLine($"[广播] {message.Content}");
            if (message.Parameters != null && message.Parameters.Length > 0)
            {
                Console.WriteLine($"参数: {string.Join(", ", message.Parameters)}");
            }
        }

        /// <summary>
        /// 处理踢出消息
        /// </summary>
        private void HandleKickMessage(Kick kick)
        {
            Console.WriteLine($"被服务器踢出: {kick.Reason} (类型: {kick.KickType})");

            if (kick.AllowReconnectAfter > 0)
            {
                Console.WriteLine($"允许在 {kick.AllowReconnectAfter} 秒后重连");
            }
        }

        /// <summary>
        /// 处理关闭通知
        /// </summary>
        private void HandleShutdownNotification(ShutdownNotification shutdown)
        {
            Console.WriteLine($"服务器关闭通知: {shutdown.Reason}");
            Console.WriteLine($"延迟: {shutdown.DelaySeconds} 秒");

            if (shutdown.EstimatedRecoveryTime > 0)
            {
                var recoveryTime = new DateTime(shutdown.EstimatedRecoveryTime);
                Console.WriteLine($"预计恢复时间: {recoveryTime:yyyy-MM-dd HH:mm:ss}");
            }
        }

        /// <summary>
        /// 处理位置同步响应
        /// </summary>
        private void HandlePositionSyncResponse(PositionSyncResponse response)
        {
            Console.WriteLine($"位置同步确认: 序列号={response.AcknowledgedSequence}, 服务器时间={response.ServerTimestamp}");
        }

        /// <summary>
        /// 连接断开事件处理
        /// </summary>
        private void OnDisconnected(object sender, SessionDisconnectedEventArgs e)
        {
            Console.WriteLine($"连接断开: {e.Reason}, 会话ID: {e.SessionId}");
        }

        /// <summary>
        /// 错误事件处理
        /// </summary>
        private void OnError(object sender, Exception e)
        {
            Console.WriteLine($"客户端错误: {e.Message}");
        }

        /// <summary>
        /// 获取频道名称
        /// </summary>
        private string GetChannelName(int channelType)
        {
            return channelType switch
            {
                0 => "世界",
                1 => "私聊",
                2 => "队伍",
                3 => "公会",
                _ => $"频道{channelType}"
            };
        }

        /// <summary>
        /// 运行客户端示例
        /// </summary>
        public static async Task RunExampleAsync(string host = "127.0.0.1", int port = 8888)
        {
            var example = new ClientExample();

            try
            {
                // 连接到服务器
                await example.ConnectAsync(host, port);

                // 等待一段时间，模拟用户操作
                Console.WriteLine("等待3秒后发送测试消息...");
                await Task.Delay(3000);

                // 发送测试聊天消息
                await example.SendChatMessageAsync("大家好！这是测试消息。");

                // 发送位置更新
                await example.SendPositionUpdateAsync(10.5f, 20.3f, 5.0f, 90.0f, 5.0f);

                // 等待用户输入退出
                Console.WriteLine("按 Enter 键断开连接...");
                Console.ReadLine();

                // 断开连接
                await example.DisconnectAsync("用户主动断开");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"客户端运行失败: {ex.Message}");
            }
            finally
            {
                example.Dispose();
            }
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _cts?.Cancel();
            _client?.Dispose();
            _cts?.Dispose();

            Console.WriteLine("客户端资源已清理");
        }
    }
}