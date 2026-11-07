# P0/P1 质量改进设计

日期：2026-11-05
状态：待确认
适用范围：Unity 客户端、共享同步规则、网络传输、.NET 服务端、构建与质量门禁
依据：2026-11-05 全模块质量审计，以及当前仓库中的模块 README、既有网络/战斗设计与测试基线

## 1. 意图

当前项目已经具备可演示的完整闭环，但“功能能跑”与“可安全扩展、可公开联机、可移动端发布”之间仍有明显差距。本轮不继续堆玩法，而是把现有系统收敛到一个可信的工程基线：

1. 不破坏玩家数据，也不修改共享配置资产作为运行时状态。
2. 战斗、移动、防御、登录与世界归属由服务端掌握最终裁决权。
3. 所有网络入口、队列、消息和资源都有明确上限、背压与失败策略。
4. Unity 主线程、服务端世界线程和传输线程之间的所有权清楚且可测试。
5. Android/iOS 构建、PlayMode 测试和持续质量门禁可重复运行。
6. P0 清零后才允许继续新增联网玩法；P1 清零后才进入公开多人测试或商店发布准备。

本设计把审计项拆为 P0（阻断级）和 P1（发布前高优先级）。P2 及更远的功能拓展只登记边界，不纳入本轮实施。

## 2. 当前基线

审计时的可复现基线：

- 服务端测试：409/409 通过。
- Unity EditMode：629/630 通过；唯一失败来自外部 Unity MCP `ping` 错误日志污染，单独重跑该测试通过。
- Unity PlayMode：当前活动构建目标为 WebGL，而 `KcpNet.PlayModeTests.asmdef` 带 `!UNITY_WEBGL`，因此注册测试数为 0。
- 当前客户端实际接线在 WebGL 和桌面均选择 WebSocket；KCP 代码存在，但不是当前生产客户端默认路径。
- Addressables 包装层存在，但没有 `Assets/AddressableAssetsData/`，玩法代码没有通过 `Res` 加载，UI 仍使用 `Resources.Load`。
- 当前只有 Windows/WebGL 构建入口；Android/iOS 自动构建、CI 和覆盖率门禁缺失。
- 当前工作树已有大量与本设计无关的改动；执行时只允许暂存计划列出的具体文件。

## 3. 优先级定义

| 等级 | 定义 | 进入条件 |
|---|---|---|
| P0 | 会造成数据损坏、跨世界串线、客户端伪造权威状态、远程耗尽资源、线程竞态或无法证明核心链路正确 | 立即处理；P0 未清零时冻结新增联网玩法 |
| P1 | 在几十至数百实体、移动设备、长时间运行、持续交付或维护扩展时高概率失效 | P0 门禁通过后处理；P1 未清零时不做公开多人测试/商店发布 |
| P2 | 体验扩展、长期演进或规模跃迁项 | 本设计只登记，不进入实施任务 |

优先级不按“改动大小”决定，而按失败后果决定。P0 可以是大型重构，也可以先采取可证明安全的收口措施，再由 P1 完成最终架构。

## 4. 目标、非目标与反目标

### 4.1 目标

- 建立可自动验证的 P0/P1 质量门禁。
- 消除背包整理的数据丢失与初始化崩溃。
- 技能运行时伤害上下文与 ScriptableObject/配置资产完全分离。
- 客户端网络回调只入有界邮箱，所有 Unity API 只在主线程触达。
- 服务端以接收 tick、单调序列和地图约束校验移动，不相信客户端墙钟。
- 格挡与精力由服务端状态机控制；位置同步不再携带可直接生效的权威布尔值。
- 登录幂等，生产模式不接受占位密码或匿名身份。
- 队列、帧大小、连接数、未认证时长和每 tick 工作量均有上限。
- 真实多世界能力通过 `WorldInstance` 隔离；在完成前只允许单一 `Global` 世界。
- Addressables 具备并发去重、失败释放和显式租约，并至少迁移 UI 首批资源。
- Android/iOS ARM64 IL2CPP 构建与非零 PlayMode 测试成为自动门禁。

### 4.2 非目标

- 不安装 HybridCLR；`Hotfix.*` 仍是普通 asmdef，不承诺代码热重载。
- 不做 DOTS/ECS 重写，不替换 Unity 2022.3 LTS，不重写全部游戏系统。
- 不新增聊天、交易、公会、副本、装备或新技能等玩法。
- 不在应用协议层自研加密；公网机密性由 TLS/WSS、受控网关，或经 ADR-0003 批准的 DTLS/Noise provider 提供。
- 不同时维护多条默认生产传输路径。每个平台/构建配置只能选择一个主传输。
- 不在本轮建设完整账号中心、支付、跨服或数据库分片；但必须提供可替换的票据校验接口和稳定角色身份。
- 不做可视化行为树、完整技能编辑器或通用资源框架重写。

### 4.3 反目标

以下结果即使“功能能跑”也视为失败：

- 用零散 `ZoneId` 条件掩盖全局 `EntityStore`，继续让世界状态共享同一容器。
- 通过扩大无界队列、提高超时或吞异常来“修复”压力问题。
- 继续把客户端 `IsDefending`、时间戳、速度或位置当权威值直接落库。
- 用 `lock` 包住所有网络逻辑，制造不可观测的长临界区。
- 为追求异步而返回无法取消、无法释放、无法观察异常的任务。
- 继续新增 `Resources.Load`、运行时 `Instantiate/Destroy` 高频路径或非内部 MonoBehaviour 的 `GetComponent/FindObject*`。
- 把当前 0 个 PlayMode 测试报告为“测试通过”。

