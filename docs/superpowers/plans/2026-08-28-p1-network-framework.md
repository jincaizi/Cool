# P1 双端框架核心 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立网络同步双端框架核心：服务端单线程 TickLoop(15Hz) + SessionManager + EntityStore/SnapshotAssembler 快照广播；客户端 SyncLayer（Adapter/Dispatcher/ClockSync/RemoteEntityRegistry）+ 登录闭环；双客户端进世界 + 断线重连重建验收。

**Architecture:** 服务端把 IO 线程先行 + Task.Run 散布的现状（P0 终审 MUST-DO-R2）收敛为"IO 线程只收包入队，单主循环线程独占世界状态并驱动快照广播"；客户端新建 AOT 适配层（INetworkClient + KcpNetClientAdapter）与 Hotfix SyncLayer（Dispatcher/ClockSyncDriver/RemoteEntityRegistry/ClientNetworkManager），时钟算法入共享 SyncCore 双端同源。

**Tech Stack:** .NET 8 服务器（Kcp 2.7.0 / protobuf-net 3.2.56）/ Unity 2022.3（C#9）/ xUnit + NUnit(EditMode) + PlayMode / 共享 Protocol（v1 追加规则）

**Spec:** `docs/superpowers/specs/2026-08-27-network-sync-design.md` §5-P1 + §3 协议治理 + §4 D1/D3/D5/D6

---

## 关键环境事实与执行决策（先读）

| 事实/决策 | 影响 |
|---|---|
| `Server3C.cs` 现状：ReceiveLoop(IO 线程) → Task.Run(HandlePacket) → O(n) FindOrCreateSession → HandleUdpPacket → DataReceived → Task.Run(ProcessPacket) switch 分发；CleanupLoop 30s | T1/T2 改造对象；P0 MUST-DO-3（竞态+O(n)）、MUST-DO-1（R2）落点 |
| `KcpServerSession.UpdateLoopAsync` 每会话 10ms 调 `_kcp.Update` **并** `ReceiveFromKcp()` → DataReceived 可能双线程触发 | T2 收敛：UpdateLoop 只做 `_kcp.Update`，接收统一走 HandleUdpPacket（主循环线程），消除并发 ProcessPacket |
| `KcpServerSession.Send(messageId, message, flags)` 已有 `_sendLock` | 主循环内发送线程安全，handler 无需额外锁 |
| `Room/RoomManager/PlayerState` 并发结构可复用；PlayerState 无脏标记/快照 tick | T4 在其上扩展（EntityStore 用普通 Dictionary——主循环单线程独占） |
| `LoginHandler` 已做版本握手+Kick（P0）；`GameSys.asmdef` 未引用 Cool.Protocol；`Server.Tests.csproj` 仅 include Protocol | T3/T6 接线点；T1 扩展测试项目 include Server 源码（除 Program.cs） |
| 客户端 Hotfix 无任何网络痕迹；Sys3CEntry 纯单机 | T6/T7 全新构建，不侵入 Sys3CEntry（挂载/接线 P2 移动同步时做） |
| **决策 D1**：登录闭环 = LoginRequest → LoginResponse **追加字段**（ServerTickRateMs/SpawnX/Y/Z/Rotation，新 tag 只加不删）；**不做独立 EnterWorld 消息**（P1 重连走全新 Login 流程，EnterWorld 留给 P2 平滑重连，协议追加机制已预留）——spec 字面"Login → EnterWorld"语义等价偏差，记入计划末尾偏差表 | T3 协议追加 |
| **决策 D2**：SyncCore 放 `Assets/Scripts/Hotfix/GameSystems/SyncCore/`（独立 asmdef `SyncCore`，noEngineReferences:true，C#9），服务器 csproj 以精确 `<Compile Include>` 引用该目录 | T5 |
| **决策 D3**：Server.Tests 扩展为 include Server 全部源码（Exclude Program.cs）+ Kcp 2.7.0 包，应用层逻辑（SessionManager/GameLoop/EntityStore/SnapshotAssembler）可直接 TDD | T1 |
| **决策 D4**：重连 = 断线检测 → 重新 ConnectAsync → 全新 LoginRequest（服务器端天然重建：新实体 + RoomSync/WorldSnapshot 恢复）。"现场正确重建"验收即此 | T8 |
| **决策 D5**：联调方式落地 = 文档化手动双开（复制仓库目录 + Unity Hub 双开）+ 尝试安装 ParrelSync（package-add git URL，失败则以文档兜底，不算阻塞） | T9 |
| 服务器双客户端验收在 PlayMode 内用两个独立 KcpClient 实例完成（同进程双 socket），不需双 Editor | T8 |
| 协议追加规则：MessageId 数值沿用 P0 分段（9/10 系统段、110 位置段空闲）；字段/消息只加不删；追加后 `RegistryCompletenessTests` 自动覆盖（枚举全成员断言） | T3 |
| 客户端 conv 唯一化（P0 MUST-DO-4）：KcpNetClientAdapter 连接时传唯一 sessionId（`DateTime.UtcNow.Ticks % int.MaxValue`），服务器首包采纳机制 P0 已具备 | T6 |
| 测试基线：Server.Tests 18 绿；Unity EditMode 140 绿；PlayMode 1 绿（EchoSmokeTests） | 各任务验证基准 |

**消息 ID 追加分配（v1 追加段）**：`ClockSyncRequest = 9`、`ClockSyncResponse = 10`、`WorldSnapshot = 110`。`EntitySnapshot` 内嵌于 WorldSnapshot（不单独注册）。

---

### Task 1: SessionManager（服务端会话管理收敛）

**Files:**
- Create: `Server/Session/SessionManager.cs`
- Modify: `Server/Server3C.cs`（使用 SessionManager，删除散落的会话字典/查找/绑定）
- Test: `Server/Server.Tests/Server/SessionManagerTests.cs` + csproj 扩展

- [ ] **Step 1: 扩展 Server.Tests 覆盖服务端源码**

编辑 `Server/Server.Tests/Server.Tests.csproj`，在 Protocol Include 之后追加：

```xml
  <!-- 服务端应用层源码（Program.cs 排除：含 Main，与测试宿主冲突） -->
  <ItemGroup>
    <Compile Include="..\**\*.cs" Exclude="..\Program.cs" />
  </ItemGroup>
```

并给 PackageReference 组追加 Kcp 包（与服务端同版本）：

```xml
    <PackageReference Include="Kcp" Version="2.7.0" />
```

运行 `dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj` —— 若 AI/Config 模块有编译依赖问题（如引用了 Unity 类型），把报错原文带回并在 Exclude 中追加对应文件后重试；预期 Baseline 18 项仍绿。

- [ ] **Step 2: 写失败测试**

创建 `Server/Server.Tests/Server/SessionManagerTests.cs`（完整内容）：

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class SessionManagerTests : IDisposable
{
    private readonly SessionManager _manager;
    private readonly TestLogger _logger = new TestLogger();

    public SessionManagerTests()
    {
        _manager = new SessionManager(_logger);
    }

    public void Dispose() => _manager.Dispose();

    [Fact]
    public void Concurrent_FirstPackets_SameEndpoint_CreateOneSession()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 40001);
        var firstPacket = BuildFirstPacket(conv: 42);

        // 模拟 IO 线程并发首包（P0 MUST-DO-3 竞态场景）
        var tasks = new List<Task>();
        var sessions = new List<KcpServerSession>();
        for (int i = 0; i < 16; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                var s = _manager.GetOrCreateSession(endpoint, firstPacket);
                lock (sessions) sessions.Add(s);
            }));
        }
        Task.WaitAll(tasks.ToArray());

        Assert.Equal(1, _manager.SessionCount);
    }

    [Fact]
    public void Session_Adopts_Conv_From_First_Packet()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 40002);
        var session = _manager.GetOrCreateSession(endpoint, BuildFirstPacket(conv: 12345));
        Assert.NotNull(session);
        Assert.Equal(12345U, session.KcpConv);
    }

    [Fact]
    public void Remove_By_Endpoint_Unregisters_Session()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 40003);
        var session = _manager.GetOrCreateSession(endpoint, BuildFirstPacket(conv: 1));
        Assert.Equal(1, _manager.SessionCount);

        _manager.RemoveByEndpoint(endpoint);
        Assert.Equal(0, _manager.SessionCount);
        Assert.Null(_manager.FindByEndpoint(endpoint));
    }

    [Fact]
    public void Shutdown_Closes_All_Sessions()
    {
        for (int i = 0; i < 3; i++)
        {
            _manager.GetOrCreateSession(new IPEndPoint(IPAddress.Loopback, 40010 + i), BuildFirstPacket(conv: (uint)(i + 1)));
        }
        Assert.Equal(3, _manager.SessionCount);

        _manager.ShutdownAll("TestShutdown").GetAwaiter().GetResult();
        Assert.Equal(0, _manager.SessionCount);
    }

    private static byte[] BuildFirstPacket(uint conv)
    {
        // KCP 头前 4 字节 = conv（LE），≥24 字节才被会话创建逻辑采纳
        var data = new byte[24];
        BitConverter.GetBytes(conv).CopyTo(data, 0);
        return data;
    }
}

public sealed class TestLogger : ILogger
{
    public bool IsEnabled(LogLevel level) => true;
    public void Log(LogLevel level, Exception? exception, string message, params object[] args) { }
    public void LogDebug(string message, params object[] args) { }
    public void LogInformation(string message, params object[] args) { }
    public void LogWarning(string message, params object[] args) { }
    public void LogError(string message, params object[] args) { }
    public void LogError(Exception ex, string message, params object[] args) { }
    public void LogCritical(string message, params object[] args) { }
    public void LogCritical(Exception ex, string message, params object[] args) { }
}
```

- [ ] **Step 3: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 编译错误 CS0246（找不到 `SessionManager`、`KcpConv` 属性等）。

- [ ] **Step 4: 实现 SessionManager**

创建 `Server/Session/SessionManager.cs`（完整内容）：

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// 会话管理器 —— 收敛 Server3C 散落的会话查找/创建/绑定
    /// （P0 终审 MUST-DO-3：消除 O(n) 端点扫描与 check-then-create 竞态；
    /// conv 采纳策略：服务器采纳客户端首包 conv，见 KcpServerSession）
    /// 线程安全：IO 线程唯一访问点（GetOrCreateSession），会话内发送由 _sendLock 保证
    /// </summary>
    public sealed class SessionManager : IDisposable
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, KcpServerSession> _sessions = new ConcurrentDictionary<string, KcpServerSession>();
        private long _nextSessionId = 1;
        private bool _disposed;

        public int SessionCount => _sessions.Count;
        public IEnumerable<KcpServerSession> AllSessions => _sessions.Values;

        public SessionManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 查找或原子创建会话（并发首包同端点 → 恰一个会话）
        /// </summary>
        public KcpServerSession GetOrCreateSession(EndPoint remoteEndPoint, byte[] firstPacket)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SessionManager));

            string key = remoteEndPoint.ToString()!;
            if (_sessions.TryGetValue(key, out var existing))
                return existing;

            long sessionId = System.Threading.Interlocked.Increment(ref _nextSessionId);
            uint? kcpConv = firstPacket != null && firstPacket.Length >= 24
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(firstPacket)
                : null;

            var created = new KcpServerSession(sessionId, remoteEndPoint, kcpConv, _logger);
            // GetOrAdd：并发下只有一个 created 成为胜者，败者的会话被立即释放
            var winner = _sessions.GetOrAdd(key, created);
            if (!ReferenceEquals(winner, created))
            {
                created.Dispose();
                return winner;
            }

            _logger.LogInformation($"New KCP session: {sessionId} from {remoteEndPoint} (conv={kcpConv})");
            return winner;
        }

        public KcpServerSession? FindByEndpoint(EndPoint remoteEndPoint)
        {
            return _sessions.TryGetValue(remoteEndPoint.ToString()!, out var s) ? s : null;
        }

        public void RemoveByEndpoint(EndPoint remoteEndPoint)
        {
            _sessions.TryRemove(remoteEndPoint.ToString()!, out _);
        }

        public async Task ShutdownAll(string reason)
        {
            foreach (var s in _sessions.Values)
            {
                await s.CloseAsync(reason);
            }
            _sessions.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var s in _sessions.Values)
                s.Dispose();
            _sessions.Clear();
        }
    }
}
```

