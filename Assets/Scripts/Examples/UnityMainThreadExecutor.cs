using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Examples
{
    /// <summary>
    /// Unity主线程执行器示例
    /// 在Unity中，网络消息处理通常需要在主线程执行
    /// </summary>
    public class UnityMainThreadExecutor : KcpNet.UnityMainThreadExecutor
    {
        private static UnityMainThreadExecutor _instance;
        private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static UnityMainThreadExecutor Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new UnityMainThreadExecutor();
                }
                return _instance;
            }
        }

        private UnityMainThreadExecutor() { }

        /// <summary>
        /// 执行操作（在主线程）
        /// </summary>
        public override void Execute(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            // 将操作加入队列
            _actionQueue.Enqueue(action);
        }

        /// <summary>
        /// 更新方法，需要在Unity的Update循环中调用
        /// </summary>
        public void Update()
        {
            // 处理队列中的所有操作
            while (_actionQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error executing action on main thread: {ex}");
                }
            }
        }

        /// <summary>
        /// 清空队列
        /// </summary>
        public void Clear()
        {
            while (_actionQueue.TryDequeue(out _)) { }
        }

        /// <summary>
        /// 获取队列中的操作数量
        /// </summary>
        public int GetQueueCount()
        {
            return _actionQueue.Count;
        }

        /// <summary>
        /// Unity MonoBehaviour示例，展示如何集成
        /// </summary>
        public class ExecutorMonoBehaviour : MonoBehaviour
        {
            private UnityMainThreadExecutor _executor;

            void Awake()
            {
                _executor = UnityMainThreadExecutor.Instance;
                DontDestroyOnLoad(gameObject);
            }

            void Update()
            {
                // 每帧处理队列中的操作
                _executor.Update();
            }

            void OnDestroy()
            {
                _executor.Clear();
            }

            /// <summary>
            /// 示例：在Unity中使用KCP客户端
            /// </summary>
            public async void StartClientExample()
            {
                try
                {
                    // 创建配置
                    var options = new KcpNet.KcpOptions
                    {
                        SendWindowSize = 32,
                        ReceiveWindowSize = 32,
                        NoDelay = true,
                        Mtu = 1400
                    };

                    // 创建日志记录器（使用Unity的Debug）
                    var logger = new UnityLogger();

                    // 创建传输层
                    var transport = new KcpNet.KcpClientTransport(options, logger);

                    // 创建客户端
                    var client = new KcpNet.KcpClient(
                        sessionId: DateTime.UtcNow.Ticks,
                        remoteEndPoint: new System.Net.IPEndPoint(System.Net.IPAddress.Parse("127.0.0.1"), 8888),
                        transport: transport,
                        options: options,
                        logger: logger,
                        messageExecutor: _executor  // 使用Unity主线程执行器
                    );

                    // 注册事件（这些事件将在主线程触发）
                    client.MessageReceived += (sender, message) =>
                    {
                        Debug.Log($"收到消息: {message.GetType().Name}");
                        // 可以在这里更新UI或游戏对象
                    };

                    client.Disconnected += (sender, e) =>
                    {
                        Debug.Log($"连接断开: {e.Reason}");
                    };

                    // 连接服务器
                    await client.ConnectAsync("127.0.0.1", 8888);
                    Debug.Log("已连接到服务器");

                    // 发送登录请求
                    var loginRequest = new KcpNet.LoginRequest
                    {
                        Username = "UnityPlayer",
                        PasswordHash = "hash",
                        ClientVersion = Application.version,
                        DeviceInfo = SystemInfo.deviceModel
                    };

                    await client.SendAsync(loginRequest, KcpNet.MessageFlags.Reliable);
                    Debug.Log("已发送登录请求");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"客户端示例失败: {ex}");
                }
            }
        }

        /// <summary>
        /// Unity日志记录器实现
        /// </summary>
        public class UnityLogger : KcpNet.ILogger
        {
            public void Log(KcpNet.LogLevel level, Exception exception, string message, params object[] args)
            {
                var formattedMessage = args.Length > 0 ? string.Format(message, args) : message;
                var fullMessage = $"[KCP] {formattedMessage}";

                switch (level)
                {
                    case KcpNet.LogLevel.Debug:
                        Debug.Log(fullMessage);
                        break;
                    case KcpNet.LogLevel.Information:
                        Debug.Log(fullMessage);
                        break;
                    case KcpNet.LogLevel.Warning:
                        Debug.LogWarning(fullMessage);
                        break;
                    case KcpNet.LogLevel.Error:
                        if (exception != null)
                            Debug.LogError($"{fullMessage}\nException: {exception}");
                        else
                            Debug.LogError(fullMessage);
                        break;
                    case KcpNet.LogLevel.Critical:
                        Debug.LogError($"[CRITICAL] {fullMessage}");
                        if (exception != null)
                            Debug.LogException(exception);
                        break;
                }
            }

            public void LogDebug(string message, params object[] args) =>
                Log(KcpNet.LogLevel.Debug, null, message, args);

            public void LogInformation(string message, params object[] args) =>
                Log(KcpNet.LogLevel.Information, null, message, args);

            public void LogWarning(string message, params object[] args) =>
                Log(KcpNet.LogLevel.Warning, null, message, args);

            public void LogError(string message, params object[] args) =>
                Log(KcpNet.LogLevel.Error, null, message, args);

            public void LogError(Exception exception, string message, params object[] args) =>
                Log(KcpNet.LogLevel.Error, exception, message, args);

            public void LogCritical(string message, params object[] args) =>
                Log(KcpNet.LogLevel.Critical, null, message, args);

            public void LogCritical(Exception exception, string message, params object[] args) =>
                Log(KcpNet.LogLevel.Critical, exception, message, args);

            public bool IsEnabled(KcpNet.LogLevel level)
            {
                // 在生产版本中可能禁用调试日志
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                return true;
#else
                return level >= KcpNet.LogLevel.Information;
#endif
            }
        }
    }
}