## 5. 全局约束

- Unity 固定 `2022.3.25f1`；服务端固定 .NET 8；协议继续使用 Protobuf。
- 服务端世界状态仍由单线程拥有；IO 线程只进行验证、解码和入有界邮箱。
- `SyncCore` 保持零 `UnityEngine` 依赖，并由 Unity EditMode 与服务端 xUnit 双端验证。
- 协议字段只追加不复用 tag；破坏性语义变更必须提升 `ProtocolVersion`。
- 客户端 Unity API 只能在主线程调用；网络线程不得读取 `Time.*`、`Transform`、Animator 或 UI。
- 任何模块行为变化必须同步该模块 README。
- 非内部 MonoBehaviour 禁止 `GetComponent/FindObject*`；允许例外只能出现在显式白名单的组合根。
- 高频对象使用对象池；资源加载使用 Addressables 的显式生命周期。
- 执行计划采用 TDD、小步提交和逐任务审查；不得顺手修改任务外文件。

## 6. 目标架构

### 6.0 本 Spec 采用的实施默认值

以下选择由本 Spec 作出，不代表用户此前已逐项确认；它们用于消除实施者必须猜测的空白：

- **认证**：本轮交付 HMAC-SHA256 票据验证器。票据权威产出 AccountId、CharacterId、AllowedWorldId；客户端字段不得覆盖。密钥来自环境秘密。开发票据仅 loopback/Debug 可用。
- **传输**：按平台选择唯一主传输，同一平台/构建配置不得同时启用多个主传输，也不得用一个平台的结果替代另一个平台：
  - **Android/iOS**：KCP + Protobuf；公网候选包必须叠加经 ADR-0003 批准的标准安全层；不得同时启用 WebSocket 主传输。
  - **WebGL**：WebSocket；公网为 WSS；不得编译、打包或启用 KCP 玩家路径。公网使用完整 `wss://` URI、证书与反向代理。
  - **Editor/Windows 测试**：按测试 lane 显式选择主传输；Native KCP lane 与 WebSocket/WebGL lane 分开报告；缺少显式选择时 fail-fast。
  - 如果后续改变主传输，必须先更新平台传输 ADR（ADR-0001）、本 Spec 与 Gate 断言，不能只修改运行时代码。
- **TLS**：生产由受控反向代理终止 WSS，仓库提供 Caddy 示例；Unity/服务端之间的内部明文 WS 只允许 loopback 或受控私网。
- **协议**：本轮将 `ProtocolVersion` 升到 2。生产拒绝 v1；开发环境可显式开放 v1 诊断，但不得无鉴权进入世界。
- **CI**：使用 GitHub Actions。Unity/Android 使用带 Unity 2022.3.25f1 许可证的 Windows self-hosted runner；iOS 完整导出使用 macOS self-hosted runner，未配置时必须报告“未执行”，不能标绿为已验证。
- **覆盖率**：P1 先提交基线并禁止下降；服务端 80%、Unity 核心纯逻辑 70% 是后续提升目标，不作为本轮 P1 完成的硬性比例。
- **可观测性**：使用 `System.Diagnostics.Metrics` + OpenTelemetry Prometheus exporter；健康/指标只绑定可配置管理地址，默认 loopback。告警路由由部署环境决定，不写死到代码。
- **停机持久化**：本轮没有权威角色/背包数据库，不虚构持久化。优雅停机只停止准入、排空有限队列、结束世界 tick 并关闭会话；持久化留 P2。

### 6.0.1 移动校验精确口径

- elapsed 使用入站信封在解码前捕获的单调接收时间，比较连续两次已接受移动包；不用客户端时间，也不用排队后的处理时刻。
- elapsed 夹到 `[1/120s, 0.25s]`；位移预算为 `MaxSpeed * elapsed * 1.2 + 0.25m`，不能靠长时间停包积攒额度。
- 旋转归一到 `[0, 360)`；垂直上升预算为 `8m/s * elapsed + 0.25m`，下降最终仍由地图投影约束。
- 终点投影到服务端 navmesh 的最大距离为 `0.5m`，并验证上一权威点到候选点的可走线段；投影或线段验证失败则保持上一权威位置。不为每个移动包执行完整 A*。

### 6.0.2 准入速率默认值

| 类别 | 速率 / burst | 超限策略 |
|---|---|---|
| 单 IP 未认证 datagram | 10/s，burst 20 | 静默丢弃并计数 |
| 单 IP 新 session 创建 | 2/s，burst 4 | 拒绝创建 |
| 单 session Login | 1/s，burst 3 | 拒绝；10 秒内 3 次超限断开 |
| 已认证总消息 | 120/s，burst 240 | 状态消息丢弃；可靠命令累计违规后断开 |
| 已认证总字节 | 256KiB/s，burst 512KiB | 累计违规后断开 |
| PositionSync | 30/s，burst 30 | 只保留最高 sequence 的待处理项 |
| Cast | 20/s，burst 20 | 拒绝并计 strike |
| 控制消息 | 10/s，burst 20 | 拒绝；累计违规后断开 |

### 6.0.3 性能基准场景