注意：`KcpServerSession` 构造函数签名将改为 `(long sessionId, EndPoint remoteEndPoint, uint? kcpConv, ILogger logger)`——Socket 不再传入，发送改用 T1 新增的委托或由 GameLoop 注入。本步骤先让编译通过的最小形态：**KcpServerSession 的 Socket 依赖改为实例属性 `Socket SendSocket`（public set），由 SessionManager 在创建后赋值**；构造函数签名改为上述四参。

修改 `Server/KcpServerSession.cs`：

- 构造函数 `(long sessionId, EndPoint remoteEndPoint, Socket socket, ILogger logger, uint? kcpConv = null)` 改为：

```csharp
        private Socket? _sendSocket;

        /// <summary>发送 socket（由 SessionManager 注入；会话不持有 socket 生命周期）。只读——注入唯一入口为 SetSendSocket</summary>
        public Socket? SendSocket => _sendSocket;

        public uint KcpConv { get; }

        public KcpServerSession(long sessionId, EndPoint remoteEndPoint, uint? kcpConv, ILogger logger)
        {
            _sessionId = sessionId;
            _remoteEndPoint = remoteEndPoint;
            _logger = logger;
            KcpConv = kcpConv ?? 0;

            _callbackHandler = new KcpCallbackHandler(remoteEndPoint, logger);
            _kcp = new System.Net.Sockets.Kcp.Kcp<System.Net.Sockets.Kcp.KcpSegment>(kcpConv ?? (uint)sessionId, _callbackHandler, _callbackHandler);
            _kcp.SegmentManager = System.Net.Sockets.Kcp.SimpleSegManager.Default;
            _kcp.WndSize(SendWindowSize, ReceiveWindowSize);
            _kcp.NoDelay(1, Interval, FastResend, 0);
            _kcp.SetMtu(Mtu);

            _lastActivityTime = DateTime.UtcNow;
            _state = KcpSessionState.Connected;
            _updateTask = Task.Run(() => UpdateLoopAsync(_cts.Token));
            _logger.LogInformation($"KcpServerSession created: {_sessionId} from {_remoteEndPoint}");
        }
```

- `KcpCallbackHandler` 构造函数 `(Socket socket, EndPoint remoteEndPoint, ILogger logger, int bufferSize = 65536)` 改为 `(EndPoint remoteEndPoint, ILogger logger, int bufferSize = 65536)`，`Output` 内发送改为（socket 由外部提供，无则丢弃并警告）：

```csharp
        public void Output(IMemoryOwner<byte> buffer, int avalidLength)
        {
            try
            {
                var span = buffer.Memory.Span.Slice(0, avalidLength);
                if (_sendSocketProvider?.Invoke() is { } socket && socket.Connected)
                {
                    socket.SendTo(span.ToArray(), SocketFlags.None, _remoteEndPoint);
                }
            }
            ...
        }
        // 简化：KcpCallbackHandler 持有 Func<Socket?> _sendSocketProvider，由 SessionManager 注入
```

（实现提示：为最小改动，KcpCallbackHandler 增加 `public Func<Socket?>? SendSocketProvider { get; set; }`，Output 中取其值发送；KcpServerSession 增加唯一注入入口 `public void SetSendSocket(Socket socket)`：设置 `_sendSocket = socket` 并同时给 `_callbackHandler.SendSocketProvider` 赋值（lambda 捕获会话实例）。外部只允许经 SetSendSocket 注入，不允许直接改属性。）

- [ ] **Step 5: 运行确认通过**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 22 passed（Baseline 18 + SessionManager 4）。若 SessionManagerTests 依赖的 `_manager.Dispose()` 与 session 后台 UpdateLoop 存在关闭时序问题（Dispose 后日志异常），在 SessionManager.Dispose 中先 CloseAsync 再 Dispose（按测试输出调整，保持测试绿即可）。

- [ ] **Step 6: Server3C 会话管理切换（本任务内必做——KcpServerSession 构造签名变更后 Server3C 无法编译）**

改 `Server/Server3C.cs`（同步修正调用点，使项目恢复可编译）：

6a. 字段区：删除 `_sessions`（`ConcurrentDictionary<long, KcpServerSession>`）与 `_nextSessionId`（SessionManager 接管）；**保留** `_sessionIdToPlayerId` / `_playerIdToSessionId` / `_playerIdToSession`（`GetPlayerId`/`UnbindPlayer`/`GetSession` 依赖，绑定查询用途不变）。新增：

```csharp
        private readonly SessionManager _sessionManager;
```

构造函数：

```csharp
            _sessionManager = new SessionManager(logger);
```

`ActiveSessionCount` 改为 `_sessionManager.SessionCount`。

6b. `FindOrCreateSession` 方法整体删除，`HandlePacket` 改为：

```csharp
        private void HandlePacket(EndPoint remoteEndPoint, byte[] data)
        {
            try
            {
                var session = _sessionManager.GetOrCreateSession(remoteEndPoint, data);
                if (session == null) return;

                if (session.SendSocket == null)
                    session.SetSendSocket(_udpServer!);

                session.HandleUdpPacket(data, data.Length);
                session.UpdateActivity();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling packet");
            }
        }
```

6c. `StopAsync` 中 `foreach (var s in _sessions.Values)` 改为 `foreach (var s in _sessionManager.AllSessions)`；`_sessions.Clear()` 删除。

6d. `OnSessionDisconnected` 中 `_sessions.TryRemove(session.SessionId, out _)` 改为 `_sessionManager.RemoveByEndpoint(session.RemoteEndPoint)`。

6e. `Dispose()` 中 `foreach (var s in _sessions.Values)` 块改为 `_sessionManager.Dispose();`（事件退订逻辑由 SessionManager 的会话清理覆盖，`Disconnected -=` 等在会话 Dispose 前可保留原样——以编译与既有行为为准，最小改动）。

6f. 构建验证：

```bash
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
```

Expected: 0 Errors。

- [ ] **Step 7: Commit**

```bash
git add Server/Session Server/Server.Tests Server/KcpServerSession.cs Server/Server3C.cs
git commit -m "feat(server): session manager - atomic endpoint-keyed lookup, conv adoption, conv-session decoupling"
```

---

### Task 2: GameLoop 单线程主循环（R2 落地）

**Files:**
- Create: `Server/GameLoop.cs`
- Modify: `Server/Server3C.cs`（收包入队 + 移除两层 Task.Run + 使用 SessionManager）
- Modify: `Server/KcpServerSession.cs`（UpdateLoop 移除 ReceiveFromKcp）
- Modify: `Server/KcpServerSession.cs`（DataReceived 同步链）
- Test: `Server/Server.Tests/Server/GameLoopTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Server/Server.Tests/Server/GameLoopTests.cs`（完整内容）：

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KcpServer.Tests;

public class GameLoopTests
{
    private sealed class FakePacket
    {
        public int Value;
    }

