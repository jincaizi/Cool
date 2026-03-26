using System;
using System.Threading;
using System.Threading.Tasks;
using KcpNet;

namespace Examples
{
    /// <summary>
    /// KCP服务器使用示例
    /// </summary>
    public class ServerExample
    {
        private KcpServer _server;
        private CancellationTokenSource _cts;

        /// <summary>
        /// 启动服务器
        /// </summary>
        public async Task StartServerAsync(int port = 8888)
        {
            try
            {
                Console.WriteLine($"正在启动KCP服务器，端口: {port}");

                // 1. 创建配置选项
                var options = new KcpOptions
                {
                    // KCP参数
                    SendWindowSize = 32,
                    ReceiveWindowSize = 32,
                    NoDelay = true,
                    Interval = 10,
                    Mtu = 1400,

                    // 网络参数
                    BindAddress = System.Net.IPAddress.Any,
                    ConnectionTimeout = 30,

                    // 心跳与超时
                    HeartbeatInterval = 5,
                    HeartbeatTimeout = 30,
                    SessionTimeout = 300,

                    // 限流参数
                    MaxConnectionsPerIp = 10,
                    MaxMessagesPerSecond = 1000,

                    // 性能调优
                    SendBufferSize = 65536,
                    ReceiveBufferSize = 65536
                };

                // 2. 创建日志记录器（使用控制台输出）
                ILogger logger = ConsoleLogger.Instance;

                // 3. 创建服务器实例
                _server = new KcpServer(options, logger);
                _cts = new CancellationTokenSource();

                // 4. 注册消息处理器
                RegisterMessageHandlers();

                // 5. 启动服务器
                await _server.StartAsync(port, _cts.Token);

                Console.WriteLine($"服务器已启动，监听端口: {port}");
                Console.WriteLine("按 Ctrl+C 停止服务器...");

                // 等待停止信号
                var stopSignal = new TaskCompletionSource<bool>();
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    stopSignal.SetResult(true);
                };

                await stopSignal.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动服务器时发生错误: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public async Task StopServerAsync()
        {
            if (_server == null) return;

            try
            {
                Console.WriteLine("正在停止服务器...");
                await _server.StopAsync(_cts?.Token ?? CancellationToken.None);
                Console.WriteLine("服务器已停止");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止服务器时发生错误: {ex.Message}");
            }
            finally
            {
                _server?.Dispose();
                _cts?.Dispose();
            }
        }

        /// <summary>
        /// 注册消息处理器
        /// </summary>
        private void RegisterMessageHandlers()
        {
            // 注册登录请求处理器
            _server.RegisterHandler<LoginRequest>(HandleLoginRequest);

            // 注册聊天消息处理器
            _server.RegisterHandler<ChatMessage>(HandleChatMessage);

            // 注册位置同步请求处理器
            _server.RegisterHandler<PositionSyncRequest>(HandlePositionSyncRequest);

            Console.WriteLine("消息处理器注册完成");
        }

        /// <summary>
        /// 处理登录请求
        /// </summary>
        private async Task HandleLoginRequest(KcpSession session, LoginRequest request, CancellationToken cancellationToken)
        {
            Console.WriteLine($"收到登录请求: 用户名={request.Username}, 设备={request.DeviceInfo}");

            // 简单的身份验证逻辑
            bool isAuthenticated = AuthenticateUser(request.Username, request.PasswordHash);

            var response = new LoginResponse
            {
                Success = isAuthenticated,
                Message = isAuthenticated ? "登录成功" : "用户名或密码错误",
                UserId = isAuthenticated ? GenerateUserId() : 0,
                SessionToken = isAuthenticated ? GenerateSessionToken() : string.Empty,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };

            // 发送响应
            await session.SendAsync(response, MessageFlags.Reliable, cancellationToken);

            if (isAuthenticated)
            {
                Console.WriteLine($"用户 {request.Username} 登录成功，分配用户ID: {response.UserId}");

                // 发送欢迎消息
                var welcomeChat = new ChatMessage
                {
                    SenderId = 0, // 系统消息
                    SenderName = "系统",
                    Content = $"欢迎 {request.Username} 加入服务器！",
                    ChannelType = 0, // 世界频道
                    Timestamp = DateTime.UtcNow.Ticks
                };

                await _server.BroadcastAsync(welcomeChat, MessageFlags.Reliable);
            }
            else
            {
                Console.WriteLine($"用户 {request.Username} 登录失败");
            }
        }

        /// <summary>
        /// 处理聊天消息
        /// </summary>
        private async Task HandleChatMessage(KcpSession session, ChatMessage message, CancellationToken cancellationToken)
        {
            Console.WriteLine($"收到聊天消息: [{message.SenderName}] {message.Content}");

            // 广播聊天消息到所有客户端
            await _server.BroadcastAsync(message, MessageFlags.Reliable);

            Console.WriteLine($"已广播聊天消息到所有客户端");
        }

        /// <summary>
        /// 处理位置同步请求
        /// </summary>
        private async Task HandlePositionSyncRequest(KcpSession session, PositionSyncRequest request, CancellationToken cancellationToken)
        {
            // 这里可以处理玩家位置同步逻辑
            // 例如：更新玩家位置、广播给其他玩家等

            var response = new PositionSyncResponse
            {
                AcknowledgedSequence = request.Sequence,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };

            // 发送确认响应
            await session.SendAsync(response, MessageFlags.Reliable, cancellationToken);

            // 可以根据需要将位置信息广播给其他玩家
            // await BroadcastPositionToOtherPlayers(session.SessionId, request);
        }

        /// <summary>
        /// 简单的用户认证
        /// </summary>
        private bool AuthenticateUser(string username, string passwordHash)
        {
            // 这里应该实现真正的认证逻辑
            // 示例中只检查用户名不为空
            return !string.IsNullOrEmpty(username) && username.Length >= 3;
        }

        /// <summary>
        /// 生成用户ID
        /// </summary>
        private long GenerateUserId()
        {
            return DateTime.UtcNow.Ticks % 1000000;
        }

        /// <summary>
        /// 生成会话令牌
        /// </summary>
        private string GenerateSessionToken()
        {
            return Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// 运行服务器示例
        /// </summary>
        public static async Task RunExampleAsync()
        {
            var example = new ServerExample();

            try
            {
                await example.StartServerAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"服务器运行失败: {ex.Message}");
            }
            finally
            {
                await example.StopServerAsync();
            }
        }
    }
}