- 专用 self-hosted `cool-perf` runner：Windows 10/11 x64、.NET 8、至少 8 逻辑核与 16GiB RAM；具体 CPU/内存/OS 记录进报告。
- Release 构建，预热 2 分钟，采样 10 分钟。
- 单 `Global`/单 WorldInstance：200 bots、50 monsters、50 模拟客户端；客户端各以 15Hz 位置上报、2Hz 施法。
- 自动门禁：tick p95 < 50ms、p99 < 66ms、队列无持续增长、进程 working set 在稳态窗口内无单调增长。
- 非专用 runner 只产出趋势数据，不用来推翻或放宽上述门禁。

### 6.0.4 端点与公网安全契约

- **唯一构造边界**：只有可测试的 `NetworkTransportFactory`（或等价名称）可以引用和构造 `KcpNetClientAdapter`、`WebSocketClientAdapter`、`WebGLWebSocketChannel` 与 `DotNetWebSocketChannel`；其余 `GameSys` 代码只能依赖 `INetworkClient` 和不可变的 `NetworkEndpoint`。编译分支顺序固定：`UNITY_WEBGL && !UNITY_EDITOR` 只允许 WebSocket；Android/iOS Player 只允许 KCP；Editor/Windows 必须由 Gate lane 或显式开发配置选择，缺少选择时 fail-fast，禁止从 Inspector、上次运行状态或隐式默认值漂移。`TransportFactoryBoundaryGuardTests` 扫描生产调用点，factory 外的具体 transport 构造立即失败；源码扫描不能替代目标平台编译/Player Build 证据。
- **端点**：`NetworkEndpoint` 明确携带 transport kind、host、port，以及 WebSocket 的完整 scheme/path；`WebSocketClientAdapter` 接收已验证的完整 `Uri`，不得内部硬编码 `ws://` 或 `/`。公网 WebGL 强制 `wss://`；明文 `ws://` 只允许 loopback 或 Development Build。
- **KCP 地址解析**：KCP 端点支持域名、IPv4 与 IPv6。连接流程始终保留原始 hostname 作为服务端身份验证输入；DNS 通过系统 resolver 异步解析并去重候选，并按每个结果创建匹配 `AddressFamily` 的 socket。禁止用 `IPAddress.Parse(host)` 处理域名路径，禁止固定 `AddressFamily.InterNetwork`；NAT64 依赖系统 DNS64/getaddrinfo 结果，不在应用内手工拼接 IPv6 地址。
- **有界候选竞争**：多地址连接尊重系统候选顺序，首个候选立即尝试，250ms 后启动下一地址族候选，最多同时 2 个；单候选握手超时 3 秒、整个连接 10 秒。这些值只能由受版本控制的配置收紧，不得在同一 Gate run 临时改变。首个完成服务端身份验证与安全握手的候选获胜，其余候选取消并释放；首个不可达必须继续后续候选，全部失败时返回去敏后的聚合错误；调用方取消必须立即停止 DNS、计时器、socket 与握手。
- **KCP 公网安全**：KCP + Protobuf 本身不提供机密性或对端认证；HMAC 登录票据只用于身份/完整性，不能替代传输加密。Development/loopback/受控测试网络可显式启用明文 KCP；非 Development 公网候选包必须 fail-closed：缺少经 ADR-0003 批准的安全 provider 时拒绝启动。服务端身份验证必须绑定 `NetworkEndpoint` 的原始 hostname，而不是某次 DNS 解析得到的 IP。
- **安全 provider**：ADR-0003 在运行时代码实施前锁定一个经过维护、支持 Unity 2022 IL2CPP Android/iOS 的成熟 DTLS 或 Noise 实现，禁止自研密码学原语；ADR 至少确定协议/库版本、许可证、服务端与客户端集成点、身份与密钥来源、双向密钥派生、AEAD、nonce/序列号、重放窗口、重连/rekey/轮换、版本降级拒绝、秘密清理与观测脱敏。ADR-0003 未批准、provider 未完成或候选包仍可回退明文 KCP 时，移动公网传输不得声明已验证。
- **WebGL 构建边界**：KCP 源码保持 `!UNITY_WEBGL` 编译守卫，全部 KCP PluginImporter 显式排除 WebGL。`KcpPluginPlatformGuardTests` 按程序集名/GUID 枚举所有 Kcp plugin importer，发现重复副本、未知路径、Any Platform/WebGL enabled 或固定路径漏检均失败；WebGL Player 构建产物必须同时证明没有 Kcp 托管/原生 plugin 和 `KcpNetClientAdapter`。通用 PlayMode asmdef 移除直接 `KcpNet` 引用不等于完成生产边界隔离。

### 6.1 服务端数据流

```text
UDP / WebSocket Listener
    -> AdmissionGate（源/IP/连接/帧大小/速率）
    -> AuthenticatedSession（登录状态机、稳定身份、发送背压）
    -> WorldRouter（session -> worldInstanceId）
    -> WorldInstance.Mailbox（有界、单消费者、每 tick 预算）
    -> EntityStore / AOI / Combat / Monster / Navigation / Avoidance
```

`Session` 只负责连接、鉴权、速率与路由；`WorldInstance` 才拥有可变世界状态。世界之间不得共享实体、观察者、怪物、ORCA 邻居或战斗候选。

### 6.2 客户端数据流

```text
INetworkClient callback thread
    -> Decode + classify
    -> ClientIngressMailbox（有界；状态类 latest-wins，命令类 FIFO）
    -> ClientNetworkManager.Update（主线程、每帧预算）
    -> RemoteEntityRegistry / UI / 3C / VFX
```

`MessageDispatcher` 不再在网络线程直接执行业务委托。所有订阅变更和业务分发发生在主线程；邮箱溢出必须按消息类别执行可观测的合并、拒绝或断开策略。

