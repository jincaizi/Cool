# P0/P1 质量改进执行设计

日期：2026-11-07
状态：已确认，执行基线
适用范围：Unity 客户端、共享同步规则、网络传输、.NET 服务端、构建与质量门禁

依据：

- [P0/P1 质量改进设计](2026-11-05-p0-p1-quality-improvement-design.md)
- [P0/P1 质量改进实现计划](../plans/2026-11-05-p0-p1-quality-improvement.md)
- [P0/P1 质量改进 Unity 专家复核报告](../reviews/2026-11-06-p0-p1-unity-expert-review.md)

## 1. 背景与决策

2026-11-05 的设计与 33 任务计划已经完成，但尚未开始实施（计划中 164 个步骤全部未勾选）。2026-11-06 的 Unity 专家复核结论为 **CHANGES REQUIRED**：7 项 P0 计划阻塞、13 项 P1 落地缺口。复核同时确认，问题集中在实施方式（程序集边界、BuildTarget、序列化、Domain Reload、Addressables 生命周期、IL2CPP AOT），不是方向错误。

本设计记录以下已确认决策：

1. **范围全量**：执行原 P0（任务 1–14）与 P1（任务 15–33），不缩减风险排序。
2. **滚动执行**：按原文档波次推进，一次只启动一个波次；长期推进，不设一次性完成承诺。
3. **KCP 前移**：采纳复核 U-P0-04 的推荐，任务 13 前移为 P0，排在任务 14 前。
4. **阶段 0 先行**：先修订原 Spec 与计划，把复核要求写进任务、文件清单和 Gate，再改代码。
5. **证据规则**：每个 Gate 只认本轮生成的 XML、日志、退出码和产物；未执行项标记 `not-verified`。

本文件是执行与修订基线。**任务级细节以修订后的原计划为准**；本文件不重复 33 个任务的完整步骤，只定义执行模型、修订要求、Gate 条件和遗漏补齐项。

## 2. 范围

### 2.1 纳入

- 原 P0：任务 1–14。
- 原 P1：任务 15–33。
- 复核报告 U-P0-01 至 U-P0-07、U-P1-01 至 U-P1-13 的全部修订要求。
- 复核第 5 节规定的阶段顺序与 Gate 条件。

### 2.2 不纳入

- 原 Spec 4.2 的非目标：HybridCLR、DOTS/ECS、新玩法、多传输并行生产、账号中心、支付、跨服、数据库分片。
- 复核第 8 节声明的本报告边界之外的新增验证（例如未配置 runner 的 iOS 真机验证）。
- 本设计不新增原计划之外的功能任务；阶段 0 只补文档、清单、验收条件与治理基线。

### 2.3 冻结规则

P0 Gate 通过前，不新增联网玩法。P1 Gate 通过前，不标记“公开测试/商店发布准备”。该规则来自原 Spec 第 3 节，继续有效。

## 3. 执行模型

### 3.1 波次

| 波次 | 任务 | 退出条件 | 门禁产物 |
|---|---|---|---|
| 阶段 0 | 文档修订 | 复核指出的架构矛盾全部消除；任务文件清单足以实施 | 修订后的 Spec 与计划 |
| Gate 0 | 1 | 测试入口可信，三套测试非 0 | `production/quality/gate-0-<date>.md` |
| P0-A | 2–4 | Bag、伤害上下文、EventBus/技能事务全绿 | 波次报告 |
| P0-B | 5–8 | 单世界、幂等登录、移动、格挡精力全绿 | 波次报告 |
| P0-C | 9–13 | 有界队列、准入背压、KCP、客户端邮箱全绿 | 波次报告 |
| P0 Gate | 14 | 原 Spec 10.1 + 复核 7.1 全部满足 | `production/quality/p0-gate-<date>.md` |
| P1-A | 15–18 | WorldInstance、AOI、ORCA、导航规模门禁 | 波次报告 |
| P1-B | 19–28 | 客户端收敛、资源、UI、VFX、Monster、性能 | 波次报告 |
| P1-C | 29–33 | 构建、CI、可观测性、最终报告 | `production/quality/p1-gate-<date>.md` |