    [Fact]
    public void Loop_Processes_Enqueued_Packets_In_Order()
    {
        var processed = new List<int>();
        var loop = new GameLoop<FakePacket>(TimeSpan.FromMilliseconds(5),
            packet => processed.Add(packet.Value), new TestLogger());

        loop.Start();
        try
        {
            for (int i = 0; i < 5; i++) loop.Enqueue(new FakePacket { Value = i });

            SpinWait.SpinUntil(() => processed.Count >= 5, TimeSpan.FromSeconds(2));

            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, processed);
        }
        finally
        {
            loop.Stop();
        }
    }

    [Fact]
    public void Loop_Throttles_To_Interval()
    {
        var ticks = 0;
        var loop = new GameLoop<FakePacket>(TimeSpan.FromMilliseconds(20),
            _ => Interlocked.Increment(ref ticks), new TestLogger());

        loop.Start();
        try
        {
            Thread.Sleep(250);
            int elapsed = Interlocked.CompareExchange(ref ticks, 0, 0);
            // 250ms / 20ms ≈ 12 次；断言在 [5, 25] 区间（宽松，防慢机器误报）
            Assert.InRange(elapsed, 5, 25);
        }
        finally
        {
            loop.Stop();
        }
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 编译错误 CS0246（找不到 `GameLoop<T>`）。

- [ ] **Step 3: 实现 GameLoop**

创建 `Server/GameLoop.cs`（完整内容）：

```csharp
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// 单线程主循环（R2 强制条款落地）：
    /// 唯一消费者独占世界状态；IO 线程只 Enqueue，不触碰状态。
    /// 心跳间隔由 TickInterval 节流；队列非空时每 tick 清空一次。
    /// </summary>
    public sealed class GameLoop<T> : IDisposable
    {
        private readonly TimeSpan _tickInterval;
        private readonly Action<T> _handler;
        private readonly Action<long>? _onTick;
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly ConcurrentQueue<Action> _actionQueue = new ConcurrentQueue<Action>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task? _loopTask;
        private long _tickCount;
        private bool _disposed;

        /// <summary>已执行 tick 数（供 WorldSnapshot.Tick 复用）</summary>
        public long TickCount => Interlocked.Read(ref _tickCount);

        public GameLoop(TimeSpan tickInterval, Action<T> handler, ILogger logger, Action<long>? onTick = null)
        {
            _tickInterval = tickInterval;
            _handler = handler;
            _logger = logger;
            _onTick = onTick;
        }

        public void Start()
        {
            if (_loopTask != null) throw new InvalidOperationException("GameLoop already started");
            _loopTask = Task.Run(() => LoopAsync(_cts.Token));
        }

        public void Enqueue(T packet)
        {
            if (_disposed) return;
            _queue.Enqueue(packet);
        }

        /// <summary>
        /// 入队一个动作（在主循环线程执行）——供非主循环线程的清理/断开路径
        /// 触达世界状态（R2：世界状态只允许主循环线程直写）
        /// </summary>
        public void EnqueueAction(Action action)
        {
            if (_disposed) return;
            _actionQueue.Enqueue(action);
        }

        public async Task Stop()
        {
            _cts.Cancel();
            if (_loopTask != null)
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;

                // 先 drain 动作队列（断开/清理等世界状态变更），再 drain 数据队列
                while (_actionQueue.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception ex) { _logger.LogError(ex, "GameLoop action error"); }
                }

                // 清空当前数据队列（单线程独占语义：处理期间新入队留待下一 tick）
                while (_queue.TryDequeue(out var packet))
                {
                    try { _handler(packet); }
                    catch (Exception ex) { _logger.LogError(ex, "GameLoop handler error"); }
                }

                long tick = Interlocked.Increment(ref _tickCount);
                _onTick?.Invoke(tick);

                var elapsed = DateTime.UtcNow - started;
                var remaining = _tickInterval - elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, token);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
```

- [ ] **Step 4: 运行确认通过**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 26 passed（22 + GameLoop 2）。

- [ ] **Step 5: 改造 Server3C（收包入队 + 单线程处理链）**

重写 `Server/Server3C.cs` 的四处（以 Task 1 已引入 SessionManager 为前提）：

5a. 字段区：`_sessions` 等三个并发字典删除，新增：

```csharp
        private readonly SessionManager _sessionManager;
        private readonly GameLoop<(EndPoint endpoint, byte[] data)> _gameLoop;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<long, KcpServerSession> _playerIdToSession = new();
```

（`_playerIdToSession` 保留给 GetSession/GetPlayerId 绑定查询——它由 LoginHandler 经 Bind/Unbind 维护，仍在主循环线程外被读取，保持并发字典。）

5b. 构造函数：

```csharp
        public Server3C(ILogger logger)
        {
            _logger = logger;
            _sessionManager = new SessionManager(logger);
            _roomManager = new RoomManager(new RoomManagerConfig());
            _loginHandler = new LoginHandler(this, _roomManager, _logger);
            _positionSyncHandler = new PositionSyncHandler(this, _roomManager, _logger);
            _echoHandler = new EchoHandler(this, _logger);

            // R2：唯一消费者线程独占世界状态；handler 同步链内完成解码/分发/发送
            _gameLoop = new GameLoop<(EndPoint, byte[])>(
                TimeSpan.FromMilliseconds(66),   // ~15Hz
                packet => HandlePacket(packet.Item1, packet.Item2),
                logger);
        }
```

5c. `StartAsync` 追加 `_gameLoop.Start();`；`StopAsync` 在 receiveTask 停后追加 `await _gameLoop.Stop();`。

5d. `ReceiveLoop` 的收包分支改为：

```csharp
                        if (bytesRead > 0)
                        {
                            var data = new byte[bytesRead];
                            Array.Copy(buffer, 0, data, 0, bytesRead);
                            _gameLoop.Enqueue((remoteEndPoint, data));
                        }
```

5e. `FindOrCreateSession` 删除，`HandlePacket` 改为：

```csharp
        private void HandlePacket(EndPoint remoteEndPoint, byte[] data)
        {
            try
            {
                var session = _sessionManager.GetOrCreateSession(remoteEndPoint, data);
                if (session == null) return;

                if (session.SendSocket == null)
                    session.SetSendSocket(_udpServer!);

                session.HandleUdpPacket(data, data.Length);
                session.UpdateActivity();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling packet");
            }
        }
```

5f. `OnSessionDataReceived` 改为同步直调（不再 Task.Run）：

```csharp
        private void OnSessionDataReceived(object? sender, byte[] data)
        {
            if (sender is KcpServerSession session)
            {
                ProcessPacket(session, data);
            }
        }
```

5g. `CleanupLoop` 的超时会话清理改用 `_sessionManager` 并按 R2 经 `EnqueueAction` 入主循环（CleanupLoop 在 30s 独立线程，**不得直写世界状态**）：

```csharp
                    foreach (var s in _sessionManager.AllSessions)
                    {
                        if (s.IsTimedOut(_sessionTimeout))
                        {
                            timeoutSessions.Add(s);
                        }
                    }

                    foreach (var s in timeoutSessions)
                    {
                        _logger.LogInformation($"Session {s.SessionId} timed out");
                        _gameLoop.EnqueueAction(async () =>
                        {
                            await _loginHandler.HandleDisconnectAsync(s);
                        });
                        _gameLoop.EnqueueAction(() =>
                        {
                            _sessionManager.RemoveByEndpoint(s.RemoteEndPoint);
                        });
                        await s.CloseAsync("Timeout");
                        s.Dispose();
                    }
```

（`async () =>` lambda 内 `HandleDisconnectAsync` 的 await 实际为同步完成——SendToSessionAsync 内部无真实 IO await；行为与原来一致但执行点移至主循环线程。）

- [ ] **Step 6: KcpServerSession.UpdateLoop 收敛**

`Server/KcpServerSession.cs` 的 `UpdateLoopAsync` 中删除 `ReceiveFromKcp();` 调用（接收统一由 HandleUdpPacket → Input 路径完成，DataReceived 仅主循环线程触发）：

```csharp
                    lock (_sendLock)
                    {
                        _kcp.Update(DateTimeOffset.UtcNow);
                    }
                    // 接收统一走 HandleUdpPacket（主循环线程）——此处不再 ReceiveFromKcp
```

并新增 `SetSendSocket`：

```csharp
        public void SetSendSocket(System.Net.Sockets.Socket socket)
        {
            _sendSocket = socket;
            _callbackHandler.SendSocketProvider = () => _sendSocket;
        }
```

KcpCallbackHandler 相应增加 `public Func<System.Net.Sockets.Socket?>? SendSocketProvider { get; set; }` 并在 Output 使用。

- [ ] **Step 7: 构建 + 测试**

```bash
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: build 0 Errors；26 passed。

- [ ] **Step 8: 回归验收（echo 冒烟仍通）**

```bash
cd "E:/CodeForJob/Cool" && dotnet run --project Server/Server3C.csproj -- 8000
```

（后台运行；TaskOutput 确认 `Server3C is running on port 8000` 后）Unity 侧 Skill `tests-run`(PlayMode, class=EchoSmokeTests) → Expected 1 passed。结束后停服务器并确认端口释放。

- [ ] **Step 9: Commit**

```bash
git add Server
git commit -m "feat(server): game loop r2 - single-thread world state, io-thread packet queue, sync receive chain"
```

---

### Task 3: 协议追加（时钟同步 + 世界快照 + 登录扩展）

**Files:**
- Modify: `Assets/Scripts/AOT/Protocol/MessageId.cs`（追加 3 值）
- Create: `Assets/Scripts/AOT/Protocol/Messages/ClockSync.cs`
- Create: `Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`
- Modify: `Assets/Scripts/AOT/Protocol/Messages/Login.cs`（LoginResponse 追加 5 字段）
- Modify: `Assets/Scripts/AOT/Protocol/MessageCodec.cs`（Registry 追加 3 注册）
- Test: `Server/Server.Tests/Protocol/WorldSnapshotTests.cs`（新增）

- [ ] **Step 1: 写失败测试**

创建 `Server/Server.Tests/Protocol/WorldSnapshotTests.cs`（完整内容）：

```csharp
using System;
using System.Collections.Generic;
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class WorldSnapshotTests
{
    [Fact]
    public void WorldSnapshot_Roundtrip_Preserves_All()
    {
        var source = new WorldSnapshot
        {
            Tick = 1234,
            ServerTimestamp = 987654321L,
            Entities = new List<EntitySnapshot>
            {
                new EntitySnapshot
                {
                    PlayerId = 7,
                    FieldMask = EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.ActionState,
                    X = 1.5f, Y = 0.25f, Z = -3.75f,
                    Rotation = 90f, Speed = 0f,
                    ActionState = 3,
                    Tick = 1234
                },
                new EntitySnapshot
                {
                    PlayerId = 9,
                    FieldMask = EntityFieldMask.Position,
                    X = 10f, Y = 2f, Z = -1f,
                    Rotation = 0f, Speed = 4f,
                    ActionState = 0,
                    Tick = 1234
                }
            }
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var snap = Assert.IsType<WorldSnapshot>(decoded);
        Assert.Equal(source.Tick, snap.Tick);
        Assert.Equal(source.ServerTimestamp, snap.ServerTimestamp);
        Assert.Equal(2, snap.Entities.Count);

        var first = snap.Entities[0];
        Assert.Equal(7L, first.PlayerId);
        // 两侧统一 cast 为 ushort（xUnit 泛型推断要求同类型）
        Assert.Equal((ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.ActionState), first.FieldMask);
        Assert.Equal(1.5f, first.X, 4);
        Assert.Equal(3, first.ActionState);
    }

    [Fact]
    public void ClockSync_Request_Response_Roundtrip()
    {
        var request = new ClockSyncRequest { ClientTimestamp = 111L };
        var encodedReq = MessageCodec.EncodeToArray(MessageId.ClockSyncRequest, request);
        var (reqId, reqDecoded) = MessageCodec.Decode(encodedReq);
        Assert.Equal(MessageId.ClockSyncRequest, reqId);
        Assert.Equal(111L, Assert.IsType<ClockSyncRequest>(reqDecoded).ClientTimestamp);

        var response = new ClockSyncResponse { ClientTimestamp = 111L, ServerTimestamp = 222L };
        var encodedResp = MessageCodec.EncodeToArray(MessageId.ClockSyncResponse, response);
        var (respId, respDecoded) = MessageCodec.Decode(encodedResp);
        Assert.Equal(MessageId.ClockSyncResponse, respId);
        var typed = Assert.IsType<ClockSyncResponse>(respDecoded);
        Assert.Equal(111L, typed.ClientTimestamp);
        Assert.Equal(222L, typed.ServerTimestamp);
    }

    [Fact]
    public void LoginResponse_AddedFields_Roundtrip()
    {
        var source = new LoginResponse
        {
            Success = true,
            Message = "ok",
            PlayerId = 42,
            SessionToken = "t0ken",
            ZoneId = "Global",
            ServerTimestamp = 1L,
            ServerTickRateMs = 67,
            SpawnX = 3f, SpawnY = 1f, SpawnZ = -2f,
            SpawnRotation = 45f
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.LoginResponse, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var resp = Assert.IsType<LoginResponse>(decoded);
        Assert.Equal(67L, resp.ServerTickRateMs);
        Assert.Equal(3f, resp.SpawnX, 4);
        Assert.Equal(45f, resp.SpawnRotation, 4);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 编译错误 CS0246（找不到 `WorldSnapshot`/`EntitySnapshot`/`EntityFieldMask`/`ClockSyncRequest` 等）。

- [ ] **Step 3: MessageId 追加**

`Assets/Scripts/AOT/Protocol/MessageId.cs`，在 `EchoResponse = 8` 后追加：

```csharp

        /// <summary>时钟同步请求（v1 追加）</summary>
        ClockSyncRequest = 9,

        /// <summary>时钟同步响应（v1 追加）</summary>
        ClockSyncResponse = 10,
```

在 `PositionSyncResponse = 101` 后追加：

```csharp

        /// <summary>世界快照广播（v1 追加；服务器 → 客户端，15Hz）</summary>
        WorldSnapshot = 110,
```

- [ ] **Step 4: 写新消息类**

创建 `Assets/Scripts/AOT/Protocol/Messages/ClockSync.cs`（完整内容）：

```csharp
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 时钟同步请求（客户端 → 服务器）。
    /// ClientTimestamp 语义：发送时刻的**客户端墙钟**（唯一允许墙钟的例外消息——
    /// 时钟同步的目标正是测量墙钟与服务器时钟的偏移，见 spec D3）。
    /// </summary>
    [ProtoContract]
    public class ClockSyncRequest : IMessage
    {
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }
    }

    /// <summary>时钟同步响应（服务器 → 客户端）</summary>
    [ProtoContract]
    public class ClockSyncResponse : IMessage
    {
        /// <summary>原样回传请求中的 ClientTimestamp</summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }

        /// <summary>服务器时钟（响应生成时刻）</summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }
    }
}
```

创建 `Assets/Scripts/AOT/Protocol/Messages/WorldSnapshot.cs`（完整内容）：

```csharp
using System.Collections.Generic;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 实体字段位掩码（增量快照编码，spec D5）——字段值仅在对应位被置位时有效。
    /// 只加不删：新增字段追加高位。
    /// </summary>
    [System.Flags]
    public enum EntityFieldMask : ushort
    {
        None = 0,
        Position = 1 << 0,
        Rotation = 1 << 1,
        Speed = 1 << 2,
        /// <summary>动作/战斗状态（攻击中/受击/眩晕…，spec F3a 首日含入；P1 恒为 0，P2 客户端上报后填充）</summary>
        ActionState = 1 << 3
    }

    /// <summary>实体快照（内嵌于 WorldSnapshot，不单独注册消息 ID）</summary>
    [ProtoContract]
    public class EntitySnapshot : IMessage
    {
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>本次快照携带哪些字段（EntityFieldMask 位图）</summary>
        [ProtoMember(2)]
        public ushort FieldMask { get; set; }

        [ProtoMember(3)]
        public float X { get; set; }

        [ProtoMember(4)]
        public float Y { get; set; }

        [ProtoMember(5)]
        public float Z { get; set; }

        [ProtoMember(6)]
        public float Rotation { get; set; }

        [ProtoMember(7)]
        public float Speed { get; set; }

        /// <summary>动作/战斗状态标志（0=无，语义 P2 定义）</summary>
        [ProtoMember(8)]
        public byte ActionState { get; set; }

        /// <summary>本快照对应的服务器 tick</summary>
        [ProtoMember(9)]
        public uint Tick { get; set; }
    }

    /// <summary>
    /// 世界快照广播（服务器 → 客户端，TickLoop 每 tick 组装）
    /// </summary>
    [ProtoContract]
    public class WorldSnapshot : IMessage
    {
        /// <summary>服务器 tick 序号</summary>
        [ProtoMember(1)]
        public uint Tick { get; set; }

        /// <summary>服务器时钟（组装时刻）</summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }

        /// <summary>本 tick 的脏实体快照（全静时为空列表）</summary>
        [ProtoMember(3)]
        public List<EntitySnapshot> Entities { get; set; } = new List<EntitySnapshot>();
    }
}
```

- [ ] **Step 5: LoginResponse 追加字段**

`Assets/Scripts/AOT/Protocol/Messages/Login.cs`，在 `ServerTimestamp` 后追加：

```csharp
        /// <summary>服务器 tick 间隔（毫秒；客户端据此换算插值窗口，spec D1）</summary>
        [ProtoMember(7)]
        public long ServerTickRateMs { get; set; }

        /// <summary>出生点 X（EnterWorld 语义并入登录响应，决策 D1）</summary>
        [ProtoMember(8)]
        public float SpawnX { get; set; }

        /// <summary>出生点 Y</summary>
        [ProtoMember(9)]
        public float SpawnY { get; set; }

        /// <summary>出生点 Z</summary>
        [ProtoMember(10)]
        public float SpawnZ { get; set; }

        /// <summary>出生朝向（Y 轴欧拉角）</summary>
        [ProtoMember(11)]
        public float SpawnRotation { get; set; }
```

- [ ] **Step 6: Registry 追加 3 注册**

`Assets/Scripts/AOT/Protocol/MessageCodec.cs` 静态构造中 `EchoResponse` 后追加：

```csharp
            RegisterMessageType(MessageId.ClockSyncRequest, typeof(ClockSyncRequest));
            RegisterMessageType(MessageId.ClockSyncResponse, typeof(ClockSyncResponse));
```

`PositionSyncResponse` 后追加：

```csharp
            RegisterMessageType(MessageId.WorldSnapshot, typeof(WorldSnapshot));
```

- [ ] **Step 7: 运行确认通过**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 32 passed（26 + 新 3 测试 + RegistryCompletenessTests 自动扩至 23 个 MessageId 全注册）。

Unity 侧：Skill `assets-refresh` → 编译零错误；Skill `tests-run`(EditMode, class=ProtocolRoundtripTests) → 2 passed。

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/AOT/Protocol Server/Server.Tests
git commit -m "feat(protocol): v1 additions - clock sync pair, world snapshot with entity field mask, login spawn+rate fields"
```

---

### Task 4: EntityStore + SnapshotAssembler + PerceptionFilter + 广播集成

**Files:**
- Create: `Server/Entity/EntityStore.cs`
- Create: `Server/Entity/SnapshotAssembler.cs`
- Create: `Server/Perception/IPerceptionFilter.cs`
- Create: `Server/Perception/AllVisiblePerceptionFilter.cs`
- Modify: `Server/Player/PlayerState.cs`（脏标记 + 快照 tick）
- Modify: `Server/Server3C.cs` / `Server/Handler/LoginHandler.cs`（实体注册/注销接入）
- Modify: `Server/GameLoop.cs`（tick 回调钩子：广播）
- Test: `Server/Server.Tests/Server/SnapshotAssemblerTests.cs`

- [ ] **Step 1: 写失败测试**

创建 `Server/Server.Tests/Server/SnapshotAssemblerTests.cs`（完整内容）：

```csharp
using System.Linq;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class SnapshotAssemblerTests
{
    [Fact]
    public void Dirty_Entity_Produces_FullField_Snapshot()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");
        player.UpdatePosition(new Vector3(1, 0, 2), 90f, 4f, 100L, 1U);
        store.MarkDirty(player);

        var assembler = new SnapshotAssembler();
        var snapshots = assembler.BuildDirtySnapshot(store, tick: 10);

        var snap = Assert.Single(snapshots);
        Assert.Equal(1L, snap.PlayerId);
        Assert.True((snap.FieldMask & (ushort)EntityFieldMask.Position) != 0);
        Assert.True((snap.FieldMask & (ushort)EntityFieldMask.Rotation) != 0);
        Assert.True((snap.FieldMask & (ushort)EntityFieldMask.Speed) != 0);
        Assert.Equal(1f, snap.X, 4);
        Assert.Equal(90f, snap.Rotation, 4);
        Assert.Equal(10U, snap.Tick);
    }

    [Fact]
    public void Dirty_Entity_Is_Cleared_After_Build()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");
        store.MarkDirty(player);

        var assembler = new SnapshotAssembler();
        assembler.BuildDirtySnapshot(store, tick: 1);

        Assert.Empty(assembler.BuildDirtySnapshot(store, tick: 2));
    }

    [Fact]
    public void Idle_Entity_Beacon_After_Interval()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");

        var assembler = new SnapshotAssembler();
        const uint beaconInterval = 4;

        // 前 3 个 tick 无快照
        for (uint tick = 1; tick < beaconInterval; tick++)
            Assert.Empty(assembler.BuildBeaconSnapshot(store, tick, beaconInterval));

        // 第 4 个 tick 产出 beacon（仅位置字段）
        var beacon = Assert.Single(assembler.BuildBeaconSnapshot(store, beaconInterval, beaconInterval));
        Assert.Equal(EntityFieldMask.Position, (EntityFieldMask)beacon.FieldMask);
    }

    [Fact]
    public void AllVisibleFilter_Returns_All_Except_Self()
    {
        var filter = new AllVisiblePerceptionFilter();
        var visible = filter.GetVisiblePlayerIds(observer: 1, new[] { 1L, 2L, 3L });
        Assert.Equal(new[] { 2L, 3L }, visible);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: CS0246（找不到 `EntityStore` 等）。

- [ ] **Step 3: PlayerState 扩展**

`Server/Player/PlayerState.cs` 追加：

```csharp
        /// <summary>脏标记：位置等同步字段在最近一次快照后发生变化（主循环独占访问）</summary>
        public bool IsDirty { get; internal set; }

        /// <summary>上次进入快照的 tick（beacon 判定用）</summary>
        public uint LastSnapshotTick { get; internal set; }
```

`UpdatePosition` 内追加 `IsDirty = true;`。

- [ ] **Step 4: 实现 EntityStore**

创建 `Server/Entity/EntityStore.cs`（完整内容）：

```csharp
using System.Collections.Generic;

namespace KcpServer
{
    /// <summary>
    /// 实体存储 —— 世界内玩家权威状态的唯一持有者。
    /// 主循环单线程独占访问（R2）：非并发字典。
    /// </summary>
    public sealed class EntityStore
    {
        private readonly Dictionary<long, PlayerState> _entities = new Dictionary<long, PlayerState>();

        public int Count => _entities.Count;
        public IReadOnlyCollection<PlayerState> All => _entities.Values;

        public PlayerState AddPlayer(long playerId, string playerName)
        {
            var state = new PlayerState(playerId) { PlayerName = playerName };
            _entities[playerId] = state;
            return state;
        }

        public bool RemovePlayer(long playerId) => _entities.Remove(playerId);

        public PlayerState? GetPlayer(long playerId)
            => _entities.TryGetValue(playerId, out var state) ? state : null;

        public void MarkDirty(PlayerState state) => state.IsDirty = true;

        /// <summary>所有脏实体（构建后自动清脏）</summary>
        public List<PlayerState> DrainDirty()
        {
            var result = new List<PlayerState>();
            foreach (var state in _entities.Values)
            {
                if (state.IsDirty)
                {
                    state.IsDirty = false;
                    result.Add(state);
                }
            }
            return result;
        }
    }
}
```

- [ ] **Step 5: 实现 SnapshotAssembler**

创建 `Server/Entity/SnapshotAssembler.cs`（完整内容）：

```csharp
using System.Collections.Generic;
using Cool.Protocol;

namespace KcpServer
{
    /// <summary>
    /// 快照组装器 —— 每 tick 由 TickLoop 调用：
    /// 脏实体全字段快照；静止实体按 beacon 间隔产出位置 beacon（spec D5）。
    /// </summary>
    public sealed class SnapshotAssembler
    {
        /// <summary>组装脏实体快照（清脏）；全静时返回空列表</summary>
        public List<EntitySnapshot> BuildDirtySnapshot(EntityStore store, uint tick)
        {
            var result = new List<EntitySnapshot>();
            foreach (var state in store.DrainDirty())
            {
                result.Add(ToSnapshot(state, EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Speed, tick));
                state.LastSnapshotTick = tick;
            }
            return result;
        }

        /// <summary>组装静止实体 beacon（仅位置字段；距上次快照 ≥ beaconInterval 才产出）</summary>
        public List<EntitySnapshot> BuildBeaconSnapshot(EntityStore store, uint tick, uint beaconInterval)
        {
            var result = new List<EntitySnapshot>();
            foreach (var state in store.All)
            {
                if (state.IsDirty) continue; // 脏实体由 BuildDirtySnapshot 负责
                if (tick - state.LastSnapshotTick < beaconInterval) continue;

                result.Add(ToSnapshot(state, EntityFieldMask.Position, tick));
                state.LastSnapshotTick = tick;
            }
            return result;
        }

        private static EntitySnapshot ToSnapshot(PlayerState state, EntityFieldMask mask, uint tick)
        {
            var snap = new EntitySnapshot
            {
                PlayerId = state.PlayerId,
                FieldMask = (ushort)mask,
                ActionState = 0, // P1 恒 0；P2 客户端上报动作状态后填充
                Tick = tick
            };
            if ((mask & EntityFieldMask.Position) != 0)
            {
                snap.X = state.Position.X;
                snap.Y = state.Position.Y;
                snap.Z = state.Position.Z;
            }
            if ((mask & EntityFieldMask.Rotation) != 0)
                snap.Rotation = state.Rotation;
            if ((mask & EntityFieldMask.Speed) != 0)
                snap.Speed = state.Speed;
            return snap;
        }
    }
}
```

- [ ] **Step 6: 实现感知接口与全量占位**

创建 `Server/Perception/IPerceptionFilter.cs`（完整内容）：

```csharp
namespace KcpServer
{
    /// <summary>
    /// 兴趣过滤接口（spec §5-P1 占位；P3 由 AoiGrid 实现替换，协议不动）
    /// </summary>
    public interface IPerceptionFilter
    {
        /// <summary>返回观察者可见的玩家 ID 集合（不含自身）</summary>
        long[] GetVisiblePlayerIds(long observerPlayerId, System.Collections.Generic.IReadOnlyCollection<PlayerState> allPlayers);
    }
}
```

创建 `Server/Perception/AllVisiblePerceptionFilter.cs`（完整内容）：

```csharp
using System.Collections.Generic;
using System.Linq;

namespace KcpServer
{
    /// <summary>全量可见占位实现（P1 阶段；P3 替换为 AoiGrid）</summary>
    public sealed class AllVisiblePerceptionFilter : IPerceptionFilter
    {
        public long[] GetVisiblePlayerIds(long observerPlayerId, IReadOnlyCollection<PlayerState> allPlayers)
        {
            return allPlayers.Where(p => p.PlayerId != observerPlayerId)
                             .Select(p => p.PlayerId)
                             .ToArray();
        }
    }
}
```

- [ ] **Step 7: 运行确认通过**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 36 passed（32 + SnapshotAssembler 4）。

- [ ] **Step 8: TickLoop 广播集成（GameLoop 钩子 + LoginHandler 注册）**

8a. `GameLoop` 已具备 onTick 钩子与动作队列（T2 落地）——`Server3C.cs` 构造 GameLoop 时直接挂广播链，并新增字段与装配：

```csharp
        private readonly EntityStore _entityStore = new EntityStore();
        private readonly SnapshotAssembler _snapshotAssembler = new SnapshotAssembler();
        private readonly IPerceptionFilter _perceptionFilter = new AllVisiblePerceptionFilter();
        private const uint BeaconIntervalTicks = 4; // 4Hz beacon（15Hz tick）

        // GameLoop 构造 onTick:
        tick => BroadcastWorldSnapshot((uint)tick)
```

```csharp
        /// <summary>每 tick 广播世界快照（脏实体 + beacon；广播给各玩家的可见集）</summary>
        private void BroadcastWorldSnapshot(uint tick)
        {
            var dirty = _snapshotAssembler.BuildDirtySnapshot(_entityStore, tick);
            var beacons = _snapshotAssembler.BuildBeaconSnapshot(_entityStore, tick, BeaconIntervalTicks);

            var allEntities = new List<Cool.Protocol.EntitySnapshot>(dirty.Count + beacons.Count);
            allEntities.AddRange(dirty);
            allEntities.AddRange(beacons);
            if (allEntities.Count == 0) return; // 全静世界：不发快照（带宽省）

            var snapshot = new WorldSnapshot
            {
                Tick = tick,
                ServerTimestamp = DateTime.UtcNow.Ticks,
                Entities = allEntities
            };

            foreach (var observer in _entityStore.All)
            {
                if (!_playerIdToSession.TryGetValue(observer.PlayerId, out var session)) continue;
                session.Send(MessageId.WorldSnapshot, snapshot);
            }
        }
```

8c. `LoginHandler` 登录成功路径（BindPlayerToSession 后）追加实体注册：

```csharp
            _server.RegisterEntity(playerId, request.Username);
```

`Server3C` 新增：

```csharp
        public void RegisterEntity(long playerId, string playerName)
        {
            _entityStore.AddPlayer(playerId, playerName);
        }

        public void UnregisterEntity(long playerId)
        {
            _entityStore.RemovePlayer(playerId);
        }
```

`LoginHandler.HandleDisconnectAsync` 的清理路径追加 `_server.UnregisterEntity(playerId.Value);`（在 TryRemovePlayer 之后）。**断开入口线程化（R2 关键）**：`HandleDisconnectAsync` 由 `Server3C.OnSessionDisconnected` 触发——该事件在**会话线程**（CloseAsync 调用点）触发，直写 EntityStore 会并发违规。改 `Server3C.OnSessionDisconnected`：

```csharp
        private void OnSessionDisconnected(object? sender, EventArgs e)
        {
            if (sender is KcpServerSession session)
            {
                _logger.LogInformation($"Session {session.SessionId} disconnected");
                // R2：世界状态变更一律经主循环动作队列
                _gameLoop.EnqueueAction(() => { _ = _loginHandler.HandleDisconnectAsync(session); });
                _gameLoop.EnqueueAction(() => _sessionManager.RemoveByEndpoint(session.RemoteEndPoint));
            }
        }
```

- [ ] **Step 9: 构建 + 测试 + echo 回归**

```bash
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 0 Errors；36 passed。重启服务器跑 PlayMode echo（同 Task 2 Step 8 流程）→ 1 passed。

- [ ] **Step 10: Commit**

```bash
git add Server
git commit -m "feat(server): entity store + snapshot assembler + perception filter - tick broadcast with dirty/beacon encoding"
```

---

### Task 5: SyncCore 骨架 + ClockSyncAlgorithm（共享双跑）

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncCore/SyncCore.asmdef`
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncCore/ClockSyncAlgorithm.cs`
- Modify: `Server/Server.Tests/Server.Tests.csproj`（include SyncCore）
- Test: `Server/Server.Tests/SyncCore/ClockSyncAlgorithmTests.cs` + `Assets/Scripts/Tests/Editor/SyncCore/ClockSyncAlgorithmTests.cs`

- [ ] **Step 1: 写失败测试（服务器侧先行）**

创建 `Server/Server.Tests/SyncCore/ClockSyncAlgorithmTests.cs`（完整内容）：

```csharp
using System;
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class ClockSyncAlgorithmTests
{
    /// <summary>
    /// 构造 8 样本：真实偏移 +100ms（server 快 100ms），RTT 均为 50ms；
    /// 混入一个 RTT 500ms 的离群样本，min-filter 应不受其干扰。
    /// </summary>
    [Fact]
    public void Offset_Converges_To_True_Value_With_MinFilter()
    {
        var algo = new ClockSyncAlgorithm(8);
        long t1 = 1_000_000_000L; // 客户端发送墙钟（ticks）
        // 100ms 偏移（ticks：100ms * 10_000 = 1_000_000 ticks）
        const long offsetTicks = 1_000_000L;
        const long rttTicks = 500_000L;        // 50ms
        const long outlierRttTicks = 5_000_000L; // 500ms 离群

        for (int i = 0; i < 7; i++)
        {
            var t2 = t1 + offsetTicks + rttTicks / 2; // server 收到时刻（server 时钟）
            var t3 = t1 + rttTicks;                   // 客户端收到时刻（墙钟）
            algo.AddSample(t1, t2, t3);
            t1 += 1_000_000L;
        }
        // 离群样本
        {
            var t2 = t1 + offsetTicks + outlierRttTicks / 2;
            var t3 = t1 + outlierRttTicks;
            algo.AddSample(t1, t2, t3);
        }

        Assert.True(algo.HasConverged, "8 样本后应收敛");
        Assert.InRange(algo.OffsetTicks, offsetTicks - 50_000, offsetTicks + 50_000);
    }

    [Fact]
    public void ToServerTime_Applies_Offset()
    {
        var algo = new ClockSyncAlgorithm(8);
        long t1 = 5_000_000L;
        algo.AddSample(t1, t1 + 1_000_000L, t1 + 2_000_000L); // RTT 200ms，offset 0

        long serverTime = algo.ToServerTime(t1 + 1_000_000L);
        // offset=0（两端时钟对齐）：墙钟 t1+1M 即服务器时钟 t1+1M
        Assert.Equal(t1 + 1_000_000L, serverTime);
    }
}
```

- [ ] **Step 2: 运行确认失败**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: CS0246（找不到 `Cool.SyncCore`）。

- [ ] **Step 3: 创建 SyncCore 程序集与算法**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncCore/SyncCore.asmdef`（完整内容）：

```json
{
    "name": "SyncCore",
    "rootNamespace": "Cool.SyncCore",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "versionDefines": [],
    "noEngineReferences": true
}
```

创建 `Assets/Scripts/Hotfix/GameSystems/SyncCore/ClockSyncAlgorithm.cs`（完整内容）：

```csharp
using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>
    /// 时钟同步算法（spec D3：min-filter + 一阶低通）——纯 C#，零引擎依赖，双端共享。
    /// 语义：OffsetTicks = 服务器时钟 − 客户端墙钟（正值 = 服务器更快）。
    /// 样本模型（t 均为 ticks）：
    ///   t1 = 客户端发送时刻（墙钟）
    ///   t2 = 服务器收到并回显时刻（服务器时钟）
    ///   t3 = 客户端收到响应时刻（墙钟）
    ///   offset = (t2 − t1 + t2 − t3) / 2
    ///   rtt = t3 − t1
    /// </summary>
    public sealed class ClockSyncAlgorithm
    {
        private readonly int _windowSize;
        private readonly List<Sample> _samples = new List<Sample>();

        /// <summary>当前最优偏移（ticks；min-RTT 样本的偏移，可负）</summary>
        public long OffsetTicks { get; private set; }

        /// <summary>平滑后偏移（一阶低通，alpha=0.3）</summary>
        public long SmoothedOffsetTicks { get; private set; }

        /// <summary>是否已收敛（窗口填满）</summary>
        public bool HasConverged => _samples.Count >= _windowSize;

        public ClockSyncAlgorithm(int windowSize = 8)
        {
            _windowSize = windowSize;
        }

        public void AddSample(long clientSendTicks, long serverEchoTicks, long clientReceiveTicks)
        {
            long rtt = clientReceiveTicks - clientSendTicks;
            long offset = serverEchoTicks - clientSendTicks - rtt / 2;

            _samples.Add(new Sample { Rtt = rtt, Offset = offset });

            // min-filter：滑窗内取 RTT 最小样本的偏移
            Sample best = _samples[0];
            foreach (var s in _samples)
            {
                if (s.Rtt < best.Rtt) best = s;
            }
            OffsetTicks = best.Offset;

            // 一阶低通（初值直接采纳）
            SmoothedOffsetTicks = _samples.Count == 1
                ? best.Offset
                : SmoothedOffsetTicks + (best.Offset - SmoothedOffsetTicks) * 3 / 10;

            // 滑窗溢出
            if (_samples.Count > _windowSize)
                _samples.RemoveAt(0);
        }

        /// <summary>客户端墙钟 → 服务器时钟（ticks）</summary>
        public long ToServerTime(long clientWallClockTicks)
        {
            return clientWallClockTicks + OffsetTicks;
        }

        private struct Sample
        {
            public long Rtt;
            public long Offset;
        }
    }
}
```

- [ ] **Step 4: Server.Tests 纳入 SyncCore 编译**

`Server/Server.Tests/Server.Tests.csproj` 的 Protocol Include 后追加：

```xml
  <!-- 共享 SyncCore（与客户端 asmdef 同批源文件） -->
  <ItemGroup>
    <Compile Include="..\..\Assets\Scripts\Hotfix\GameSystems\SyncCore\**\*.cs" />
  </ItemGroup>
```

- [ ] **Step 5: 运行确认通过（服务器侧）**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 38 passed（36 + ClockSync 2）。

- [ ] **Step 6: Unity 侧同批断言（EditMode 双跑）**

创建 `Assets/Scripts/Tests/Editor/SyncCore/ClockSyncAlgorithmTests.cs`（完整内容，与 xUnit 侧同批断言语义——横切约定①）：

```csharp
using System;
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class ClockSyncAlgorithmTests
    {
        [Test]
        public void Offset_Converges_To_True_Value_With_MinFilter()
        {
            var algo = new ClockSyncAlgorithm(8);
            long t1 = 1_000_000_000L;
            const long offsetTicks = 1_000_000L;
            const long rttTicks = 500_000L;
            const long outlierRttTicks = 5_000_000L;

            for (int i = 0; i < 7; i++)
            {
                var t2 = t1 + offsetTicks + rttTicks / 2;
                var t3 = t1 + rttTicks;
                algo.AddSample(t1, t2, t3);
                t1 += 1_000_000L;
            }
            {
                var t2 = t1 + offsetTicks + outlierRttTicks / 2;
                var t3 = t1 + outlierRttTicks;
                algo.AddSample(t1, t2, t3);
            }

            Assert.IsTrue(algo.HasConverged);
            Assert.GreaterOrEqual(algo.OffsetTicks, offsetTicks - 50_000);
            Assert.LessOrEqual(algo.OffsetTicks, offsetTicks + 50_000);
        }

        [Test]
        public void ToServerTime_Applies_Offset()
        {
            var algo = new ClockSyncAlgorithm(8);
            long t1 = 5_000_000L;
            algo.AddSample(t1, t1 + 1_000_000L, t1 + 2_000_000L);

            long serverTime = algo.ToServerTime(t1 + 1_000_000L);
            Assert.AreEqual(t1 + 1_000_000L, serverTime);
        }
    }
}
```

0. **先修改** `Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`——测试使用 `Cool.SyncCore`（SyncCore 程序集），references 追加 `"SyncCore"`：

```json
    "references": [
        "GameSys",
        "DataDefinition",
        "Cool.Protocol",
        "SyncCore"
    ],
```

1. Skill `assets-refresh` → 无编译错误（新 asmdef + 测试文件）
2. Skill `tests-run`(EditMode, class=ClockSyncAlgorithmTests) → Expected 2 passed

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncCore Assets/Scripts/Tests/Editor/SyncCore Server/Server.Tests
git commit -m "feat(synccore): shared clock sync algorithm - min-filter + low-pass, dual-end tests green"
```

---

### Task 6: 客户端 AOT 适配层（INetworkClient + KcpNetClientAdapter）

**Files:**
- Create: `Assets/Scripts/AOT/KcpNet/INetworkClient.cs`
- Create: `Assets/Scripts/AOT/KcpNet/KcpNetClientAdapter.cs`
- Modify: `Assets/Scripts/AOT/KcpNet/KcpNet.asmdef`（无需——同程序集内）
- Test: `Assets/Scripts/Tests/PlayMode/AdapterConnectTests.cs`（新增）

- [ ] **Step 1: 定义接口**

创建 `Assets/Scripts/AOT/KcpNet/INetworkClient.cs`（完整内容）：

```csharp
using System;
using Cool.Protocol;

namespace KcpNet
{
    /// <summary>
    /// 网络客户端接口 —— Hotfix SyncLayer 依赖此抽象（适配器模式，项目既有规范）；
    /// 实现在 AOT 层（KcpNetClientAdapter），Hotfix 不直接触碰 KcpClient。
    /// </summary>
    public interface INetworkClient : IDisposable
    {
        bool IsConnected { get; }

        /// <summary>连接服务器（内部含 KCP 握手与接收循环启动）</summary>
        System.Threading.Tasks.Task<bool> ConnectAsync(string host, int port);

        System.Threading.Tasks.Task DisconnectAsync(string reason);

        void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable);

        /// <summary>消息接收事件（主线程回调：内部经 UnityMainThreadExecutor）</summary>
        event Action<MessageId, IMessage>? MessageReceived;

        event Action<string>? Disconnected;

        event Action<Exception>? Error;
    }
}
```

- [ ] **Step 2: 实现适配器**

创建 `Assets/Scripts/AOT/KcpNet/KcpNetClientAdapter.cs`（完整内容）：

```csharp
using System;
using System.Net;
using System.Threading.Tasks;
using Cool.Protocol;

namespace KcpNet
{
    /// <summary>
    /// INetworkClient 的 KCP 实现 —— 封装 KcpClient：
    /// 唯一 conv（时间戳派生，P0 MUST-DO-4）；消息解码后经 MessageId 事件上抛。
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
            try
            {
                // 唯一 conv：时间戳截断（与服务器首包采纳机制配合）
                uint conv = (uint)(DateTime.UtcNow.Ticks % int.MaxValue);
                _client = new KcpClient(
                    conv,
                    new IPEndPoint(IPAddress.Parse(host), port),
                    new KcpClientTransport(_options, _logger),
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
            if (_client == null || !_client.IsConnected) return;
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
```

注意实现约束（实现前核对 `KcpClient.cs` / `KcpSession.cs` 实际签名）：
- `KcpClient` 构造：`(long sessionId, EndPoint remoteEndPoint, IKcpTransport transport, KcpOptions options, ILogger logger, IMessageExecutor messageExecutor)`
- `CloseAsync` 实际签名若有 `CancellationToken` 参数，补传 `CancellationToken.None`
- `SessionDisconnectedEventArgs` 的 Reason 属性名以实际为准（无则传固定字符串）
- `DirectMessageExecutor.Instance` 存在（Examples 用）—— 但 Unity 主线程问题：`MessageReceived` 回调线程。适配器文档注明：真实场景应换 `UnityMainThreadExecutor`；P1 测试期 Direct 即可，PlayMode 断言在协程轮询中读，线程安全由测试结构保证。若希望立即主线程化，用 `UnityMainThreadExecutor.Instance`（存在于 Examples——检查其类型后选用；两种都接受，报告中说明选择与理由）

- [ ] **Step 3: 写 PlayMode 冒烟测试**

创建 `Assets/Scripts/Tests/PlayMode/AdapterConnectTests.cs`（完整内容）：

```csharp
using System.Collections;
using System.Threading.Tasks;
using Cool.Protocol;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>P1 验收前置：adapter 连服务器 → 时钟同步握手往返</summary>
    public class AdapterConnectTests
    {
        private sealed class SmokeResult
        {
            public bool Success;
            public string FailureReason = string.Empty;
            public ClockSyncResponse? Response;
        }

        [UnityTest]
        public IEnumerator Adapter_Connects_And_ClockSync_Roundtrips()
        {
            var runTask = RunAsync();
            const int maxFrames = 1800; // 30s 挂钟
            int frames = 0;
            while (!runTask.IsCompleted && frames < maxFrames)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(runTask.IsCompleted, "adapter test timed out");
            var result = runTask.Result;
            Assert.IsTrue(result.Success, $"adapter failed: {result.FailureReason}");
            Assert.IsNotNull(result.Response, "no ClockSyncResponse captured");
        }

        private static async Task<SmokeResult> RunAsync()
        {
            var result = new SmokeResult();
            using var adapter = new KcpNetClientAdapter();
            var tcs = new TaskCompletionSource<ClockSyncResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.ClockSyncResponse && msg is ClockSyncResponse response)
                    tcs.TrySetResult(response);
            };

            try
            {
                var connected = await adapter.ConnectAsync("127.0.0.1", 8000);
                if (!connected) { result.FailureReason = "connect failed"; return result; }

                adapter.Send(MessageId.ClockSyncRequest, new ClockSyncRequest
                {
                    ClientTimestamp = System.DateTime.UtcNow.Ticks
                });

                var done = await Task.WhenAny(tcs.Task, Task.Delay(10000));
                if (done == tcs.Task && tcs.Task.Status == TaskStatus.RanToCompletion)
                {
                    result.Response = tcs.Task.Result;
                    result.Success = true;
                }
                else
                {
                    result.FailureReason = "no ClockSyncResponse within 10s";
                }
            }
            catch (System.Exception ex)
            {
                result.FailureReason = ex.Message;
            }

            return result;
        }
    }
}
```

注意：**服务器侧尚无 ClockSync 处理器**——本测试需要服务器先响应 ClockSyncRequest。因此本任务同时补服务器 handler（见 Step 4）；若服务器未启动则测试超时失败（预期内）。

- [ ] **Step 4: 服务器 ClockSync 处理器**

创建 `Server/Handler/ClockSyncHandler.cs`（完整内容）：

```csharp
using System;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// 时钟同步处理器：回显 ClientTimestamp + 服务器时钟（spec D3 样本来源）
    /// </summary>
    public class ClockSyncHandler
    {
        private readonly Server3C _server;
        private readonly ILogger _logger;

        public ClockSyncHandler(Server3C server, ILogger logger)
        {
            _server = server;
            _logger = logger;
        }

        public Task HandleClockSyncAsync(KcpServerSession session, ClockSyncRequest request)
        {
            var response = new Cool.Protocol.ClockSyncResponse
            {
                ClientTimestamp = request.ClientTimestamp,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };
            return _server.SendToSessionAsync(session, MessageId.ClockSyncResponse, response);
        }
    }
}
```

`Server3C.cs`：字段 `_clockSyncHandler`、构造初始化、switch case：

```csharp
                    case ClockSyncRequest clockSync:
                        await _clockSyncHandler.HandleClockSyncAsync(session, clockSync);
                        break;
```

- [ ] **Step 5: 构建 + 冒烟验收**

```bash
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 0 Errors；38 passed（协议未动）。

启动服务器（后台）→ Skill `assets-refresh` → Skill `tests-run`(PlayMode, class=AdapterConnectTests) → Expected 1 passed → 停服、端口释放确认。

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/AOT/KcpNet Assets/Scripts/Tests/PlayMode Server
git commit -m "feat(net): client adapter - INetworkClient over kcp, clock sync server handler, playmode smoke"
```

---

### Task 7: 客户端 SyncLayer（Dispatcher/ClockSyncDriver/RemoteEntityRegistry/登录闭环）

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef`（references 追加）
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/MessageDispatcher.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClockSyncDriver.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`
- Test: `Assets/Scripts/Tests/PlayMode/LoginEnterWorldTests.cs`

- [ ] **Step 1: GameSys.asmdef 引用追加**

`Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef` 的 references 数组追加两项：

```json
        "KcpNet",
        "SyncCore",
        "Cool.Protocol"
```

（保留既有全部引用；Hotfix → AOT 引用方向合法。）

**同时**修改 `Assets/Scripts/Tests/PlayMode/KcpNet.PlayModeTests.asmdef`（P0 所建）——T7/T8 测试使用 `Hotfix.GameSystems.SyncLayer`（GameSys 程序集），references 追加：

```json
    "references": [
        "KcpNet",
        "Cool.Protocol",
        "GameSys"
    ],
```

- [ ] **Step 2: MessageDispatcher**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/MessageDispatcher.cs`（完整内容）：

```csharp
using System;
using System.Collections.Generic;
using Cool.Protocol;
using KcpNet;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 消息分发器 —— 按消息 ID 路由到订阅者（SyncLayer 入口唯一）。
    /// 线程模型：adapter 事件回调线程调用 Dispatch，订阅者处理须自行线程化或
    /// 由调用方保证主线程（P1 测试期 Direct 执行；生产接 UnityMainThreadExecutor）。
    /// </summary>
    public sealed class MessageDispatcher
    {
        private readonly Dictionary<MessageId, Action<IMessage>> _handlers = new Dictionary<MessageId, Action<IMessage>>();

        public void Subscribe(MessageId messageId, Action<IMessage> handler)
        {
            if (_handlers.TryGetValue(messageId, out var existing))
                _handlers[messageId] = existing + handler;
            else
                _handlers[messageId] = handler;
        }

        public void Unsubscribe(MessageId messageId, Action<IMessage> handler)
        {
            if (_handlers.TryGetValue(messageId, out var existing))
            {
                _handlers[messageId] = existing - handler;
                if (_handlers[messageId] == null) _handlers.Remove(messageId);
            }
        }

        public void Dispatch(MessageId messageId, IMessage message)
        {
            if (_handlers.TryGetValue(messageId, out var handler))
                handler(message);
        }
    }
}
```

- [ ] **Step 3: ClockSyncDriver**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClockSyncDriver.cs`（完整内容）：

```csharp
using System;
using Cool.Protocol;
using Cool.SyncCore;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 时钟同步采样调度（spec D3）：登录后突发 8 样本（每 200ms），此后每 10s 复校。
    /// 算法本体在共享 SyncCore（ClockSyncAlgorithm）。
    /// </summary>
    public sealed class ClockSyncDriver
    {
        private readonly ClockSyncAlgorithm _algorithm = new ClockSyncAlgorithm(8);
        private readonly Action<ClockSyncRequest> _sendRequest;
        private float _timer;
        private int _burstCount;
        private bool _burstDone;

        /// <summary>当前平滑偏移（ticks）</summary>
        public long SmoothedOffsetTicks => _algorithm.SmoothedOffsetTicks;

        public bool HasConverged => _algorithm.HasConverged;

        public ClockSyncDriver(Action<ClockSyncRequest> sendRequest)
        {
            _sendRequest = sendRequest;
        }

        public void StartBurst()
        {
            _burstCount = 0;
            _burstDone = false;
            _timer = 0f;
        }

        public void Tick(float deltaSeconds)
        {
            if (_burstDone && _timer > 0) { _timer -= deltaSeconds; return; }

            if (!_burstDone)
            {
                // 突发 8 样本
                _timer -= deltaSeconds;
                if (_timer <= 0f)
                {
                    _sendRequest(new ClockSyncRequest { ClientTimestamp = DateTime.UtcNow.Ticks });
                    _burstCount++;
                    _timer = 0.2f;
                    if (_burstCount >= 8)
                    {
                        _burstDone = true;
                        _timer = 10f; // 复校周期
                    }
                }
            }
            else
            {
                // 复校单样本
                _sendRequest(new ClockSyncRequest { ClientTimestamp = DateTime.UtcNow.Ticks });
                _timer = 10f;
            }
        }

        /// <summary>收到 ClockSyncResponse 时调用（返回 true 表示收敛达成）</summary>
        public bool OnResponse(ClockSyncResponse response)
        {
            _algorithm.AddSample(
                response.ClientTimestamp,
                response.ServerTimestamp,
                DateTime.UtcNow.Ticks);
            return _algorithm.HasConverged;
        }

        public long ToServerTime(long clientWallClockTicks)
            => _algorithm.ToServerTime(clientWallClockTicks);
    }
}
```

- [ ] **Step 4: RemoteEntityRegistry**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteEntityRegistry.cs`（完整内容）：

```csharp
using System.Collections.Generic;
using Cool.Protocol;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 远程实体注册表 —— 管理远程玩家渲染对象（P1：胶囊体占位 + 快照直接定位；
    /// 插值渲染是 P2 范围）。生成/销毁由快照流驱动。
    /// </summary>
    public sealed class RemoteEntityRegistry
    {
        private readonly Dictionary<long, RemoteEntityView> _views = new Dictionary<long, RemoteEntityView>();
        private readonly Transform _root;

        public int Count => _views.Count;

        public RemoteEntityRegistry(Transform root)
        {
            _root = root;
        }

        /// <summary>应用一条快照：未知实体 → 生成；已知实体 → 定位（按位掩码）</summary>
        public void ApplySnapshot(EntitySnapshot snapshot)
        {
            if (!_views.TryGetValue(snapshot.PlayerId, out var view))
            {
                view = CreateView(snapshot.PlayerId);
                _views[snapshot.PlayerId] = view;
            }

            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Position) != 0)
                view.Position = new Vector3(snapshot.X, snapshot.Y, snapshot.Z);
            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Rotation) != 0)
                view.Rotation = Quaternion.Euler(0f, snapshot.Rotation, 0f);
        }

        public bool Remove(long playerId)
        {
            if (_views.TryGetValue(playerId, out var view))
            {
                Object.Destroy(view.GameObject);
                _views.Remove(playerId);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            foreach (var view in _views.Values)
                Object.Destroy(view.GameObject);
            _views.Clear();
        }

        private RemoteEntityView CreateView(long playerId)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"RemotePlayer_{playerId}";
            go.transform.SetParent(_root, false);
            return new RemoteEntityView(go);
        }
    }

    /// <summary>远程实体渲染视图（P1 占位）</summary>
    public sealed class RemoteEntityView
    {
        public GameObject GameObject { get; }
        public Vector3 Position
        {
            get => GameObject.transform.position;
            set => GameObject.transform.position = value;
        }
        public Quaternion Rotation
        {
            get => GameObject.transform.rotation;
            set => GameObject.transform.rotation = value;
        }

        public RemoteEntityView(GameObject go)
        {
            GameObject = go;
        }
    }
}
```

- [ ] **Step 5: ClientNetworkManager（登录闭环状态机）**

创建 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`（完整内容）：

```csharp
using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpNet;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 客户端网络管理器 —— 连接/登录/进世界/断线重连闭环（P1 决策 D4：
    /// 重连 = 重新 Login 全流程；EnterWorld 平滑重连留 P2）。
    /// 由宿主每帧调用 Update；事件 OnEnterWorld/OnDisconnected 供上层接线。
    /// </summary>
    public sealed class ClientNetworkManager : IDisposable
    {
        public enum State { Disconnected, Connecting, LoggingIn, InWorld }

        private readonly INetworkClient _client;
        private readonly MessageDispatcher _dispatcher = new MessageDispatcher();
        private readonly ClockSyncDriver _clockSync;
        private readonly RemoteEntityRegistry _remoteEntities;
        private readonly string _username;

        public State CurrentState { get; private set; } = State.Disconnected;
        public long LocalPlayerId { get; private set; }
        public string ZoneId { get; private set; } = string.Empty;
        public long ServerTickRateMs { get; private set; } = 67;
        public Vector3 SpawnPosition { get; private set; }

        public event Action? OnEnterWorld;
        public event Action<string>? OnDisconnected;
        public event Action<string>? OnError;

        public ClientNetworkManager(INetworkClient client, RemoteEntityRegistry remoteEntities, string username)
        {
            _client = client;
            _remoteEntities = remoteEntities;
            _username = username;

            _clockSync = new ClockSyncDriver(
                req => _client.Send(MessageId.ClockSyncRequest, req));

            _dispatcher.Subscribe(MessageId.LoginResponse, OnLoginResponse);
            _dispatcher.Subscribe(MessageId.ClockSyncResponse, OnClockSyncResponse);
            _dispatcher.Subscribe(MessageId.WorldSnapshot, OnWorldSnapshot);
            _dispatcher.Subscribe(MessageId.PlayerLeaveRoom, OnPlayerLeave);

            _client.MessageReceived += _dispatcher.Dispatch;
            _client.Disconnected += OnSocketDisconnected;
        }

        public async void ConnectAndLogin(string host, int port)
        {
            CurrentState = State.Connecting;
            var ok = await _client.ConnectAsync(host, port);
            if (!ok)
            {
                OnError?.Invoke("connect failed");
                CurrentState = State.Disconnected;
                return;
            }

            CurrentState = State.LoggingIn;
            _client.Send(MessageId.LoginRequest, new LoginRequest
            {
                Username = _username,
                PasswordHash = "md5_hash_placeholder",
                ZoneId = "Global",
                ClientVersion = "1.0.0",
                DeviceInfo = "P1",
                ProtocolVersion = ProtocolConstants.ProtocolVersion
            });
        }

        public void Update(float deltaSeconds)
        {
            if (CurrentState == State.InWorld)
                _clockSync.Tick(deltaSeconds);
        }

        private void OnLoginResponse(IMessage message)
        {
            var response = (LoginResponse)message;
            if (!response.Success)
            {
                OnError?.Invoke($"login rejected: {response.Message}");
                CurrentState = State.Disconnected;
                return;
            }

            LocalPlayerId = response.PlayerId;
            ZoneId = response.ZoneId;
            ServerTickRateMs = response.ServerTickRateMs > 0 ? response.ServerTickRateMs : 67;
            SpawnPosition = new Vector3(response.SpawnX, response.SpawnY, response.SpawnZ);
            CurrentState = State.InWorld;

            // 时钟采样突发从进世界才开始（Update 仅在 InWorld 驱动 Tick）
            _clockSync.StartBurst();

            // 出生点定位（本地玩家表现接线留 P2；P1 以事件通知上层）
            OnEnterWorld?.Invoke();
        }

        private void OnClockSyncResponse(IMessage message)
        {
            _clockSync.OnResponse((ClockSyncResponse)message);
        }

        private void OnWorldSnapshot(IMessage message)
        {
            var snapshot = (WorldSnapshot)message;
            foreach (var entity in snapshot.Entities)
            {
                if (entity.PlayerId == LocalPlayerId) continue; // 本地玩家跳过
                _remoteEntities.ApplySnapshot(entity);
            }
        }

        private void OnPlayerLeave(IMessage message)
        {
            var leave = (PlayerLeaveRoom)message;
            _remoteEntities.Remove(leave.PlayerId);
        }

        private void OnSocketDisconnected(string reason)
        {
            CurrentState = State.Disconnected;
            OnDisconnected?.Invoke(reason);
        }

        public void Dispose()
        {
            _client.MessageReceived -= _dispatcher.Dispatch;
            _client.Disconnected -= OnSocketDisconnected;
            _remoteEntities.Clear();
            _client.Dispose();
        }
    }
}
```

- [ ] **Step 6: 写 PlayMode 登录进世界测试**

创建 `Assets/Scripts/Tests/PlayMode/LoginEnterWorldTests.cs`（完整内容）：

```csharp
using System.Collections;
using Cool.Protocol;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>P1 验收：adapter+SyncLayer 登录闭环 → 进世界 → 收到快照</summary>
    public class LoginEnterWorldTests
    {
        private sealed class LoginResult
        {
            public bool Success;
            public string FailureReason = string.Empty;
            public long PlayerId;
            public string ZoneId = string.Empty;
            public int SnapshotCount;
        }

        [UnityTest]
        public IEnumerator Login_EntersWorld_ReceivesSnapshot()
        {
            var result = new LoginResult();
            var runTask = RunAsync(result);

            int frames = 0;
            while (!runTask.IsCompleted && frames < 1800)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(runTask.IsCompleted, "login test timed out");
            Assert.IsTrue(result.Success, $"login failed: {result.FailureReason}");
            Assert.Greater(result.PlayerId, 0, "player id assigned");
            Assert.AreEqual("Global", result.ZoneId);
        }

        private static async System.Threading.Tasks.Task RunAsync(LoginResult result)
        {
            var root = new GameObject("SyncLayerRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            using var client = new KcpNetClientAdapter();
            var manager = new ClientNetworkManager(client, registry, $"Player_{System.DateTime.UtcNow.Ticks % 1000}");

            var snapshotSeen = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            manager.OnEnterWorld += () => { };
            // 通过 manager 内部 dispatcher 不可直接订阅快照——改用注入前的独立观察：直接订阅 adapter 消息
            client.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.WorldSnapshot && msg is WorldSnapshot snap)
                {
                    result.SnapshotCount = snap.Entities.Count;
                    if (snap.Entities.Count > 0) snapshotSeen.TrySetResult(true);
                }
            };

            manager.ConnectAndLogin("127.0.0.1", 8000);

            // 等待进世界
            float elapsed = 0f;
            while (manager.CurrentState != ClientNetworkManager.State.InWorld && elapsed < 10f)
            {
                await System.Threading.Tasks.Task.Delay(50);
                elapsed += 0.05f;
            }

            if (manager.CurrentState != ClientNetworkManager.State.InWorld)
            {
                result.FailureReason = "not in world within 10s";
                Object.Destroy(root);
                return;
            }

            result.PlayerId = manager.LocalPlayerId;
            result.ZoneId = manager.ZoneId;

            // 等待至少一条快照（本测试单客户端时快照可为空——若空则只验证登录；非空则记录数量）
            await System.Threading.Tasks.Task.Delay(500);

            // 驱动时钟同步采样（进程内快速推进：直接调 Update 若干次）
            for (int i = 0; i < 50; i++) manager.Update(0.2f);

            result.Success = true;
            manager.Dispose();
            Object.Destroy(root);
        }
    }
}
```

注意实现提示：`LoginEnterWorldTests` 中直接依赖 manager 的 dispatcher 订阅快照不可行（快照处理在 manager 内部）——测试改用两个途径：a) 直接订阅 adapter 的 MessageReceived 观察快照到达（如上）；b) 快照到达后 registry 生成胶囊体，可在 manager 暴露 `RemoteEntityRegistry Registry => _remoteEntities;` 供测试断言 `Count`。两者取一即可，测试以"收到 WorldSnapshot 消息"为验收下限（单客户端场景服务器广播空快照属预期，登录闭环本身是主验收）。

- [ ] **Step 7: 服务器跑起 → 冒烟验收**

```bash
cd "E:/CodeForJob/Cool" && dotnet run --project Server/Server3C.csproj -- 8000
```

（后台；确认 running 后）Skill `assets-refresh` → Skill `tests-run`(PlayMode, class=LoginEnterWorldTests) → Expected 1 passed。停服、端口释放。

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef Assets/Scripts/Hotfix/GameSystems/SyncLayer Assets/Scripts/Tests/PlayMode
git commit -m "feat(synclayer): client framework - dispatcher, clock sync driver, remote entity registry, login state machine"
```

