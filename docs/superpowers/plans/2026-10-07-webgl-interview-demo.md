# WebGL 面试演示版 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 让面试官用自己的设备打开一个 URL 就能玩到本项目，并能现场复现 P6 单机玩法、P4 服务端权威战斗、P3 AOI（`--bots 100`）、P5 怪物 AI 四条声称面。

**架构：** 先解除 WebGL 的构建阻塞（`System.Net.Sockets` 在 WebGL 不存在，而 `KcpNet` 有 6 个文件依赖它）→ 新增一条 WebSocket 传输通道（客户端 `INetworkClient` 新实现 + 服务端 `IServerSession` 抽取与 WS 双栈），**并把 PC 也切到这条通道**（D-W11：两端共用同一个 `WebSocketClientAdapter`，只在最底层 channel 上按平台分叉），业务与同步逻辑零改动 → 补移动端触屏输入 → 在浏览器形态下复跑声称面验收 → 构建、部署、录屏。

**技术栈：** Unity 2022.3.25f1（WebGL + IL2CPP）、浏览器 WebSocket API（经 `.jslib`）、.NET 8 服务端（`System.Net.WebSockets`，与既有 UDP/KCP 同进程双栈）、protobuf-net（既有 `MessageCodec` 复用）。

---

## 决策记录（执行时不变更；偏差须在 Task 10 记录）

| # | 决策 | 内容 |
|---|------|------|
| **D-W1** | 优先 **WebGL 网页版**，微信小游戏延后 | 两者技术代价几乎相同（同一套 WSS 通道），但小游戏额外需要微信账号 + 类目审核 + 域名备案——不可控的非技术依赖。网页版只要一个域名 |
| **D-W2** | `INetworkClient` **搬到 `Cool.Protocol`**（成员签名一字不改）；**但 `KcpNet.asmdef` 不排除 WebGL、`GameSys` 保留对 `KcpNet` 的引用**——改为 6 个 socket 文件加 `#if !UNITY_WEBGL`，`ClientNetworkHost` 用 `#if UNITY_WEBGL` 选 **channel**（adapter 两端统一，见 D-W11） | 写计划时实测推翻了"排除程序集 + 移除引用"：asmdef 引用**不可平台条件化**，移除后 PC 端也看不到 `KcpNetClientAdapter`；而平台专属 provider 程序集需要场景注入，但 `DemoDay` 是 PC/WebGL **共享**场景。**代价**：`KcpNet` 在 WebGL 下是空壳程序集（体积/编译时间，非正确性）。**收益**：零场景改动、零注册表、零 link.xml |
| **D-W3** | 服务端**同进程双栈**（UDP/KCP + WS） | `IServerSession` 抽取是 WS 路径本来就要做的，双栈几乎白送；且让"PC 端 + 浏览器端同场"成为 AOI/战斗的活证据 |
| **D-W4** | **不做包体优化，也不测量首屏加载**（人类裁定） | 先落地 WebGL 形态；后期清理未使用资源后再优化。**代价**：后期优化没有 before 数可对照，且首屏加载在面试前始终是不可估的未知数 |
| **D-W5** | 移动端输入**复用既有 `IInputAdapter` 抽象**，但**重写实现** | 抽象（5 成员）可复用、`KeyboardInputAdapter` 不受影响；`JoystickInputAdapter` 读的三个 legacy 轴**不存在**（实测），调用即抛异常，且 `normalized` 丢弃模拟量。T6 要建真实 UI 摇杆驱动适配器——**`IInputAdapter` 契约不变** |
| **D-W6** | WebGL 的验证**分层**：逻辑进 EditMode，jslib/浏览器留人工 | 承认 WebGL 不可自动化，但把可测的部分最大化 |
| **D-W7** | 演示形态下**不做鉴权** | 人类已接受：公网暴露意味着任何人知道 URL 即可连，风险在规格 R3 显式登记 |
| **D-W8** | `WebSocketClientAdapter` 内部再分一层 `IWebSocketChannel` | WebGL 必须走 jslib（浏览器 API），而**编辑器里 `ClientWebSocket` 可用**——分层后适配器逻辑可在 EditMode 单测，只有 jslib 与浏览器运行时留人工（D-W6 的落点） |
| **D-W9** | WS 上**一条消息 = 一个 WS 帧**，直接复用 `MessageCodec.Encode`/`TryDecode` | `MessageCodec` 完全与传输解耦（规格 F2）；WS 本身是消息边界的，无需再做流式拆帧 |
| **D-W10** | 应用**从未使用** KCP 的非可靠通道（全部 `MessageFlags.Reliable`） | 所以 WebSocket(TCP) 的语义与现有实际行为**一致**，不存在"换 TCP 反而变差"的落差（规格 F3） |
| **D-W11** | **两端统一走 WebSocket**：`WebSocketClientAdapter` 是 PC 与 WebGL **唯一**的客户端传输；`KcpNetClientAdapter` 保留为第二实现但**不再是默认**（人类裁定："保留原有 kcp 代码，以后再删"）。落点在 T4 Step 4 | **收益**：F3 表明 KCP 的不可靠通道是死代码，让它继续当 PC 默认是零收益；统一后 **PC 自我验证与浏览器演示走同一条代码路径**——旧形态下"在 PC 上验的每一件事，走的都是面试官那边不存在的那条路"。平台分叉从"两条传输"降级为"两个 channel"。<br>**代价**：master 的 PC 传输与 P6 冻结包所用的 `KcpNetClientAdapter` 不再一致（冻结包不随 master 演进，接受）。<br>**不影响 PlayMode**：9 个 PlayMode 测试全部**直接构造 `KcpNetClientAdapter`**，不经 `ClientNetworkHost`（已实测）——故 18/18 基线与 KCP 传输测试完全不受影响 |

**Out of scope（显式排除，防范围蔓延）**：微信小游戏、P7 打怪升级、P8 PvP、P2 移动同步的现场演示、原生 Android/iOS 打包、包体优化与首屏加载测量、鉴权、聊天/背包/组队/交易、`Launched/Down/GetUp/Death` 的表现层补齐（P6 已登记 backlog）。

**验收目标（规格 §1）**：① 面试官用**自己的设备**打开 URL 就能玩；② P6 单机 / P4 / P3（`--bots 100`）/ P5 四条**在浏览器里现场可复现**；③ 无崩溃、无卡死、无需刷新；④ 有录屏兜底；⑤ 有"已知边界"话术卡。

**回归基线（每任务后必须保持）**：Server.Tests **132/132** · EditMode **222/222** · PlayMode（三配置并集）**18/18** · PC 包照常可出。

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Assets/Scripts/AOT/Protocol/INetworkClient.cs` | **移动**（自 `AOT/KcpNet/`） | 传输抽象，命名空间改 `Cool.Protocol`；供 KCP 与 WS 两个实现共用 |
| `Assets/Scripts/AOT/KcpNet/Client/KcpClientTransport.cs` | 修改 | 加 `#if !UNITY_WEBGL` |
| `Assets/Scripts/AOT/KcpNet/Common/KcpTransportBase.cs` | 修改 | 同上 |
| `Assets/Scripts/AOT/KcpNet/Common/Kcp.cs` | 修改 | 同上（KCP 算法本体依赖 `System.Net.Sockets.Kcp`） |
| `Assets/Scripts/AOT/KcpNet/Server/KcpListenerWrapper.cs` | 修改 | 同上 |
| `Assets/Scripts/AOT/KcpNet/Server/KcpServer.cs` | 修改 | 同上 |
| `Assets/Scripts/AOT/KcpNet/Server/KcpServerTransport.cs` | 修改 | 同上 |
| `Assets/Scripts/AOT/KcpNet/KcpNetClientAdapter.cs` | 修改 | `#if !UNITY_WEBGL` 包住（它传递依赖上面三个客户端文件） |
| `Assets/Scripts/AOT/WebSocketNet/WebSocketNet.asmdef` | 创建 | 新程序集，**所有平台**（WebGL 要编译它） |
| `Assets/Scripts/AOT/WebSocketNet/IWebSocketChannel.cs` | 创建 | D-W8 的通道抽象 |
| `Assets/Scripts/AOT/WebSocketNet/DotNetWebSocketChannel.cs` | 创建 | `ClientWebSocket` 实现（编辑器/原生） |
| `Assets/Scripts/AOT/WebSocketNet/WebGLWebSocketChannel.cs` | 创建 | jslib 实现（仅 WebGL） |
| `Assets/Scripts/AOT/WebSocketNet/WebSocketClientAdapter.cs` | 创建 | `INetworkClient` 的 WS 实现 |
| `Assets/Plugins/WebGL/WebGLWebSocket.jslib` | 创建 | C# ↔ 浏览器 WebSocket 的唯一 JS 边界 |
| `Assets/Scripts/Tests/Editor/WebSocketNet/WebSocketClientAdapterTests.cs` | 创建 | 适配器行为单测（经 `FakeChannel`，非真 socket——真实往返由 T4/T5 覆盖） |
| `Server/IServerSession.cs` | 创建 | 业务真正需要的会话契约 |
| `Server/KcpServerSession.cs` | 修改 | `: IServerSession` |
| `Server/WebSocketServerSession.cs` | 创建 | `System.Net.WebSockets` 会话 |
| `Server/WebSocketServerListener.cs` | 创建 | WS 监听 → 建会话 → 喂 `ProcessPacket` |
| `Server/Server3C.cs` | 修改 | 8 处 `KcpServerSession` → `IServerSession` |
| `Server/Session/SessionManager.cs` | 修改 | 同上（全部） |
| `Server/Program.cs` | 修改 | 启动 WS 监听（与 UDP 并存） |
| `Server/Server.Tests/**` | 修改 | 既有测试改用接口；新增 WS 会话单测 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/JoystickInputAdapter.cs` | **重写** | 读 UI 摇杆，保留模拟量 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs` | 创建 | 按平台注入适配器 |
| 触屏 UI（摇杆/按钮） | 创建 | UGUI 组件 + 场景挂载 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/TouchCameraInput.cs` | 创建 | 触屏拖拽 → `HandleRotationInput` |
| `Assets/Scripts/Editor/WebGLBuildScript.cs` | 创建 | WebGL 构建入口（batchmode 可调） |
| `docs/superpowers/guides/webgl-demo-runbook.md` | 创建 | 部署步骤 + 已知边界话术卡 |

---

### Task 1: 解除 WebGL 构建阻塞

**Files:**
- 移动：`Assets/Scripts/AOT/KcpNet/INetworkClient.cs` → `Assets/Scripts/AOT/Protocol/INetworkClient.cs`（含 `.meta`）
- 修改：`Assets/Scripts/AOT/KcpNet/` 下 7 个文件（6 个 socket 文件 + `KcpNetClientAdapter.cs`）
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/` 下 5 个文件的 `using`
- 创建：`Assets/Scripts/AOT/WebSocketNet/`（asmdef + 占位适配器）
- 测试：无新增（本任务由**编译与构建**验证）

**为什么这是硬门槛**：不做完，WebGL 打不出包，后面 9 个任务全部无法验证。

- [ ] **Step 1: 移动接口文件并改命名空间**

用 `git mv` 移动（保留 `.meta` 与 GUID）：

```bash
git mv Assets/Scripts/AOT/KcpNet/INetworkClient.cs Assets/Scripts/AOT/Protocol/INetworkClient.cs
git mv Assets/Scripts/AOT/KcpNet/INetworkClient.cs.meta Assets/Scripts/AOT/Protocol/INetworkClient.cs.meta
```

把文件里的 `namespace KcpNet` 改为 `namespace Cool.Protocol`。**文件内容其余部分一行不动**——7 个成员与签名保持完全一致（这是本任务最重要的约束）。

同时删除文件顶部的 `using Cool.Protocol;`（自己就在这个命名空间里了）。

- [ ] **Step 2: 逐一修正 `using`（已预先核实，照此改即可）**

| 文件 | 改动 |
|---|---|
| `Hotfix/.../SyncLayer/ClientNetworkHost.cs` | **保留** `using KcpNet;`（此刻仍需 `KcpNetClientAdapter`；**T4 Step 4 统一到 WS 后即失效，届时删除**）；**新增** `using Cool.Protocol;`（字段要用 `INetworkClient`） |
| `Hotfix/.../SyncLayer/ClientNetworkManager.cs` | **删除** `using KcpNet;`（已 `using Cool.Protocol`） |
| `Hotfix/.../SyncLayer/CombatNetworkBridge.cs` | **删除** `using KcpNet;`（已 `using Cool.Protocol`） |
| `Hotfix/.../SyncLayer/MessageDispatcher.cs` | **删除** `using KcpNet;`（已 `using Cool.Protocol`） |
| `Hotfix/.../SyncLayer/MovementReporter.cs` | **删除** `using KcpNet;`（已 `using Cool.Protocol`） |
| `Tests/Editor/SyncLayer/CombatNetworkBridgeTests.cs` | **删除** `using KcpNet;`（`:8`；已 `using Cool.Protocol`） |