允许的并行关系沿用原计划：P0-A 与 P0-B 可并行；P0-C 的服务端 GameLoop 与客户端邮箱可并行；其余依赖按原计划第 9 节。一次会话只推进一个波次内的任务，不跨波次并行。

### 3.2 会话、分支与提交

- **前置**：当前工作树有 115 个未提交改动，其中包含已删除的 UI 贴图、插件 DLL 变更和当前功能分支的工作。阶段 0 开始前先分类处理：提交、stash 或丢弃（逐项确认），确保质量工作从干净的 `git status` 开始。
- **分支**：每个波次一个分支，命名 `quality/<wave>`；需要隔离时使用 worktree。波次 Gate 通过后合并。
- **提交**：每任务独立提交；只暂存任务清单列出的路径；禁止 `git add .` / `git add -A`。
- **会话**：每个波次或任务独立会话；上下文到 20–30 万 token 时 compact；不把多个波次揉进一次会话。

### 3.3 任务完成判定

每项任务至少同时满足：

1. 项目完整编译通过（Unity 与服务端），无程序集引用错误；
2. 对应 EditMode 或 PlayMode 测试被实际发现且通过；
3. 结果由本轮独立 XML、日志、构建产物或设备证据证明；
4. 失败、取消、销毁、重复初始化和关闭 Domain Reload 等生命周期路径有明确行为；
5. 不以历史 Console 日志、手工预启动服务或未验证的平台状态作为通过证据。

### 3.4 Gate 证据与报告

- 每个 Gate 生成报告，至少包含：测试总数、通过、失败、跳过、耗时、构建目标、测试程序集、服务端进程端口、XML/日志路径、产物路径、未执行项及 `not-verified` 原因。
- Gate 0 记录修订前基线：服务端 409/409；Unity EditMode 629/630（唯一失败来自外部 Unity MCP `ping` 错误日志污染）；Unity PlayMode 当前为 0（活动目标 WebGL，且 `KcpNet.PlayModeTests.asmdef` 带 `!UNITY_WEBGL`）。
- 门禁例外必须写明到期日、责任人、风险和恢复条件；不能用“已知问题”无限期豁免。
- 报告写入 `production/quality/`，并由当次提交引用。

## 4. 阶段 0：文档修订

阶段 0 只改文档、asmdef 清单和脚本设计，不改运行时代码。退出条件是复核第 7 节的检查项全部有落点。

### 4.1 U-P0 修订映射

| 复核项 | 落点 | 必须写入的内容 |
|---|---|---|
| U-P0-01 | 任务 1 | 新建 Editor-only `GameSys.EditorQuality.asmdef`；`QualityGate` 只做纯验证，不切 BuildTarget；`InternalsVisibleTo("GameSys.EditorTests")`；asmdef、`.meta`、程序集引用和提交清单全部列出 |
| U-P0-02 | 任务 1、14、31 | 原生 Unity CLI runner 前移到任务 1；启动独立进程时传 `-buildTarget StandaloneWindows64`；测试进程内不执行 `SwitchActiveBuildTarget`；EditMode、通用 PlayMode、Native PlayMode 各自独立 XML 与日志；MCP `tests-run` 仅本地便利；0 tests、XML 缺失/不可解析、异常退出均返回非零；WebGL 构建单独验证；门禁输出总数/通过/失败/跳过/耗时 |
| U-P0-03 | 任务 1、14、31 | Native suite 自行启动测试服务器：动态端口、以 TCP 端口可连接作为 ready 判据（服务端 ready 日志同时保存）、启动失败/提前退出/端口冲突/teardown 残留均失败；`finally` 停止服务器并保存日志；通用与 Native 分别传 `-assemblyNames`、分别生成 XML；迁移测试文件及 `.meta` 清单 |
| U-P0-04 | 任务 13、14 | 任务 13 前移为 P0，排在任务 14 前；KCP 连接、断开、重复释放、接收循环停止有自动测试；保留“生产默认 WSS、KCP 实验性默认禁用”的检查；30 分钟 soak 保留 |
| U-P0-05 | 任务 4 | `EventSubscription` 改为 `sealed class`，用 `Interlocked.Exchange` 或等价原子状态保证跨副本幂等退订；`Emit` 使用 copy-on-write 快照，不在热路径 `ToArray()`；`ResetAll` 同时清普通订阅、定向订阅和暂停状态并回收空桶；`SubsystemRegistration` 重置；测试 `Copied_Tokens_Dispose_Only_Once`、重复 Dispose 不抛异常、Emit 热路径无临时数组分配 |
| U-P0-06 | 任务 7 | 从生产 `MovementReporter` 删除 `injectedSendDelayMs` 和 `Thread.Sleep`；把 `DateTime.UtcNow` 改为可注入时钟；延迟、抖动、丢包模拟移到 fake transport 或可控 test scheduler；增加 `Injected_Network_Delay_Does_Not_Block_MainThread`；服务端移动校验 v2 的其余内容仍按原任务 7 执行 |
| U-P0-07 | 任务 12 | `Update` 先 drain 登录、错误、断线和其他 control 消息，再根据状态决定是否执行 InWorld tick；明列 `ClientEnvelope.cs`、`DrainResult.cs`、`LoginResult.cs` 或说明其嵌套类型；`ClientNetworkHost` 持有连接 lifetime CTS 和正在执行的 Task；`Disconnect` 与 `OnDestroy` 取消任务并观察所有异常；禁止未观察的 fire-and-forget；测试 `Pump_Drains_LoginResponse_While_LoggingIn`、`Pump_Drains_Error_And_Disconnect_Before_InWorld`、Host 销毁取消且无未观察异常 |