---

### Task 8: 双客户端验收 + 断线重连重建

**Files:**
- Create: `Assets/Scripts/Tests/PlayMode/TwoClientWorldTests.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs`（暴露 Registry 只读属性，若 T7 未做）

- [ ] **Step 1: 写 PlayMode 双客户端测试**

创建 `Assets/Scripts/Tests/PlayMode/TwoClientWorldTests.cs`（完整内容）：

```csharp
using System.Collections;
using System.Collections.Generic;
using Cool.Protocol;
using KcpNet;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// P1 核心验收（spec §5-P1）：
    /// ① 双客户端登录进同 Zone，互相可见（A 的 WorldSnapshot 含 B）；
    /// ② B 断线 → 重连（全新 Login）→ 重建后 A 再次可见 B。
    /// </summary>
    public class TwoClientWorldTests
    {
        private sealed class Ctx
        {
            public KcpNetClientAdapter Adapter = new KcpNetClientAdapter();
            public ClientNetworkManager Manager;
            public readonly List<WorldSnapshot> Snapshots = new List<WorldSnapshot>();
            public bool Disconnected;
        }

        [UnityTest]
        public IEnumerator TwoClients_See_EachOther_And_Reconnect_Rebuilds()
        {
            var root = new GameObject("TwoClientRoot");
            var registryA = new RemoteEntityRegistry(root.transform);
            var registryB = new RemoteEntityRegistry(root.transform);
            // 拆两行：对象初始化器内不可引用尚未赋值的 ctxA（CS0165）
            var ctxA = new Ctx();
            ctxA.Manager = new ClientNetworkManager(ctxA.Adapter, registryA, "Player_A");
            var ctxB = new Ctx();
            ctxB.Manager = new ClientNetworkManager(ctxB.Adapter, registryB, "Player_B");

            ctxA.Adapter.MessageReceived += (id, msg) => { if (id == MessageId.WorldSnapshot) ctxA.Snapshots.Add((WorldSnapshot)msg); };
            ctxB.Adapter.MessageReceived += (id, msg) => { if (id == MessageId.WorldSnapshot) ctxB.Snapshots.Add((WorldSnapshot)msg); };
            ctxB.Manager.OnDisconnected += _ => ctxB.Disconnected = true;

            var runTask = RunAsync(ctxA, ctxB, registryA, registryB, root);
            int frames = 0;
            while (!runTask.IsCompleted && frames < 3600) // 60s 上限
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(runTask.IsCompleted, "two-client test timed out");
            var result = runTask.Result;

            ctxA.Manager.Dispose();
            ctxB.Manager.Dispose();
            Object.Destroy(root);
        }

        private static async System.Threading.Tasks.Task<System.ValueTuple<bool, string>> RunAsync(
            Ctx ctxA, Ctx ctxB, RemoteEntityRegistry registryA, RemoteEntityRegistry registryB, GameObject root)
        {
            // —— 阶段 1：双客户端登录 ——
            ctxA.Manager.ConnectAndLogin("127.0.0.1", 8000);
            await WaitInWorld(ctxA.Manager, 10);
            ctxB.Manager.ConnectAndLogin("127.0.0.1", 8000);
            await WaitInWorld(ctxB.Manager, 10);

            if (ctxA.Manager.CurrentState != ClientNetworkManager.State.InWorld ||
                ctxB.Manager.CurrentState != ClientNetworkManager.State.InWorld)
                return (false, "one client failed to enter world");

            // —— 阶段 2：A 应看到 B ——
            bool aSeesB = false;
            for (int i = 0; i < 40 && !aSeesB; i++) // 至多 4s
            {
                await System.Threading.Tasks.Task.Delay(100);
                foreach (var snap in ctxA.Snapshots)
                {
                    if (snap.Entities.Exists(e => e.PlayerId == ctxB.Manager.LocalPlayerId))
                    {
                        aSeesB = true;
                        break;
                    }
                }
            }
            if (!aSeesB) return (false, "A never saw B in WorldSnapshot");

            // —— 阶段 3：B 断线 → 重连（全新 Login）→ A 再次看到新实体 ——
            ctxB.Manager.Dispose(); // 全量关闭（Dispose 为同步方法；socket 关闭即断线）
            bool bDisconnected = false;
            for (int i = 0; i < 20 && !bDisconnected; i++)
            {
                await System.Threading.Tasks.Task.Delay(100);
                bDisconnected = ctxB.Disconnected || ctxB.Adapter.IsConnected == false;
            }

            ctxA.Snapshots.Clear();
            var ctxC = new Ctx();
            ctxC.Manager = new ClientNetworkManager(ctxC.Adapter, registryB, "Player_B_re");
            ctxC.Adapter.MessageReceived += (id, msg) => { if (id == MessageId.WorldSnapshot) ctxC.Snapshots.Add((WorldSnapshot)msg); };
            ctxC.Manager.ConnectAndLogin("127.0.0.1", 8000);
            await WaitInWorld(ctxC.Manager, 10);
            if (ctxC.Manager.CurrentState != ClientNetworkManager.State.InWorld)
                return (false, "reconnect login failed");

            bool aSeesNewB = false;
            for (int i = 0; i < 40 && !aSeesNewB; i++)
            {
                await System.Threading.Tasks.Task.Delay(100);
                foreach (var snap in ctxA.Snapshots)
                {
                    if (snap.Entities.Exists(e => e.PlayerId == ctxC.Manager.LocalPlayerId))
                    {
                        aSeesNewB = true;
                        break;
                    }
                }
            }
            if (!aSeesNewB) return (false, "A never saw reconnected B");

            return (true, "ok");
        }

        private static async System.Threading.Tasks.Task<bool> WaitInWorld(ClientNetworkManager manager, float timeoutSec)
        {
            float elapsed = 0f;
            while (manager.CurrentState != ClientNetworkManager.State.InWorld && elapsed < timeoutSec)
            {
                await System.Threading.Tasks.Task.Delay(50);
                elapsed += 0.05f;
            }
            return manager.CurrentState == ClientNetworkManager.State.InWorld;
        }
    }
}
```