- [ ] **Step 3: 给 7 个 KcpNet 文件加平台守卫**

在 `Assets/Scripts/AOT/KcpNet/` 下这 7 个文件的**整个内容**外面包一层条件编译：

```
Client/KcpClientTransport.cs
Common/KcpTransportBase.cs
Common/Kcp.cs
Server/KcpListenerWrapper.cs
Server/KcpServer.cs
Server/KcpServerTransport.cs
KcpNetClientAdapter.cs          ← 传递依赖上面三个客户端文件，必须一起守卫
```

写法（`using` 也要包进去，因为 `using System.Net.Sockets;` 本身就是 WebGL 不存在的）：

```csharp
#if !UNITY_WEBGL
using System;
using System.Net.Sockets;
// ... 其余 using ...

namespace KcpNet
{
    // ... 文件原有内容，缩进不动 ...
}
#endif
```

> **注意**：`Common/KcpOptions.cs`、`Common/ILogger.cs`、`Common/IMessageExecutor.cs` **不加守卫**——它们无 socket 依赖，且 WebGL 下保留它们可让 `namespace KcpNet` 仍然存在（避免 `using KcpNet;` 报"命名空间不存在"）。

- [ ] **Step 4: 刷新并验证 Editor 编译与全量回归**

```bash
# 经 Unity MCP：assets-refresh
```
预期：**无编译错误**。

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q
```
预期：**132/132 通过**。

经 Unity MCP `tests-run`：`testMode=EditMode` → 预期 **222/222**；`testMode=PlayMode`（三配置）→ 预期并集 **18/18**。

- [ ] **Step 5: 创建 `WebSocketNet` 程序集与占位适配器**

创建 `Assets/Scripts/AOT/WebSocketNet/WebSocketNet.asmdef`：

```json
{
    "name": "WebSocketNet",
    "rootNamespace": "WebSocketNet",
    "references": [
        "Cool.Protocol"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "versionDefines": [],
    "noEngineReferences": false
}
```

> `includePlatforms` / `excludePlatforms` **都留空**——这个程序集必须在**所有平台**（含 WebGL）编译。

创建 `Assets/Scripts/AOT/WebSocketNet/WebSocketClientAdapter.cs` 占位（T3 会填满它）：

```csharp
using System;
using System.Threading.Tasks;
using Cool.Protocol;

namespace WebSocketNet
{
    /// <summary>
    /// INetworkClient 的 WebSocket 实现 —— 占位骨架，Task 3 填充。
    /// 之所以在 Task 1 就建，是为了让 ClientNetworkHost 的平台分支在本任务即可编译。
    /// </summary>
    public sealed class WebSocketClientAdapter : INetworkClient
    {
        public bool IsConnected => false;

        public event Action<MessageId, IMessage>? MessageReceived;
        public event Action<string>? Disconnected;
        public event Action<Exception>? Error;

        public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(false);
        public Task DisconnectAsync(string reason) => Task.CompletedTask;
        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
        public void Dispose() { }
    }
}
```

- [ ] **Step 6: `ClientNetworkHost` 按平台选择 adapter**

把 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs` 的字段与创建点改为：

```csharp
        // Task 1：字段类型改为接口——KCP 与 WS 两个实现共用
        private INetworkClient? _adapter;
```

创建点（原 `:181` 的 `_adapter = new KcpNetClientAdapter();`）改为：

```csharp
#if UNITY_WEBGL
            // WebGL 无 System.Net.Sockets，KCP 传输不可用 —— 走浏览器 WebSocket
            _adapter = new WebSocketNet.WebSocketClientAdapter();
#else
            _adapter = new KcpNetClientAdapter();
#endif
```

**并加 `using WebSocketNet;`**（仅 WebGL 分支用到，但 `using` 不能条件化——该程序集在所有平台都在，故安全）。

> **本步是过渡形态，不是终态。** `WebSocketClientAdapter` 的构造函数参数在 T3 补（通道），PC 分支在 **T4 Step 4 改成 `DotNetWebSocketChannel`**（D-W11：两端统一走 WS）。本步的 `#else` 保持 KCP 是因为此刻还没有可用的 PC 侧 WebSocket 通道——中途态必须每一步都能编译。

- [ ] **Step 7: 验证 PC/Editor 侧未回归**

重复 Step 4 的三套测试。预期数字不变：Server 132/132、EditMode 222/222、PlayMode 并集 18/18。

- [ ] **Step 8: 验证 WebGL 能编译（本任务存在的唯一理由）**

**不需要完整出包**——把编辑器的活动构建目标切到 WebGL，就会触发一次完整的 WebGL 编译，这足以证明 `System.Net.Sockets` 不再是阻塞。

经 Unity MCP `script-execute`：

```csharp
UnityEditor.EditorUserBuildSettings.SwitchActiveBuildTarget(
    UnityEditor.BuildTargetGroup.WebGL,
    UnityEditor.BuildTarget.WebGL);
```

预期：切换完成，Console **无编译错误**——特别确认**没有** `System.Net.Sockets` 相关的 `CS0246`（找不到类型）或 `CS0234`（命名空间不存在）。

**若有该错误**：说明有文件漏加守卫（最可能是 `KcpNetClientAdapter.cs` 的传递依赖）。按错误信息定位并补上守卫，重跑本步。

**验证完切回** StandaloneWindows64，以免影响后续任务：

```csharp
UnityEditor.EditorUserBuildSettings.SwitchActiveBuildTarget(
    UnityEditor.BuildTargetGroup.Standalone,
    UnityEditor.BuildTarget.StandaloneWindows64);
```

> 若本机未安装 WebGL 模块，`SwitchActiveBuildTarget` 会失败并提示——此时**停下报 BLOCKED** 并说明，不要跳过本步：跳过就等于把整个计划的地基留成未验证状态。

- [ ] **Step 9: Commit**

```bash
git add -A Assets/Scripts Server
git commit -m "build(webgl): unblock WebGL compilation - platform-guard the socket-bound KCP files

WebGL has no System.Net.Sockets, and KcpNet's dependency on it runs through
seven files: KcpClientTransport, KcpTransportBase, Kcp.cs (the KCP algorithm
itself implements System.Net.Sockets.Kcp.IKcpCallback), the three Server/ files
that back the in-process server the PlayMode tests use, and KcpNetClientAdapter
transitively. Guarding them with #if !UNITY_WEBGL lets the assembly compile
everywhere.

INetworkClient moves to Cool.Protocol with its seven member signatures
untouched, so both transports can share it and the abstraction is no longer
owned by KCP. KcpOptions/ILogger/IMessageExecutor deliberately stay unguarded so
the KcpNet namespace still exists under WebGL.

GameSys keeps its KcpNet reference - asmdef references cannot be
platform-conditional, and dropping it would hide KcpNetClientAdapter from PC
builds too."
```

---

### Task 2: 抽取 `IServerSession`（纯机械替换）

**Files:**
- 创建：`Server/IServerSession.cs`
- 修改：`Server/KcpServerSession.cs`（`:96` 加接口）
- 修改：`Server/Server3C.cs`（8 处）
- 修改：`Server/Session/SessionManager.cs`（全部）
- 测试：既有 `Server.Tests` 即验收（行为不变）

**为什么先做这个**：WS 路径要建自己的会话类型，而 `Server3C` 现在绑定在 `KcpServerSession` **具体类型**上。不抽接口，两条传输无法汇入同一个 `ProcessPacket`。

- [ ] **Step 1: 定义 `IServerSession`**

创建 `Server/IServerSession.cs`——**只放业务真正用到的成员**（从 `KcpServerSession` 的公开面里筛，KCP 专属的 `SendSocket`/`KcpConv`/`SetSendSocket`/`EnsureWired`/`HandleUdpPacket`/`QueuedSendCount`/`DroppedSendCount` **不进接口**）：

```csharp
using System;
using System.Threading.Tasks;
using Cool.Protocol;

namespace KcpServer
{
    /// <summary>
    /// 服务端会话契约 —— 传输无关。
    /// 业务代码（Server3C 主循环 / 各 Handler / AOI）只依赖本接口，
    /// 因此 KCP(UDP) 与 WebSocket 两条传输可以汇入同一个 ProcessPacket。
    /// 成员取自 KcpServerSession 的既有公开面，KCP 专属成员（SendSocket / KcpConv /
    /// SetSendSocket / EnsureWired / HandleUdpPacket / QueuedSendCount / DroppedSendCount）
    /// 刻意不进接口。
    /// </summary>
    public interface IServerSession : IDisposable
    {
        long SessionId { get; }
        bool IsConnected { get; }
        DateTime LastActivityTime { get; }

        event EventHandler<byte[]>? DataReceived;
        event EventHandler? Disconnected;
        event EventHandler<Exception>? Error;

        void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable);
        void UpdateActivity();
        bool IsTimedOut(TimeSpan timeout);
        Task CloseAsync(string reason = "Normal");
    }
}
```

- [ ] **Step 2: 让 `KcpServerSession` 实现它**

修改 `Server/KcpServerSession.cs:96`：

```csharp
    public sealed class KcpServerSession : IServerSession
```

**方法体一行不改**——`KcpServerSession` 已具备接口要求的全部成员（`SessionId:131`、`IsConnected:133`、`LastActivityTime:134`、三个事件 `:136-138`、`Send:281`、`UpdateActivity:334`、`IsTimedOut:342`、`CloseAsync:350`、`IDisposable`）。

- [ ] **Step 3: `Server3C` 的 8 处改为接口**

`Server/Server3C.cs` 逐处替换（行为完全不变）：

| 行 | 原 | 改 |
|---|---|---|
| `:30` | `ConcurrentDictionary<long, KcpServerSession>` | `ConcurrentDictionary<long, IServerSession>` |
| `:169` | `BindPlayerToSession(long, KcpServerSession)` | `IServerSession` |
| `:225` | `GetPlayerId(KcpServerSession)` | `IServerSession` |
| `:230` | `GetSession(long) → KcpServerSession?` | `IServerSession?` |
| `:235` | `SendToSessionAsync(KcpServerSession, …)` | `IServerSession` |
| `:345` `:399` `:410` | `if (sender is KcpServerSession session)` | `if (sender is IServerSession session)` |
| `:351` | `ProcessPacket(KcpServerSession session, byte[] data)` | `IServerSession` |
| `:425` | `List<KcpServerSession>` | `List<IServerSession>` |

调用方（`CastHandler` / `LoginHandler` / `PositionSyncHandler` / `ClockSyncHandler` / `EchoHandler`）**只把 session 透传**，不需要改。

- [ ] **Step 4: 修掉一个把 `RemoteEndPoint` 泄进业务的点（读码时发现）**

`Server3C.OnSessionDisconnected`（`:405-415`）目前的实现是：

```csharp
                _gameLoop.EnqueueAction(() => { _loginHandler.HandleDisconnectAsync(session); });
                _gameLoop.EnqueueAction(() => _sessionManager.RemoveByEndpoint(session.RemoteEndPoint));
```

**`RemoteEndPoint` 是 UDP 特有概念**——WebSocket 会话是 HTTP 连接，没有 EndPoint。若把它塞进 `IServerSession`（给 WS 造一个假 EndPoint），接口语义会被污染。

**改法：按 `SessionId` 移除**（两条传输都有稳定 id）。在 `SessionManager` 增加：

```csharp
        /// <summary>按会话 id 移除 —— 传输无关（RemoteEndPoint 只对 UDP 有意义）</summary>
        public bool RemoveById(long sessionId)
        {
            foreach (var kv in _sessions)
            {
                if (kv.Value.SessionId == sessionId) return _sessions.TryRemove(kv.Key, out _);
            }
            return false;
        }
```

并把 `Server3C.cs:410` 那行改为：

```csharp
                _gameLoop.EnqueueAction(() => _sessionManager.RemoveById(session.SessionId));
```

> `RemoveByEndpoint` **保留不动**——KCP 的接收路径仍在用。本步只是让断开清理不再依赖 EndPoint。

- [ ] **Step 5: `SessionManager` 改用接口**

`Server/Session/SessionManager.cs`：`ConcurrentDictionary<string, KcpServerSession>` → `<string, IServerSession>`；`AllSessions`、`GetOrCreateSession`、`FindByEndpoint` 的返回类型同步改接口。

**`GetOrCreateSession` 内部仍 `new KcpServerSession(...)`**——它是 KCP 监听器的入口点，构造具体类型是它的职责。

- [ ] **Step 6: 加一个公开的会话注册点**

WS 监听器建完会话后要把它接进 `Server3C` 的事件链。现有 KCP 路径是在 UDP 收包处内联接线的（`:330-333`），WS 路径需要等价入口。在 `Server3C` 增加**纯增量**的公开方法：

```csharp
        /// <summary>
        /// 注册一个会话到事件链 —— 传输无关。
        /// KCP 路径在 UDP 收包处内联接线（不属本次改动范围），
        /// WebSocket 监听器建完会话后调用本方法。
        /// </summary>
        public void RegisterSession(IServerSession session)
        {
            session.DataReceived += OnSessionDataReceived;
            session.Disconnected += OnSessionDisconnected;
            session.Error += OnSessionError;
        }
```

> `DataReceived` → `OnSessionDataReceived`（`:344`）→ `ProcessPacket` 这条链**本来就传输无关**——WS 会话只要触发 `DataReceived`，就自动汇入同一个 `ProcessPacket`。这正是 D-W3 说"双栈几乎白送"的原因。

- [ ] **Step 7: 验证行为未变**

```bash
dotnet build Server/Server3C.csproj
dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q
```

预期：构建 0 错误；**132/132 通过**（本任务是纯类型替换，任何测试变化都说明改错了）。

追加验证：**KCP 路径仍能跑**——起服务器 + PlayMode 三配置并集 **18/18**（这是"双栈之前 KCP 没被弄坏"的证据）。

- [ ] **Step 8: Commit**

```bash
git add Server
git commit -m "refactor(server): extract IServerSession from KcpServerSession

The WebSocket transport needs its own session type, but Server3C binds to the
concrete KcpServerSession in eight places, so the two transports could not share
ProcessPacket. The interface carries only what business code actually touches -
SessionId, IsConnected, LastActivityTime, the three events, Send, UpdateActivity,
IsTimedOut, CloseAsync - and deliberately leaves out the KCP-specific surface
(SendSocket, KcpConv, SetSendSocket, EnsureWired, HandleUdpPacket, and the
send-queue drop counters).

Pure type substitution: no method body changed, so 132/132 green is the proof."
```

---

### Task 3: `WebSocketClientAdapter`（逻辑层，EditMode 可测）

**Files:**
- 创建：`Assets/Scripts/AOT/WebSocketNet/IWebSocketChannel.cs`
- 创建：`Assets/Scripts/AOT/WebSocketNet/DotNetWebSocketChannel.cs`
- 重写：`Assets/Scripts/AOT/WebSocketNet/WebSocketClientAdapter.cs`（填充 T1 的占位）
- 创建：`Assets/Scripts/Tests/Editor/WebSocketNet/WebSocketClientAdapterTests.cs`

**分层理由（D-W8）**：WebGL 必须走 jslib，而**编辑器里 `ClientWebSocket` 可用**。把通道抽出来，适配器逻辑就能在 EditMode 单测，只有 jslib 与浏览器运行时留人工。

**测试策略说明**：本任务的单测用**假通道**（`FakeChannel : IWebSocketChannel`），不用真 WS 服务器——Unity 的 EditMode 里 `HttpListener` 可用性不确定，而项目既有 `RecordingClient : INetworkClient` 就是这个范式。**真实往返由 T4（浏览器）与 T5（集成）覆盖。**

- [ ] **Step 1: 定义通道抽象**

创建 `Assets/Scripts/AOT/WebSocketNet/IWebSocketChannel.cs`：

```csharp
using System;
using System.Threading.Tasks;

namespace WebSocketNet
{
    /// <summary>
    /// WebSocket 通道抽象 —— 把"平台怎么收发字节"与"协议怎么编解码"分开。
    /// 实现有两个：DotNetWebSocketChannel（编辑器/原生，用 ClientWebSocket）
    /// 与 WebGLWebSocketChannel（仅 WebGL，走 jslib 到浏览器 API）。
    /// 分层的目的：适配器逻辑在 EditMode 单测时注入假通道即可，无需真 socket。
    /// </summary>
    public interface IWebSocketChannel : IDisposable
    {
        bool IsConnected { get; }

        Task<bool> ConnectAsync(string uri);
        Task CloseAsync(string reason);

        /// <summary>发送一帧（一条消息一帧，见 D-W9）</summary>
        Task SendAsync(byte[] data, int offset, int length);

        /// <summary>收到一帧（回调线程由实现决定；WebGL 下是浏览器事件线程）</summary>
        event Action<byte[]>? MessageReceived;
        event Action<string>? Closed;
        event Action<Exception>? Error;
    }
}
```

- [ ] **Step 2: 写失败的测试**

创建 `Assets/Scripts/Tests/Editor/WebSocketNet/WebSocketClientAdapterTests.cs`（命名空间 `GameSys.EditorTests`，与既有测试一致）：

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using NUnit.Framework;
using WebSocketNet;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Task 3：WebSocketClientAdapter 的协议接线 —— 用假通道验证
    /// "编码 → 发送" 与 "收到 → 解码 → 事件" 两条链路，以及连接状态与错误上抛。
    /// 真实 WS 往返由 T4（浏览器）与 T5（集成）覆盖，不在此处。
    /// </summary>
    public class WebSocketClientAdapterTests
    {
        private sealed class FakeChannel : IWebSocketChannel
        {
            public readonly List<byte[]> Sent = new List<byte[]>();
            public bool IsConnected { get; private set; }
            public string? LastUri;
            public string? CloseReason;

            public event Action<byte[]>? MessageReceived;
            public event Action<string>? Closed;
            public event Action<Exception>? Error;

            public Task<bool> ConnectAsync(string uri)
            {
                LastUri = uri;
                IsConnected = true;
                return Task.FromResult(true);
            }

            public Task CloseAsync(string reason)
            {
                CloseReason = reason;
                IsConnected = false;
                return Task.CompletedTask;
            }

            public Task SendAsync(byte[] data, int offset, int length)
            {
                var copy = new byte[length];
                Array.Copy(data, offset, copy, 0, length);
                Sent.Add(copy);
                return Task.CompletedTask;
            }

            // 测试用驱动器
            public void Deliver(byte[] frame) => MessageReceived?.Invoke(frame);
            public void RaiseClosed(string reason) => Closed?.Invoke(reason);
            public void RaiseError(Exception ex) => Error?.Invoke(ex);

            public void Dispose() { }
        }

        [Test]
        public async Task test_connect_builds_ws_uri_from_host_and_port()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);

            // Act
            bool ok = await adapter.ConnectAsync("127.0.0.1", 8000);

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual("ws://127.0.0.1:8000/", channel.LastUri);
            Assert.IsTrue(adapter.IsConnected);
        }

        [Test]
        public async Task test_send_encodes_message_onto_one_frame()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            await adapter.ConnectAsync("127.0.0.1", 8000);
            var echo = new EchoRequest { Content = "hi" };

            // Act
            adapter.Send(MessageId.EchoRequest, echo);

            // Assert（一条消息 = 一帧，且能被 MessageCodec 解回来）
            Assert.AreEqual(1, channel.Sent.Count, "一条消息应只产生一帧（D-W9）");
            Assert.IsTrue(MessageCodec.TryDecode(channel.Sent[0], out var decoded));
            Assert.AreEqual(MessageId.EchoRequest, decoded.messageId);
            Assert.AreEqual("hi", ((EchoRequest)decoded.message).Content);
        }

        [Test]
        public async Task test_received_frame_raises_message_received_with_decoded_id()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            await adapter.ConnectAsync("127.0.0.1", 8000);

            MessageId? seenId = null;
            IMessage? seenMsg = null;
            adapter.MessageReceived += (id, msg) => { seenId = id; seenMsg = msg; };

            var payload = MessageCodec.EncodeToArray(
                MessageId.EchoResponse, new EchoResponse { Content = "pong" }, MessageFlags.Reliable);

            // Act
            channel.Deliver(payload);

            // Assert
            Assert.AreEqual(MessageId.EchoResponse, seenId);
            Assert.AreEqual("pong", ((EchoResponse)seenMsg!).Content);
        }

        [Test]
        public async Task test_malformed_frame_is_dropped_without_raising()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            await adapter.ConnectAsync("127.0.0.1", 8000);

            bool raised = false;
            Exception? errored = null;
            adapter.MessageReceived += (_, _) => raised = true;
            adapter.Error += ex => errored = ex;

            // Act（一段不是合法编码的字节）
            channel.Deliver(new byte[] { 0xFF, 0x00, 0x01 });

            // Assert（不崩、不上抛假消息；解码失败要留痕但不能打断接收循环）
            Assert.IsFalse(raised, "畸形帧不得伪装成一条消息");
            Assert.IsNotNull(errored, "畸形帧应经由 Error 事件留痕，而不是静默吞掉");
        }

        [Test]
        public async Task test_channel_closed_raises_disconnected()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            await adapter.ConnectAsync("127.0.0.1", 8000);

            string? reason = null;
            adapter.Disconnected += r => reason = r;

            // Act
            channel.RaiseClosed("server shutdown");

            // Assert
            Assert.AreEqual("server shutdown", reason);
            Assert.IsFalse(adapter.IsConnected);
        }

        [Test]
        public async Task test_send_when_not_connected_is_dropped_silently()
        {
            // Arrange（未连接）
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);

            // Act
            adapter.Send(MessageId.EchoRequest, new EchoRequest { Content = "x" });

            // Assert（与 KcpNetClientAdapter 的既有语义一致：未连接时静默丢弃）
            Assert.AreEqual(0, channel.Sent.Count);
        }
    }
}
```

> `EchoRequest` / `EchoResponse` 是既有协议类型（`Assets/Scripts/AOT/Protocol/Messages/Echo.cs`）；`MessageCodec.EncodeToArray` 是既有 API（`Server.Tests` 已在用）。若字段名与上述不符，按实际类型改，**不要改测试意图**。

- [ ] **Step 3: 运行测试验证失败**

经 Unity MCP `tests-run`，`testMode=EditMode`，`testClass=WebSocketClientAdapterTests`
预期：**编译失败**（`WebSocketClientAdapter` 还是 T1 的占位，构造函数不接通道）。

- [ ] **Step 4: 实现适配器**

重写 `Assets/Scripts/AOT/WebSocketNet/WebSocketClientAdapter.cs`：

```csharp
using System;
using System.Threading.Tasks;
using Cool.Protocol;