### 6.3 资源数据流

```text
业务模块 -> Res.AcquireAsync<T>(key, cancellationToken)
         -> 单飞中的 in-flight load（同 key 同类型只加载一次）
         -> ResourceLease<T>（Dispose/ReleaseAsync 显式释放）
         -> Addressables backend
```

资源句柄不得靠调用者记忆 key 后手工配对；失败、取消、重复场景加载和管理器销毁都必须释放底层 handle。

### 6.3.1 生命周期契约

**Addressables 生产验证编排**：外层 `quality-gate.ps1` 保存原 Addressables settings 与 data builder；清理目标平台的旧 catalog、hash 与 bundle 输出；用目标平台 Unity 进程执行 `BuildPlayerContent` 并写 content manifest/hash；设置 Use Existing Build，后续生命周期测试进程必须读取该设置与本轮 manifest；测试内验证实际 catalog hash 等于本轮 manifest，禁止回退 Use Asset Database；生命周期测试全部结束后，外层 `finally` 才恢复 settings，恢复失败使 Gate 失败；整条流程在隔离 worktree 执行，避免进程崩溃污染日常 Editor 工作区。

**全局 `ResourceManager.ShutdownAsync` 与 `ResourceScope.CloseAsync` 分离**：

- `ResourceManager` 是跨场景全局服务；只有应用真正退出、测试套件结束或显式全局重启时才调用 `ShutdownAsync`，场景切换不得让全局服务进入 `Stopped`；
- `ApplicationResourceScope`/`UIResourceScope` 持有 CoreUI 等跨场景 lease；`GameSessionScope`、`VfxPoolScope` 等场景/会话 scope 只拥有自己的 scene、VFX、实例与 pending load；每个 lease 必须登记稳定 owner ID，禁止无 owner 的 acquire；
- 场景切换前显式 `await GameSessionScope.CloseAsync()`：停止该 scope 的事件接入 → 取消该 scope 的 pending loads → 销毁该 scope 的 active/idle instances → 卸载该 scope 的 Addressable scenes → 释放该 scope 的 asset leases，并断言该 owner 的 lease 数为 0；不得释放 application/UI scope 的 CoreUI lease；
- 全局关闭状态固定为 `Running → ShuttingDown → Stopped`；进入 `ShuttingDown` 后拒绝新的 Acquire/Load；多次或并发 `ShutdownAsync` 共享同一个完成任务，并在所有 scope 已关闭后释放全局 lease、清纯托管静态引用；
- Unity `OnDestroy`/`OnApplicationQuit` 只做 best-effort 兜底，不能宣称完成正常异步卸载；`SubsystemRegistration` 只清纯托管静态引用并报告 outstanding handles，不执行异步释放；
- `ResetForTests` 在任一 owner 仍有 outstanding handle 时必须失败，不能绕过 scope close/global shutdown 静默清表；覆盖 `CharacterSelect` CoreUI 保留 → 创建并关闭 `DemoDay` world scope → 返回 `CharacterSelect`、重复/并发 close，以及全局 shutdown 后拒绝新加载。

**UI failure Presenter 注册**：注册 token 同时记录实例身份和 generation；Presenter B 替换 A 后，A 的旧 token Dispose 不得注销 B；新 Presenter 注册时立即更新已存在的持久化 `UIManager`，不能只等待下一次 `AfterSceneLoad`；测试覆盖 A 注册 → B 替换 → A 销毁，以及场景往返和 Domain Reload disabled。

**VfxCatalog 初始化**：`VfxId` 使用带显式数值、禁止复用旧值的 enum；`VfxCatalog : ScriptableObject, IVfxCatalog` 只保存 `VfxId → address`，不保存 Prefab 直接引用；build-time 校验重复 ID、空 address、缺失 Addressable entry 和非 `GameObject` 类型，任一错误均失败；场景 `VfxPoolScope` 持有 catalog、pool 和全部 asset lease，预加载到一半失败时释放已取得 lease、销毁已预热 clone、不订阅事件并返回初始化失败；核心 Hit/Heal 全量预热，可选技能 VFX 只按当前角色/场景所需 ID 预热。

**IL2CPP 协议 fixture**：不允许只对空消息做 roundtrip；为每种协议 shape 准备非默认值 fixture，覆盖 `IList<T>`、嵌套消息、空/非空集合、枚举、负数/边界整数和字符串；runtime 完成标志包含 build SHA、`MessageTypeRegistry` hash、Addressables catalog hash 与 ABI；harness 验证 `ro.product.cpu.abi == arm64-v8a`；ARM64 emulator 只证明安装、启动、IL2CPP/AOT、协议和场景进入，帧时间、GC、热状态、网络切换、前后台、低内存、ANR 与 30 分钟 soak 必须使用已批准的物理参考设备。

## 7. P0 设计

### P0-0：可证明的测试入口

在任何行为修复前，先让测试能真实发现回归：

