# WebGL 面试演示版 设计

> **它是什么**：把项目从"PC 包自己跑"推进到"**面试官用自己的设备打开一个链接就能玩**"。
> P1–P6 已完成（服务端权威同步、AOI、战斗裁决、怪物 AI、PC 交付包）。本设计解决的是**交付形态**问题：真正的目标平台是手机/浏览器，不是 PC。

## 0. 前置结论（本设计的事实基础）

以下均为 2026-10-07 在本仓库实测/读码得出，不是推测。

| # | 事实 | 影响 |
|---|---|---|
| F1 | `INetworkClient` 只有 **7 个成员**，且`ClientNetworkManager` / `CombatNetworkBridge` 依赖的是**接口**而非 `KcpClient`；测试里已有 `RecordingClient : INetworkClient` 假实现 | 抽象可用，加一条传输通道是"加实现"而非"重写" |
| F2 | `MessageCodec`（305 行）是**纯静态** `Encode(…, IBufferWriter<byte>)` / `TryDecode(byte[])` | 编解码零改动 |
| F3 | 应用层**从未使用** KCP 的非可靠通道——4 个客户端发送点（`ClientNetworkManager.cs:58/111`、`CombatNetworkBridge.cs:49`、`MovementReporter.cs:65`）全部使用 `INetworkClient.Send` 的默认值 `MessageFlags.Reliable`；服务端 `SendToSessionAsync` 默认 `Reliable`；唯一非 Reliable 的用法在 `Assets/Scripts/Examples/`（示例代码，不进游戏） | ① **WebSocket(TCP) 的语义与现有实际行为一致**，无"换 TCP 反而变差"的落差；② 对"有序 + 可靠"的数据，KCP 相对 TCP 的唯一优势是更激进的 RTO（~100ms vs 200–300ms）——**队头阻塞两者都有**，不是 KCP 的优势。故"PC 走 KCP"不带来任何应用层收益（见 D-W11） |
| F4 | `INetworkClient.cs` 位于 `Assets/Scripts/AOT/KcpNet/`，即**住在 `KcpNet` 程序集内** | 见 F6——这是本设计的核心阻塞点 |
| F5 | `KcpNet.asmdef` 的 `includePlatforms` / `excludePlatforms` **均为空** → 所有平台都编译；而 `KcpClientTransport` 使用 `System.Net.Sockets` | WebGL 无 `System.Net.Sockets` → **WebGL 构建编译失败** |
| F6 | `GameSys.asmdef`（Hotfix）`references: ["KcpNet"]`，且 5 个文件 `using KcpNet`；`ClientNetworkHost.cs:181` 直接 `new KcpNetClientAdapter()` | 抽象被实现绑架：只要 `GameSys` 引用 `KcpNet`，WebGL 就**打不出包** |
| **F10** | `KcpNet` 里对 `System.Net.Sockets` 的依赖**贯穿 6 个文件**——`Client/KcpClientTransport`、`Common/KcpTransportBase`、**`Common/Kcp.cs`（KCP 算法本体，实现 `System.Net.Sockets.Kcp.IKcpCallback`）**，以及 `Server/` 下 3 个给 PlayMode 用的进程内服务器文件 | 排除整个 `KcpNet` 程序集是**唯一**能解除 `System.Net.Sockets` 的办法；靠 `#if` 掏空体积很大 |
| **F11** | **asmdef 的引用不能按平台条件化**。若 `GameSys` 不引用 `KcpNet`，则 **PC 端也看不到** `KcpNetClientAdapter`；而"平台专属 provider 程序集 + 场景注入"绕不开——`DemoDay` 场景是 PC 与 WebGL **共享**的，挂不下两种平台专属 provider | 原定的"排除程序集 + 移除引用"两动作**行不通**，T1 形态据此修正（见 D-W2） |
| F7 | `IInputAdapter` 抽象**已存在且干净**（5 个成员，`KeyboardInputAdapter.cs:8`）。但 `JoystickInputAdapter`（71 行）**不是可用实现**——它读 `Horizontal_Joystick` / `Vertical_Joystick` / `Mouse X_Joystick` 三个轴，而 `ProjectSettings/InputManager.asset` 里**这三个轴都不存在**（实测），`GetAxisRaw` 对未定义轴会**抛异常**；且它第 27 行 `input.normalized` 丢弃模拟量（P6 审查点过同一个坑）。全项目从未实例化它（`InputManager.cs:40` 硬编码 `new KeyboardInputAdapter()`） | **抽象可复用，实现要重写**——不是"接线" |
| F8 | `MessageFlags` 定义在 `Cool.Protocol`（`MessageCodec` 签名使用它），该程序集无 socket 依赖 | `Cool.Protocol` 是搬运 `INetworkClient` 的落点 |
| F9 | 服务端业务代码只把 session 当**不透明句柄**传给 `SendToSessionAsync`；但 `Server3C.cs` 有 8 处 `KcpServerSession` 具体类型引用，`SessionManager.cs` 整个围绕它写 | 服务端需抽 `IServerSession`（机械替换） |