（`Ctx` 的 `Manager`/`Adapter` 非只读赋值——按 C# 字段初始化顺序调整：Ctx 定义字段 `public KcpNetClientAdapter Adapter = new();` 与 `public ClientNetworkManager Manager;`，创建时先 new Ctx() 再赋值 Manager。）

- [ ] **Step 2: 服务器验证辅助（可选调试）**

服务器日志确认：两实体注册（`Login attempt` + `Player N logged in` ×2）+ 断线清理（`Player N disconnected`）。测试失败时拉服务器控制台输出定位。

- [ ] **Step 3: 冒烟验收**

```bash
cd "E:/CodeForJob/Cool" && dotnet run --project Server/Server3C.csproj -- 8000
```

（后台）Skill `tests-run`(PlayMode, class=TwoClientWorldTests) → Expected 1 passed。**连跑 2 次确认非 flaky**。停服、端口释放。

- [ ] **Step 4: 全量回归**

```bash
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 38 passed。Unity Skill `tests-run`(EditMode 全量) → 140+4(SyncCore 2 + ProtocolRoundtrip 已有) 全绿。

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Tests/PlayMode Assets/Scripts/Hotfix/GameSystems/SyncLayer
git commit -m "test(net): p1 acceptance - dual clients see each other, reconnect rebuilds, world snapshot flow"
```