- 以 Windows 原生 CLI runner `tools/quality-gate.ps1` 启动独立 Unity 进程，传 `-buildTarget StandaloneWindows64`；Editor 内验证 `BuildTarget.StandaloneWindows64`，测试进程内不执行 `SwitchActiveBuildTarget`。同一 project 已被 Unity 打开时 fail-fast，并提示使用独立 worktree。
- EditMode、通用 PlayMode 与 Native PlayMode 各自启动独立进程，分别输出 XML 与日志；PlayMode 测试实际发现数必须大于 0。WebGL 使用独立 `-buildTarget WebGL` 进程和明确的 compile/build lane；通用测试程序集 compile contract、WebGL Player Build 与 runtime smoke 分别记录，不能用其中一种替代另一种。
- Native 测试按 `native-clean`、`native-bots`、`native-monsters` 和确有测试时的 `native-navigation` profile 执行；每个 profile 独立启动服务器、动态选择同时可用的 TCP/UDP 端口并记录 PID，以 application-ready 日志加对应传输 probe 作为 ready 判据；启动失败、提前退出、端口冲突、超时或 teardown 残留均失败，`finally` 只停止本次 PID/子进程树并保存日志。
- 门禁只使用本次运行产生的 XML 与日志，并输出总数、通过、失败、跳过和耗时；0 tests、XML 缺失/不可解析、进程非零退出、超时和未解释的测试数下降均失败。MCP `tests-run` 只是本地便利入口（`tools/unity-tests-mcp.sh`），不得作为 Gate 证据。
- 每次运行使用统一 run ID，所有产物校验新鲜度与 SHA-256；`production/quality/test-manifest.json` 保存每个 assembly/profile 的预期测试全名，测试新增、删除或迁移必须显式更新 manifest。
- Gate 0 覆盖服务端测试、Unity EditMode、通用 PlayMode、Native PlayMode 与协议双端一致性测试。
- dirty worktree 上 `--infra` 与 `--baseline` 只能输出 `DIAGNOSTIC_ONLY` 并保存 patch hash；`--p0`、`--p1` 与 Mobile Release Readiness 默认要求 clean worktree，dirty 时不得产生 PASS。

### P0-1：背包数据正确性与安全降级

- `Compact` 只稳定压缩空洞，保持非空槽位相对顺序；不得合并堆栈、重建物品或生成新实例 ID。
- `Compact` 必须保留原 `ItemData` 引用、`InstanceId`、锁定、过期、耐久、等级、附加属性和扩展容量。
- 所有公开写操作要求已初始化；未初始化写操作返回明确的失败结果且不产生任何数据变更，不抛 `NullReferenceException`。
- `LoadFromServer(null)` 明确失败并保留当前数据，不能把传输/反序列化异常静默解释为空背包。
- `AddItem` 返回实际变更的槽位/物品集合；旧三元组 API 标记过渡废弃，不再返回伪造的槽位 0。
- 在服务端权威库存完成前，联网模式只读展示或隐藏 Bag UI；不得本地提交永久物品变更。

### P0-2：不可变技能配置与确定性事件生命周期

- `DamageBlock`、`SkillData` 和其他 ScriptableObject 只作为配置读取。
- 每次施法创建独立 `DamageContext`；每个目标从施法上下文派生独立 `DamageHitContext`。
- `SkillDamageHelper` 返回计算结果，不回写配置。
- 技能启动采用“验证 -> 启动 -> 提交精力/冷却/注册”的事务顺序；启动失败不消耗资源。
- EventBus 订阅返回可释放 token；`EventSubscription` 改为 `sealed class`，用 `Interlocked.Exchange` 或等价原子状态保证跨副本幂等退订，重复 `Dispose` 不抛异常。
- `Emit` 使用 copy-on-write 快照，不在热路径 `ToArray()` 分配临时数组；允许回调安全增删订阅。
- `ResetAll()` 同时清普通订阅、定向订阅和暂停状态；空桶被回收；`SubsystemRegistration` 时重置纯托管订阅状态。
- 测试覆盖 `Copied_Tokens_Dispose_Only_Once`、重复 `Dispose` 不抛异常，以及 `Emit` 热路径无临时数组分配。

### P0-3：世界边界收口

P0 不在全局容器上零散补过滤，而先关闭尚未实现的多世界表面：

- 登录只接受服务器配置白名单中的 `Global`；任意客户端 ZoneId 不能创建 Room。
- Room 与 `EntityStore` 使用同一个 `PlayerState` 实例，禁止双份状态漂移。
- `RoomManager.MaxRooms`、最大人数、Zone 名称长度和空房回收生效。
- 换区入口在 `WorldInstance` 完成前返回“不支持”，不得先删旧房再尝试加新房。

这是一项安全收口，不是最终多世界实现。P1 再引入 `WorldInstance`。

### P0-4：服务端移动、防御与精力权威

- `MovementValidator` 接收服务器计算的 `elapsedSeconds`，不接收客户端前后时间戳作为预算依据。
- `PlayerState` 使用 wrap-safe sequence 比较；重复、倒序和重放包不更新状态，但返回当前权威 ack。
- 对 X/Y/Z、Rotation、Speed 做有限值检查；异常输入回退到上次权威状态。
- 一次可接受位移只由 `MaxSpeed * serverElapsed * tolerance + jitterAllowance` 决定；删除 50m 绕过门槛和 `dt=0` 绕过路径。
- Y 也要钳制；最终位置必须通过地图投影/可达性策略。地图服务不可用时采取“保持上一权威位置”，不信任客户端位置。
- 客户端上报时间戳仅作诊断，不参与裁决；`MovementReporter` 使用可注入时钟而非直接读取 `DateTime.UtcNow`，并沿用已经同步的服务器时钟语义。
- 从生产 `MovementReporter` 删除 `injectedSendDelayMs` 与 `Thread.Sleep`；延迟、抖动和丢包模拟只允许放在 fake transport 或可控 test scheduler 中。
- 测试 `Injected_Network_Delay_Does_Not_Block_MainThread` 证明注入网络延迟不会阻塞 Unity 主线程。
- 防御改为独立 `DefendCommand(Start/Stop, sequence)`；服务端持有起手、收手、朝向、死亡清理和精力消耗。
- 位置同步中的旧 `IsDefending` 字段仅兼容读取并忽略，后续协议版本废弃。

