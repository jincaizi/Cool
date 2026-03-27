# KCP网络框架使用示例

本目录包含KCP网络框架的客户端和服务端使用示例。

## 文件结构

- `ServerExample.cs` - 服务器端示例
- `ClientExample.cs` - 客户端示例
- `UnityMainThreadExecutor.cs` - Unity主线程执行器示例
- `README.md` - 本说明文件

## 快速开始

### 1. 运行服务器示例

服务器示例可以在控制台应用程序或Unity中运行。

```csharp
// 在控制台应用程序中运行
await Examples.ServerExample.RunExampleAsync();

// 或在Unity中
var serverExample = new Examples.ServerExample();
await serverExample.StartServerAsync(8888);
```

服务器启动后将：
- 监听指定端口（默认8888）
- 注册消息处理器（登录、聊天、位置同步）
- 等待客户端连接

### 2. 运行客户端示例

客户端示例可以在控制台应用程序或Unity中运行。

```csharp
// 在控制台应用程序中运行
await Examples.ClientExample.RunExampleAsync("127.0.0.1", 8888);

// 或在Unity中
var clientExample = new Examples.ClientExample();
await clientExample.ConnectAsync("127.0.0.1", 8888);
await clientExample.SendChatMessageAsync("Hello World!");
```

客户端将：
- 连接到指定服务器
- 自动发送登录请求
- 启动接收循环
- 可以发送聊天消息和位置更新

## 详细示例说明

### 服务器端 (ServerExample.cs)

#### 初始化服务器

```csharp
// 1. 创建配置选项
var options = new KcpNet.KcpOptions
{
    SendWindowSize = 32,
    ReceiveWindowSize = 32,
    NoDelay = true,
    Interval = 10,
    Mtu = 1400,
    BindAddress = IPAddress.Any,
    ConnectionTimeout = 30,
    HeartbeatInterval = 5,
    HeartbeatTimeout = 30,
    SessionTimeout = 300,
    MaxConnectionsPerIp = 10,
    MaxMessagesPerSecond = 1000
};

// 2. 创建日志记录器
ILogger logger = ConsoleLogger.Instance;

// 3. 创建服务器实例
var server = new KcpNet.KcpServer(options, logger);
```

#### 注册消息处理器

```csharp
// 注册登录请求处理器
server.RegisterHandler<LoginRequest>(HandleLoginRequest);

// 注册聊天消息处理器
server.RegisterHandler<ChatMessage>(HandleChatMessage);

// 注册位置同步请求处理器
server.RegisterHandler<PositionSyncRequest>(HandlePositionSyncRequest);
```

#### 处理消息示例

```csharp
private async Task HandleLoginRequest(KcpSession session, LoginRequest request, CancellationToken cancellationToken)
{
    // 验证用户
    bool isAuthenticated = AuthenticateUser(request.Username, request.PasswordHash);

    var response = new LoginResponse
    {
        Success = isAuthenticated,
        Message = isAuthenticated ? "登录成功" : "认证失败",
        UserId = isAuthenticated ? GenerateUserId() : 0,
        SessionToken = isAuthenticated ? GenerateSessionToken() : string.Empty,
        ServerTimestamp = DateTime.UtcNow.Ticks
    };

    await session.SendAsync(response, MessageFlags.Reliable, cancellationToken);
}
```

#### 广播消息

```csharp
// 广播消息到所有客户端
await server.BroadcastAsync(chatMessage, MessageFlags.Reliable);
```

### 客户端端 (ClientExample.cs)

#### 初始化客户端

```csharp
// 1. 创建配置
var options = new KcpNet.KcpOptions
{
    SendWindowSize = 32,
    ReceiveWindowSize = 32,
    NoDelay = true,
    Interval = 10,
    Mtu = 1400,
    ConnectionTimeout = 30,
    HeartbeatInterval = 5,
    HeartbeatTimeout = 30,
    MaxReconnectAttempts = 3
};

// 2. 创建日志记录器
ILogger logger = ConsoleLogger.Instance;

// 3. 创建消息执行器（直接执行）
IMessageExecutor executor = DirectMessageExecutor.Instance;

// 4. 创建传输层
var transport = new KcpClientTransport(options, logger);

// 5. 创建客户端
var client = new KcpClient(
    sessionId: DateTime.UtcNow.Ticks,
    remoteEndPoint: new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8888),
    transport: transport,
    options: options,
    logger: logger,
    messageExecutor: executor
);
```