## 1. 目标与成功判据

### 目标

在**视频面试**中，让面试官**用自己的设备**打开一个 URL 就能玩到本项目，并能现场复现你要声称的技术能力。

### 成功判据（这就是"完成"的定义）

1. 面试官收到**一个 URL**，用自己的设备（iOS / Android / 桌面浏览器任一）打开**就能玩**
2. 能复现声称面：**P6 单机玩法 + P4 服务端权威战斗 + P3 AOI（`--bots 100`）+ P5 怪物 AI**
3. **无崩溃、无卡死、无需刷新**
4. 有一段**录屏兜底**（现场失败可放）
5. 有一张**"已知边界"话术卡**，被问到能主动讲清

## 2. 范围边界

### 做

| 层 | 内容 |
|---|---|
| **解除构建阻塞** | `INetworkClient` 搬到 `Cool.Protocol`；`KcpNet` 下 6 个 socket 文件加 `#if !UNITY_WEBGL`；`ClientNetworkHost` 按平台选择 **channel**（adapter 两端统一）；新增 `WebSocketNet` 程序集（见 D-W2、D-W11） |
| **传输通道** | **所有平台**的客户端都走 `WebSocketClientAdapter : INetworkClient`——PC/编辑器用 `DotNetWebSocketChannel`，WebGL 用 `WebGLWebSocket.jslib`；`KcpNetClientAdapter` 保留为第二实现但不再是默认（见 D-W11） |
| **服务端双栈** | `IServerSession` 抽取 + `WebSocketServerSession` + `WebSocketServerListener`（与既有 UDP/KCP 同进程并存） |
| **移动端输入** | `MobileInputAdapter`（摇杆）+ `TouchCameraInput`（触屏转视角）+ 攻击/技能/跳跃按钮 |
| **交付** | WebGL 构建脚本 + 静态托管 + WSS 域名/证书 |
| **演示制品** | 录屏、已知边界话术卡 |

### 明确不做

微信小游戏（同一套 WS 通道将来可复用，只差微信账号与类目审核）· P7 打怪升级 · P8 PvP · P2 移动同步的现场演示 · 原生 Android/iOS 打包 · **包体优化与首屏加载测量**（人类裁定后期清理资源后再做）· 聊天/背包/组队/交易 · **鉴权**（演示形态下明确不做——公网暴露意味着任何人知道 URL 即可连，风险在 R3 显式接受，见 D-W7）

## 3. 架构

### 数据流

```
[面试官的浏览器]                          [你的 PC 构建（自我验证）]
  Unity WebGL 构建                          Unity Standalone 构建
    MessageCodec / ClientNetworkManager / CombatNetworkBridge /
    RemoteEntityRegistry / NetMovementPolicy          ← 两端同一份，零改动
    WebSocketClientAdapter : INetworkClient           ← 两端同一个适配器（成员签名一字不改）
      ├─ WebGLWebSocketChannel → WebGLWebSocket.jslib ← 仅 WebGL（T4）
      └─ DotNetWebSocketChannel（ClientWebSocket）    ← PC/编辑器（T3）
  ──────────────────────── WSS / WS ────────────────────────
[你的服务器 .NET 8，单进程双栈]
  WebSocketServerListener → WebSocketServerSession : IServerSession   ← 新增，两端共用
  UdpSession / KcpServerSession : IServerSession                      ← 既有，保留（KCP 代码本次不删）
        ↓ 两条来源汇入同一个 ProcessPacket（复用）
  Handler / AoiGrid / EntityStore / CombatResolver / MonsterModule     ← 全部零改动
```

### 双栈为什么是白送的