namespace WebSocketNet
{
    /// <summary>
    /// INetworkClient 的 WebSocket 实现 —— 与 KcpNetClientAdapter 同构：
    /// 上层（ClientNetworkManager / CombatNetworkBridge）只认接口，换传输零改动。
    /// 一条消息 = 一个 WS 帧（D-W9），直接复用 MessageCodec，无需再拆帧。
    /// 通道可注入（D-W8）——测试注假通道，运行时注 DotNet/WebGL 通道。
    /// </summary>
    public sealed class WebSocketClientAdapter : INetworkClient
    {
        private readonly IWebSocketChannel _channel;

        public bool IsConnected => _channel.IsConnected;

        public event Action<MessageId, IMessage>? MessageReceived;
        public event Action<string>? Disconnected;
        public event Action<Exception>? Error;

        public WebSocketClientAdapter(IWebSocketChannel channel)
        {
            _channel = channel;
            _channel.MessageReceived += OnFrame;
            _channel.Closed += OnClosed;
            _channel.Error += OnError;
        }

        public async Task<bool> ConnectAsync(string host, int port)
        {
            try
            {
                // 演示形态下走明文 ws:// —— wss 由部署层的反向代理终止（Task 9）
                return await _channel.ConnectAsync("ws://" + host + ":" + port + "/");
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                return false;
            }
        }

        public Task DisconnectAsync(string reason) => _channel.CloseAsync(reason);

        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            if (!_channel.IsConnected) return;   // 与 KcpNetClientAdapter 同语义：未连接静默丢弃

            byte[] frame = MessageCodec.EncodeToArray(messageId, message, flags);
            // fire-and-forget：不在调用线程等待（与 KcpNetClientAdapter 一致，避免主线程死锁）
            _ = _channel.SendAsync(frame, 0, frame.Length);
        }

        private void OnFrame(byte[] data)
        {
            if (!MessageCodec.TryDecode(data, out var decoded))
            {
                // 畸形帧留痕但不打断接收循环——静默吞掉会让排查无线索
                Error?.Invoke(new InvalidOperationException("WebSocket frame failed to decode"));
                return;
            }
            MessageReceived?.Invoke(decoded.messageId, decoded.message);
        }

        private void OnClosed(string reason) => Disconnected?.Invoke(reason);