#### 连接服务器

```csharp
// 连接服务器
await client.ConnectAsync("127.0.0.1", 8888);

// 注册事件处理
client.MessageReceived += OnMessageReceived;
client.Disconnected += OnDisconnected;
client.Error += OnError;

// 启动接收循环
_ = Task.Run(() => ReceiveLoopAsync(cancellationToken));
```

#### 发送消息

```csharp
// 发送登录请求
var loginRequest = new LoginRequest
{
    Username = "Player123",
    PasswordHash = "md5_hash",
    ClientVersion = "1.0.0",
    DeviceInfo = "PC"
};
await client.SendAsync(loginRequest, MessageFlags.Reliable);

// 发送聊天消息
var chatMessage = new ChatMessage
{
    SenderId = client.SessionId,
    SenderName = "Player123",
    Content = "Hello World!",
    ChannelType = 0, // 世界频道
    Timestamp = DateTime.UtcNow.Ticks
};
await client.SendAsync(chatMessage, MessageFlags.Reliable);

// 发送位置更新（使用不可靠传输）
var positionRequest = new PositionSyncRequest
{
    X = 10.5f,
    Y = 20.3f,
    Z = 5.0f,
    Rotation = 90.0f,
    Speed = 5.0f,
    Timestamp = DateTime.UtcNow.Ticks,
    Sequence = 1
};
await client.SendAsync(positionRequest, MessageFlags.Unreliable);
```

#### 接收消息

```csharp
private void OnMessageReceived(object sender, IMessage message)
{
    switch (message)
    {
        case LoginResponse loginResponse:
            Console.WriteLine($"登录响应: {loginResponse.Message}");
            break;

        case ChatMessage chatMessage:
            Console.WriteLine($"[聊天] {chatMessage.SenderName}: {chatMessage.Content}");
            break;

        case Kick kick:
            Console.WriteLine($"被踢出: {kick.Reason}");
            break;
    }
}
```

### Unity集成 (UnityMainThreadExecutor.cs)

在Unity中，网络消息处理需要在主线程执行。使用`UnityMainThreadExecutor`可以确保消息处理在Unity主线程进行。

#### 创建Unity主线程执行器

```csharp
public class UnityMainThreadExecutor : KcpNet.UnityMainThreadExecutor
{
    private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();

    public override void Execute(Action action)
    {
        _actionQueue.Enqueue(action);
    }

    // 在Update中处理队列
    public void Update()
    {
        while (_actionQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Debug.LogError(ex);
            }
        }
    }
}
```

#### 在Unity MonoBehaviour中使用

```csharp
public class NetworkManager : MonoBehaviour
{
    private UnityMainThreadExecutor _executor;
    private KcpNet.KcpClient _client;

    void Awake()
    {
        _executor = UnityMainThreadExecutor.Instance;
    }

    void Update()
    {
        // 处理网络消息（在主线程）
        _executor.Update();
    }

    public async void ConnectToServer()
    {
        var options = new KcpNet.KcpOptions { /* ... */ };
        var logger = new UnityLogger();
        var transport = new KcpNet.KcpClientTransport(options, logger);

        _client = new KcpNet.KcpClient(
            sessionId: DateTime.UtcNow.Ticks,
            remoteEndPoint: new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8888),
            transport: transport,
            options: options,
            logger: logger,
            messageExecutor: _executor  // 使用Unity主线程执行器
        );

        // 事件将在主线程触发
        _client.MessageReceived += (sender, message) =>
        {
            // 可以安全地更新UI或游戏对象
            if (message is KcpNet.ChatMessage chat)
            {
                chatText.text += $"\n{chat.SenderName}: {chat.Content}";
            }
        };

        await _client.ConnectAsync("127.0.0.1", 8888);
    }
}
```

#### Unity日志记录器

```csharp
public class UnityLogger : KcpNet.ILogger
{
    public void Log(KcpNet.LogLevel level, Exception exception, string message, params object[] args)
    {
        var formattedMessage = string.Format(message, args);

        switch (level)
        {
            case KcpNet.LogLevel.Debug:
            case KcpNet.LogLevel.Information:
                Debug.Log($"[KCP] {formattedMessage}");
                break;
            case KcpNet.LogLevel.Warning:
                Debug.LogWarning($"[KCP] {formattedMessage}");
                break;
            case KcpNet.LogLevel.Error:
            case KcpNet.LogLevel.Critical:
                Debug.LogError($"[KCP] {formattedMessage}");
                if (exception != null)
                    Debug.LogException(exception);
                break;
        }
    }

    // 实现其他接口方法...
}
```