### P0-5：幂等登录与认证边界

- 会话登录状态机：`Connected -> Authenticating -> InWorld -> Closing`。
- `LoginRequest` 增加 `RequestId`、`AuthTicket`、`CharacterId`；同一 RequestId 重传返回同一响应且不重复创建实体。
- 同一 session 已登录时不得再创建 playerId，也不得切换角色；同一角色的并发登录执行明确的“拒绝新会话”策略。
- 引入 `ILoginTicketValidator`。开发票据 `--allow-dev-auth` 只在 Debug/loopback 启用；生产 profile 拒绝开发验证器；生产模式未配置验证器必须拒绝启动。
- 生产验证器解析 HMAC-SHA256 票据，签名载荷覆盖 AccountId、CharacterId、AllowedWorldId、到期时间与 nonce，并权威给出身份；请求中的 CharacterId/ZoneId 只能与票据匹配，不能改变身份或路由；无效或过期票据不得创建 session/player/world 状态。
- `ProtocolVersion` 升到 2；生产 profile 拒绝 v1，开发 profile 只在显式诊断开关下解析 v1 且仍必须先通过票据认证，禁止无鉴权降级路径。
- 客户端移除 `md5_hash_placeholder`；不得在日志记录票据或密码。
- 断线清理按稳定 CharacterId 幂等执行；旧实体、Room 项、observer 和反向映射必须一次性清完。

### P0-6：有界主循环与网络资源

以下数值是本 Spec 的初始安全预算，可由压测下调，不能在没有证据时上调：

| 项 | 上限/策略 |
|---|---|
| 服务端 tick | 15Hz；世界工作 p95 < 50ms，p99 < 66ms |
| 每 tick 入站处理 | 最多 2048 条且最多 20ms；先到任一上限即留到下 tick |
| 全局入站队列 | 8192 条；状态类 latest-wins，命令类满时拒绝并记指标 |
| 单 session 未处理消息 | 256 条；持续超限断开 |
| 未认证时长 | 10 秒 |
| WebSocket 单消息 | 64KiB（匹配协议 ushort 长度上限）；分片聚合 5 秒超时 |
| WebSocket 待发 | 256 帧或 4MiB，先到者为准；慢客户端断开 |
| UDP/KCP 未认证连接 | 全局 512、单 IP 8；无状态 cookie 验证后才创建高频会话 |

- `GameLoop` 只处理 tick 开始时的快照数量，不在同一 tick 追赶新入队项。
- KCP 的 `Input/Update/Send/TryRecv` 由同一执行上下文拥有；不允许每会话多个线程并发触达 KCP 实例。
- 每个平台/构建配置只能启用一个主传输；Android/iOS Player 用 KCP + Protobuf + ADR-0003 安全层，WebGL 用 WebSocket/WSS，Editor/Windows 测试 lane 显式选择。生产默认不再是全平台 WSS；KCP 只在非 WebGL 的移动 Player 路径启用。
- 协议收到 `Encrypted/Compressed` 标志但当前未配置实现时立即拒绝；XOR/空压缩不得标作安全能力。

### P0-7：客户端主线程封送与有界消费

- 新增 `ClientIngressMailbox`，按消息类别区分：
  - `WorldSnapshot`：有界 FIFO，不得直接 latest-wins；当前快照含增量字段掩码，丢中间帧可能永久丢状态。
  - 位置纠偏、名字变化、离场、复活、错误：有界 FIFO。
  - 战斗结果与可靠命令：有界 FIFO，不允许静默丢弃；溢出触发重同步或断线。
- 邮箱容量 512 条；主线程每帧最多处理 64 条或 2ms。发生溢出时设置一次性 fatal 标志，下一主线程帧断线并重建世界；不能静默丢可靠消息。
- 主线程 `Update` 先 drain 登录、错误、断线和其他 control 消息，再根据状态决定是否执行 InWorld tick；登录中的登录响应、InWorld 前的错误与断线必须在状态迁移前被消费。
- 明确列出 `ClientEnvelope.cs`、`DrainResult.cs`、`LoginResult.cs`；若实现为既有文件的嵌套类型，计划必须说明宿主文件，不得用“相关文件”占位。
- `ClientNetworkHost` 持有连接 lifetime CTS 与正在执行的 Task；`Disconnect` 与 `OnDestroy` 取消任务并观察所有异常，禁止未观察的 fire-and-forget。
- 测试覆盖 `Pump_Drains_LoginResponse_While_LoggingIn`、`Pump_Drains_Error_And_Disconnect_Before_InWorld`，以及 Host 销毁取消且无未观察异常。
- `OnError`、`OnDisconnected`、`OnServerRespawn` 等事件只在主线程触发。
- `MessageDispatcher` 只在主线程访问，或内部做不可变订阅快照；不得让普通 Dictionary 被并发读写。
- `ConnectAndLogin` 返回 `UniTask<LoginResult>` 或 `Task<LoginResult>` 并接收 CancellationToken，不再使用 `async void`。

## 8. P1 设计

### P1-1：WorldInstance 与空间系统规模化