        private void OnError(Exception ex) => Error?.Invoke(ex);

        public void Dispose()
        {
            _channel.MessageReceived -= OnFrame;
            _channel.Closed -= OnClosed;
            _channel.Error -= OnError;
            _channel.Dispose();
        }
    }
}
```

- [ ] **Step 5: 运行测试验证通过**

`tests-run`，`testMode=EditMode`，`testClass=WebSocketClientAdapterTests`
预期：**6/6 PASS**。

- [ ] **Step 6: 实现 `DotNetWebSocketChannel`（编辑器/原生用真 socket）**

创建 `Assets/Scripts/AOT/WebSocketNet/DotNetWebSocketChannel.cs`。它的职责：把 `ClientWebSocket` 的分片接收聚合成一帧，转成 `MessageReceived`。

```csharp
#if !UNITY_WEBGL
using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace WebSocketNet
{
    /// <summary>
    /// 非 WebGL 平台的通道实现（编辑器 / PC / 原生）。
    /// WebGL 下 System.Net.WebSockets 不可用，编译期整体排除——
    /// 与 KcpNet 的平台守卫同一手法（见 Task 1）。
    /// 注意：本类存在的意义是让"编辑器里能跑真 WS"，从而把 jslib 的验证范围缩到最小。
    /// </summary>
    public sealed class DotNetWebSocketChannel : IWebSocketChannel
    {
        private const int ReceiveBufferSize = 64 * 1024;

        private ClientWebSocket? _socket;
        private CancellationTokenSource? _cts;

        public bool IsConnected => _socket?.State == WebSocketState.Open;

        public event Action<byte[]>? MessageReceived;
        public event Action<string>? Closed;
        public event Action<Exception>? Error;

        public async Task<bool> ConnectAsync(string uri)
        {
            _socket = new ClientWebSocket();
            _cts = new CancellationTokenSource();
            try
            {
                await _socket.ConnectAsync(new Uri(uri), _cts.Token);
                _ = ReceiveLoopAsync();   // fire-and-forget：接收循环独立于连接调用
                return IsConnected;
            }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                return false;
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[ReceiveBufferSize];
            var accumulated = new MemoryStream();

            try
            {
                while (_socket != null && _socket.State == WebSocketState.Open)
                {
                    var result = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), _cts!.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Closed?.Invoke("closed by peer");
                        return;
                    }

                    accumulated.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;   // 分片：聚合到一帧完整消息

                    var frame = accumulated.ToArray();
                    accumulated.SetLength(0);
                    MessageReceived?.Invoke(frame);       // 一条消息一帧（D-W9）
                }
            }
            catch (OperationCanceledException) { /* 主动关闭，非错误 */ }
            catch (Exception ex)
            {
                Error?.Invoke(ex);
                Closed?.Invoke("receive loop terminated");
            }
        }

        public async Task SendAsync(byte[] data, int offset, int length)
        {
            if (_socket == null || _socket.State != WebSocketState.Open) return;
            await _socket.SendAsync(
                new ArraySegment<byte>(data, offset, length),
                WebSocketMessageType.Binary, endOfMessage: true, _cts!.Token);
        }

        public async Task CloseAsync(string reason)
        {
            try
            {
                if (_socket != null && _socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
            }
            catch { /* 关闭路径的异常不值得上抛 */ }
            finally { _cts?.Cancel(); }
        }

        public void Dispose()
        {
            try { _socket?.Dispose(); } catch { }
            _cts?.Dispose();
            _socket = null;
            _cts = null;
        }
    }
}
#endif
```

> **`#if !UNITY_WEBGL` 是必须的**：`System.Net.WebSockets` 在 WebGL 下同样不存在（与 `System.Net.Sockets` 一样）。这个守卫让"编辑器能跑真 WS"与"WebGL 编译得过"同时成立。

- [ ] **Step 7: 在 `ClientNetworkHost` 里接上真实通道**

`ClientNetworkHost.cs` 的 Step 6 代码块改为按平台选**通道**（适配器本身只有一个）：

```csharp
#if UNITY_WEBGL
            _adapter = new WebSocketNet.WebSocketClientAdapter(
                new WebSocketNet.WebGLWebSocketChannel());
#else
            // 过渡态：PC 侧 WebSocket 通道要等 T4 才接线（D-W11）
            _adapter = new KcpNetClientAdapter();
#endif
```

> `WebGLWebSocketChannel` 在 Task 4 创建。本步骤**先不要改这一行**——等 T4 完成再改，否则编译不过。本任务只到 Step 6 为止；`ClientNetworkHost` 的 WebGL 分支暂用 T1 的占位（`new WebSocketNet.WebSocketClientAdapter(...)` 的参数在 T4 补）。
>
> **`DotNetWebSocketChannel` 本步已创建（Step 6），但先不接进来。** 接线放在 T4 Step 4，理由是那里紧接着跑一次 PlayMode 回归（T4 Step 5）——行为变更与它的验证放在同一步，比跨任务追溯可靠。

- [ ] **Step 8: 跑全量 EditMode 回归**

`tests-run`，`testMode=EditMode`
预期：**228/228**（基线 222 + 本任务 6 条新增）。

- [ ] **Step 9: Commit**

```bash
git add Assets/Scripts
git commit -m "feat(websocket): WebSocketClientAdapter with an injectable channel

Splits platform byte-shuffling from protocol encoding: IWebSocketChannel has two
implementations, DotNetWebSocketChannel (ClientWebSocket, editor and native) and
the jslib one arriving in Task 4. That split is what makes the adapter testable -
the six new EditMode tests drive it through a fake channel, so only the jslib
boundary and the browser runtime need human verification.

One message per WebSocket frame, reusing MessageCodec as-is - WebSocket already
has message boundaries, so no stream reframing is needed. Malformed frames raise
Error rather than being swallowed, so a bad frame leaves a trail.

DotNetWebSocketChannel is guarded with #if !UNITY_WEBGL for the same reason the
KCP transports are: System.Net.WebSockets does not exist there either."
```

---

### Task 4: `WebGLWebSocket.jslib` + 浏览器 echo

**Files:**
- 创建：`Assets/Plugins/WebGL/WebGLWebSocket.jslib`
- 创建：`Assets/Scripts/AOT/WebSocketNet/WebGLWebSocketChannel.cs`
- 创建：`Assets/Scripts/AOT/WebSocketNet/WebSocketNetBridge.cs`（MonoBehaviour，JS 回调用）
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs`（接上真实通道）
- 修改：`Server/Program.cs`（起一个 WS echo，供本任务验证）

**本任务是 R2 的落点，采用两条把风险压到最低的取舍：**

| 方向 | 做法 | 为什么 |
|---|---|---|
| C# → JS（发送） | `[DllImport("__Internal")]` 传 `byte[]` | 这是**简单方向**：Unity 直接把数组封送成指针 |
| JS → C#（接收） | **`SendMessage` + base64** | `dynCall` + `_malloc` + 手动 `dynCall_viii` 签名极易出错且难调试。base64 有 33% 开销，但演示消息量（15 帧/秒、几百字节）**完全无感** |

- [ ] **Step 1: 写 jslib**

创建 `Assets/Plugins/WebGL/WebGLWebSocket.jslib`：

```javascript
mergeInto(LibraryManager.library, {
  $WebSocketNetState: {
    sockets: {},
    nextId: 1,
    // JS 回调进入 Unity 的目标：运行时由 C# 侧创建的隐藏 GameObject
    bridgeName: 'WebSocketNetBridge'
  },

  WebSocketNet_Connect: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var id = WebSocketNetState.nextId++;
    try {
      var ws = new WebSocket(url);
      ws.binaryType = 'arraybuffer';
    } catch (e) {
      return -1;
    }
    WebSocketNetState.sockets[id] = ws;

    ws.onopen = function () {
      gameInstance.SendMessage(WebSocketNetState.bridgeName, 'OnOpen', String(id));
    };
    ws.onmessage = function (ev) {
      // 二进制帧 → base64 → SendMessage（见本任务开头的取舍表）
      var bytes = new Uint8Array(ev.data);
      var binary = '';
      for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
      var b64 = btoa(binary);
      gameInstance.SendMessage(WebSocketNetState.bridgeName, 'OnFrame', id + '|' + b64);
    };
    ws.onclose = function (ev) {
      delete WebSocketNetState.sockets[id];
      gameInstance.SendMessage(WebSocketNetState.bridgeName, 'OnClose', id + '|' + (ev.reason || 'closed'));
    };
    ws.onerror = function () {
      gameInstance.SendMessage(WebSocketNetState.bridgeName, 'OnError', String(id));
    };

    return id;
  },

  WebSocketNet_Send: function (handle, dataPtr, length) {
    var ws = WebSocketNetState.sockets[handle];
    if (!ws || ws.readyState !== 1) return 0;
    // 必须拷贝：HEAPU8 的视图在下次内存增长后会失效
    var copy = new Uint8Array(length);
    copy.set(HEAPU8.subarray(dataPtr, dataPtr + length));
    ws.send(copy);
    return 1;
  },

  WebSocketNet_Close: function (handle, reasonPtr) {
    var ws = WebSocketNetState.sockets[handle];
    if (!ws) return;
    ws.close(1000, reasonPtr ? UTF8ToString(reasonPtr) : 'normal');
    delete WebSocketNetState.sockets[handle];
  },

  WebSocketNet_IsOpen: function (handle) {
    var ws = WebSocketNetState.sockets[handle];
    return (ws && ws.readyState === 1) ? 1 : 0;
  }
});
```

> **`Assets/Plugins/WebGL/` 下的 `.jslib` 会被自动链接**，无需额外配置。但**注意**：目录里若同时存在 `.asmdef`，`.jslib` 的链接范围会受限——本目录不放 asmdef。

- [ ] **Step 2: 写 JS 回调的 C# 接收端**

创建 `Assets/Scripts/AOT/WebSocketNet/WebSocketNetBridge.cs`：

```csharp
#if UNITY_WEBGL
using System;
using UnityEngine;

namespace WebSocketNet
{
    /// <summary>
    /// jslib 经 SendMessage 进入 Unity 的唯一落点。
    /// 必须是场景里的 GameObject（SendMessage 的目标），由 WebGLWebSocketChannel
    /// 在首次用时创建并 DontDestroyOnLoad。
    /// 三个方法名（OnOpen / OnFrame / OnClose / OnError）必须与 .jslib 里的字符串**逐字一致**。
    /// </summary>
    public sealed class WebSocketNetBridge : MonoBehaviour
    {
        internal static WebSocketNetBridge? Instance;

        internal event Action<int>? Opened;
        internal event Action<int, byte[]>? Framed;
        internal event Action<int, string>? Closed;
        internal event Action<int>? Errored;

        private void Awake()
        {
            Instance = this;
        }

        // SendMessage 只支持单参数（string）——故载荷用 "handle|payload" 拼装
        public void OnOpen(string payload)
        {
            if (int.TryParse(payload, out int handle)) Opened?.Invoke(handle);
        }

        public void OnFrame(string payload)
        {
            int sep = payload.IndexOf('|');
            if (sep <= 0) return;
            if (!int.TryParse(payload.Substring(0, sep), out int handle)) return;
            try
            {
                byte[] data = Convert.FromBase64String(payload.Substring(sep + 1));
                Framed?.Invoke(handle, data);
            }
            catch (FormatException)
            {
                Errored?.Invoke(handle);
            }
        }

        public void OnClose(string payload)
        {
            int sep = payload.IndexOf('|');
            if (sep <= 0) return;
            if (!int.TryParse(payload.Substring(0, sep), out int handle)) return;
            Closed?.Invoke(handle, payload.Substring(sep + 1));
        }