## 消息类型说明

### 内置消息类型

| 消息类型 | 说明 | 用途 |
|---------|------|------|
| `LoginRequest` | 登录请求 | 客户端连接后发送的认证请求 |
| `LoginResponse` | 登录响应 | 服务器对登录请求的响应 |
| `Heartbeat` | 心跳消息 | 维持连接活性，计算RTT |
| `Kick` | 踢出消息 | 服务器主动断开客户端 |
| `DisconnectRequest` | 断开连接请求 | 客户端主动断开连接 |
| `ShutdownNotification` | 关闭通知 | 服务器关闭前通知客户端 |
| `ChatMessage` | 聊天消息 | 玩家间聊天通信 |
| `BroadcastMessage` | 广播消息 | 服务器向所有客户端广播 |
| `PositionSyncRequest` | 位置同步请求 | 客户端发送位置信息 |
| `PositionSyncResponse` | 位置同步响应 | 服务器确认位置接收 |

### 消息标志

- `MessageFlags.None` - 默认标志
- `MessageFlags.Reliable` - 可靠传输（保证送达）
- `MessageFlags.Unreliable` - 不可靠传输（可能丢失，延迟低）
- `MessageFlags.Encrypted` - 加密传输（如果配置了加密）

## 配置选项

### KCP参数
- `SendWindowSize` - 发送窗口大小（默认32）
- `ReceiveWindowSize` - 接收窗口大小（默认32）
- `NoDelay` - 无延迟模式（默认true）
- `Interval` - 内部更新间隔毫秒（默认10）
- `Mtu` - 最大传输单元（默认1400）

### 网络参数
- `BindAddress` - 绑定IP地址（默认Any）
- `BindPort` - 绑定端口（默认0，系统分配）
- `ConnectionTimeout` - 连接超时秒数（默认30）

### 心跳与超时
- `HeartbeatInterval` - 心跳发送间隔秒数（默认5）
- `HeartbeatTimeout` - 心跳超时时间（默认30）
- `SessionTimeout` - 会话超时时间（默认300）

### 限流参数
- `MaxConnectionsPerIp` - 单IP最大连接数（默认100）
- `MaxMessagesPerSecond` - 每秒最大消息数（默认1000）

## 最佳实践

1. **连接管理**
   - 客户端实现自动重连机制
   - 服务器监控连接状态，清理超时会话
   - 使用心跳维持连接活性

2. **消息处理**
   - 重要消息使用可靠传输（如登录、交易）
   - 实时数据使用不可靠传输（如位置、动画）
   - 避免在单帧内发送大量消息

3. **性能优化**
   - 根据网络状况调整KCP参数
   - 使用合适的MTU大小（通常1400）
   - 批量发送小消息减少开销

4. **错误处理**
   - 实现完善的错误处理和日志记录
   - 客户端处理服务器断开和重连
   - 服务器防止DDoS攻击（IP限制、速率限制）

## 常见问题

### 1. 连接失败
- 检查防火墙设置
- 确认服务器端口已开放
- 验证服务器IP地址和端口

### 2. 消息丢失
- 重要消息使用`MessageFlags.Reliable`
- 调整KCP窗口大小和重传参数
- 检查网络状况

### 3. 高延迟
- 启用`NoDelay`模式
- 减少心跳间隔
- 优化消息大小和频率

### 4. Unity中消息处理
- 使用`UnityMainThreadExecutor`确保主线程执行
- 避免在消息处理中执行耗时操作
- 使用协程处理异步操作

## 扩展开发

### 添加自定义消息类型

```csharp
[ProtoContract]
public class CustomMessage : KcpNet.IMessage
{
    [ProtoMember(1)]
    public int CustomId { get; set; }

    [ProtoMember(2)]
    public string CustomData { get; set; }
}

// 注册处理器
server.RegisterHandler<CustomMessage>(HandleCustomMessage);

private async Task HandleCustomMessage(KcpSession session, CustomMessage message, CancellationToken cancellationToken)
{
    // 处理自定义消息
}
```

## 技术支持

如有问题，请参考：
- KCP协议文档
- protobuf-net序列化文档
- Unity网络编程指南

## 许可证

本示例代码遵循MIT许可证。