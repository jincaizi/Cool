# WebGL 面试演示版 设计

> **它是什么**：把项目从"PC 包自己跑"推进到"**面试官用自己的设备打开一个链接就能玩**"。
> P1–P6 已完成（服务端权威同步、AOI、战斗裁决、怪物 AI、PC 交付包）。本设计解决的是**交付形态**问题：真正的目标平台是手机/浏览器，不是 PC。

## 0. 前置结论（本设计的事实基础）

以下均为 2026-10-07 在本仓库实测/读码得出，不是推测。

| # | 事实 | 影响 |
|---|---|---|
| F1 | `INetworkClient` 只有 **7 个成员**，且`ClientNetworkManager` / `CombatNetworkBridge` 依赖的是**接口**而非 `KcpClient`；测试里已有 `RecordingClient : INetworkClient` 假实现 | 抽象可用，加一条传输通道是"加实现"而非"重写" |
| F2 | `MessageCodec`（305 行）是**纯静态** `Encode(…, IBufferWriter<byte>)` / `TryDecode(byte[])` | 编解码零改动 |
| F3 | 应用**从未使用** KCP 的非可靠通道——`MessageFlags` 实际只用到 `Reliable`/`None`/`Encrypted`/`Compressed`，服务端 `SendToSessionAsync` 默认 `Reliable`，`WorldSnapshot` 亦按 `Reliable` 编码 | **WebSocket(TCP) 的语义与现有实际行为一致**，无"换 TCP 反而变差"的落差 |
| F4 | `INetworkClient.cs` 位于 `Assets/Scripts/AOT/KcpNet/`，即**住在 `KcpNet` 程序集内** | 见 F6——这是本设计的核心阻塞点 |
| F5 | `KcpNet.asmdef` 的 `includePlatforms` / `excludePlatforms` **均为空** → 所有平台都编译；而 `KcpClientTransport` 使用 `System.Net.Sockets` | WebGL 无 `System.Net.Sockets` → **WebGL 构建编译失败** |
| F6 | `GameSys.asmdef`（Hotfix）`references: ["KcpNet"]`，且 5 个文件 `using KcpNet`；`ClientNetworkHost.cs:181` 直接 `new KcpNetClientAdapter()` | 抽象被实现绑架：只要 `GameSys` 引用 `KcpNet`，WebGL 就**打不出包** |
| F7 | `IInputAdapter` 抽象**已存在**（`KeyboardInputAdapter.cs:8`），`JoystickInputAdapter` **已实现**（`JoystickInputAdapter.cs:8`）但全项目**从未实例化**（`InputManager.cs:40` 硬编码 `new KeyboardInputAdapter()`） | 移动端输入是"加实现 + 按平台注入"，不是重写 |
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
| **解除构建阻塞** | `INetworkClient` 搬到 `Cool.Protocol`；`KcpNet.asmdef` 排除 WebGL；`GameSys` 改引用；adapter 按平台创建 |
| **传输通道** | 客户端 `WebSocketClientAdapter : INetworkClient` + `WebGLWebSocket.jslib` |
| **服务端双栈** | `IServerSession` 抽取 + `WebSocketServerSession` + `WebSocketServerListener`（与既有 UDP/KCP 同进程并存） |
| **移动端输入** | `MobileInputAdapter`（摇杆）+ `TouchCameraInput`（触屏转视角）+ 攻击/技能/跳跃按钮 |
| **交付** | WebGL 构建脚本 + 静态托管 + WSS 域名/证书 |
| **演示制品** | 录屏、已知边界话术卡 |

### 明确不做

微信小游戏（同一套 WS 通道将来可复用，只差微信账号与类目审核）· P7 打怪升级 · P8 PvP · P2 移动同步的现场演示 · 原生 Android/iOS 打包 · **包体优化与首屏加载测量**（人类裁定后期清理资源后再做）· 聊天/背包/组队/交易 · **鉴权**（演示形态下明确不做——公网暴露意味着任何人知道 URL 即可连，风险在 R3 显式接受，见 D-W7）

## 3. 架构

### 数据流

```
[面试官的浏览器]
  Unity WebGL 构建
    WebSocketClientAdapter          ← 新增，实现 INetworkClient（成员签名一字不改）
    MessageCodec                    ← 复用
    ClientNetworkManager / CombatNetworkBridge / RemoteEntityRegistry / NetMovementPolicy  ← 复用
    WebGLWebSocket.jslib            ← 新增，C# ↔ 浏览器 WebSocket API 的唯一边界
  ─────────────────── WSS ───────────────────
[你的服务器 .NET 8，单进程双栈]
  WebSocketServerListener  →  WebSocketServerSession : IServerSession   ← 新增
  UdpSession / KcpServerSession : IServerSession                        ← 既有，原生客户端继续用
        ↓ 两条来源汇入同一个 ProcessPacket（复用）
  Handler / AoiGrid / EntityStore / CombatResolver / MonsterModule      ← 全部零改动
```