        public void OnError(string payload)
        {
            if (int.TryParse(payload, out int handle)) Errored?.Invoke(handle);
        }
    }
}
#endif
```

- [ ] **Step 3: 实现 `WebGLWebSocketChannel`**

创建 `Assets/Scripts/AOT/WebSocketNet/WebGLWebSocketChannel.cs`：

```csharp
#if UNITY_WEBGL
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace WebSocketNet
{
    /// <summary>
    /// WebGL 平台通道 —— 经 .jslib 调用浏览器 WebSocket API。
    /// 与 DotNetWebSocketChannel 是同一 IWebSocketChannel 的两个实现，
    /// 因此 WebSocketClientAdapter 与其测试完全不需要区分平台。
    /// 平台守卫是必须的：本类引用 UnityEngine 且 .jslib 只在 WebGL 构建里存在。
    /// </summary>
    public sealed class WebGLWebSocketChannel : IWebSocketChannel
    {
        [DllImport("__Internal")] private static extern int WebSocketNet_Connect(string url);
        [DllImport("__Internal")] private static extern int WebSocketNet_Send(int handle, byte[] data, int length);
        [DllImport("__Internal")] private static extern void WebSocketNet_Close(int handle, string reason);
        [DllImport("__Internal")] private static extern int WebSocketNet_IsOpen(int handle);

        private const int InvalidHandle = -1;

        private int _handle = InvalidHandle;
        private bool _open;
        private WebSocketNetBridge? _bridge;

        public bool IsConnected => _open && _handle != InvalidHandle && WebSocketNet_IsOpen(_handle) == 1;

        public event Action<byte[]>? MessageReceived;
        public event Action<string>? Closed;
        public event Action<Exception>? Error;

        public WebGLWebSocketChannel()
        {
            var go = new GameObject("WebSocketNetBridge");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _bridge = go.AddComponent<WebSocketNetBridge>();

            _bridge.Opened += h => { if (h == _handle) _open = true; };
            _bridge.Framed += (h, data) => { if (h == _handle) MessageReceived?.Invoke(data); };
            _bridge.Closed += (h, reason) =>
            {
                if (h != _handle) return;
                _open = false;
                Closed?.Invoke(reason);
            };
            _bridge.Errored += h =>
            {
                if (h != _handle) return;
                Error?.Invoke(new Exception("WebSocket error in browser"));
            };
        }

        public Task<bool> ConnectAsync(string uri)
        {
            // 句柄在 Connect 返回时就已分配（JS 侧同步建 WebSocket 对象），
            // 但 open 是异步事件 —— 这里等待到 _open 或超时。
            _handle = WebSocketNet_Connect(uri);
            if (_handle == InvalidHandle)
            {
                Error?.Invoke(new Exception("Failed to create browser WebSocket: " + uri));
                return Task.FromResult(false);
            }
            return WaitForOpenAsync();
        }

        private async Task<bool> WaitForOpenAsync()
        {
            const int timeoutMs = 5000;
            int waited = 0;
            while (!_open && waited < timeoutMs)
            {
                await Task.Delay(50);
                waited += 50;
            }
            if (!_open) Error?.Invoke(new Exception("WebSocket open timed out: " + timeoutMs + "ms"));
            return _open;
        }

        public Task SendAsync(byte[] data, int offset, int length)
        {
            if (!IsConnected) return Task.CompletedTask;
            // offset 非 0 时先切片 —— jslib 只接受整数组 + 长度
            if (offset != 0)
            {
                var slice = new byte[length];
                Array.Copy(data, offset, slice, 0, length);
                WebSocketNet_Send(_handle, slice, length);
            }
            else
            {
                WebSocketNet_Send(_handle, data, length);
            }
            return Task.CompletedTask;
        }

        public Task CloseAsync(string reason)
        {
            if (_handle != InvalidHandle) WebSocketNet_Close(_handle, reason);
            _open = false;
            _handle = InvalidHandle;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            CloseAsync("dispose").GetAwaiter().GetResult();
            if (_bridge != null) UnityEngine.Object.Destroy(_bridge.gameObject);
            _bridge = null;
        }
    }
}
#endif
```

- [ ] **Step 4: `ClientNetworkHost` 接上真实通道——并把 PC 也统一到 WS（D-W11 的落点）**

把 Task 1 Step 6 / Task 3 Step 7 里预留的那两行补全。两端共用**同一个** `WebSocketClientAdapter`，只在最底层 channel 上按平台分叉：

```csharp
#if UNITY_WEBGL
            // WebGL 无 System.Net.Sockets —— 走浏览器 WebSocket
            _adapter = new WebSocketNet.WebSocketClientAdapter(
                new WebSocketNet.WebGLWebSocketChannel());
#else
            // D-W11：PC 也走 WebSocket（DotNetWebSocketChannel）。
            // 应用层 4 个发送点全部使用默认的 MessageFlags.Reliable，KCP 的不可靠
            // 通道从未被调用；保留它作为 PC 默认，只会让"自测路径"（PC=KCP）与
            // "演示路径"（浏览器=WS）分叉——那正是最不该分叉的两条路。
            _adapter = new WebSocketNet.WebSocketClientAdapter(
                new WebSocketNet.DotNetWebSocketChannel());
#endif
```

**同时删掉 `ClientNetworkHost.cs` 顶部的 `using KcpNet;`**——本步之后该文件不再引用 `KcpNet` 的任何类型（`MessageId` / `MessageFlags` 都在 `Cool.Protocol`，字段类型是 `INetworkClient`），留着只是噪声。

> **`KcpNet` 程序集与 `KcpNetClientAdapter` 都不删**（人类裁定："保留原有 kcp 代码，以后再删"）。`GameSys.asmdef` 对 `KcpNet` 的引用按 T1 保持不动，`KcpNet.PlayModeTests.asmdef` 亦然。
>
> 本步之后 `GameSys` 内确实已经没有 `KcpNet` 的使用者，那条引用变成多余——但**删除它零收益**：`KcpNet` 程序集无论有没有人引用，在 WebGL 下都要编译，T1 的守卫照旧必要。属于"以后再删"的范围。

- [ ] **Step 5: 验证 Editor 侧未回归**

`tests-run`，`testMode=EditMode` → 预期 **228/228**；三配置 PlayMode → 并集 **18/18**。

> **为什么 PC 改了传输而 PlayMode 数字不变**：9 个 PlayMode 测试**全部直接构造 `KcpNetClientAdapter`**（`AdapterConnectTests.cs:50/69`、`AoiWorldTests.cs:19`、`CombatSyncTests.cs:26`、`LoginEnterWorldTests.cs:46`、`MonsterWorldTests.cs:31`、`MovementSyncTests.cs:18/305`、`TwoClientWorldTests.cs:21`），**没有任何一个经过 `ClientNetworkHost`**。所以 18/18 同时也证明了"KCP 传输本身没有被本步弄坏"。
>
> **代价要说清楚**：反过来也成立——PlayMode **覆盖不到** `ClientNetworkHost` 的接线，Step 4 改的那两行不在任何自动化测试里。它的真实覆盖在 **T5 Step 7**（Editor 走 WS 连真服务器进世界）与 **T8** 的浏览器验收；T5 之前不要把它当成已验证。

- [ ] **Step 6: 出 WebGL 包并部署到本地做 echo 验证**

用 T9 的 `WebGLBuildScript` 骨架（本步先建最小版）出包，用任意静态服务器托管（`python -m http.server` 即可），**同一台机器**上起一个 WS echo：

```bash
# 用一个最简 echo 起在 8000（本步只验证传输，不接业务服务器）
cd E:/CodeForJob/Cool && dotnet run --project Server/Server3C.csproj -- 8000
```

预期（**人工，浏览器**）：
1. 打开 `http://localhost:8080`（静态服务器上的 WebGL 包）
2. 控制台出现 `[NetHost] 联网模式——连接 127.0.0.1:8000`
3. 左下浮层状态走到 **`InWorld`** 或至少 **`LoggingIn`**（能发出 LoginRequest 并收到响应即证明双向通了）

> **本步的判据是"通道通"，不是"游戏能玩"** —— 完整玩法由 T8 验收。
>
> 若 `InWorld` 走不到：按 R2 的路径排查——先在浏览器 Network 面板确认 WS 已建立（101 Switching Protocols），再看有没有帧在传。**jslib 的问题九成出在回调名拼写**（`OnFrame` / `OnClose` 必须与 C# 方法名逐字一致）。

- [ ] **Step 7: Commit**

```bash
git add Assets Server
git commit -m "feat(webgl): browser WebSocket channel over a jslib bridge

Two deliberate risk reductions, since this is where a WebGL port usually gets
stuck. Outbound (C# to JS) passes a byte[] straight through DllImport, which
Unity marshals for you. Inbound (JS to C#) uses SendMessage with base64 rather
than dynCall plus manual _malloc and hand-written dyncall signatures - base64
costs 33% on the wire, which is imperceptible at 15 frames per second with
few-hundred-byte payloads, and it removes the part most likely to fail opaquely.

Frames are copied before ws.send because a HEAPU8 view is invalidated when the
wasm heap grows.

The bridge is a runtime-created hidden GameObject because SendMessage needs a
target. Its method names must match the jslib strings exactly - that is the
first thing to check if a callback never arrives.

This commit also unifies the client transport (D-W11): PC now uses
WebSocketClientAdapter with DotNetWebSocketChannel instead of
KcpNetClientAdapter. The application layer only ever sends MessageFlags.Reliable
- all four call sites take the default - so KCP's unreliable path was dead code,
and keeping it as the PC default meant every self-verification run exercised a
path the browser demo never touches. KCP stays in the tree as a second
implementation of INetworkClient; nothing is deleted.

PlayMode is unaffected: all nine tests construct KcpNetClientAdapter directly
and never go through ClientNetworkHost."
```

---

### Task 5: `WebSocketServerSession` + `WebSocketServerListener`（双栈）

**Files:**
- 创建：`Server/WebSocketServerSession.cs`
- 创建：`Server/WebSocketServerListener.cs`
- 修改：`Server/Program.cs`（起 WS 监听）
- 测试：`Server/Server.Tests/WebSocket/WebSocketServerSessionTests.cs`

**本任务兑现 D-W3**：WS 与 UDP/KCP 在**同一个进程**里并存，且都汇入同一个 `ProcessPacket`。

- [ ] **Step 1: 写失败的测试**

创建 `Server/Server.Tests/WebSocket/WebSocketServerSessionTests.cs`（xUnit，与既有测试一致）：

```csharp
using System;
using System.Threading.Tasks;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace Server.Tests.WebSocketTests
{
    /// <summary>
    /// Task 5：WebSocketServerSession 的契约测试。
    /// 用内存中的假 WebSocket 驱动，不建真连接——
    /// 真连接由 Task 8 的浏览器验收覆盖。
    /// </summary>
    public class WebSocketServerSessionTests
    {
        [Fact]
        public void SessionId_IsPreserved()
        {
            var session = new WebSocketServerSession(42L, new FakeSocket(), new TestLogger());
            Assert.Equal(42L, session.SessionId);
        }

        [Fact]
        public void Send_IsQueuedNotBlocking()
        {
            var socket = new FakeSocket();
            var session = new WebSocketServerSession(1L, socket, new TestLogger());

            session.Send(MessageId.EchoResponse, new EchoResponse { Content = "x" });

            // Send 是同步的（IServerSession 契约）：调用后消息应在队列里，
            // 而不是已经同步写完 —— 否则主循环会被网络写阻塞
            Assert.Equal(1, session.QueuedSendCount);
        }

        [Fact]
        public async Task InboundFrame_RaisesDataReceived()
        {
            var socket = new FakeSocket();
            var session = new WebSocketServerSession(1L, socket, new TestLogger());

            byte[]? received = null;
            session.DataReceived += (_, data) => received = data;

            var frame = MessageCodec.EncodeToArray(
                MessageId.EchoRequest, new EchoRequest { Content = "hi" }, MessageFlags.Reliable);
            await socket.PushInboundAsync(frame);

            Assert.NotNull(received);
            Assert.Equal(frame, received);
        }

        [Fact]
        public void IsTimedOut_RespectsLastActivity()
        {
            var session = new WebSocketServerSession(1L, new FakeSocket(), new TestLogger());
            Assert.False(session.IsTimedOut(TimeSpan.FromSeconds(30)));

            // UpdateActivity 之后同样不该超时（基线语义：活动即续期）
            session.UpdateActivity();
            Assert.False(session.IsTimedOut(TimeSpan.FromSeconds(30)));
        }
    }
}
```

> `FakeSocket` 是测试内的假 WebSocket 实现（`System.Net.WebSockets.WebSocket` 是抽象类，可继承）；`TestLogger` 是既有测试工具（`Server/Server.Tests/TestLogger.cs`）。若 `WebSocketServerSession` 的构造签名与上述不同，按实现调整构造调用，**不要改断言意图**。

- [ ] **Step 2: 运行测试验证失败**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q
```
预期：**编译失败**（`WebSocketServerSession` 不存在）。

- [ ] **Step 3: 实现 `WebSocketServerSession`**

创建 `Server/WebSocketServerSession.cs`。要点：

- 实现 `IServerSession`（Task 2 定义的接口）
- `Send(MessageId, IMessage, MessageFlags)` **同步入队**（与 `KcpServerSession` 同语义）→ 后台 drain 循环 `SendAsync` 发出
- 后台 receive 循环 → `DataReceived?.Invoke(this, frame)`
- 记录 `QueuedSendCount`（与 KCP 会话同惯例，便于观测）

```csharp
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Cool.Protocol;