- 新增 `WorldInstance`，独占 `EntityStore`、AOI、Combat、Monster、Navigation、Avoidance 与世界邮箱。
- `WorldRouter` 只负责实例创建/查找/销毁和 session 路由；实例票据决定进入哪个世界。
- 跨实例 AOI、伤害、仇恨、ORCA 和广播全部由测试证明为 0。
- AOI 使用 cell -> entity 增量索引；反向观察者查询不得 O(N²)。
- ORCA 使用空间邻居并保留最近 K 个，不按 ID 截断；最终位移再次投影 navmesh。
- 怪物感知分桶并受每 tick AI 预算控制；导航改为可续跑工作项或后台结果邮箱，旧 generation 结果不得回写。

### P1-2：战斗、3C 与模块边界收敛

- 在正式技能形状/权威命中链之外，不保留第二套不完整的 `AttackHitbox/HitZone/PlayerHitZone` 伤害语义；确认无生产调用后删除，或改为只产生接触候选的薄适配器。
- `StateCoordinator` 使用显式 `IBaseStateView`/`IHitStateView`，移除 `object`、反射与魔法整数；删除无消费者的空生命周期 API。
- `Sys3CEntry` 按“校验 -> 装配 -> 注册 -> 启用”执行；失败时禁用组件并撤销注册。
- 修复 SkillCoordinator 的 lambda 退订和执行器释放；所有异步绑定带销毁 CancellationToken。
- 更新 `Sys3C-CurrentDesign.md`，移除不存在的 AttackFSM。
- P1 只先拆依赖较低的 Bag、Stamina asmdef，并用依赖守卫证明它们不反向引用 `GameSys`；Sys3C/Skills/Combat/Monster/SyncLayer 的大规模拆分延期到 P2。当前没有 HybridCLR，不以热更粒度作为理由。

### P1-3：Addressables 与 UI 生命周期

- `ResourceManager` 支持同 key/type 并发单飞、取消、失败释放、重复场景保护和测试后 reset。
- 新 API 返回 `ResourceLease<T>`；旧 `Load/Release(key)` 仅作为过渡层并标记废弃。
- 创建 Addressables settings/groups，key 集中在 `GameConsts`；首批迁移 UI 面板与高频 VFX prefab。
- UI Show/Hide 使用每面板串行状态机和取消 token；最终目标状态不会被迟到动画覆盖。
- CanvasGroup 隐藏时同步设置 `interactable=false` 和 `blocksRaycasts=false`。
- 加入 Safe Area、返回键/模态栈和资源加载失败占位；本地化/无障碍只建立接口与基础门禁，不在本轮翻译全部内容。

### P1-4：移动端性能与表现生命周期

- Monster 感知改为 5–10Hz 分桶；真正实现 `ResetForSpawn/ResetForDespawn`，否则删除伪对象池。
- Nameplate/FloatText 使用复用缓冲、平方距离、可见状态缓存和 generation token；移除无用 GraphicRaycaster。
- HitStop 仅响应本地来源，默认使用局部 Animator/Playable 速度，不修改全局 `Time.timeScale`。
- VFX 池按 prefab key 分区，追踪活动对象并在场景卸载时完整回收；接入移动端质量预算。
- 目标档位：参考 Android 中端机 30 FPS（p95 frame <= 33.3ms）、无持续每帧 GC；实际参考机型在执行时记录到验收报告。

### P1-5：移动构建、CI 与架构门禁

- 新增统一 `MobileBuildScript`：Android/iOS、IL2CPP、ARM64、显式场景、批处理失败非零退出。
- PlayMode 拆为 transport-independent 与 native-network 两个测试程序集：WebGL 可运行前者，StandaloneWindows64 运行两者；任何 0 tests 都是失败。
- 构建身份从 CI/环境配置读取；检测到 `DefaultCompany` 或默认 application id 直接失败。开发缺省 id 使用 `com.jincaizi.cool.dev`，不得用于商店包。
- CI 至少执行：服务端测试、Unity EditMode、非零 PlayMode 核心套件、Android Development Build、协议/配置守卫。
- iOS 在具备 macOS runner 时执行 Xcode 导出；Windows CI 先验证 Unity iOS player 生成前置配置，不伪报完整 iOS 构建通过。
- 增加架构守卫：禁止新增非白名单 `GetComponent/FindObject*`、禁止玩法层 `Resources.Load`、禁止跨层 asmdef 反向引用。
- 覆盖率作为趋势指标：P1 提交可复现基线并禁止任一目标程序集下降超过 1 个百分点；服务端核心域 80%、Unity SyncCore/Bag/Skills 核心纯逻辑 70% 登记为 P2 提升目标。

### P1-6：可观测性与公网运行基线

- 结构化指标：tick p50/p95/p99、queue depth/drop、session/unauth 数、WS/KCP backpressure、AOI 可见数、nav p95、GC/分配；通过 OpenTelemetry Prometheus exporter 暴露。
- 健康检查区分进程存活与世界 tick 健康；连续 tick overrun 触发告警。
- 生产入口使用 Caddy（示例配置）或等价可信反向代理终止 WSS；管理/指标端点默认只绑定 loopback；禁止明文票据穿越公网。
- 配置、怪物表、地图和协议版本在生产模式 fail-fast；开发 fallback 必须显式开启。
- 优雅停机先停止准入，再排空有限队列、关闭世界与会话；本轮没有权威数据库，不声称持久化角色或背包状态。

## 9. 分阶段交付与依赖