`IServerSession` 的抽取是 WS 路径**本来就必须做**的（`Server3C` 不能再依赖 `KcpServerSession` 具体类型）。抽完之后，两条传输来源汇入同一个既有的 `ProcessPacket(session, data)`——**没有额外的架构工作**。

双栈带来的收益：PC 构建与浏览器客户端**连同一个服务器进程、且走同一条 WS 通道**——演示时"PC 端 + 浏览器端同场"本身是 AOI 同步与战斗裁决的活证据；更重要的是，**在 PC 上做的自我验证与面试官在浏览器里跑的，是同一条代码路径**（D-W11）。

> UDP/KCP 监听之所以保留，是"本次不删 KCP 代码"这条人类裁定（D-W11），**不是**因为它还被默认使用——默认路径已经全部是 WebSocket。

### 接口搬运的边界（重要）

`INetworkClient` **成员签名一字不动**，只是从 `KcpNet` 程序集搬到 `Cool.Protocol`。因此：

- `RecordingClient`（测试假实现）**不需要改**
- `ClientNetworkManager` / `CombatNetworkBridge` 的构造与调用**不需要改**（只改一行 `using`）
- 既有 `KcpNetClientAdapter` **不需要改**（保留为第二实现，但不再是 PC 默认——见 D-W11）

搬家的目的正是**保住这条约束**——让它不再被 KCP 的实现细节与平台限制绑架。

## 4. 组件分解

| # | 组件 | 位置 | 职责 | 可独立测试 |
|---|---|---|---|---|
| C1 | `INetworkClient`（搬运） | `AOT/Protocol/` | 传输抽象，供两端实现 | 编译即验证 |
| C2 | 6 个 socket 文件加 `#if !UNITY_WEBGL` | `AOT/KcpNet/` | `KcpClientTransport` / `KcpTransportBase` / `Kcp.cs` / `Server/` 下 3 个 | 构建即验证 |
| C3 | **channel** 平台选择 | `Hotfix/.../SyncLayer/ClientNetworkHost` | adapter **两端统一**为 `WebSocketClientAdapter`；`#if UNITY_WEBGL` → `WebGLWebSocketChannel`，否则 `DotNetWebSocketChannel` | 构建即验证 |
| C4 | `WebSocketClientAdapter` | `AOT/WebSocketNet/`（新程序集） | `INetworkClient` 的 WS 实现：连接、收发、心跳、事件 | ✅ **EditMode 可测**——`ClientWebSocket` 在编辑器里可用，只有 WebGL 构建不可用 |
| C5 | `WebGLWebSocket.jslib` | `Assets/Plugins/WebGL/` | **单向**边界：C# → 浏览器 `WebSocket`（`Connect`/`Send`/`Poll`/`GetState`/`GetCloseCode`/`GetDropped`/`Close`）；JS 侧回调只改状态、**不回调 C#** | ✅ 状态机 **Node 单测**（`tests/webgl/`）；⚠️ 7 个入口函数与浏览器运行时人工（R2） |
| C6 | `IServerSession`（抽取） | `Server/` | 业务需要的会话契约：`SessionId` / `Send(MessageId, IMessage, MessageFlags)` / `IsConnected` / `LastActivityTime` | 既有测试即验证 |
| C7 | `WebSocketServerSession` | `Server/` | `System.Net.WebSockets` 收发（比 KCP 版简单——TCP 自带流控） | Server.Tests |
| C8 | `WebSocketServerListener` | `Server/` | 接受 WS → 建会话 → 喂 `ProcessPacket` | ⚠️ 集成测 |
| C9 | `JoystickInputAdapter` **重写** + UI 摇杆/按钮 | `Hotfix/.../Sys3C/Input/` + UI | 触屏输入：UI 摇杆组件提供 `Vector2`，适配器读它并**保留模拟量**（不 normalize）；**`IInputAdapter` 契约不变** | EditMode（模拟量解析） |
| C10 | `TouchCameraInput` | `Hotfix/.../Sys3C/Camera/` | 触屏拖拽 → 既有 `HandleRotationInput` | ⚠️ 真机人工 |
| C11 | `WebGLBuildScript` | `Assets/Scripts/Editor/` | WebGL 构建 + 打包 | ⚠️ 人工 |
| C12 | 部署 | 仓库外 | 静态托管 + WSS 域名 + 证书 | ⚠️ 人工（R3） |
| C13 | 演示制品 | `docs/` | 录屏、已知边界话术卡 | ⚠️ 人工 |