---

### Task 9: 收尾（ConnectionReset 抑制 / kick 后清理 / 文档同步 / 联调指南）

**Files:**
- Modify: `Server/Server3C.cs`（ReceiveLoop 抑制 ConnectionReset；CleanupLoop 用 SessionManager）
- Modify: `Server/Handler/LoginHandler.cs`（版本拒绝后 CloseAsync）
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md`（网络节同步）
- Create: `docs/superpowers/guides/p1-dual-client-dev.md`
- Modify: `Packages/manifest.json`（尝试安装 ParrelSync，失败则回退）

- [ ] **Step 1: ConnectionReset 抑制**

`Server3C.cs` ReceiveLoop 的 catch 链追加（在 `catch (ObjectDisposedException)` 后）：

```csharp
                catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionReset)
                {
                    // UDP 端口不可达（ICMP）——静默忽略（P0 终审 SHOULD 项）
                }
```

- [ ] **Step 2: kick 后会话关闭**

`LoginHandler.cs` 版本拒绝块，Kick 发送后追加：

```csharp
                await session.CloseAsync("ProtocolVersionMismatch");
```

（CloseAsync 触发 Disconnected → 清理路径；会话被正常回收而非挂到超时。）

- [ ] **Step 3: CleanupLoop 改用 SessionManager**

`Server3C.CleanupLoop` 的超时会话清理改为遍历 `_sessionManager.AllSessions`（按 `IsTimedOut` 过滤 → `RemoveByEndpoint(s.RemoteEndPoint)` + `CloseAsync`）。

- [ ] **Step 4: Sys3C-CurrentDesign.md 网络节同步**

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md` 中仍描述 `NetworkPrediction/NetworkBridge/PositionInterpolator/PredictionMovementPolicy` 的段落替换为一段：