```text
Gate 0 测试可信
  -> P0-A 客户端数据正确性（Bag + DamageContext + EventBus）
  -> P0-B 单世界收口 + 登录幂等
  -> P0-C 移动/防御权威
  -> P0-D 服务端与传输有界化
  -> P0-E 客户端邮箱与主线程化
  -> P0 Gate（冻结解除）
  -> P1-A WorldInstance/空间规模化
  -> P1-B 战斗/3C/程序集收敛
  -> P1-C Addressables/UI/移动表现
  -> P1-D 移动构建/CI/可观测性
  -> P1 Gate（公开测试准备）
```

允许并行：P0-A 与 P0-B；P0-D 的 GameLoop 与客户端 P0-E。
禁止并行：P0-C 必须基于 P0-B 的稳定玩家身份/单一状态；P1-A 必须在单世界收口测试通过后开始；Addressables 迁移必须在 lease API 稳定后开始。

## 10. 验收门禁

### 10.1 P0 Gate

- 服务端全量测试通过；Unity EditMode 全量通过；PlayMode 核心套件实际发现并运行至少 1 个测试，0 tests 失败。
- Bag 整理前后逐实例元数据、扩展容量与总数量完全一致。
- 两个施法者共享同一技能资产时，任意命中顺序都不改变资产字段或彼此结果。
- 登录重传不会增加实体/Room/session 映射数量；占位密码路径不存在。
- 非白名单 Zone 登录被拒绝，且不会创建 Room/Entity。
- 重复、倒序、伪造时间戳、NaN/Infinity、异常 Y 的移动包均不污染权威状态。
- 客户端永久发送防御 true 不能获得无限格挡；精力归零后服务端强制结束防御。
- 持续洪泛下 tick 仍按预算推进；队列与内存保持有界；慢 WS 客户端被主动断开。
- KCP 竞态压力测试无并发触达同一 KCP 对象；未认证 UDP 洪泛不创建高频会话任务。
- 客户端所有 Unity 对象更新只发生在主线程；邮箱溢出策略有测试和指标。

### 10.2 P1 Gate

- 两个 WorldInstance 在相同坐标下互不可见、不可命中、不可进入仇恨或 ORCA 邻居集合。
- 200 bot + 配置数量怪物的服务器压测下，tick p95 < 50ms、p99 < 66ms；无持续队列增长。
- Android ARM64 IL2CPP Development Build 成功；iOS 构建状态按 runner 能力诚实报告。
- Addressables 同 key 100 个并发请求只产生一次底层加载；失败/取消/场景重复不泄漏 handle。
- 进入/退出 UI、场景和战斗 30 分钟后，资源句柄、活动池对象和订阅数量回到稳定基线。
- 参考 Android 档位 p95 frame <= 33.3ms，稳态无每帧 GC；名字板/VFX/怪物预算降级可观测。
- CI 对 0 tests、默认包名、协议漂移、配置错误、架构护栏和覆盖率下降明确失败。
- TLS/WSS、生产鉴权验证器、结构化指标和优雅停机演练完成后，才可标记“公开测试准备”。

## 11. 风险与回滚

| 风险 | 缓解 | 回滚策略 |
|---|---|---|
| P0 同时触及协议与两端状态，回归面大 | 每个协议变化先加双端 roundtrip 测试；保留一版兼容读取 | 通过 ProtocolVersion 拒绝不兼容客户端，不做隐式降级 |
| WorldInstance 改造影响所有服务端模块 | P0 先单世界收口；P1 先建 `Global` 实例适配层，再逐模块迁移 | 保留单 `Global` 实例配置，不回退到跨区全局容器 |
| 有界队列造成此前隐藏的丢包 | 按消息类别定义合并/拒绝/断开，全部打指标 | 调整生产者速率或预算；不得改回无界队列 |
| 服务端格挡改变现有手感 | 客户端继续预测动画，服务端回包纠偏；起手/收手参数配置化 | 回滚参数，不回滚权威所有权 |
| Addressables 迁移引发引用泄漏或首屏变慢 | 先迁移一个 UI 面板和一类 VFX，记录句柄/耗时基线 | 单个资源可退回旧适配层，但禁止新增 `Resources.Load` |
| asmdef 拆分引发循环依赖 | 先提取纯接口/数据，使用架构测试锁依赖 | 每个拆分任务独立提交，可逐程序集回滚 |
| 移动端实体预算没有真实设备数据 | 先定义逻辑预算和采样报告，执行时记录实际机型 | 降低 Full/特效/名字板预算，不跳过门禁 |

## 12. 明确延期到 P2

- 服务端权威背包持久化、装备、交易和经济系统；P0 只保证数据不损坏并禁止联网本地永久写。
- 多地图跨服迁移、数据库分片、灾备与账号中心。
- 完整输入重映射、本地化内容生产、屏幕阅读器和完整无障碍认证。
- 动态障碍导航、分层 navmesh、客户端命中回滚/延迟补偿。
- HybridCLR、DOTS/ECS、可视化行为树和多传输并行生产。

## 13. 文档治理

- 本设计批准后，实施任务必须引用本文件。
- 行为变化同步更新对应模块 README；旧网络设计中 HybridCLR 与“客户端默认 KCP”的描述不再作为当前事实。
- 每个 P0/P1 Gate 生成一次验收报告，记录测试总数、构建目标、压测参数、参考设备与未执行项目。
- 任何门禁例外必须写明到期日、责任人、风险和恢复条件；不能用“已知问题”无限期豁免。