## 5. 错误处理

**复用 P6 已交付的屏幕出口**（`ClientNetworkHost` 的 `_pendingReturnToMenu` + `OnGUI` 状态提示，见 P6 设计 §6）。Web 形态下新增两条：

| 场景 | 处理 |
|---|---|
| **浏览器标签页切后台** | 浏览器会暂停 WebGL 与渲染；切回时可能已被服务器超时清理。**列为已知边界话术**（面试时不切标签），不做自动恢复 |
| **移动端浏览器杀后台标签** | 同上。演示时若需切 App（如看简历），提前说明 |
| 首屏加载慢 | 加载期间显示进度，不显示空白（最小要求）；**优化本身出范围**（D-W4） |

## 6. 依赖序任务清单（不排日历）

按依赖排序。**T1 是硬门槛——不做完，后面全部无法验证。**

### L0 · 让 WebGL 构建能成立

| # | 任务 | 规模 | 验收 |
|---|---|---|---|
| **T1** | `INetworkClient` 搬到 `Cool.Protocol`（命名空间随之改为 `Cool.Protocol`）；`KcpNet` 下 6 个 socket 文件加 `#if !UNITY_WEBGL`；`ClientNetworkHost` 用 `#if UNITY_WEBGL` 选择 adapter（**过渡态**：T1 时 PC 侧仍是 KCP 占位，通道在 T3 补、PC 侧统一在 T4 完成——见 D-W11）；新增 `WebSocketNet` 程序集（**所有平台**，先放一个占位实现供 T3 填充）。<br>**注意**：`GameSys.asmdef` 的 `KcpNet` 引用**保留不动**；`KcpNet.PlayModeTests.asmdef` 亦不动 | 中 | ① 原 PC/Editor 构建与全部测试**照常通过**；② **WebGL 平台能编译**（空场景构建足以证明 `System.Net.Sockets` 不再是阻塞；真实场景的 WebGL 构建在 T4/T9 验证） |

### L1 · 打通 WS 通道（面试官"能连上"）

| # | 任务 | 规模 | 验收 |
|---|---|---|---|
| **T2** | 抽 `IServerSession`，`Server3C`（8 处）与 `SessionManager` 改依赖接口 | 小 | Server.Tests 132/132 全绿（纯机械替换，行为不变） |
| **T3** | `WebSocketClientAdapter : INetworkClient` + `IWebSocketChannel` / `DotNetWebSocketChannel`（新程序集 `WebSocketNet`） | 中 | EditMode：经**假通道**（`FakeChannel`）跑通连接/收发/心跳/断开事件——真实往返由 T4（浏览器）与 T5（集成）覆盖 |
| **T4** | `WebGLWebSocket.jslib`（**C# 拉模式，JS 不回调 C#**）+ 浏览器验证；**并把 PC 也切到 `WebSocketClientAdapter`（D-W11 的落点）** | 中 | ① `node --test tests/webgl/websocket-state.test.js` 全绿（状态机 11 条）；② 浏览器里 WS 帧**双向流动**（**本任务的主要风险在 jslib 边界，R2**）；③ 切换后 PlayMode 并集仍 18/18（既有 PlayMode 测试直接构造 `KcpNetClientAdapter`，不经 `ClientNetworkHost`，故不受影响） |
| **T5** | `WebSocketServerSession` + `WebSocketServerListener`（双栈） | 中 | 浏览器客户端 + PC 包**同时**连同一服务器（**两端都走 WS**），互相可见（AOI 双端渲染） |

### L2 · 让它在手机上能玩

| # | 任务 | 规模 | 验收 |
|---|---|---|---|
| **T6** | UI 摇杆/按钮 + **重写** `JoystickInputAdapter` 读它 | 中 | 手机浏览器能移动、攻击、放技能；**摇杆模拟量不被 normalize 丢弃**；`IInputAdapter` 契约与 `KeyboardInputAdapter` 均不受影响 |
| **T7** | `TouchCameraInput` | 小 | 手机浏览器能拖屏转视角（复用 P6 的 `HandleRotationInput`） |

### L3 · 让"声称面"在浏览器里成立