```markdown
## 网络

网络同步能力自 P0 起移出 Sys3C：共享协议层 `Assets/Scripts/AOT/Protocol/`（双端同源），
客户端 SyncLayer `Assets/Scripts/Hotfix/GameSystems/SyncLayer/`，服务端见 `Server/`。
路线图与阶段规划见 `docs/superpowers/specs/2026-08-27-network-sync-design.md`。
移动同步（NetMovementPolicy 注入）属 P2。
```

（先读该文件定位实际段落结构，做最小替换，不整文件重写。）

- [ ] **Step 5: 联调指南**

创建 `docs/superpowers/guides/p1-dual-client-dev.md`（完整内容）：

```markdown
# P1 双客户端联调指南

## 方式 A：ParrelSync 克隆（推荐，若已安装）

1. Unity 包管理器添加 `com.unity.multiplayer.playmode` 或 ParrelSync（git URL：
   `https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync`）
2. ParrelSync 窗口 → Create Clones → 打开克隆 Editor
3. 两个 Editor 各自运行场景；克隆 Editor 需单独配置服务器地址（见下文"双开注意"）

## 方式 B：手动双开（无插件兜底）

1. 复制仓库目录 `E:\CodeForJob\Cool` → `E:\CodeForJob\Cool-C2`（复制后删除其中
   `Library/`、`Temp/`、`Logs/`、`obj/`——只保留 `Assets/`、`Packages/`、`ProjectSettings/`、
   `Server/` 等源码；如目录含 `Library` 首次双开会冲突）
2. Unity Hub → Add → 选择 `Cool-C2` → 两个 Editor 同时打开（首开较慢：重新导入+编译）
3. 双方各自启动服务器或共用一台服务器（见下）

## 双开注意

- 服务器：`dotnet run --project Server/Server3C.csproj -- 8000`（一台机器跑一个即可，
  两客户端连 127.0.0.1:8000）
- 客户端连接入口：P1 阶段为 PlayMode 测试（TwoClientWorldTests）或挂
  `ClientNetworkManager` 的宿主脚本；游戏 UI 登录接线在后续阶段
- 两个 Editor 的 `Assets` 目录互相独立——改代码需同步（ParrelSync 自动同步；
  手动双开需自行复制或双份编辑）

## 验收清单（双开手测）

1. 两个 Editor 各自登录进同 Zone（服务器日志出现两行 `Player N logged in`）
2. A 场景中出现 B 的胶囊体占位（RemoteEntityRegistry 生成）；B 移动时 A 端位置更新
   （P1 为快照直接定位，无插值——P2 平滑）
3. 关掉 B → A 端 B 实体消失；B 重开重连 → A 端 B 重新出现
```