### 双栈为什么是白送的

`IServerSession` 的抽取是 WS 路径**本来就必须做**的（`Server3C` 不能再依赖 `KcpServerSession` 具体类型）。抽完之后，两条传输来源汇入同一个既有的 `ProcessPacket(session, data)`——**没有额外的架构工作**。

双栈带来的收益：PC 包与浏览器客户端**连同一个服务器进程**，演示时"PC 端 + 浏览器端同场"本身就是 AOI 同步与战斗裁决的活证据。

### 接口搬运的边界（重要）

`INetworkClient` **成员签名一字不动**，只是从 `KcpNet` 程序集搬到 `Cool.Protocol`。因此：

- `RecordingClient`（测试假实现）**不需要改**
- `ClientNetworkManager` / `CombatNetworkBridge` 的构造与调用**不需要改**（只改一行 `using`）
- 既有 `KcpNetClientAdapter` **不需要改**

搬家的目的正是**保住这条约束**——让它不再被 KCP 的实现细节与平台限制绑架。

## 4. 组件分解

| # | 组件 | 位置 | 职责 | 可独立测试 |
|---|---|---|---|---|
| C1 | `INetworkClient`（搬运） | `AOT/Protocol/` | 传输抽象，供两端实现 | 编译即验证 |
| C2 | `KcpNet.asmdef` 平台排除 | `AOT/KcpNet/` | `excludePlatforms: ["WebGL"]` | 构建即验证 |
| C3 | adapter 工厂 | `Hotfix/.../SyncLayer/` | 按平台返回 KCP 或 WS 适配器 | EditMode |
| C4 | `WebSocketClientAdapter` | `AOT/WebSocketNet/`（新程序集） | `INetworkClient` 的 WS 实现：连接、收发、心跳、事件 | ✅ **EditMode 可测**——`ClientWebSocket` 在编辑器里可用，只有 WebGL 构建不可用 |
| C5 | `WebGLWebSocket.jslib` | `Assets/Plugins/WebGL/` | C#↔浏览器 `WebSocket` 的唯一边界 | ⚠️ 人工（R2） |
| C6 | `IServerSession`（抽取） | `Server/` | 业务需要的会话契约：`SessionId` / `Send(MessageId, IMessage, MessageFlags)` / `IsConnected` / `LastActivityTime` | 既有测试即验证 |
| C7 | `WebSocketServerSession` | `Server/` | `System.Net.WebSockets` 收发（比 KCP 版简单——TCP 自带流控） | Server.Tests |
| C8 | `WebSocketServerListener` | `Server/` | 接受 WS → 建会话 → 喂 `ProcessPacket` | ⚠️ 集成测 |
| C9 | `MobileInputAdapter` + 摇杆/按钮 UI | `Hotfix/.../Sys3C/Input/` + UI | 触屏输入，**实现既有的 `IInputAdapter`** | EditMode（模拟量解析） |
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
| **T1** | `INetworkClient` 搬到 `Cool.Protocol`；`KcpNet.asmdef` 排除 WebGL；`GameSys` **移除**对 `KcpNet` 的引用（`Cool.Protocol` 已在引用列表中，无需新增）；5 个文件的 `using KcpNet` 改一行；`ClientNetworkHost` 改为经工厂创建 adapter。<br>**实现注意**：`KcpNet.PlayModeTests.asmdef` 引用了 `KcpNet`——测试程序集不进 player 构建，故**不阻塞 WebGL 出包**，但可能产生编辑器告警，实现时确认一次 | 中 | ① 原 PC/Editor 构建与全部测试**照常通过**；② **WebGL 平台能编译**（空场景构建足以证明引用链不再阻塞；真实场景的 WebGL 构建在 T4/T9 验证） |

### L1 · 打通 WS 通道（面试官"能连上"）

| # | 任务 | 规模 | 验收 |
|---|---|---|---|
| **T2** | 抽 `IServerSession`，`Server3C`（8 处）与 `SessionManager` 改依赖接口 | 小 | Server.Tests 132/132 全绿（纯机械替换，行为不变） |
| **T3** | `WebSocketClientAdapter : INetworkClient`（新程序集 `WebSocketNet`） | 中 | EditMode：对**本地 echo WS 服务器**跑通连接/收发/心跳/断开事件 |
| **T4** | `WebGLWebSocket.jslib` + 在 WebGL 构建里跑通 echo | 中 | 浏览器打开 → echo 往返成功（**本任务的主要风险在 jslib 边界，R2**） |
| **T5** | `WebSocketServerSession` + `WebSocketServerListener`（双栈） | 中 | 浏览器客户端 + PC 包**同时**连同一服务器，互相可见（AOI 双端渲染） |