| # | 任务 | 规模 | 验收 |
|---|---|---|---|
| **T8** | 在 WebGL 形态下复跑声称面验收 | 中 | **对照现成清单** `docs/superpowers/guides/p6-human-acceptance.md`，取其中与四条声称面相关的项（A 组单机全部、C 组联网全部、D 组表现两项）在**浏览器里逐条复现**；并**记录手机端帧率**（R4）。<br>该清单是为 PC 包写的——**浏览器形态下的点击方式不同**（触屏而非键鼠），判据本身不变 |

### L4 · 交付

| # | 任务 | 规模 | 验收 |
|---|---|---|---|
| **T9** | `WebGLBuildScript` + 部署（静态托管 + WSS 域名 + 证书） | 中 | 一台**局外设备**（非你本机）打开 URL 能玩 |
| **T10** | 录屏 + 已知边界话术卡 | 小 | 一段覆盖四条声称面的录屏；一张含 5 条边界的卡片 |

## 7. 测试策略

**核心约束：WebGL 只能在浏览器里跑，PlayMode 覆盖不到。** 所以策略是**把可测部分与不可测部分切开**：

| 层 | 方式 |
|---|---|
| `WebSocketClientAdapter` 的逻辑（编解码、心跳、事件、重连） | **EditMode 单测**，经 `FakeChannel` 驱动（D-W8）——真实往返由 T4（浏览器 echo）与 T5（集成）覆盖 |
| `IServerSession` 抽取 | 既有 Server.Tests 全绿即证明行为未变 |
| `WebSocketServerSession` | Server.Tests 单测 |
| `MobileInputAdapter` 的模拟量解析 | EditMode |
| `ClientNetworkHost` 的接线 + PC 真实往返（`DotNetWebSocketChannel`） | **人工**——Editor 跑 `DemoDay` 或 PC 构建连真服务器（T5 Step 7 第 2 项）。9 个 PlayMode 测试全部直接构造 `KcpNetClientAdapter`，**覆盖不到**这条路径 |
| jslib 的**状态机**（入队 / 取帧 / 背压 / 队列上限 / 关闭码） | **Node 单测**（`node --test tests/webgl/websocket-state.test.js`，零依赖）——状态机把 heap 作为**参数**传入，因此不认识 Unity，可在 Node 里直接跑 |
| jslib 的入口函数 + 浏览器运行时 | **人工**（7 个入口，除 `Connect` 外各 1-3 行——这就是全部的不可测残留） |
| 四条声称面 | **人工**，在真实浏览器里 |

**回归基线**：Server.Tests 132/132 · EditMode 222/222 · PlayMode 18/18（三配置并集）· PC 包照常可出。

## 8. 风险清单

| # | 风险 | 影响 | 应对 |
|---|---|---|---|
| **R1** | **首屏加载时间**（包现 136MB） | "打开链接等 1 分钟"在面试里与崩溃同级 | 人类裁定：**本批次不做，也不测量**——先落地 WebGL 形态，后期清理未使用资源后再优化。**本设计不设缓解措施，风险被显式接受** |
| **R2** | **jslib 边界** | 本设计最容易卡住的纯技术点 | **反转跨界方向**：不让 JS 回调 C#——`gameInstance` 在 Unity 2020.1 就被移除（loader API 改名 `createUnityInstance`），而 `SendMessage` 还要字符串方法名且失败静默。改成 C# 每帧 `Poll`，跨界只剩 `DllImport` 一个方向。**状态机进 Node 单测**，入口函数缩到 7 个、除 `Connect` 外各 1-3 行。T4 单独成任务，失败早暴露 |
| **R3** | **WSS 需要域名 + 证书** | 非技术依赖 | 比微信备案轻得多（一个域名 + Let's Encrypt）。**演示形态下不做鉴权**——公网暴露意味着任何人知道 URL 即可连，需在演示期接受并说明 |
| **R4** | **手机端性能未知** | PC 上 `--bots 100` 能跑，手机浏览器不一定 | T8 明确记录手机端帧率；若跑不动，P3 的声称降级为"桌面浏览器演示" |
| **R5** | **标签页后台 / 移动端杀标签** | 切 App 回来断线 | 列为已知边界话术（§5） |
| **R6** | **WebGL 不可自动化** | 覆盖缺口 | §7 的切分策略把逻辑层拉进 EditMode；jslib 与浏览器层明确记为人工 |

## 9. 决策记录