namespace KcpServer
{
    /// <summary>
    /// WebSocket 会话 —— IServerSession 的第二实现（KCP 之外）。
    /// 与 KcpServerSession 的关键差别：TCP 自带流控与重传，
    /// 故**不需要** KCP 那套发送窗口 / 丢包计数 / conv 采纳。
    /// Send 保持"同步入队 + 后台 drain"的形状，让主循环永不被网络写阻塞——
    /// 这与 KcpServerSession 的契约一致，是 IServerSession 能同时容纳两者的前提。
    /// </summary>
    public sealed class WebSocketServerSession : IServerSession
    {
        private const int ReceiveBufferSize = 64 * 1024;

        private readonly WebSocket _socket;
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<byte[]> _sendQueue = new();
        private readonly CancellationTokenSource _cts = new();

        public long SessionId { get; }
        public bool IsConnected => _socket.State == WebSocketState.Open;
        public DateTime LastActivityTime { get; private set; } = DateTime.UtcNow;

        /// <summary>待发队列深度（观测用，与 KcpServerSession.QueuedSendCount 同惯例）</summary>
        public int QueuedSendCount => _sendQueue.Count;

        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;

        public WebSocketServerSession(long sessionId, WebSocket socket, ILogger logger)
        {
            SessionId = sessionId;
            _socket = socket;
            _logger = logger;
            _ = ReceiveLoopAsync();
            _ = DrainLoopAsync();
        }

        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            if (!IsConnected) return;
            _sendQueue.Enqueue(MessageCodec.EncodeToArray(messageId, message, flags));
        }

        public void UpdateActivity() => LastActivityTime = DateTime.UtcNow;

        public bool IsTimedOut(TimeSpan timeout) => DateTime.UtcNow - LastActivityTime > timeout;

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[ReceiveBufferSize];
            var accumulated = new System.IO.MemoryStream();
            try
            {
                while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Disconnected?.Invoke(this, EventArgs.Empty);
                        return;
                    }
                    accumulated.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage) continue;      // 分片聚合到一帧

                    var frame = accumulated.ToArray();
                    accumulated.SetLength(0);
                    UpdateActivity();
                    DataReceived?.Invoke(this, frame);       // → Server3C.OnSessionDataReceived → ProcessPacket
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Error?.Invoke(this, ex);
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        private async Task DrainLoopAsync()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    if (!_sendQueue.TryDequeue(out var frame))
                    {
                        await Task.Delay(2, _cts.Token);
                        continue;
                    }
                    if (_socket.State != WebSocketState.Open) return;
                    await _socket.SendAsync(new ArraySegment<byte>(frame),
                        WebSocketMessageType.Binary, endOfMessage: true, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Error?.Invoke(this, ex); }
        }

        public async Task CloseAsync(string reason = "Normal")
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
            }
            catch { /* 关闭路径的异常不值得上抛 */ }
            finally { _cts.Cancel(); }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
            try { _socket.Dispose(); } catch { }
        }
    }
}
```

- [ ] **Step 4: 运行测试验证通过**

```bash
dotnet test Server/Server.Tests/Server.Tests.csproj --nologo -v q
```
预期：基线 132 条 + 新增 4 条 = **136/136 通过**。

- [ ] **Step 5: 实现 `WebSocketServerListener`**

创建 `Server/WebSocketServerListener.cs`：

```csharp
using System;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpServer
{
    /// <summary>
    /// WebSocket 监听器 —— 与 UDP/KCP 同进程并存的第二条传输入口（D-W3）。
    /// 每接受一个连接就建 WebSocketServerSession，并交给 Server3C.RegisterSession 接进事件链；
    /// 之后它触发的 DataReceived 会自动汇入既有的 ProcessPacket，业务侧零改动。
    /// </summary>
    public sealed class WebSocketServerListener : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private readonly Server3C _server;
        private readonly ILogger _logger;
        private readonly Func<long> _nextSessionId;
        private CancellationTokenSource? _cts;

        public WebSocketServerListener(Server3C server, ILogger logger, Func<long> nextSessionId)
        {
            _server = server;
            _logger = logger;
            _nextSessionId = nextSessionId;
        }

        /// <summary>prefix 形如 "http://127.0.0.1:8000/"（本机开发）或 "http://+:8000/"（对外，需 URL ACL，见 Task 9）</summary>
        public void Start(string prefix)
        {
            _listener.Prefixes.Add(prefix);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = AcceptLoopAsync(_cts.Token);
            _logger.LogInformation($"WebSocket listener started on {prefix}");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (_listener.IsListening && !ct.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (Exception) { return; }   // 监听器已停

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;   // 只服务 WS，不服务 HTTP
                    context.Response.Close();
                    continue;
                }

                try
                {
                    var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                    var session = new WebSocketServerSession(_nextSessionId(), wsContext.WebSocket, _logger);
                    _server.RegisterSession(session);     // ← Task 2 Step 6 的接线点
                    _logger.LogInformation($"WebSocket session {session.SessionId} accepted");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WebSocket accept failed");
                }
            }
        }

        public void Dispose()
        {
            try { _cts?.Cancel(); _listener.Stop(); _listener.Close(); } catch { }
            _cts?.Dispose();
        }
    }
}
```

- [ ] **Step 6: 在 `Program.cs` 里起第二个监听**

在 UDP 服务器启动之后、进入主循环之前，起 WS 监听（**UDP 8000 与 TCP 8000 不冲突**）。prefix 默认本机开发用：

```csharp
            // D-W3：同进程双栈 —— UDP(KCP) 给原生客户端，TCP(WS) 给浏览器客户端
            var wsListener = new WebSocketServerListener(
                server, logger, () => Interlocked.Increment(ref _wsSessionIdSeed));
            wsListener.Start(wsPrefix);   // 默认 "http://127.0.0.1:8000/"
```

`wsPrefix` 从命令行参数取，默认 `http://127.0.0.1:8000/`（便于本机验证；对外部署的 URL ACL 见 Task 9）。

> **为什么默认是 `127.0.0.1` 而不是 `localhost`——这是 `HttpListener` 的一个经典坑**：prefix 的 host 段是**字面匹配**请求的 `Host` 头，`http://localhost:8000/` **不**接受发往 `http://127.0.0.1:8000/` 的请求（两者是不同字符串）。而本项目所有客户端都拨 `127.0.0.1`：PlayMode 测试用 `127.0.0.1:8000`、`WebSocketClientAdapter` 拼出的是 `ws://<host>:<port>/`。用 `localhost` 作 prefix 会得到一个**静默不匹配**——`HttpListener` 既不转发也不报错，客户端只看到连接被拒。prefix 必须与客户端实际拨的地址**逐字一致**。

- [ ] **Step 7: 验证双栈共存**

```bash
dotnet run --project Server/Server3C.csproj -- 8000
```

预期：日志同时出现 UDP 与 WS 两个监听已启动。然后：

1. **P6 冻结包**（走 KCP）连 `127.0.0.1:8000` → 能进世界（**证明 KCP 路径未被破坏**——那个包用的就是 `KcpNetClientAdapter`）
2. **Editor 里跑 `DemoDay`**（D-W11 之后走 **WS**，`#if UNITY_WEBGL` 为假 → `DotNetWebSocketChannel`）连同一服务器 → 能进世界。**这是 `DotNetWebSocketChannel` 唯一的端到端验证点**——9 个 PlayMode 测试全部直接构造 `KcpNetClientAdapter`，覆盖不到它
3. **浏览器**（Task 4 的 WebGL 包）连同一服务器 → 也能进世界（**证明 WS 路径在浏览器侧成立**）
4. **2 与 3 同时在线** → 浏览器里能看到 Editor 玩家、Editor 里能看到浏览器玩家（**这是 D-W3 要的活证据**，也是 D-W11"两端同一条路径"的直观确认）

- [ ] **Step 8: Commit**

```bash
git add Server
git commit -m "feat(server): WebSocket session and listener, side by side with KCP

Both transports now feed the same ProcessPacket: a WebSocket session raises
DataReceived, Server3C's existing handler picks it up, and business code never
learns which transport delivered the frame. RegisterSession is the additive hook
the listener uses; the KCP path keeps its inline wiring untouched.

WebSocketServerSession is deliberately thinner than KcpServerSession - TCP
already provides flow control and retransmission, so none of the send window,
drop accounting or conv adoption is needed. Send stays synchronous-queue so the
main loop is never blocked on a network write, which is what lets
IServerSession hold both implementations.

UDP 8000 and TCP 8000 do not collide, so the demo runs one process."
```

---

### Task 6: UI 摇杆 + 重写 `JoystickInputAdapter`

**Files:**
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs`
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileActionButtons.cs`
- **重写**：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/JoystickInputAdapter.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`（加 `SetAdapter`）
- 创建：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs`
- 场景：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 挂 UI
- 测试：`Assets/Scripts/Tests/Editor/MobileInputAdapterTests.cs`

**为什么必须重写**：现有 `JoystickInputAdapter` 读 `"Horizontal_Joystick"` / `"Vertical_Joystick"` / `"Mouse X_Joystick"` 三个轴，而 `ProjectSettings/InputManager.asset` 里**这三个轴都不存在**（实测）——`GetAxisRaw` 对未定义轴会**抛异常**。且第 27 行 `input.normalized` 丢弃模拟量（P6 审查点过同一个坑）。它是**会崩的代码**，不是"写好了没接线"。

- [ ] **Step 1: 写失败的测试（模拟量语义）**

创建 `Assets/Scripts/Tests/Editor/MobileInputAdapterTests.cs`：

```csharp
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Task 6：摇杆适配器的模拟量语义 —— 这是 P6 审查记录过的坑：
    /// 推一半必须走一半，不能被 normalized 抹成全速。
    /// </summary>
    public class MobileInputAdapterTests
    {
        [Test]
        public void test_half_push_keeps_half_magnitude()
        {
            // Arrange（摇杆推到一半）
            var adapter = new JoystickInputAdapter(() => new Vector2(0.5f, 0f), () => Vector2.zero);

            // Act
            Vector3 move = adapter.GetMoveInput();

            // Assert（关键：不 normalize —— 原实现的 normalized 会让推一半也全速）
            Assert.AreEqual(0.5f, Mathf.Abs(move.x), 0.001f, "推一半必须走一半");
            Assert.AreEqual(0f, move.z, 0.001f);
        }

        [Test]
        public void test_dead_zone_returns_zero()
        {
            // Arrange（推到死区内）
            var adapter = new JoystickInputAdapter(() => new Vector2(0.02f, 0f), () => Vector2.zero);

            // Act & Assert
            Assert.AreEqual(Vector3.zero, adapter.GetMoveInput());
            Assert.IsFalse(adapter.HasMoveInput());
        }

        [Test]
        public void test_full_push_reaches_full_magnitude()
        {
            // Arrange
            var adapter = new JoystickInputAdapter(() => new Vector2(0f, 1f), () => Vector2.zero);

            // Act
            Vector3 move = adapter.GetMoveInput();

            // Assert（满推仍是 1，未被放大或钳制错）
            Assert.AreEqual(1f, move.z, 0.001f);
        }

        [Test]
        public void test_look_area_delta_is_forwarded()
        {
            // Arrange
            var adapter = new JoystickInputAdapter(() => Vector2.zero, () => new Vector2(3f, -2f));

            // Act
            Vector2 look = adapter.GetCameraRotationInput();

            // Assert（触屏拖拽量原样透传，灵敏度由相机侧既有参数负责）
            Assert.AreEqual(new Vector2(3f, -2f), look);
            Assert.IsTrue(adapter.HasCameraRotationInput());
        }
    }
}
```

- [ ] **Step 2: 运行测试验证失败**

`tests-run`，`testMode=EditMode`，`testClass=MobileInputAdapterTests`
预期：**编译失败**（`JoystickInputAdapter` 现在的构造是无参的）。