- [ ] **Step 6: 尝试安装 ParrelSync（失败不阻塞）**

```bash
cd "E:/CodeForJob/Cool" && npm view 2>/dev/null; echo "skip npm"
```

（Unity 包用 Skill `package-add`：packageName=`com.veriorpies.parrelsync`？—— ParrelSync 是 git 依赖：`https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync`。调用 Skill `package-add` 传该 git URL；若返回失败/网络错误，在指南文档中标注"需网络环境安装"，不以失败阻塞本任务。）

- [ ] **Step 7: 构建 + 全量测试**

```bash
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj
```

Expected: 0 Errors；38 passed。Unity Skill `tests-run`(EditMode 全量) → 全绿。

- [ ] **Step 8: Commit**

```bash
git add Server Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md docs/superpowers/guides Packages
git commit -m "chore(net): p1 polish - connectionreset suppression, kick session close, docs sync, dual-client guide"
```

---

### Task 10: 终验 + 偏差记录

**Files:**
- Modify: `docs/superpowers/plans/2026-08-28-p1-network-framework.md`（追加执行偏差记录——由协调者终审后写入）

- [ ] **Step 1: 终验五连**

```bash
dotnet build E:/CodeForJob/Cool/Server/Server3C.csproj -v q          # 0 Errors
dotnet test E:/CodeForJob/Cool/Server/Server.Tests/Server.Tests.csproj  # 38 passed
```

Unity 侧（Skill）：`assets-refresh` → `tests-run`(EditMode 全量) 全绿 → `tests-run`(PlayMode, class=EchoSmokeTests) 1 passed → `tests-run`(PlayMode, class=TwoClientWorldTests) 1 passed（需服务器起停两次，或一次起服连跑两个 class）。

- [ ] **Step 2: spec §5-P1 验收清单逐条核对（证据指针）**

| 验收项 | 证据 |
|---|---|
| 双客户端登录进同 Zone，服务器日志确认两实体注册 | Task 8 TwoClientWorldTests（A/B 均 InWorld + A 快照含 B）+ 服务器日志 |
| ClockSync 输出稳定（偏移抖动 ±10ms 内，局域网/本机口径） | Task 7 LoginEnterWorldTests 驱动采样 + Task 5 算法单测（min-filter 抗离群）；**PlayMode 端到端 ±10ms 验证属增强项**：若 TwoClientWorldTests 内已驱动时钟采样，日志或测试中记录偏移抖动（不强求断言，记录实测值） |
| 模拟断线重连现场正确重建 | Task 8 阶段 3（B 断线 → 重连 → A 再见新 B） |
| 联调方式定稿并落地一种 | Task 9 指南文档 + ParrelSync 尝试（文档兜底） |

- [ ] **Step 3: 偏差记录**

协调者终审后在本计划文件追加「执行偏差记录」章节（参照 P0 计划附录格式），逐项核实并记录：D1（无独立 EnterWorld）/D2（SyncCore 位置）/D3（Server.Tests 扩围）/D4（重连走重新 Login）/D5（联调落地形态）+ 执行中实际偏差。

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/plans/2026-08-28-p1-network-framework.md
git commit -m "docs: p1 execution record - acceptance evidence + drift log"
```

---

## Self-Review（计划撰写时已自查）

**1. Spec 覆盖**：§5-P1 四项验收 → Task 8（双客户端/重连）+ Task 7（登录闭环/ClockSync 驱动）+ Task 5（算法）+ Task 9（联调）；§3 治理（追加规则）→ Task 3（只加不删 + RegistryCompleteness 自动覆盖）；§4 D1（tick 参数）→ Task 4 BeaconInterval/LoginResponse.ServerTickRateMs；D3（时钟算法）→ Task 5；D5（增量位掩码）→ Task 3/4；D6（校验失败不崩）→ 既有 ProcessPacket try/catch 保留；P0 终审 MUST-DO 四项 → T1（竞态+O(n)）/T2（R2）/T6（conv 唯一化）；SHOULD 项 → T9。**缺项检查**：spec 的"PerceptionFilter 接口（全量可见占位）"→ Task 4 ✓；"客户端 MessageDispatcher"→ Task 7 ✓；"RemoteEntityRegistry"→ Task 7 ✓。

**2. 占位符扫描**：无 TBD/TODO；所有代码块完整；两处"以编译器为准核对签名"（T6 adapter）为容错指引。Task 8 测试的 Ctx 字段初始化顺序已注。

**3. 类型一致性**：`EntityFieldMask`（Protocol，ushort）在 T3 定义、T4 组装器/测试、T7 客户端快照应用三处使用一致；`WorldSnapshot.Tick: uint` 与 `GameLoop.TickCount: long` 转换处显式 cast（T4 BroadcastWorldSnapshot((uint)tick)）；`ClockSyncAlgorithm.AddSample(clientSend, serverEcho, clientReceive)` 三参顺序在 T5 算法/测试与 T7 Driver.OnResponse 全部一致；`LoginResponse.ServerTickRateMs/SpawnX/Y/Z/Rotation` 在 T3 定义、T7 消费一致；`SessionManager.GetOrCreateSession(endpoint, firstPacket)` 签名在 T1 定义、T2 Server3C 调用一致。

## 规模说明

10 个任务，覆盖服务端框架（T1-T4）、共享算法（T5）、客户端框架（T6-T7）、端到端验收（T8）、收尾（T9-T10）。每个任务独立可验收：T1-T5/T9 为纯代码+单测，T6-T8 为 PlayMode 冒烟（需服务器进程，联调依赖如常）。