### L2 · 让它在手机上能玩

| # | 任务 | 规模 | 验收 |
|---|---|---|---|
| **T6** | `MobileInputAdapter` + 摇杆/攻击/技能/跳跃 UI | 中 | 手机浏览器能移动、攻击、放技能；**摇杆模拟量不被 normalize 丢弃**（P6 审查点出过这个坑） |
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
| `WebSocketClientAdapter` 的逻辑（编解码、心跳、事件、重连） | **EditMode 单测**——`ClientWebSocket` 在编辑器里可用，只有 WebGL 构建不可用。对本地 echo server 跑 |
| `IServerSession` 抽取 | 既有 Server.Tests 全绿即证明行为未变 |
| `WebSocketServerSession` | Server.Tests 单测 |
| `MobileInputAdapter` 的模拟量解析 | EditMode |
| **jslib 边界 + 浏览器运行时** | **人工**（不可自动化，这是本设计的已知缺口） |
| 四条声称面 | **人工**，在真实浏览器里 |

**回归基线**：Server.Tests 132/132 · EditMode 222/222 · PlayMode 18/18（三配置并集）· PC 包照常可出。

## 8. 风险清单

| # | 风险 | 影响 | 应对 |
|---|---|---|---|
| **R1** | **首屏加载时间**（包现 136MB） | "打开链接等 1 分钟"在面试里与崩溃同级 | 人类裁定：**本批次不做，也不测量**——先落地 WebGL 形态，后期清理未使用资源后再优化。**本设计不设缓解措施，风险被显式接受** |
| **R2** | **jslib 边界**（C#↔JS 字符串封送、回调时机） | 本设计最容易卡住的纯技术点 | 有成熟第三方 WebSocket 插件可参照/采用；T4 单独成任务，失败早暴露 |
| **R3** | **WSS 需要域名 + 证书** | 非技术依赖 | 比微信备案轻得多（一个域名 + Let's Encrypt）。**演示形态下不做鉴权**——公网暴露意味着任何人知道 URL 即可连，需在演示期接受并说明 |
| **R4** | **手机端性能未知** | PC 上 `--bots 100` 能跑，手机浏览器不一定 | T8 明确记录手机端帧率；若跑不动，P3 的声称降级为"桌面浏览器演示" |
| **R5** | **标签页后台 / 移动端杀标签** | 切 App 回来断线 | 列为已知边界话术（§5） |
| **R6** | **WebGL 不可自动化** | 覆盖缺口 | §7 的切分策略把逻辑层拉进 EditMode；jslib 与浏览器层明确记为人工 |

## 9. 决策记录

| # | 决策 | 理由 |
|---|---|---|
| **D-W1** | 优先 **WebGL 网页版**，微信小游戏延后 | 两者技术代价几乎相同（同一套 WSS 通道），但小游戏额外需要微信账号 + 类目审核 + 域名备案——**那是可能卡住整个计划的、不可控的非技术依赖**。网页版只要一个域名 |
| **D-W2** | `INetworkClient` **搬到 `Cool.Protocol`**（成员签名一字不改） | 接口住在 `KcpNet` 程序集里，而 `KcpNet` 依赖 `System.Net.Sockets` → `GameSys` 引用它 → **WebGL 根本打不出包**。搬家是保住"抽象接口不改"这条约束的**前提**，不是违反它 |
| **D-W3** | 服务端**同进程双栈**（UDP/KCP + WS） | `IServerSession` 抽取是 WS 路径本来就要做的，双栈几乎白送；且让"PC 端 + 浏览器端同场"成为 AOI/战斗的活证据 |
| **D-W4** | **不做包体优化，也不测量首屏加载**（人类裁定） | 先落地 WebGL 形态；后期清理未使用资源后再优化。**代价**：后期优化没有 before 数可对照，且 R1 在面试前始终是不可估的未知数 |
| **D-W5** | 移动端输入**复用既有 `IInputAdapter` 抽象** | 接口与 `JoystickInputAdapter` 都已存在，只是从未实例化（F7）。这是"加实现 + 按平台注入" |
| **D-W6** | WebGL 的验证**分层**：逻辑进 EditMode，jslib/浏览器留人工 | 承认 WebGL 不可自动化，但把可测的部分最大化 |
| **D-W7** | 演示形态下**不做鉴权** | 人类已选"面试官自己玩"，加鉴权会把 URL 分发变成账号分发，得不偿失。风险在 R3 显式登记 |