- [ ] **Step 3: 重写 `JoystickInputAdapter`**

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 移动端输入适配器 —— 从 UI 摇杆与触屏观察区取输入。
    /// **重写原因**（见 Task 6 说明）：原实现读三个不存在的 legacy Input 轴
    /// （GetAxisRaw 对未定义轴会抛异常），且 normalized 丢弃模拟量。
    ///
    /// 本类实现既有的 IInputAdapter 契约，成员签名未变 ——
    /// InputManager 与 KeyboardInputAdapter 都不受影响。
    /// 输入源用委托注入，因此本类可在 EditMode 单测（不需要真实 UI）。
    /// </summary>
    public class JoystickInputAdapter : IInputAdapter
    {
        private const float DeadZone = 0.05f;

        private readonly Func<Vector2> _moveSource;
        private readonly Func<Vector2> _lookSource;

        public string AdapterName => "Joystick";

        public JoystickInputAdapter(Func<Vector2> moveSource, Func<Vector2> lookSource)
        {
            _moveSource = moveSource;
            _lookSource = lookSource;
        }

        /// <summary>
        /// 摇杆输入 → 世界方向。
        /// **不 normalize**：保留模拟量，推一半走一半（P6 审查记录的坑）。
        /// 死区以下的抖动视为无输入。
        /// </summary>
        public Vector3 GetMoveInput()
        {
            Vector2 v = _moveSource();
            if (v.sqrMagnitude < DeadZone * DeadZone) return Vector3.zero;
            return new Vector3(v.x, 0f, v.y);   // 注意是 v.y → z（俯视平面）
        }

        /// <summary>触屏拖拽量原样透传（灵敏度由相机侧既有参数负责，不在此再缩放）</summary>
        public Vector2 GetCameraRotationInput() => _lookSource();

        public bool HasMoveInput() => GetMoveInput().sqrMagnitude > 0f;

        public bool HasCameraRotationInput() => GetCameraRotationInput().sqrMagnitude > 0f;
    }
}
```

- [ ] **Step 4: 运行测试验证通过**

`tests-run`，`testMode=EditMode`，`testClass=MobileInputAdapterTests`
预期：**4/4 PASS**。

- [ ] **Step 5: 建 UI 组件**

创建三个 MonoBehaviour（全序列化引用，不用 `GetComponent`）：

**`VirtualJoystick.cs`** —— 实现 `IPointerDownHandler` / `IDragHandler` / `IPointerUpHandler`：

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 虚拟摇杆。Value 是**归一化前的**方向（模长 0–1），
    /// 由 JoystickInputAdapter 读取 —— 它负责死区，本类只管几何。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _background;
        [SerializeField] private RectTransform _handle;
        [SerializeField] private float _radius = 80f;

        private Vector2 _value;
        public Vector2 Value => _value;

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _background, e.position, e.pressEventCamera, out Vector2 local);
            Vector2 clamped = Vector2.ClampMagnitude(local, _radius);
            _handle.anchoredPosition = clamped;
            _value = clamped / _radius;        // 0–1 的模拟量，**不 normalize**
        }

        public void OnPointerUp(PointerEventData e)
        {
            _value = Vector2.zero;
            _handle.anchoredPosition = Vector2.zero;
        }
    }
}
```

**`TouchLookArea.cs`** —— 屏幕右侧的透明拖拽区，暴露 `Delta`：

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>触屏观察区：拖拽量（像素/帧）由 JoystickInputAdapter 读取。</summary>
    public class TouchLookArea : MonoBehaviour, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private float _sensitivity = 0.2f;

        private Vector2 _delta;
        public Vector2 Delta => _delta;

        public void OnDrag(PointerEventData e)
        {
            _delta = e.delta * _sensitivity;
        }

        public void OnPointerUp(PointerEventData e) => _delta = Vector2.zero;
    }
}
```

**`MobileActionButtons.cs`** —— 攻击/技能/跳跃按钮，**调用既有的 `InputManager` 语义**而不是另造一套：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 移动端动作按钮 —— 按下时置位，由 MobileInputBinder 每帧汇入 InputManager 的
    /// 既有按下语义。不自造第二套输入通道。
    /// </summary>
    public class MobileActionButtons : MonoBehaviour
    {
        public bool AttackHeld { get; private set; }
        public bool DefendHeld { get; private set; }
        public bool JumpPressed { get; private set; }

        // 三个按钮在 Inspector 里各绑 OnPointerDown/Up（不在此写死 UI 结构）
        public void OnAttackDown() => AttackHeld = true;
        public void OnAttackUp() => AttackHeld = false;
        public void OnDefendDown() => DefendHeld = true;
        public void OnDefendUp() => DefendHeld = false;
        public void OnJumpDown() => JumpPressed = true;

        /// <summary>由 MobileInputBinder 在每帧末调用，消费掉一次性按下</summary>
        public void ConsumeJump() => JumpPressed = false;
    }
}
```

- [ ] **Step 6: 加 `InputManager.SetAdapter` 与 `MobileInputBinder`**

`InputManager.cs` 增加（**纯增量**，不改变 PC 路径）：

```csharp
        /// <summary>按平台注入适配器 —— 移动端由 MobileInputBinder 调用；不调用则维持键鼠。</summary>
        public void SetAdapter(IInputAdapter adapter)
        {
            if (adapter != null) _adapter = adapter;
        }
```

创建 `MobileInputBinder.cs`（挂在 DemoDay 的 UI 根上）：

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.UI;

namespace Hotfix.GameSystems.Sys3C.Input
{
    /// <summary>
    /// 移动端输入接线 —— 只在触屏平台生效（WebGL 跑在手机上即为触屏）。
    /// 用 Application.isMobilePlatform 判定：PC 浏览器仍走键鼠，不影响既有验收。
    /// </summary>
    public class MobileInputBinder : MonoBehaviour
    {
        [SerializeField] private Sys3CEntry _entry;
        [SerializeField] private VirtualJoystick _joystick;
        [SerializeField] private TouchLookArea _lookArea;
        [SerializeField] private MobileActionButtons _buttons;
        [SerializeField] private GameObject _mobileUiRoot;

        private void Start()
        {
            bool mobile = Application.isMobilePlatform;
            if (_mobileUiRoot != null) _mobileUiRoot.SetActive(mobile);
            if (!mobile || _entry == null) return;

            var adapter = new JoystickInputAdapter(
                () => _joystick != null ? _joystick.Value : Vector2.zero,
                () => _lookArea != null ? _lookArea.Delta : Vector2.zero);
            _entry.InputManager.SetAdapter(adapter);
        }
    }
}
```

> `Sys3CEntry.InputManager` 是私有字段（`:44 private InputManager _inputManager;`）——需要暴露一个只读访问器，或让 binder 通过 `GetComponent<InputManager>()`（`Sys3CEntry` 是 MonoBehaviour，其所在对象上就有 `InputManager`，见 `:125` 的 `GetComponent<InputManager>()`）。**用后者更省改动**；实现时择一并说明。

- [ ] **Step 7: 场景挂载（用 Unity MCP）**

在 `DemoDay.unity` 里：

1. 建 `MobileUiRoot`（含 `Canvas`，`Screen Space - Overlay`，`CanvasScaler` 设 `Scale With Screen Size` + 参考分辨率 1920×1080）
2. 其下建 `Joystick`（`VirtualJoystick`，背景 + 手柄两个 `Image`）与 `LookArea`（右侧半屏透明 `Image` + `TouchLookArea`，`raycastTarget = true`）
3. 建攻击/防御/跳跃三个 `Button`，各绑 `MobileActionButtons` 的对应方法
4. 在场景里挂 `MobileInputBinder` 并连好四个引用
5. `_mobileUiRoot` 默认**不激活**（PC 上不显示；`Start` 里按平台开关）

- [ ] **Step 8: 跑全量 EditMode 与 PC 侧 PlayMode 回归**

预期：EditMode **232/232**（228 + 4）；PlayMode 三配置并集 **18/18**（PC 侧不受影响——`isMobilePlatform` 在编辑器为 false）。

- [ ] **Step 9: Commit**

```bash
git add Assets
git commit -m "feat(input): touch controls, and rewrite JoystickInputAdapter to work at all

The old JoystickInputAdapter read Horizontal_Joystick, Vertical_Joystick and
Mouse X_Joystick - none of which exist in InputManager.asset, so GetAxisRaw
would throw the moment it was constructed. It also normalized its output,
turning a half-pushed stick into full speed, the same defect P6's review
recorded.

Rewritten against injected input sources, so the analog semantics are unit
tested without any UI present. The IInputAdapter contract is unchanged and
KeyboardInputAdapter is untouched; MobileInputBinder only swaps the adapter on
touch platforms, so PC browsers and the editor keep the existing path."
```

---

### Task 7: 触屏相机

**Files:**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs`（若有需要）

**说明**：本任务**可能很薄**——P6 的 Task 7 已经把相机接线做完了（`Sys3CEntry.cs:173-177` 每帧调 `_camera.HandleRotationInput(_inputManager.GetCameraRotationInput())`），而 Task 6 的 `JoystickInputAdapter.GetCameraRotationInput()` 已经返回触屏拖拽量。**所以链路已经通了，本任务只剩验证**。

- [ ] **Step 1: 确认链路（不写代码，先读）**

逐跳确认：

```
TouchLookArea.OnDrag  →  Delta
JoystickInputAdapter.GetCameraRotationInput()  →  Delta
Sys3CEntry.Update → _camera.HandleRotationInput(_inputManager.GetCameraRotationInput())
ThirdPersonCameraController.HandleRotationInput → 改 _horizontalAngle / _verticalAngle
```

若四跳都成立，**本任务无需改代码**，直接进 Step 2 验证。若某跳断了，补上它并在报告里说明。

- [ ] **Step 2: 真机验证（人工）**

在**手机浏览器**打开 Task 4 部署的 WebGL 包：
- **预期**：右侧半屏拖拽 → 视角转动；松开后停止
- **预期**：左侧摇杆拖动 → 角色移动，且**半推走慢、满推走快**（验证模拟量没被丢）

- [ ] **Step 3: 若 Step 1 发现断链则修，否则本任务只留验证记录**

若无代码改动，**不产生提交**——在报告里写明"链路已通、无改动"，避免制造空提交。

- [ ] **Step 4: （仅在改了代码时）Commit**

```bash
git add Assets
git commit -m "fix(camera): wire touch look input through to camera rotation"
```

---

### Task 8: 在 WebGL 形态下复跑声称面验收

**Files:** 无（本任务是**验收**，不是实现）

**判据来源**：`docs/superpowers/guides/p6-human-acceptance.md`——取其中与四条声称面相关的项，在**浏览器**里逐条复现。**判据本身不变**，只是点击方式从键鼠变成触屏。

- [ ] **Step 1: 起服务器（带怪物与 bot）**

```bash
dotnet run --project E:/CodeForJob/Cool/Server/Server3C.csproj -- 8000 --monsters 4 --bots 100
```

> `--monsters 4` 与 `--bots 100` **同时**开，才能一次覆盖 P3（100 个远程玩家）与 P5（服务器怪物）。

- [ ] **Step 2: 桌面浏览器逐条复现（先桌面，排除触屏变量）**

在 Chrome/Edge 打开 WebGL 包，按 `p6-human-acceptance.md` 的 **A 组（单机玩法）+ C 组（联网）+ D 组（表现）** 逐条走。特别确认：

| 声称面 | 对应验收项 | 预期 |
|---|---|---|
| **P6 单机玩法** | A3 / A4 / A5 / A6 / A7 / A8 | 打怪有反馈、被打有受击+击退、死亡后不能动且不施法、约 3 秒复活、相机可转不穿墙、刷怪不爆炸 |
| **P4 服务端权威战斗** | C4 + 战斗中伤害来自服务端 | 打怪伤害由 `DamageResult` 驱动（不是本地算的） |
| **P3 AOI 大世界与带宽** | C4 的 `远程实体` 计数 | `--bots 100` 时浮层 `远程实体` 显著增大；走近/走远有进出 |
| **P5 服务器驱动怪物 AI** | C4 + 怪物行为 | 怪物巡逻/追击/攻击；**杀掉服务器后怪物消失**（客户端断线回菜单） |

- [ ] **Step 3: 手机浏览器复现同一批（触屏变量）**

在**真实手机**上打开同一 URL，重复 Step 2 的关键项，并**记录帧率**（R4）：

- 用浏览器开发者工具或屏幕上的 FPS 计数（若既有无则目测：是否明显卡顿）
- **判据**：`--bots 100` 下能操作、不卡死即通过；若明显不可用，**P3 的声称降级为"桌面浏览器演示"**并在 T10 的话术卡里写明

- [ ] **Step 4: 记录结果与差集**

把逐条结果写进报告，**特别列出所有失败的项**——它们是后续修复的输入（按依赖序，修复排在 T9 之前或之后由失败严重度决定）。

- [ ] **Step 5: 无代码改动则无提交**

本任务只产出验收记录；若发现必修的问题，**另开修复任务**并在报告里写明。

---

### Task 9: `WebGLBuildScript` + 部署

**Files:**
- 创建：`Assets/Scripts/Editor/WebGLBuildScript.cs`
- 创建：`docs/superpowers/guides/webgl-demo-runbook.md`
- 仓库外：静态托管 + 域名 + 证书

- [ ] **Step 1: 写构建脚本**

创建 `Assets/Scripts/Editor/WebGLBuildScript.cs`，与既有 `BuildScript.cs` 同构（菜单 + batchmode 双入口）：