### 4.2 U-P1 修订映射

| 复核项 | 落点 | 必须写入的内容 |
|---|---|---|
| U-P1-01 | 任务 19 | 定义唯一运行时伤害模型：`DamageRequest` 含不可变 `SkillDamageContext`、方向和 `HitContact`；建立生产调用者、实现者、测试 fake 的迁移矩阵；先保留 `[Obsolete] DamageBlock` 适配 overload，迁移完成后删除；每阶段先编译和目标测试，再扫描 Prefab GUID；完成后无 Missing Script、无旧 GUID、无旧接口调用 |
| U-P1-02 | 任务 20 | `PlayerSpawner`、`PlayerSpawnedEvent` 和场景组合根纳入任务；PlayerSpawner 使用类型化 Prefab 根；实例化后、`Start` 前由组合根注入 Camera 和其他场景依赖；`PlayerSpawnedEvent` 直接携带 `Sys3CEntry`；`Sys3CEntry` 在注册 Physics/EventBus 前一次性验证全部依赖；初始化失败清理半注册对象；两个玩家 Prefab 和真实入口场景均有 PlayMode 覆盖 |
| U-P1-03 | 任务 21 | `AddressablesResourceBackend` 使用 UniTask Addressables awaiter 或 Completed callback；所有 Addressables API 从 Unity 主线程进入；删除玩家运行路径中的 `WaitForCompletion()`；同步兼容接口只允许 Editor 工具调用并带明确守卫；增加 WebGL Player 编译门禁和源码守卫；加载失败、取消和 shutdown 不泄漏 handle |
| U-P1-04 | 任务 21 | `SceneLease` 保存 `AsyncOperationHandle<SceneInstance>`；提供幂等 `UniTask UnloadAsync()` 或等价 async-dispose 契约；加载取消后若底层操作已开始，等待完成再卸载和释放；明确 Additive、Single、重复卸载和 ResourceManager shutdown 的所有权规则；重复卸载、加载中取消、Single 切换和 shutdown 测试通过；句柄计数归零 |
| U-P1-05 | 任务 23 | 采用场景 Presenter 在 `Awake` 调用 `UIBootstrap.RegisterFailurePresenter(this)`、`AfterSceneLoad` 注入 `UIManager` 的方案，不新增 `UIBootstrapBehaviour`；增加 `SubsystemRegistration` 清理；覆盖 `CharacterSelect` 和直接进入 `DemoDay` 两条启动路径；没有 Presenter 时提供非递归、非 Addressables 依赖的降级行为；连续两次 PlayMode 不残留失效静态引用 |
| U-P1-06 | 任务 22、31 | 增加 `AddressablesBuildScript.BuildPlayerContent` batchmode 入口；快速测试使用 Use Asset Database；生产验证使用“进程级切换目标 → BuildPlayerContent → Use Existing Build → PlayMode 生命周期测试”；运行 Addressables Analyze；校验重复 address、缺失依赖、重复 bundle dependency、Resources/Addressables 双重打包；固定 Core UI、Combat VFX 等 group/schema、Build Path、Load Path、压缩和打包策略；验证 catalog、hash、bundle 产物实际存在；清洁 checkout 可重复构建，Player Build 使用同一份已验证 catalog |
| U-P1-07 | 任务 26 | 明确迁移真实 `HitSparkBurst`；未使用的 `HitSparkParticle` 不删除，但在任务中记录为无引用遗留物；把 `GameSettings.cs`、`GameSettings.asset`、`DemoDay.unity` 和实际引用 Prefab 加入文件清单；Heal 的直接 `GameObject` 引用改为 address、`AssetReference` 或 VFX descriptor；定义 World/Combat loading → prefab lease → 预热 pool → 订阅事件的完整链路；场景退出时先回收或销毁 active/idle 实例，再释放 lease；明确未 Ready 时丢弃、排队或降级的策略；CI 按 GUID 检查 Addressable VFX 不再被 Scene、Resources asset 或非 Addressable Prefab 直接引用 |
| U-P1-08 | 任务 29、31 | Android 和 iOS 进程启动时分别指定 `-buildTarget Android` / `-buildTarget iOS`；build method 只验证 active target，不负责切换；Windows 仅执行 `ValidateIosConfiguration`；用仓库变量 `HAS_IOS_RUNNER` 控制 iOS job；没有 runner 时生成 `ios-status = not-verified` artifact/summary，不无限排队；固定 runner labels，检查 Unity 版本、许可证、Build Support、SDK/NDK/JDK 与 Xcode；写明唯一 BuildScenes 清单；校验 BuildReport、APK/AAB 或 Xcode 工程确实存在；构建脚本的设置修改由 `finally` 恢复 |
| U-P1-09 | 任务 29、33 | 增加 Android IL2CPP 设备或模拟器 smoke，覆盖冷启动、CharacterSelect、Addressables 初始化、内建 UI 加载、代表性协议编解码和进入 DemoDay；根据 strip diagnostics 和 smoke 证据配置 `link.xml` 或 `[Preserve]`；固定 Managed Stripping Level；为 MessageTypeRegistry 中的消息建立可执行的 AOT 保留契约；只有真机或模拟器证据存在时才可声明 Android runtime verified |
| U-P1-10 | 任务 24、27、31、33 | 纯 C# 热路径使用隔离后的 `GC.GetAllocatedBytesForCurrentThread`；Unity 对象和帧性能使用 Unity Performance Testing package 或 Standalone Player 性能测试；设备 soak 固定设备档位、分辨率、Quality Level、预热时间、采样窗口和机器可解析输出；Coverage 固定 `-enableCodeCoverage`、`-coverageResultsPath`、`-coverageOptions`、assembly filter 和排除规则；Coverage XML 缺失、损坏或目标程序集未出现时失败；不把一次 Editor 内零分配断言作为移动端性能结论 |
| U-P1-11 | 任务 4、21、23、30 | 为 `EventBus`、`ResourceManager`、`UIManager`、`BagManager` 等静态入口定义 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` 重置策略；资源系统提供可等待的 `ShutdownAsync`；测试提供受控的 `ResetForTests`；UI 销毁顺序固定为取消加载 → 销毁实例 → 释放 descriptor lease → 清空 Instance；连续运行两轮 PlayMode，并在关闭 Domain Reload 的配置下验证无残留 |
| U-P1-12 | 任务 28 | 严格按 inactive 阶段 `PrepareForSpawn` → 激活对象 → NavMesh 投影/启用 agent/`Warp` → 确认 `isOnNavMesh` → 注册 Physics/感知/AI tick 的顺序执行；`OnEnable` 不读取上一次生命周期数据；AI 首帧前 `NavMeshAgent.isOnNavMesh == true`；连续租出和归还不累积订阅、碰撞注册或状态效果；投影失败的对象不进入活跃怪物集合 |
| U-P1-13 | 任务 23、29、33 | Android 验收补充冷启动与热启动、前台/后台恢复、Wi-Fi 与移动网络切换、断网/重连/服务端断开、Safe Area/分辨率/方向变化、Android 返回键与 modal back stack、低内存回调、场景切换期间取消 Addressables 加载；`SafeArea_Updates_On_Orientation_And_Resolution_Change` 通过 `IScreenMetrics` 或等价接口注入屏幕参数；每条 smoke 有自动结果或明确人工验收证据；崩溃、ANR、未观察异常和资源泄漏均为 0 |

### 4.3 原计划任务的显式验收补齐

以下任务在波次表中已覆盖，但原计划未在复核中逐项展开。阶段 0 必须把验收条件写回对应任务，否则执行时容易漏：

| 任务 | 必须补写的验收条件 |
|---|---|
| 5 单 Global 世界 | 登录只接受白名单 `Global`；Room 与 `EntityStore` 使用同一 `PlayerState`；`MaxRooms`、最大人数、Zone 名称长度和空房回收生效；换区返回“不支持”，不得先删旧房 |
| 6 幂等登录与票据 | `Connected → Authenticating → InWorld → Closing` 状态机；`LoginRequest` 含 `RequestId`、`AuthTicket`、`CharacterId`；同一 RequestId 重传返回同一结果；HMAC-SHA256 票据验证器权威给出身份；开发票据仅 loopback/Debug；生产未配置验证器拒绝启动；移除 `md5_hash_placeholder`；`ProtocolVersion` 升到 2；断线清理按稳定 CharacterId 幂等 |
| 8 格挡与精力权威 | 独立 `DefendCommand(Start/Stop, sequence)`；服务端持有起手、收手、朝向、死亡清理和精力消耗；客户端永久发送防御 true 不能获得无限格挡；精力归零后服务端强制结束防御；旧 `IsDefending` 仅兼容读取并忽略 |
| 9 有界 GameLoop | tick 15Hz；每 tick 最多 2048 条且最多 20ms；只处理 tick 开始时的快照数量；全局入站队列 8192；单 session 未处理消息 256；持续超限断开 |
| 10 准入与 WS 背压 | 按 Spec 6.0.2 实现单 IP/session/Login/消息/字节/PositionSync/Cast/控制消息速率；WS 单消息 64KiB；待发 256 帧或 4MiB，先到者为准，慢客户端断开；分片聚合 5 秒超时；未配置的 `Encrypted/Compressed` 标志立即拒绝；每个平台/构建配置只启用一个主传输 |
| 11 服务端 KCP | `Input/Update/Send/TryRecv` 由同一执行上下文拥有；删除每会话多线程并发触达；未认证 UDP 全局 512、单 IP 8；cookie 验证后才创建高频会话 |
| 25 局部 HitStop | 只响应本地来源；默认使用局部 Animator/Playable 速度；不修改全局 `Time.timeScale` |
| 30 架构守卫与 asmdef | 先实现守卫：禁止非白名单 `GetComponent/FindObject*`、禁止玩法层 `Resources.Load`、禁止跨层 asmdef 反向引用；守卫稳定后再拆 Bag/Stamina asmdef，并用依赖守卫证明不反向引用 `GameSys`；Sys3C/Skills/Combat/Monster/SyncLayer 的大规模拆分留 P2 |
| 32 可观测性与公网基线 | `System.Diagnostics.Metrics` + OpenTelemetry Prometheus exporter；指标含 tick p50/p95/p99、队列深度/丢弃、session/unauth 数、WS/KCP 背压、AOI 可见数、导航 p95、GC/分配；健康检查区分进程存活与世界 tick；Caddy 示例终止 WSS；管理/指标端点默认 loopback；配置/怪物表/地图/协议版本生产模式 fail-fast；优雅停机先停止准入、排空有限队列、关闭世界与会话 |
| 33 P1 Gate | 按原 Spec 10.2 与复核 7.2 逐项判定；两个 WorldInstance 互不可见/不可命中/不进入仇恨或 ORCA；200 bots + 怪物压测 tick p95 < 50ms、p99 < 66ms；Android ARM64 IL2CPP Development Build 成功；iOS 按 runner 能力诚实报告；Addressables 并发单飞与句柄归零；30 分钟资源/订阅基线；参考 Android 档位 p95 frame ≤ 33.3ms；CI 对 0 tests/默认包名/协议漂移/配置错误/架构护栏/覆盖率下降明确失败；TLS/WSS、鉴权、指标、优雅停机演练完成 |

### 4.4 Gate 条件固化

阶段 0 必须为 Gate 0、P0 Gate、P1 Gate 分别固定：

- 输入：任务范围、构建目标、测试程序集、服务端启动参数；
- 命令：完整可复制命令；
- 产物：XML、日志、构建产物、报告路径；
- 失败条件：非零退出、0 tests、XML 缺失/损坏、服务端未退出、产物缺失；
- `not-verified` 条件：无 runner、无设备、无平台环境。

同时更新原 Spec 的资源、消息泵、启动器和静态生命周期契约，使实施者不需要自行猜测。

### 4.5 治理文件与脚本

- **技术偏好**：`.claude/docs/technical-preferences.md` 目前仍是模板占位。阶段 0 用 `CLAUDE.md` 和 Spec 第 5 节的全局约束填充实际值（引擎、平台、输入、命名、性能预算、测试、禁止模式、允许库）。
- **ADR**：新建 `docs/architecture/ADR-0001-quality-baseline.md`，记录 Spec 6.0 的实施默认值（传输、协议版本、CI、覆盖率、可观测性、停机）。本轮不建立完整 ADR 积压。
- **协议双端一致性**：Gate 0 必须运行 `Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs`、`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs` 与服务端协议测试；若缺少跨端一致性断言，在任务 1 补一个双端 roundtrip 测试。
- **测试脚本定位**：`tools/unity-tests.sh` 改为原生 CLI Gate runner；MCP `tests-run` 的便利入口移到 `tools/unity-tests-mcp.sh`（或文档化命令），不得作为 Gate 证据。同步更新 `docs/agent-token-hygiene.md`。
- **README 同步**：任何模块行为变化同步更新该模块 README；波次 Gate 报告引用更新后的 README。
- **工作树分类**：阶段 0 前逐项处理 115 个未提交改动，特别是 `Assets/PreRes/Texture/UI/` 的删除和 `Assets/Plugins/NuGet/` 的 DLL 变更；不得盲提交。

## 5. 第一波执行：Gate 0（任务 1 修订版）

阶段 0 完成后，第一波只做任务 1。

### 5.1 文件

- 新建：`Assets/Scripts/Editor/Quality/GameSys.EditorQuality.asmdef` 及 `.meta`
- 新建：`Assets/Scripts/Editor/Quality/QualityGate.cs` 及 `.meta`
- 新建：`Assets/Scripts/Editor/Quality/AssemblyInfo.cs`（`InternalsVisibleTo`）及 `.meta`
- 修改：`Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`（引用 `GameSys.EditorQuality`）
- 新建：`Assets/Scripts/Tests/Editor/Quality/QualityGateTests.cs`
- 新建：`Assets/Scripts/Tests/Editor/Architecture/PlayModeAssemblyContractTests.cs`
- 修改：`Assets/Scripts/Tests/PlayMode/KcpNet.PlayModeTests.asmdef`（通用套件改名为 `GameSys.PlayModeTests`）
- 新建：`Assets/Scripts/Tests/PlayMode/NativeNetwork/KcpNet.NativePlayModeTests.asmdef` 与迁移文件清单（含 `.meta`）
- 修改：`tools/unity-tests.sh`（原生 CLI runner）
- 新建：`tools/unity-tests-mcp.sh`（便利入口）
- 新建：`tools/quality-gate.sh`
- 修改：`docs/agent-token-hygiene.md`

### 5.2 实现要点

1. `QualityGate` 只验证当前目标、测试发现数、失败数和包名；不调用 `SwitchActiveBuildTarget`。
2. 原生 runner 启动独立 Unity 进程，传 `-buildTarget StandaloneWindows64`；EditMode、通用 PlayMode、Native PlayMode 分别输出 XML 和日志。
3. Native suite 启动/停止测试服务器：动态端口 + `COOL_TEST_SERVER_PORT` 环境变量；服务端用 `dotnet <Server3C.dll> <port>` 启动；ready probe + `finally` 清理。
4. 测试文件中的硬编码 `127.0.0.1:8000` 改为 `TestServerEndpoint` 读取环境变量；迁移文件与 `.meta` 按清单逐项确认。
5. `quality-gate.sh` 依次执行服务端测试、Unity EditMode、通用 PlayMode、Native PlayMode，并汇总总数/通过/失败/跳过/耗时；任一失败返回非零。

### 5.3 验收

- 清洁环境执行 `bash tools/quality-gate.sh --core`，退出码 0。
- 三套测试各自 discovered > 0；XML 可解析；日志为本次运行生成。
- 结束后无残留服务端进程；再次执行结果一致。
- WebGL 构建单独验证通过（PlayMode 通用套件可在 WebGL 目标编译）。
- 不依赖 MCP `tests-run` 作为证据。

### 5.4 Gate 0 风险

- `-nographics` 可能导致 UI PlayMode 测试失败。先按复核命令执行；若失败，去掉 `-nographics` 并记录差异，不改变 Gate 判定。
- `-buildTarget StandaloneWindows64` 首次切换会触发资源重导入，属预期；把耗时写入 Gate 报告。
- 当前 EditMode 基线包含一个 MCP 日志污染失败；Gate 0 必须先清除历史日志再运行，确认该失败消失或给出新证据。

## 6. 风险与回滚

| 风险 | 缓解 | 回滚 |
|---|---|---|
| 全量周期长（原计划 49–86 人天） | 波次 Gate；每波次独立可交付 | 只回滚当前波次分支 |
| 阶段 0 文档修订漂移 | 复核第 7 节作为验收清单；逐项映射 | 重新修订，不进入 Gate 0 |
| KCP 前移扩大 P0 | 任务 13 独立提交；soak 失败不阻塞其他 P0 波次，但 Gate 不通过 | 任务 13 可单独回滚，KCP 保持默认禁用 |
| 移动/CI 依赖 runner | 仓库变量控制 iOS job；无 runner 时 `not-verified` | 不修改 Gate 通过状态 |
| 当前脏工作树 | 阶段 0 前逐项分类处理 | 使用 stash/临时分支隔离 |
| 性能/覆盖率门禁误报 | 按 U-P1-10 固定设备、采样和参数 | 保留原始数据，调整方法而不是放宽门禁 |

## 7. 完成判定

### 7.1 P0 完成

原 Spec 10.1 + 复核 7.1 全部满足，且任务 1–14 的 Gate 报告存在。关键项：

- 服务端全量测试通过；Unity EditMode、通用 PlayMode、Native PlayMode 各自被发现且通过。
- Native suite 可从清洁环境自行启动和停止服务端。
- 登录响应不被状态判断阻塞；Unity 主线程不存在测试延迟造成的阻塞。
- EventSubscription 重复和跨副本 Dispose 幂等；生产移动上报路径没有 `Thread.Sleep`。
- KCP 生命周期已修复并有自动化测试；生产默认仍为 WSS。
- 所有结果由本轮 XML、日志和进程退出码证明。

### 7.2 P1 完成

原 Spec 10.2 + 复核 7.2 全部满足，且任务 15–33 的 Gate 报告存在。关键项：

- 两个 WorldInstance 隔离；200 bots 压测 tick p95 < 50ms、p99 < 66ms。
- Android ARM64 IL2CPP Development Build 成功；无设备证据时 runtime 状态为 `not-verified`。
- Addressables Use Existing Build 验证通过；Analyze 无阻塞级问题；catalog/hash/bundle 产物存在。
- 热路径性能数据可复现；场景退出后 VFX 与 Monster 资源归零。
- CI 对 0 tests、默认包名、协议漂移、配置错误、架构护栏和覆盖率下降明确失败。
- 关闭 Domain Reload 后连续两轮运行无静态状态与资源泄漏。
- 无物理设备证据时，不宣称移动性能或发布准备已通过。

## 8. 参考

- [原设计规格](2026-11-05-p0-p1-quality-improvement-design.md)
- [原实现计划](../plans/2026-11-05-p0-p1-quality-improvement.md)
- [Unity 专家复核报告](../reviews/2026-11-06-p0-p1-unity-expert-review.md)
- [跨模块架构指南](../../agent-architecture.md)
- [Agent Token 使用指南](../../agent-token-hygiene.md)