| # | 决策 | 理由 |
|---|---|---|
| **D-W1** | 优先 **WebGL 网页版**，微信小游戏延后 | 两者技术代价几乎相同（同一套 WSS 通道），但小游戏额外需要微信账号 + 类目审核 + 域名备案——**那是可能卡住整个计划的、不可控的非技术依赖**。网页版只要一个域名 |
| **D-W2** | `INetworkClient` **搬到 `Cool.Protocol`**（成员签名一字不改）。**但 `KcpNet.asmdef` 不排除 WebGL、`GameSys` 保留对 `KcpNet` 的引用**——改为在这 6 个 socket 文件内加 `#if !UNITY_WEBGL`，并由 `ClientNetworkHost` 用 `#if UNITY_WEBGL` 选择 adapter | **写计划时实测推翻了原方案的另两个动作**：asmdef 引用不可平台条件化（F11），排除程序集会让 PC 端也引用不到；而平台专属 provider 程序集需要场景注入，但 `DemoDay` 是 PC/WebGL 共享场景，挂不下两种 provider。<br>**代价**：`KcpNet` 在 WebGL 下是**空壳程序集**（编译进去、无 socket 实现）——体积与编译时间的代价，非正确性代价。**收益**：零场景改动、零注册表、零 link.xml |
| **D-W3** | 服务端**同进程双栈**（UDP/KCP + WS） | `IServerSession` 抽取是 WS 路径本来就要做的，双栈几乎白送；且让"PC 端 + 浏览器端同场"成为 AOI/战斗的活证据 |
| **D-W4** | **不做包体优化，也不测量首屏加载**（人类裁定） | 先落地 WebGL 形态；后期清理未使用资源后再优化。**代价**：后期优化没有 before 数可对照，且 R1 在面试前始终是不可估的未知数 |
| **D-W5** | 移动端输入**复用既有 `IInputAdapter` 抽象**，但**重写实现** | 抽象（5 成员）可复用、`KeyboardInputAdapter` 不受影响；`JoystickInputAdapter` 读的三个 legacy 轴不存在（F7），调用即抛异常，且丢弃模拟量。T6 要建真实 UI 摇杆并让它驱动适配器——**`IInputAdapter` 契约不变** |
| **D-W6** | WebGL 的验证**分层**：逻辑进 EditMode，jslib/浏览器留人工 | 承认 WebGL 不可自动化，但把可测的部分最大化 |
| **D-W7** | 演示形态下**不做鉴权** | 人类已选"面试官自己玩"，加鉴权会把 URL 分发变成账号分发，得不偿失。风险在 R3 显式登记 |
| **D-W8** | `WebSocketClientAdapter` 内部再分一层 `IWebSocketChannel` | WebGL 必须走 jslib（浏览器 API），而**编辑器里 `ClientWebSocket` 可用**——分层后适配器逻辑可在 EditMode 单测，只有 jslib 与浏览器运行时留人工（D-W6 的落点） |
| **D-W9** | WS 上**一条消息 = 一个 WS 帧**，直接复用 `MessageCodec.Encode`/`TryDecode` | `MessageCodec` 完全与传输解耦（F2）；WS 本身是消息边界的，无需再做流式拆帧 |
| **D-W10** | 应用**从未使用** KCP 的非可靠通道（全部 `MessageFlags.Reliable`） | 所以 WebSocket(TCP) 的语义与现有实际行为**一致**，不存在"换 TCP 反而变差"的落差（F3） |
| **D-W11** | **两端统一走 WebSocket**：`WebSocketClientAdapter` 成为 PC 与 WebGL **唯一**的客户端传输；`KcpNetClientAdapter` 保留为第二实现但**不再是默认**（人类裁定："保留原有 kcp 代码，以后再删"） | **收益**：F3 表明 KCP 的不可靠通道是死代码，让它继续当 PC 默认是**零收益**；而统一之后 **PC 自我验证与浏览器演示走同一条代码路径**——旧形态下"在 PC 上验的每一件事，走的都是面试官那边不存在的那条路"，这正是"确保每个功能尽量少 bug"的落点。平台分叉也从"两条传输"降级为"两个 channel"。<br>**代价**：master 的 PC 传输与 P6 冻结包所用的 `KcpNetClientAdapter` 不再一致（冻结包不随 master 演进，接受）。<br>**面试口径**：不是"本来就用 WebSocket"，而是"实现了 KCP/UDP → 加 WebSocket 时发现应用层从未使用不可靠通道 → 把两端统一到一条路径" |