```csharp
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Cool.EditorTools
{
    /// <summary>
    /// WebGL 构建 —— 面试演示的交付入口。
    /// 与 BuildScript.BuildWindows 同构：菜单 + batchmode 双入口，场景列表显式声明。
    ///
    /// 压缩选 Disabled：部署层的反向代理（Task 9 Step 3）负责 gzip/brotli，
    /// 双重压缩会报错，且禁用后构建更快、便于本地排查。
    /// </summary>
    public static class WebGLBuildScript
    {
        private const string OutputDir = "Build/WebGL";

        private static readonly string[] Scenes =
        {
            "Assets/Scenes/CharacterSelect.unity",
            "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity",
        };

        [MenuItem("Cool/Build/WebGL")]
        public static void BuildWebGL()
        {
            foreach (var scene in Scenes)
            {
                if (!File.Exists(scene))
                {
                    Debug.LogError("[Build] 构建场景缺失: " + scene);
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                    return;
                }
            }

            Directory.CreateDirectory(OutputDir);

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            var summary = BuildPipeline.BuildPlayer(options).summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[Build] WebGL 出包失败: result=" + summary.result
                    + " errors=" + summary.totalErrors);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            Debug.Log("[Build] WebGL 出包成功: " + summary.outputPath
                + " size=" + summary.totalSize + "B");
        }
    }
}
```

- [ ] **Step 2: 出包**

```bash
Unity.exe -batchmode -quit -projectPath E:/CodeForJob/Cool \
  -executeMethod Cool.EditorTools.WebGLBuildScript.BuildWebGL -logFile -
```

预期：`Build/WebGL/` 下产出 `index.html` + `Build/` 子目录（wasm/data/framework）+ `TemplateData/`。

**并记录首屏总体积**（`du -sh Build/WebGL`）——D-W4 说不优化，但**记一个数不留成本**，供你后期清理资源时对照。

- [ ] **Step 3: 部署**

写进 `docs/superpowers/guides/webgl-demo-runbook.md`，内容至少含：

1. **静态托管**：`Build/WebGL/` 的全部文件（需 HTTPS——浏览器对 `ws://` 从 HTTPS 页面发起会拦截，故必须配 WSS 或本地开发用 http）
2. **反向代理**：把 `wss://<域名>/ws` 转发到 `tcp://127.0.0.1:8000`（WebSocket Upgrade 透传），静态文件走同一域名
3. **证书**：Let's Encrypt（`certbot`）
4. **Windows URL ACL**（若 `HttpListener` 用 `http://+:8000/`）：
   ```
   netsh http add urlacl url=http://+:8000/ user=Everyone
   ```
   若用 `http://localhost:8000/` 则仅本机可达——**对外部署必须用 `+` 或具体 IP**
5. **启动命令**：
   ```
   dotnet run --project Server/Server3C.csproj -- 8000 --monsters 4 --bots 100
   ```
6. **停服**：`taskkill //F //PID <pid>`（或 Ctrl+C，注意 Windows 上后者未必给 .NET 优雅退出）

> **WSS 的落地方式**：客户端 `WebSocketClientAdapter.ConnectAsync` 目前拼的是 `ws://host:port/`。**生产要走 `wss://`** —— 在 runbook 里写明如何让页面用同源 `wss://<域名>/ws` 连接（最简做法：客户端在 `https:` 页面下自动把 `ws://` 换成 `wss://`，并连 `location.host`）。**这一处需要在 Task 9 的实现里改 `ConnectAsync` 的 URI 拼装**，不是纯文档。

- [ ] **Step 4: 用局外设备验证**

让一台**不在这台机器上**的设备（手机 4G / 另一台电脑）打开 URL。

预期：能加载、能进世界、能动。**这一步是"面试官自己玩"这个成功判据的直接检验**，不可用本机自测代替。

- [ ] **Step 5: Commit**

```bash
git add Assets docs
git commit -m "feat(webgl): WebGL build script and deployment runbook

Same shape as the Windows build script - menu plus batchmode, explicit scene
list, exit-on-failure guarded by isBatchMode. Compression is disabled here
because the reverse proxy handles gzip/brotli and double-compressing breaks the
build.

The runbook covers the parts that are not code: HTTPS static hosting, the
WebSocket upgrade forward, Let's Encrypt, and the Windows URL ACL that
HttpListener needs before it will bind a non-localhost prefix."
```

---

### Task 10: 录屏 + 已知边界话术卡

**Files:**
- 创建：`docs/superpowers/guides/webgl-demo-script.md`
- 仓库外：一段录屏

- [ ] **Step 1: 写演示脚本**

`webgl-demo-script.md` 要回答：**8–10 分钟里出现哪几个画面、按什么顺序、每屏一句话**。结构建议（按声称面组织，每项 1–2 分钟）：

| 顺序 | 画面 | 一句话 |
|---|---|---|
| 1 | 打开 URL → 进世界 | "浏览器里跑，没有安装" |
| 2 | 单机打怪（受击/击退/死亡/复活） | "P6 把战斗反馈打通了" |
| 3 | 相机转动 + 贴墙不穿模 | "相机从零接的线" |
| 4 | 切联网 → 输入地址 → 进世界 | "同一套客户端，两条传输" |
| 5 | 服务器 `--monsters 4` 的怪物巡逻/追击 | "AI 在服务器跑" |
| 6 | `--bots 100` 的远程实体计数 | "AOI + 分级节流" |
| 7 | 打怪，指出伤害来自服务端 | "伤害是服务器掷的骰" |
| 8 | 杀掉服务器 → 3 秒回菜单 | "断线有出口" |

- [ ] **Step 2: 写"已知边界"话术卡**

**主动讲出来比被问倒好得多。** 至少含这 5 条（前 4 条 P6 已登记，第 5 条是本次新增）：

| # | 边界 | 说法 |
|---|---|---|
| 1 | 服务器怪物是**球体占位**、远程玩家是**胶囊占位** | "表现层是 backlog，同步逻辑是真的" |
| 2 | **死亡无动画**（`Death`/`IsDead` 参数在 Animator 里不存在） | "死亡的表现层属 backlog，行为门禁是真的" |
| 3 | **首屏加载慢**（包体未优化） | "资源没清过，是已知项" |
| 4 | **演示 URL 无鉴权** | "演示形态，正式版要加" |
| 5 | **标签页切后台会断线** | "移动端浏览器会杀后台标签，演示时不切" |

- [ ] **Step 3: 录制兜底录屏**

按 Step 1 的脚本**一镜到底**录 8–10 分钟，覆盖全部四条声称面。

> **录屏本身就是最强的验收**——能一镜到底录完，等于证明了声称面成立。**录的过程中发现的任何崩/卡，都要回头修**（并另开任务）。

- [ ] **Step 4: Commit**

```bash
git add docs
git commit -m "docs(webgl): demo script and known-boundaries card

The script is ordered by claim, not by feature, because the interviewer will
poke rather than follow along. The boundaries card covers five items - orb
monsters, capsule players, no death animation, unoptimised first load, no auth,
and the tab-backgrounding disconnect - so they get stated rather than
discovered."
```

---

## 执行偏差与验收记录（执行时回填，Task 10 Step 4 之后）

> 待执行时填写。格式与既有 P6 计划一致：逐任务列 commit、偏差（含现场发现与处理）、三套测试实际通过数、Task 8 的验收实测结果、本批次新发现的 backlog 项。

---

## Self-Review（撰写自查）

**1. 规格覆盖度**——规格的 10 个任务与 13 个组件逐项有落点：

| 规格项 | 任务 |
|---|---|
| T1 解除构建阻塞（C1/C2/C3） | Task 1 |
| T2 `IServerSession`（C6） | Task 2 |
| T3 `WebSocketClientAdapter`（C4） | Task 3 |
| T4 jslib + 浏览器 echo（C5） | Task 4 |
| D-W11 两端统一 WS（PC 也走 `WebSocketClientAdapter`） | Task 4 Step 4（落点）；Task 5 Step 7 第 2 项（验证） |
| T5 WS 会话与监听（C7/C8） | Task 5 |
| T6 触屏输入（C9） | Task 6 |
| T7 触屏相机（C10） | Task 7 |
| T8 声称面验收 | Task 8 |
| T9 构建与部署（C11/C12） | Task 9 |
| T10 演示制品（C13） | Task 10 |
| §5 错误处理 | 复用 P6 的屏幕出口；Web 特有两条进 T10 话术卡 |
| §7 测试策略 | 各任务的测试步骤 + T3 的假通道分层 + T8 人工验收 |
| §8 风险 | R2 在 T4 用两条取舍降险；R3 在 T9 runbook；R4 在 T8 Step 3 |

**2. 写计划过程中发现并修正的规格缺陷**（均已回改规格）：

- **D-W2 的两个动作行不通**：asmdef 引用不可平台条件化；排除程序集会让 PC 端也引用不到 adapter。已改为 `#if` 守卫 + 平台选择（规格 F10/F11 记录）
- **`JoystickInputAdapter` 不是"未接线"而是"会崩"**：它读的三个轴不存在（实测），`GetAxisRaw` 会抛异常。规格 F7/D-W5/C9/T6 已订正
- **`RemoteEndPoint` 泄进业务**：`Server3C.OnSessionDisconnected` 用它从 `SessionManager` 移除会话，而 WS 无 EndPoint。Task 2 Step 4 改为按 `SessionId` 移除
- **PC 默认走 KCP 是零收益，且让两条路径分叉**（人类裁定后修正）：实测 4 个客户端发送点（`ClientNetworkManager.cs:58/111`、`CombatNetworkBridge.cs:49`、`MovementReporter.cs:65`）全部取 `INetworkClient.Send` 的默认值 `MessageFlags.Reliable`——**KCP 的不可靠通道是死代码**，而队头阻塞对可靠有序数据是 KCP 与 TCP 共有的。原形态下"PC 自测走 KCP、演示走 WS"意味着**在 PC 上验的每一件事走的都是演示时不存在的那条路**。已改为 D-W11：两端统一走 WS，`KcpNetClientAdapter` 保留为第二实现（T4 Step 4 落点，T5 Step 7 第 2 项验证）

**3. 占位符扫描**——已清除。Task 7 明确写了"若无代码改动则不产生提交"，避免制造空提交。

**4. 类型一致性**——跨任务引用的符号已核对：

| 符号 | 定义处 | 使用处 |
|---|---|---|
| `INetworkClient`（`Cool.Protocol`） | Task 1 Step 1 | Task 3 适配器、Task 1 Step 6 |
| `IWebSocketChannel` / `DotNetWebSocketChannel` / `WebGLWebSocketChannel` | Task 3 Step 1/6、Task 4 Step 3 | Task 3 Step 4 适配器构造 |
| `WebSocketNetBridge`（`OnFrame`/`OnClose`/`OnError`/`OnOpen`） | Task 4 Step 2 | Task 4 Step 1 的 jslib **字符串必须逐字一致** |
| `IServerSession` | Task 2 Step 1 | Task 2 Step 2/3/5/6、Task 5 会话实现 |
| `Server3C.RegisterSession` | Task 2 Step 6 | Task 5 Step 5 监听器 |
| `SessionManager.RemoveById` | Task 2 Step 4 | Task 2 Step 4 的 `Server3C:410` |
| `JoystickInputAdapter(Func<Vector2>, Func<Vector2>)` | Task 6 Step 3 | Task 6 Step 1 测试、Task 6 Step 6 binder |
| `VirtualJoystick.Value` / `TouchLookArea.Delta` | Task 6 Step 5 | Task 6 Step 6 binder |

**5. 已知的落地假设（执行时首次接触须确认，均有兜底）**：

| 假设 | 若不符的处理 |
|---|---|
| `Server3C` 的事件接线在 `:330-333` 内联 | 按实际接线点调整 Task 2 Step 6 的 `RegisterSession` |
| `WebSocket` 抽象类可被继承以做测试假件 | 若不可，改用 `ClientWebSocket` 对本地 `HttpListener` 起真连接——但需先确认 Unity 侧 `HttpListener` 可用 |
| `Program.cs` 的主循环结构 | `wsListener` 的启动点按实际插；关键是**在 UDP 启动之后、主循环之前** |
| `Sys3CEntry.InputManager` 是私有字段 | 用同对象上的 `GetComponent<InputManager>()`（`:125` 已在用），或加只读访问器 |
| Unity 已安装 WebGL 模块 | Task 1 Step 8 会失败并提示——**停下报 BLOCKED**，不跳过 |
| `EchoRequest` / `EchoResponse` 的字段名 | 按实际类型改测试，**不改测试意图** |




