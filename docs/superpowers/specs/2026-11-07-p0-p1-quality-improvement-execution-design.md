# P0/P1 质量改进执行设计

日期：2026-11-07
状态：文档/治理文档已就绪；§8 仍有 1 项 `BLOCKED_NOT_VERIFIED`（Linux consumer/hash），另有 3 项执行类 DEFERRED（KCP WebGL 守卫、Assets/Packages 归属、WebGL compile contract）到 task 13/Gate 0；执行基线激活待裁决
版本：Revision 2.1（Unity 专家复核闭环版 + 可行性修正）
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
3. **KCP 前移并按平台选传输**：采纳复核 U-P0-04 的推荐，任务 13 前移为 P0，排在任务 14 前。Android/iOS 以 KCP + Protobuf 为主传输，WebGL 以 WebSocket 为主传输；公网 WebGL 由反向代理终止 WSS。每个平台和构建配置只允许一个主传输。
4. **阶段 0 先行**：先修订原 Spec 与计划，把复核要求写进任务、文件清单和 Gate，再改代码。
5. **证据规则**：每个 Gate 只认本轮生成的 XML、日志、退出码和产物；blocking 项未执行时状态为 `BLOCKED_NOT_VERIFIED`，不得显示为 PASS。
6. **工程与平台发布 Gate 分层**：P1 Engineering Gate 判断工程质量；Mobile Release Readiness 判断移动候选包、真机运行、性能与生命周期证据；WebGL Public Deployment Readiness 判断公开域名、WSS、代理与浏览器证据。被批准的 `release-scope.yaml` 明确排除的平台标记为 `NOT_IN_SCOPE`，但不得宣称该平台已验证或具备发布条件。

本文件是阶段 0 的执行与修订基线。**任务级细节以阶段 0 完成后修订并批准的原计划为准**；本文件不重复 33 个任务的完整步骤，只定义执行模型、修订要求、Gate 条件和遗漏补齐项。阶段 0 退出时必须在本文记录批准后的 Spec、计划和 Git SHA；仅当 §8 清单全部满足后才把状态改为“已确认，执行基线”，否则保持“待裁决”。

## 2. 范围

### 2.1 纳入

- 原 P0：任务 1–14。
- 原 P1：任务 15–33。
- 复核报告 U-P0-01 至 U-P0-07、U-P1-01 至 U-P1-13 的全部修订要求。
- 复核第 5 节规定的阶段顺序与 Gate 条件。
- Android/iOS 的 KCP 主传输与 WebGL 的 WebSocket 主传输选择。

### 2.2 不纳入

- 原 Spec 4.2 的非目标：HybridCLR、DOTS/ECS、新玩法、多传输并行生产、账号中心、支付、跨服、数据库分片。
- 复核第 8 节声明的本报告边界之外的新增验证。纳入 blocking platforms 但未配置 runner 或设备的平台保持 `BLOCKED_NOT_VERIFIED`；被批准范围明确排除的平台标记为 `NOT_IN_SCOPE`，不会用推测结果替代验证。
- 本设计不新增原计划之外的功能任务；阶段 0 只补文档、清单、验收条件与治理基线。

### 2.3 冻结规则

P0 Gate 通过前，不新增联网玩法。P1 Engineering Gate 通过前，不进入发布候选阶段；移动平台必须由 Mobile Release Readiness 基于非 Development 候选包通过，WebGL 若纳入公开发布范围则必须由 WebGL Public Deployment Readiness 通过，之后才可对相应平台标记“公开测试/商店发布准备”。该规则来自原 Spec 第 3 节，继续有效。

### 2.4 平台传输矩阵

| 平台 | 主传输 | P0/P1 Gate 要求 |
|---|---|---|
| Android/iOS | KCP + Protobuf；公网候选包必须叠加经 ADR 批准的标准安全层 | P0 只验证选择策略、Win64 KCP 生命周期/soak 与共享实现；P1 Engineering 对 release scope 中的 blocking mobile platforms 验证 IL2CPP 构建和安全配置；Mobile Release Readiness 用非 Development 候选包在真机验证加密 KCP、IPv4/IPv6/NAT64、前后台、网络切换、断线重连和 30 分钟 soak；不得同时启用 WebSocket 主传输 |
| WebGL | WebSocket；公网为 WSS | P0 验证 WebGL 编译和浏览器 WebSocket 往返；公开环境验证完整 WSS URI、证书与反向代理配置；不得编译、打包或启用 KCP 玩家路径 |
| Editor/Windows 测试 | 按测试 lane 显式选择 | Native KCP lane 与 WebSocket/WebGL lane 分开报告，不得用一个平台结果替代另一个平台 |

原 Spec §6.0 的“近期公开测试所有平台默认 WSS、KCP 实验性默认禁用”与本矩阵冲突；阶段 0 必须把原 Spec 和 ADR-0001 一起改写为本矩阵，改写完成前不得进入 Gate 0。

如果后续改变移动平台主传输，必须先更新平台传输 ADR、Spec、本文与 Gate 断言，不能只修改运行时代码。

## 3. 执行模型

### 3.1 波次

| 波次 | 任务 | 退出条件 | 门禁产物 |
|---|---|---|---|
| 阶段 0 | 文档修订 | 复核指出的架构矛盾全部消除；任务文件清单足以实施 | 修订后的 Spec 与计划 |
| Gate 0 | 1 | runner、测试发现、server profile、XML 与失败传播可信 | `production/quality/gate-0-<run-id>.md` |
| P0-A | 2–4 | Bag、伤害上下文、EventBus/技能事务全绿 | 波次报告 |
| P0-B | 5–8 | 单世界、幂等登录、移动、格挡精力全绿 | 波次报告 |
| P0-C | 9–13 | 有界队列、准入背压、KCP、客户端邮箱全绿 | 波次报告 |
| P0 Gate | 14 | 原 Spec 10.1 + 复核 7.1 全部满足 | `production/quality/p0-gate-<run-id>.md` |
| P1-A | 15–18 | WorldInstance、AOI、ORCA、导航规模门禁 | 波次报告 |
| P1-B1 | 19–23 | 伤害管线、Sys3C 注入、资源、Addressables 与 UI 生命周期收敛 | 波次报告 |
| P1-B2 | 24–28 | 性能、局部 HitStop、VFX 与 Monster 池收敛 | 波次报告 |
| P1-C1 | 29–31 | 移动构建、架构守卫、CI 与 Coverage | 波次报告 |
| P1-C2 | 32 | 可观测性、WSS 部署、移动 KCP 公网安全与优雅停机 | 波次报告 |
| P1 Engineering Gate | 33 | 原 Spec 10.2 的工程项 + 复核 7.2 的自动化项满足 | `production/quality/p1-engineering-gate-<run-id>.md` |
| Mobile Release Readiness | 33 | 当前目标平台的设备运行、性能和生命周期证据全部满足 | `production/quality/mobile-release-gate-<run-id>.md` |
| WebGL Public Deployment Readiness | 33 | WebGL 被列为 blocking platform 时，公开域名/WSS/证书/反向代理与浏览器往返证据全部满足 | `production/quality/webgl-public-gate-<run-id>.md` |

波次之间严格串行，P0-A 与 P0-B 不再跨波次并行。波次内部只有在文件所有权和依赖互不重叠时才允许并行，并且每个并行任务必须使用独立 worktree、独立 Unity `Library`、独立端口和独立 artifact 目录。并行分支合并到当前波次集成分支后，必须重新执行该波次完整 Gate；子分支上的旧结果不能替代合并后结果。

### 3.2 会话、分支与提交

- **前置快照**：2026-11-07、Git `c80b884b` 时工作树有 115 个未提交条目，其中包含 UI 贴图删除、插件 DLL 变更和当前功能分支工作。该数字只是一份快照，执行时必须重新采样。
- **脏工作树保护**：不得为了质量工作直接丢弃用户改动。优先把当前改动提交到备份分支、保存为具名 stash，或从已确认基线创建独立 worktree；任何丢弃都必须逐项获得明确确认。
- **分支**：每个波次一个集成分支，Codex 执行时命名 `codex/quality/<wave>`；波次 Gate 通过后合并。并行子任务从该波次基线创建独立 worktree。
- **提交**：每任务独立提交；只暂存任务清单列出的路径；禁止 `git add .` / `git add -A`。
- **会话**：每个波次或任务独立会话；上下文到 20–30 万 token 时 compact；不把多个波次揉进一次会话。

### 3.3 任务完成判定

每项任务至少同时满足：

1. 项目相关目标完整编译通过；波次 Gate 再执行 Unity 与服务端全量编译，无程序集引用错误；
2. 对应的 Unity EditMode/PlayMode 或 .NET 服务端测试被实际发现且通过；
3. 结果由本轮独立 XML、日志、构建产物或设备证据证明；
4. 失败、取消、销毁、重复初始化和关闭 Domain Reload 等生命周期路径有明确行为；
5. 不以历史 Console 日志、手工预启动服务或未验证的平台状态作为通过证据。

### 3.4 Gate 证据与报告

- 每次 Gate 生成唯一 `run-id = <UTC timestamp>-<short-git-sha>`，所有产物写入 `production/quality/artifacts/<run-id>/`。
- Gate 报告至少包含：run ID、开始/结束时间、Git SHA、分支、dirty 状态、Unity 版本、OS、runner label、完整命令、构建目标、测试程序集与 server profile、服务端 PID/端口/参数、测试总数、通过、失败、跳过、耗时、XML/日志/产物路径及 SHA-256、设备/浏览器信息、未执行项及原因。
- 涉及公开网络的 Release Readiness 必须把客户端/网页 artifact SHA、服务端二进制 SHA 或镜像 digest、非敏感部署配置 hash、transport/security provider 与协议版本、ADR Git SHA、非敏感 key ID/轮换代次、endpoint/原始 hostname/DNS 记录、部署时间和测试窗口绑定到同一 run ID。服务端、配置、key generation、DNS 或代理重新部署后，旧端到端证据自动失效；不得只凭客户端包或本地服务器结果通过。
- XML、日志与构建产物的创建时间必须晚于 Gate 开始时间；旧文件、缺失文件、不可解析文件或 hash 不匹配均失败。
- Gate 0 的历史参考快照为：2026-11-07、Git `c80b884b`，服务端 409/409；Unity EditMode 629/630；Unity PlayMode 在 WebGL 活动目标下为 0。该快照必须由 Gate 0 重新测量，不作为未来精确数量门禁，也不得把外部 MCP `ping` 污染的推断当成通过证据。
- 门禁例外必须写明到期日、责任人、风险和恢复条件；不能用“已知问题”无限期豁免。
- 报告写入 `production/quality/`，并由当次提交引用。

### 3.5 Gate 状态机

| 状态 | 含义 | 是否允许推进 |
|---|---|---|
| `PASS` | 所有 blocking 项已执行并通过 | 允许 |
| `PASS_WITH_ADVISORIES` | blocking 项全部通过，仅非 blocking 项存在已记录建议 | 允许，但报告必须列出建议 |
| `FAIL` | 已执行的 blocking 项失败 | 不允许 |
| `BLOCKED_NOT_VERIFIED` | blocking 项因缺 runner、设备、许可证或平台环境未执行 | 不允许把该 Gate 标为通过 |
| `NOT_IN_SCOPE` | 由已批准且版本受控的里程碑范围明确排除 | 不阻塞其他平台，但不得写成已验证 |
| `DIAGNOSTIC_ONLY` | 在 dirty worktree 或非标准环境中运行，只用于收集诊断 | 不产生 Gate PASS |

Android IL2CPP runtime smoke、当前目标 Android 设备性能与移动生命周期属于 Mobile Release Readiness 的 blocking 项。iOS 只有被 `production/quality/release-scope.yaml` 明确排除时才是 `NOT_IN_SCOPE`；一旦 iOS 纳入 blocking platforms，缺少 macOS/Xcode/设备证据将使 iOS Release Readiness 为 `BLOCKED_NOT_VERIFIED`。WebGL 被明确排除时 WebGL Public Deployment Readiness 为 `NOT_IN_SCOPE`；一旦纳入 blocking platforms，缺公开域名、有效证书、反向代理或批准浏览器 lane 证据即为 `BLOCKED_NOT_VERIFIED`。Gate 开始后禁止在同一 run 内修改 release scope。

### 3.6 Gate 验收归属

| 验收类别 | P0 Gate | P1 Engineering Gate | Android Release Readiness | iOS Release Readiness | WebGL Public Deployment Readiness |
|---|---|---|---|---|---|
| Unity/.NET 编译、单元/集成测试、0-tests、manifest、协议一致性 | Blocking | Blocking | 引用 Engineering 结果 | 引用 Engineering 结果 | 引用 Engineering 结果 |
| WorldInstance/AOI/ORCA/导航服务端规模与延迟 | — | Blocking | 引用 Engineering 结果 | 引用 Engineering 结果 | 引用 Engineering 结果 |
| Addressables content build、Use Existing Build、Analyze、handle/lease 归零 | — | Blocking | 引用同平台 content hash | 引用同平台 content hash | 引用 WebGL content hash |
| 平台 Player Build 与 BuildReport | — | release scope 中所有 blocking platforms 的工程构建为 Blocking | 同一 Git SHA、同一 content manifest 的非 Development 候选包为 Blocking | 同一 Git SHA、同一 content manifest 的非 Development 候选包为 Blocking | 引用实际部署的 WebGL artifact digest |
| IL2CPP/AOT 安装启动与协议 runtime smoke | — | 不宣称已验证 | Blocking | Blocking | 不适用 |
| 真机帧时间、GC、热状态、30 分钟 soak | — | 不宣称已验证 | Blocking，必须物理参考设备 | Blocking，必须物理参考设备 | 按批准的 WebGL browser profile；未纳入时为 `NOT_IN_SCOPE` |
| 前后台、网络切换、Safe Area、返回键、低内存、ANR | — | 不宣称已验证 | Blocking，必须物理设备 | Blocking，必须物理设备 | 浏览器可见性、网络恢复与页面生命周期按 release scope 判定 |
| 平台传输与公网安全 | 策略/factory、Win64 KCP 生命周期与 soak、WebGL compile/browser WS 往返为 Blocking；不得宣称移动真机或公网 WSS 已验证 | release scope 中所有 blocking platforms 的目标构建、安全 provider/config、release 明文禁用与 WSS 静态配置为 Blocking | 非 Development 候选包的加密 KCP、域名、IPv4/IPv6/NAT64 与重放/篡改拒绝为 Blocking | 非 Development 候选包的加密 KCP、域名、IPv4/IPv6/NAT64 与重放/篡改拒绝为 Blocking | 公开域名、有效证书链、WSS URI/path、反向代理与批准浏览器消息往返为 Blocking |

每一条原 Spec 10.2 与复核 7.2 检查项在阶段 0 回写时必须映射到上述某一列；同一检查项不得同时被 Engineering Gate 宣称 PASS、又在 Release Readiness 中标为未验证。

## 4. 阶段 0：文档修订

阶段 0 只改文档、asmdef 清单和脚本设计，不改运行时代码。退出条件是复核第 7 节的检查项全部有落点。

### 4.1 U-P0 修订映射

| 复核项 | 落点 | 必须写入的内容 |
|---|---|---|
| U-P0-01 | 任务 1 | 新建 Editor-only `GameSys.EditorQuality.asmdef`；`QualityGate` 提供可被 EditMode 测试直接调用的纯 preflight 验证器（active target、包名、asmdef 契约），不切 BuildTarget；测试发现数/失败数由 `quality-gate.ps1` 解析 XML，并用 `quality-gate-selftest.ps1` 的 fixture XML 做负向自测；`InternalsVisibleTo("GameSys.EditorTests")`；asmdef、`.meta`、程序集引用和提交清单全部列出 |
| U-P0-02 | 任务 1、14、31 | 原生 Unity CLI runner 前移到任务 1；启动独立进程时传 `-buildTarget StandaloneWindows64`，Editor 内再验证 `BuildTarget.StandaloneWindows64`；测试进程内不执行 `SwitchActiveBuildTarget`；EditMode、通用 PlayMode、Native PlayMode 各自独立 XML 与日志；MCP `tests-run` 仅本地便利；0 tests、XML 缺失/不可解析、异常退出均返回非零；WebGL 使用独立进程和明确的 compile/build lane；门禁输出总数/通过/失败/跳过/耗时 |
| U-P0-03 | 任务 1、14、31 | Native tests 按 `native-clean`、`native-bots`、`native-monsters` 和确有测试时的 `native-navigation` profile 执行；每个 profile 独立启动服务器、选择同时可用的 TCP/UDP 动态端口、记录 PID，并以 application-ready 日志 + 对应传输 probe 作为 ready 判据；启动失败、提前退出、端口冲突、超时或 teardown 残留均失败；`finally` 只停止本次 PID/子进程树并保存日志；各 profile 分别传 `-assemblyNames` + category/filter、分别生成 XML；迁移测试文件及 `.meta` 清单必须显式列出 |
| U-P0-04 | 任务 13、14、32 | 任务 13 前移为 P0，排在任务 14 前；KCP 连接、断开、重复释放、接收循环停止有自动测试，P0 的 30 分钟 soak 在 Win64 KCP lane 执行，移动真机 soak 归 Mobile Release Readiness；把 `ClientNetworkHost` 中直接构造 transport 的分支收敛到可测试的 platform transport factory：Android/iOS Player 构建创建 `KcpNetClientAdapter`，WebGL Player 构建创建 `WebSocketClientAdapter + WebGLWebSocketChannel`，Editor/Windows 测试必须由 lane 显式选择而不能继承隐式默认值；Gate 验证每个平台/构建配置只启用一个主传输，不再断言“KCP 全局默认禁用”；WebGL compile contract 与构建产物检查必须证明 KCP 玩家类型及 plugin 未进入 WebGL Player；公开移动候选包在任务 32 完成经 ADR 批准的标准安全层前不得 Release Ready |
| U-P0-05 | 任务 4 | `EventSubscription` 改为 `sealed class`，用 `Interlocked.Exchange` 或等价原子状态保证跨副本幂等退订；`Emit` 使用 copy-on-write 快照，不在热路径 `ToArray()`；`ResetAll` 同时清普通订阅、定向订阅和暂停状态并回收空桶；`SubsystemRegistration` 重置；测试 `Copied_Tokens_Dispose_Only_Once`、重复 Dispose 不抛异常、Emit 热路径无临时数组分配 |
| U-P0-06 | 任务 7 | 从生产 `MovementReporter` 删除 `injectedSendDelayMs` 和 `Thread.Sleep`；把 `DateTime.UtcNow` 改为可注入时钟；延迟、抖动、丢包模拟移到 fake transport 或可控 test scheduler；增加 `Injected_Network_Delay_Does_Not_Block_MainThread`；服务端移动校验 v2 的其余内容仍按原任务 7 执行 |
| U-P0-07 | 任务 12 | `Update` 先 drain 登录、错误、断线和其他 control 消息，再根据状态决定是否执行 InWorld tick；明列 `ClientEnvelope.cs`、`DrainResult.cs`、`LoginResult.cs` 或说明其嵌套类型；`ClientNetworkHost` 持有连接 lifetime CTS 和正在执行的 Task；`Disconnect` 与 `OnDestroy` 取消任务并观察所有异常；禁止未观察的 fire-and-forget；测试 `Pump_Drains_LoginResponse_While_LoggingIn`、`Pump_Drains_Error_And_Disconnect_Before_InWorld`、Host 销毁取消且无未观察异常 |

### 4.2 U-P1 修订映射

| 复核项 | 落点 | 必须写入的内容 |
|---|---|---|
| U-P1-01 | 任务 19 | 定义唯一运行时伤害模型：`DamageRequest` 含不可变 `SkillDamageContext`、方向和 `HitContact`；建立生产调用者、实现者、测试 fake 的迁移矩阵；先保留 `[Obsolete] DamageBlock` 适配 overload，迁移完成后删除；每阶段先编译和目标测试，再扫描 Prefab GUID；完成后无 Missing Script、无旧 GUID、无旧接口调用 |
| U-P1-02 | 任务 20 | `PlayerSpawner`、`PlayerSpawnedEvent` 和场景组合根纳入任务；PlayerSpawner 使用类型化 Prefab 根；实例化后、`Start` 前由组合根注入 Camera 和其他场景依赖；`PlayerSpawnedEvent` 直接携带 `Sys3CEntry`；`Sys3CEntry` 在注册 Physics/EventBus 前一次性验证全部依赖；初始化失败清理半注册对象；两个玩家 Prefab 和真实入口场景均有 PlayMode 覆盖；任务 20 当场以源码守卫证明相关 MonoBehaviour 不再用 `GetComponent`/`FindObjectOfType` 获取运行依赖 |
| U-P1-03 | 任务 21 | `AddressablesResourceBackend` 使用 UniTask Addressables awaiter或 Completed callback；玩家资源层源码守卫同时禁止 `WaitForCompletion()` 与 `AsyncOperationHandle.Task`；所有 Addressables API 从 Unity 主线程进入，后台线程调用必须 fail-fast 或 marshal 到明确的 Unity 主线程调度器；同步兼容接口只允许 Editor 工具调用并带明确守卫；增加 WebGL Player 编译门禁；加载失败、取消和 shutdown 不泄漏 handle |
| U-P1-04 | 任务 21 | `SceneLease` 保存 `AsyncOperationHandle<SceneInstance>`；提供幂等 `UniTask UnloadAsync()` 或等价 async-dispose 契约；加载取消后若底层操作已开始，等待完成再卸载和释放；明确 Additive、Single、重复卸载和 ResourceManager shutdown 的所有权规则；重复卸载、加载中取消、Single 切换和 shutdown 测试通过；句柄计数归零 |
| U-P1-05 | 任务 23 | 采用场景 Presenter 在 `Awake` 调用 `UIBootstrap.RegisterFailurePresenter(this)`、`AfterSceneLoad` 注入 `UIManager` 的方案，不新增 `UIBootstrapBehaviour`；注册返回 token，Presenter 在 `OnDestroy` 精确注销，重复注册采用新实例替换旧实例并处理 Unity fake-null；增加 `SubsystemRegistration` 清理纯托管静态引用；覆盖 `CharacterSelect → DemoDay → CharacterSelect`、Presenter 先销毁和直接进入 `DemoDay`；没有 Presenter 时提供非递归、非 Addressables 依赖的降级行为 |
| U-P1-06 | 任务 22、31 | 增加 `AddressablesBuildScript.BuildPlayerContent` batchmode 入口；快速测试使用 Use Asset Database；生产验证使用外层 orchestrator 顺序执行“目标平台进程 → 清理本平台旧输出 → BuildPlayerContent → Use Existing Build 生命周期测试 → 恢复设置”；`CoreUI` 使用 Local Build/Load Path、LZ4、Pack Together、Static Content、label `core-ui`，地址固定为 `ui/player-hud`、`ui/target`、`ui/character-select`，失败 Presenter 保持场景直接依赖而不进入 Addressables；当前 `CombatVFX` 只包含必预热的 Hit/Heal，使用 Local Build/Load Path、LZ4、Pack Together、Static Content、label `combat-vfx-core`，地址固定为 `vfx/hit-spark-burst`、`vfx/heal-green`；未来大量可选技能特效进入独立 `OptionalSkillVFX` group，Pack Separately 且仅预热当前角色/场景所需 ID；禁止使用文件路径作为 address；Android 与 WebGL 分平台构建并隔离 bundle 输出；运行 Analyze 并校验重复 address、缺失依赖、重复 bundle dependency、Resources/Addressables 双重打包；保存 content manifest/hash，Player Build 必须引用同一份 manifest；catalog、hash、bundle 缺失或 BuildPlayerContent 返回错误即失败 |
| U-P1-07 | 任务 26 | 明确迁移真实 `HitSparkBurst`；未使用的 `HitSparkParticle` 不删除，但记录为无引用遗留物；把 `GameSettings.cs`、`GameSettings.asset`、`DemoDay.unity` 和实际引用 Prefab 加入文件清单；选定唯一模型：`GameSettings` 仅保存带显式数值的 `VfxId` enum，`VfxCatalog : ScriptableObject, IVfxCatalog` 将 `VfxId` 映射为稳定 Addressables address，`ResourceManager` 获取 `AssetLease<GameObject>`，pool 通过 `Object.Instantiate` 克隆；World/Combat loading 期间获取 lease 并预热，完成后才订阅战斗事件；异常早到事件直接丢弃并输出限频警告，不排队播放过期效果；场景退出时先销毁 active/idle clone，再释放 prefab lease；禁止混用 `Addressables.InstantiateAsync`/`ReleaseInstance` 模型；CI 按 GUID 检查 Addressable VFX 不再被 Scene、Resources asset 或非 Addressable Prefab 直接引用 |
| U-P1-08 | 任务 29、31 | Android 和 iOS 进程启动时分别指定 `-buildTarget Android` / `-buildTarget iOS`；build method 只验证 active target，不负责切换；Windows 仅执行 `ValidateIosConfiguration`；用仓库变量 `HAS_IOS_RUNNER` 控制 iOS job；先读取批准的 `release-scope.yaml`：iOS 被明确排除时生成 `ios-status = NOT_IN_SCOPE`，iOS 属于 blocking platforms 但缺 runner/Xcode/设备时生成 `ios-status = BLOCKED_NOT_VERIFIED`，均写入 artifact/summary 且不无限排队；固定 runner labels，检查 Unity 版本、许可证、Build Support、SDK/NDK/JDK 与 Xcode；写明唯一 BuildScenes 清单；校验 BuildReport、APK/AAB 或 Xcode 工程确实存在；构建脚本的设置修改由 `finally` 恢复 |
| U-P1-09 | 任务 29、33 | P1 Engineering Gate 生成 Android ARM64 IL2CPP Development Build；Mobile Release Readiness 必须从同一 Git SHA、同一 Addressables content manifest 生成非 Development 候选包，并用候选包完成安装、冷启动、CharacterSelect、Addressables 初始化、内建 UI 加载、MessageTypeRegistry 非默认 fixture 编解码和进入 DemoDay；记录非敏感 signing identity 指纹、versionCode/versionName、Managed Stripping Level、Scripting Backend、Development/Debuggable 标志与 ABI；smoke 在超时前主动写出完成标志与退出状态，保存 `adb logcat`、设备型号、系统、build fingerprint 和包版本；根据 strip diagnostics 和 smoke 证据配置 `link.xml` 或 `[Preserve]`，无法构造的消息必须有显式 AOT preserve 证据；只有候选包证据存在时才可声明 Android runtime verified |
| U-P1-10 | 任务 24、27、31、33 | 纯 C# 热路径使用隔离后的 `GC.GetAllocatedBytesForCurrentThread`；Unity 对象和帧性能使用 Unity Performance Testing package 或 Standalone Player 性能测试；阶段 0 新建 `production/qa/device-profiles/android-reference.yaml`，在 P1-B2 前填入实际设备型号/SoC/RAM/系统/分辨率/Quality Level/目标帧率/预热/采样窗口/p95 算法/原始数据格式，未填则性能 Gate 为 `BLOCKED_NOT_VERIFIED`；Coverage 固定 `-enableCodeCoverage`、`-coverageResultsPath`、`-coverageOptions`、assembly filter 和排除规则；Coverage XML 缺失、损坏或目标程序集未出现时失败；不把一次 Editor 内零分配断言作为移动端性能结论 |
| U-P1-11 | 任务 4、21、23、30 | 区分全局 `ResourceManager.ShutdownAsync` 与场景/会话级 `ResourceScope.CloseAsync`：场景退出只关闭当前 owner 的 scope，不停止全局服务，也不释放 application/UI scope 的 CoreUI lease；每个 lease 必须登记 owner，scope 关闭后断言该 owner 的 lease 数为 0；全局 Shutdown 只用于应用真正退出、测试套件结束或显式全局重启；`SubsystemRegistration` 只重置纯托管静态引用，不调用异步 Addressables API，若发现未释放 handle 则在开发/测试构建报告泄漏而不是静默清表；测试提供受控的 `ResetForTests`；UI 销毁顺序固定为取消加载 → 销毁实例 → 释放 descriptor lease → 清空 Instance；增加“未先关闭 scope 直接开始第二轮 PlayMode”负向测试、`CharacterSelect CoreUI 保留 → DemoDay world scope 创建/关闭 → 返回 CharacterSelect`，并在关闭 Domain Reload 的配置下验证无残留 |
| U-P1-12 | 任务 28 | 严格按 inactive 阶段 `PrepareForSpawn` → 激活对象 → NavMesh 投影/启用 agent/`Warp` → 确认 `isOnNavMesh` → 注册 Physics/感知/AI tick 的顺序执行；`OnEnable` 不读取上一次生命周期数据；AI 首帧前 `NavMeshAgent.isOnNavMesh == true`；连续租出和归还不累积订阅、碰撞注册或状态效果；投影失败的对象不进入活跃怪物集合 |
| U-P1-13 | 任务 23、29、33 | Android 验收补充冷启动与热启动、前台/后台恢复、Wi-Fi 与移动网络切换、断网/重连/服务端断开、Safe Area/分辨率/方向变化、Android 返回键与 modal back stack、低内存回调、场景切换期间取消 Addressables 加载；通过 `IScreenMetrics`、`ILowMemorySignal` 等接口提供可测试信号；自动或人工证据统一写入 `production/qa/evidence/<run-id>/`，记录 build SHA、设备、系统、分辨率、Quality Level、步骤、期望/实际、超时、logcat/Player.log、截图或录屏、执行人和 PASS/FAIL/BLOCKED_NOT_VERIFIED/NOT_IN_SCOPE；泄漏使用明确比较点与容差，不写无法有限证明的绝对“永远为 0” |

### 4.2.1 已定生命周期契约

**Addressables 生产验证编排：**

1. 外层 `quality-gate.ps1` 保存原 Addressables settings 和 data builder；
2. 清理当前目标平台的旧 catalog、hash 和 bundle 输出；
3. 用目标平台 Unity 进程执行 `BuildPlayerContent` 并写 content manifest/hash；
4. 设置 Use Existing Build；后续生命周期测试进程必须读取该设置和本次 manifest；
5. 测试内验证实际 catalog hash 等于本次 manifest，禁止回退 Use Asset Database；
6. 生命周期测试全部结束后，外层 `finally` 才恢复 settings；恢复失败使 Gate 失败；
7. 整条流程在隔离 worktree 执行，避免进程崩溃污染日常 Editor 工作区。

**全局 ResourceManager 与资源 Scope 生命周期：**

- `ResourceManager` 是跨场景全局服务；只有应用真正退出、测试套件结束或显式全局重启时才调用 `ShutdownAsync`。场景切换不得让全局服务进入 `Stopped`；
- `ApplicationResourceScope`/`UIResourceScope` 持有 CoreUI 等跨场景 lease；`GameSessionScope`、`VfxPoolScope` 等场景/会话 scope 只拥有自己的 scene、VFX、实例和 pending load。每个 lease 必须登记稳定 owner ID，禁止无 owner 的 acquire；
- 场景切换前显式 `await GameSessionScope.CloseAsync()`：停止该 scope 的事件接入 → 取消该 scope 的 pending loads → 销毁该 scope 的 active/idle instances → 卸载该 scope 的 Addressable scenes → 释放该 scope 的 asset leases，并断言该 owner 的 lease 数为 0；不得释放 application/UI scope 的 CoreUI lease；
- 全局关闭状态固定为 `Running → ShuttingDown → Stopped`；进入 `ShuttingDown` 后拒绝新的 Acquire/Load；多次或并发 `ShutdownAsync` 共享同一个完成任务，并在所有 scope 已关闭后释放全局 lease、清纯托管静态引用；
- Unity `OnDestroy`/`OnApplicationQuit` 只做 best-effort 兜底，不能宣称完成正常异步卸载；`SubsystemRegistration` 只清纯托管静态引用并报告 outstanding handles，不执行异步释放；
- `ResetForTests` 在任一 owner 仍有 outstanding handle 时必须失败，不能绕过 scope close/global shutdown 静默清表；覆盖 `CharacterSelect` CoreUI 保留 → 创建并关闭 `DemoDay` world scope → 返回 `CharacterSelect`、重复/并发 close，以及全局 shutdown 后拒绝新加载。

**UI failure Presenter 注册：**

- 注册 token 同时记录实例身份和 generation；Presenter B 替换 A 后，A 的旧 token Dispose 不得注销 B；
- 新 Presenter 注册时立即更新已经存在的持久化 `UIManager`，不能只等待下一次 `AfterSceneLoad`；
- 测试 A 注册 → B 替换 → A 销毁，以及场景往返和 Domain Reload disabled。

**VFX Catalog 与初始化：**

- `VfxId` 使用带显式数值、禁止复用旧值的 enum；`VfxCatalog : ScriptableObject, IVfxCatalog` 只保存 `VfxId → address`，不保存 Prefab 直接引用；
- build-time 校验重复 ID、空 address、缺失 Addressable entry 和非 `GameObject` 类型，任一错误均失败；
- 场景 `VfxPoolScope` 持有 catalog、pool 和全部 asset lease；预加载到一半失败时释放已取得 lease、销毁已预热 clone、不订阅事件并返回初始化失败；
- 核心 Hit/Heal 全量预热；可选技能 VFX 只按当前角色/场景所需 ID 预热。

**IL2CPP 协议 fixture：**

- 不允许只对空消息做 roundtrip；为每种协议 shape 准备非默认值 fixture，覆盖 `IList<T>`、嵌套消息、空/非空集合、枚举、负数/边界整数和字符串；
- runtime 完成标志包含 build SHA、MessageTypeRegistry hash、Addressables catalog hash 与 ABI；harness 验证 `ro.product.cpu.abi == arm64-v8a`；
- ARM64 emulator 只证明安装、启动、IL2CPP/AOT、协议和场景进入；帧时间、GC、热状态、网络切换、前后台、低内存、ANR 与 30 分钟 soak 必须使用已批准的物理参考设备。

### 4.2.2 已定平台传输、端点与公网安全契约

**唯一构造边界：**

- 新建可测试的 `NetworkTransportFactory`（或等价名称）；只有该 factory 可以引用和构造 `KcpNetClientAdapter`、`WebSocketClientAdapter`、`WebGLWebSocketChannel` 与 `DotNetWebSocketChannel`，其余 `GameSys` 代码只能依赖 `INetworkClient` 和不可变的 `NetworkEndpoint`；
- 编译分支顺序固定：`UNITY_WEBGL && !UNITY_EDITOR` 只允许 WebSocket；Android/iOS Player 只允许 KCP；Editor/Windows 必须由 Gate lane 或显式开发配置选择，缺少选择时 fail-fast，禁止从 Inspector、上次运行状态或隐式默认值漂移；
- `TransportFactoryBoundaryGuardTests` 扫描生产调用点，任何 factory 外的具体 transport 构造立即失败；真实目标平台编译/Player Build 仍是必需证据，源码扫描不能替代编译。

**端点契约：**

- `NetworkEndpoint` 明确携带 transport kind、host、port，以及 WebSocket 的完整 scheme/path；`WebSocketClientAdapter` 接收已验证的完整 `Uri`，不得内部硬编码 `ws://` 或 `/`；公网 WebGL 强制 `wss://`，明文 `ws://` 只允许 loopback/Development Build；
- KCP 端点支持域名、IPv4 与 IPv6。连接流程始终保留原始 hostname 作为服务端身份验证输入；DNS 通过系统 resolver 异步解析并去重候选，根据每个结果创建匹配的 `AddressFamily` socket。禁止 `IPAddress.Parse(host)` 作为域名路径，禁止固定 `AddressFamily.InterNetwork`，NAT64 依赖系统 DNS64/getaddrinfo 结果，不在应用内手工拼接 IPv6 地址；
- 多地址连接采用有界 candidate race：尊重系统候选顺序，首个候选立即尝试，250ms 后启动下一地址族候选，最多同时 2 个；单候选握手超时 3 秒、整个连接 10 秒，值可由受版本控制的配置收紧但不得在同一 Gate run 临时改变。首个完成服务端身份验证与安全握手的候选获胜并取消/释放其余候选；首个不可达必须继续后续候选，全部失败时返回去敏后的聚合错误；调用方取消必须立即停止 DNS、计时器、socket 与握手；
- 实现清单必须包含 `WebSocketClientAdapter.cs`（当前 `ConnectAsync` 硬编码 `ws://host:port/`，改为接收完整 `Uri`）、`KcpNetClientAdapter.cs`（当前 `IPAddress.Parse(host)`）、`KcpClient.cs`（当前取 `addresses[0]`）以及新增的 endpoint resolver/candidate race；不得只在工厂层包装旧行为；
- 自动测试覆盖域名、IPv4、IPv6、DNS 无结果/多结果、首个失败后续成功、全部候选失败、取消、单次/总超时与 loser 清理；Mobile Release Readiness 在批准设备/网络上保存 IPv4、IPv6-only/NAT64 连接证据。

**KCP 公网安全：**

- KCP + Protobuf 本身不提供机密性或对端认证；HMAC 登录票据只用于身份/完整性，不能替代传输加密。Development/loopback/受控测试网络可显式启用明文 KCP，非 Development 公网候选包必须 fail-closed；
- 阶段 0 新建 `docs/architecture/adr-0003-kcp-session-security.md`，在运行时代码实施前选择并锁定一个经过维护、支持 Unity 2022 IL2CPP Android/iOS 的成熟 DTLS 或 Noise 实现；禁止自研密码学原语。本项通过集成外部审查过的 provider 保留原 Spec“不在应用协议层自研加密”的原则。ADR 至少确定协议/库版本、许可证、服务端与客户端集成点、身份与密钥来源、双向密钥派生、AEAD、nonce/序列号、重放窗口、重连/rekey/轮换、版本降级拒绝、秘密清理和观测脱敏；服务端身份验证必须绑定 `NetworkEndpoint` 的原始 hostname，而不是某次 DNS 解析得到的 IP；
- ADR-0003 批准前必须完成最小 POC：Unity 2022.3 Android IL2CPP 客户端与 .NET 8 服务端完成所选 provider 的握手、一条 AEAD 消息往返、篡改/重放拒绝，并记录许可证、维护状态与 IL2CPP 兼容性证据；POC 失败时 ADR 必须写明回退方案（例如移动端改用 WSS），不得锁定未验证的库；
- 任务 13 建立 security provider 接口、配置 fail-fast 与明文 release 守卫；任务 32 完成经 ADR 批准的客户端/服务端安全 provider、部署配置、密钥轮换演练与安全测试。篡改、重放、错误服务端身份、过期密钥、降级和握手超时必须被拒绝并留下不含秘密的结构化证据；
- 在 ADR 未批准、provider 未完成或候选包仍可回退明文 KCP 时，P0 只能报告“平台选择策略与共享 KCP 实现已验证”，P1 Engineering/Mobile Release Readiness 不得把移动公网传输标为 PASS。

安全 ADR 的标准依据至少包含 [KCP 官方协议说明](https://github.com/skywind3000/kcp)、[IETF DTLS 1.3（RFC 9147）](https://www.rfc-editor.org/rfc/rfc9147.html) 与 [Noise Protocol Framework](https://noiseprotocol.org/noise.html)；引用标准不等于批准某个库，库选择仍必须经过 Unity IL2CPP 兼容性、许可证与维护状态评估。

**WebGL 构建边界：**

- `GameSys.asmdef` 当前无条件引用 `KcpNet` 与 `WebSocketNet`，两个 transport asmdef 也没有平台排除。P0 任务 13 必须完成 factory 唯一构造边界、KCP 源码 `!UNITY_WEBGL` 编译守卫、全部 KCP PluginImporter 的 WebGL 排除、`TransportFactoryBoundaryGuardTests`、`KcpPluginPlatformGuardTests` 和 WebGL 构建产物检查；任务 30 只把这些既有规则纳入长期架构守卫、allowlist 与后续 asmdef 重构，P0 Gate 不依赖任务 30；
- 当前快照存在两份字节相同的 `Kcp.dll`（SHA-256 `09707B5EBA20070ED922C7293AF342404A162A263C5965189EB687C90690FB9D`）：Git 已跟踪的 `Assets/Plugins/ProtoBuf/Kcp.dll` 与未跟踪的 `Assets/Packages/Kcp.2.7.0/lib/netstandard2.1/Kcp.dll`，两份 importer 都启用 Any Platform。阶段 0 指定已跟踪副本为本轮唯一权威来源；任务 13 将其 importer 设为 WebGL 不兼容，并在再次核对 hash、GUID 引用、生成来源与工作树归属后处理未跟踪重复副本，同时修正会重新生成/提交重复 DLL 的 restore/install 配置。任何删除未跟踪副本的动作都必须先确认它是可再生输出且不含用户改动；内容漂移或归属不明时停止并请求确认，不得静默删除；去重完成前两份 `.meta` 都必须显式设为 WebGL 不兼容；阶段 0 必须明确 `Assets/Packages/` 的归属：若为 NuGetForUnity 生成目录，加入 `.gitignore` 并清理未跟踪副本；若非生成目录，纳入版本控制并解决双份 DLL；该决定写入任务 13 文件清单与提交清单；
- `KcpPluginPlatformGuardTests` 必须按程序集名/GUID 枚举所有 Kcp plugin importer，发现重复副本、未知路径、Any Platform/WebGL enabled 或固定路径漏检均失败；WebGL Player 构建产物必须同时证明没有 Kcp 托管/原生 plugin 和 `KcpNetClientAdapter`。所有依赖 KCP plugin 的源码保持 `!UNITY_WEBGL` 编译守卫。通用 PlayMode asmdef 移除直接 `KcpNet` 引用不等于完成生产边界隔离。

阶段 0 回写任务 13、30、32 时必须区分所有权：任务 13 文件清单至少包含 `ClientNetworkHost.cs`、`NetworkTransportFactory.cs`、`NetworkEndpoint.cs`、`KcpNetClientAdapter.cs`、`WebSocketClientAdapter.cs`、WebGL/.NET channel、两份现有 `Kcp.dll`/`.meta`、factory/importer/产物守卫测试及 `.meta`；任务 30 接管守卫维护与 allowlist，不重复承担 P0 实现；任务 32 包含客户端/服务端 security provider、部署配置与安全测试。若最终命名不同，计划必须给出一一对应关系，不能保留“相关文件”占位描述。`KcpNet` 当前 15 个 `.cs` 中只有 8 个带 `#if !UNITY_WEBGL`；任务 13 文件清单必须显式包含 `IKcpTransport.cs`、`ILogger.cs`、`IMessageExecutor.cs`、`KcpOptions.cs`、`KcpSession.cs`、`MessageDispatcher.cs`、`KcpServerSession.cs`，逐个确认是否引用 Kcp plugin 类型并补齐守卫或改为平台无关接口；`KcpNet.asmdef` 当前无平台排除，WebGL 排除 `Kcp.dll` 后必须仍能编译出空/安全程序集。

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
| 30 架构守卫与 asmdef | 保留并扩展任务 13 已通过 P0 Gate 的 transport factory、PluginImporter 与 WebGL 产物守卫，不在 P1 重新定义其语义；新增守卫禁止非白名单 `GetComponent/FindObject*`、玩法层 `Resources.Load` 和跨层 asmdef 反向引用；校验 `GameSys.asmdef` 对 `KcpNet`/`WebSocketNet` 的引用不能绕过 factory/编译边界；建立带责任人、原因和到期日的 legacy allowlist，现有基线只允许收缩，新增违规立即失败；守卫稳定后再拆 Bag/Stamina asmdef，并用依赖守卫证明不反向引用 `GameSys`；Sys3C/Skills/Combat/Monster/SyncLayer 的大规模拆分留 P2 |
| 32 可观测性、公网与 KCP 安全基线 | `System.Diagnostics.Metrics` + OpenTelemetry Prometheus exporter；指标含 tick p50/p95/p99、队列深度/丢弃、session/unauth 数、WS/KCP 背压、AOI 可见数、导航 p95、GC/分配；健康检查区分进程存活与世界 tick；Caddy 示例终止 WSS；按 `adr-0003-kcp-session-security.md` 完成移动 KCP 标准安全 provider、服务端身份验证、AEAD、重放保护、rekey/轮换、降级拒绝和不含秘密的指标/日志；非 Development 构建明文 fail-closed；管理/指标端点默认 loopback；配置/怪物表/地图/协议/安全 provider 生产模式 fail-fast；优雅停机先停止准入、排空有限队列、关闭世界与会话 |
| 33 P1 Gate | 分别输出 P1 Engineering Gate、Mobile Release Readiness，以及 WebGL 纳入 blocking platforms 时的 WebGL Public Deployment Readiness：两个 WorldInstance 互不可见/不可命中/不进入仇恨或 ORCA；200 bots + 怪物压测 tick p95 < 50ms、p99 < 66ms；Android ARM64 IL2CPP Development Build 与同 SHA/同 content manifest 的非 Development 候选包、runtime smoke 分开判定；iOS/WebGL 按批准的 release scope 与 runner/设备/浏览器能力报告 `PASS`、`BLOCKED_NOT_VERIFIED` 或 `NOT_IN_SCOPE`；Addressables 并发单飞与 owner 级句柄归零；30 分钟资源/订阅基线；已配置 Android 参考档位 p95 frame ≤ 33.3ms；CI 对 0 tests/测试清单异常下降/默认包名/协议漂移/配置错误/架构护栏/覆盖率下降明确失败；公网 WebGL 的域名/证书/WSS/代理/浏览器往返、移动 KCP 安全 provider、域名/IPv4/IPv6/NAT64、重放/篡改/降级拒绝、鉴权、指标、密钥轮换和优雅停机演练完成；任何 blocking 平台为 `BLOCKED_NOT_VERIFIED` 时对应 Release Readiness 不得 PASS |

### 4.4 Gate 条件固化

阶段 0 必须为 Gate 0、P0 Gate、P1 Engineering Gate、Mobile Release Readiness 和 WebGL Public Deployment Readiness 分别固定：

- 输入：任务范围、构建目标、测试程序集、服务端启动参数；
- 命令：完整可复制命令；
- 产物：XML、日志、构建产物、报告路径；
- 失败条件：非零退出、0 tests、XML 缺失/损坏、服务端未退出、产物缺失；
- 平台状态：批准范围明确排除时为 `NOT_IN_SCOPE`；属于 blocking platforms 但缺 runner、设备、许可证或平台环境时为 `BLOCKED_NOT_VERIFIED`，并按 3.5 的状态机阻塞对应 Gate。
- 超时：server build、application-ready、Unity 启动、每个 test lane、teardown、Player build 和设备 smoke 分别设置上限；超时视为失败并保留现场。
- 测试清单：每个 assembly/profile 保存预期测试全名或批准的最低数量；未解释的数量下降、`Skipped`、`Ignored`、`Inconclusive` 默认失败，例外进入带责任人和到期日的 allowlist。
- flaky：首次失败证据必须保留；重跑只用于标记 flaky，不得覆盖原始失败或把 Gate 自动变绿。
- 运行身份：统一使用 run ID，验证所有产物的新鲜度与 SHA-256。

同时更新原 Spec 的资源、消息泵、启动器和静态生命周期契约，使实施者不需要自行猜测。

### 4.5 治理文件与脚本

- **技术偏好单一来源**：新建 `docs/technical-preferences.md` 作为唯一人工维护源；由阶段 0 脚本同步生成 `.claude/docs/technical-preferences.md` 与 `.codex/docs/technical-preferences.md`，生成文件带“generated, do not edit”头。Codex 目录以仓库当前真实目录 `.codex/` 的全小写拼写为唯一契约；历史复核中的 `.Codex/` 是待修正的旧引用，不得创建大小写变体。同步更新根 `CLAUDE.md` 与 `AGENTS.md`，分别显式要求读取对应生成文件；Windows/Linux Gate 比较三份内容 hash，并通过 Git 索引/大小写敏感路径检查拒绝 `.Codex/` 或其他变体。增加 toolchain consumer sentinel：生成文件写入 schema/hash marker，Claude 与 Codex 的最小审查会话分别回报 marker 并保存证据；任一入口未读取、路径解析失败或 hash 不同均阻塞阶段 0。填入引擎、平台传输矩阵、输入、命名、性能预算、测试、禁止模式、允许库和 Unity specialist 路由。
- **ADR**：使用跨平台稳定的小写文件名，新建 `docs/architecture/adr-0001-platform-transport-matrix.md`、`docs/architecture/adr-0002-quality-gate-evidence-model.md` 与 `docs/architecture/adr-0003-kcp-session-security.md`。前者记录 Android/iOS KCP、WebGL WebSocket/WSS、endpoint/factory 边界和单平台单主传输；第二份记录 Gate 状态机、证据、例外、超时、回滚，以及 `NOT_IN_SCOPE`/`BLOCKED_NOT_VERIFIED` 的严格语义；第三份按 4.2.2 锁定移动公网 KCP 的标准安全协议/库和密钥生命周期。Coverage、可观测性和停机不再塞进一个 mega ADR，后续按独立决策补充。
- **协议双端一致性**：Gate 0 必须运行 `Assets/Scripts/Tests/Editor/Network/ProtocolRoundtripTests.cs`、`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs` 与服务端协议测试；若缺少跨端一致性断言，在任务 1 补一个双端 roundtrip 测试。
- **测试脚本定位**：Windows 上以 `tools/quality-gate.ps1` 作为进程编排权威入口，负责 Unity/服务器 PID、timeout、profile、XML 解析、hash 和清理；`tools/quality-gate.sh` 与 `tools/unity-tests.sh` 作为薄包装保留现有 Bash 使用方式；MCP `tests-run` 的便利入口移到 `tools/unity-tests-mcp.sh`，不得作为 Gate 证据。同步更新 `docs/agent-token-hygiene.md`。Linux CI 默认只做内容 hash/静态检查；若要在 Linux 执行 Unity Gate，必须显式提供 `pwsh` 或 POSIX runner，不能假设 PowerShell 可用。
- **测试清单与例外**：新建 `production/quality/test-manifest.json` 和 `production/quality/gate-exceptions.json`；manifest 结构固定为 `assembly → profile → cohort → serverArgs/testFilter/fixtures/timeout`，记录测试全名与基线；exceptions 必须包含 owner、reason、expiresAt 和恢复条件。
- **发布范围**：新建 `production/quality/release-scope.yaml`，记录 milestone、blockingPlatforms、requiredDeviceLanes、requiredBrowserLanes、publicEndpoints、approver、approvedAt 和 sourceGitSha。Gate 启动时读取并复制到 run artifact，运行期间禁止更改；只有该文件明确排除的平台才可标记 `NOT_IN_SCOPE`。
- **README 同步**：任何模块行为变化同步更新该模块 README；波次 Gate 报告引用更新后的 README。
- **工作树分类**：阶段 0 前重新采样工作树；特别关注 `Assets/PreRes/Texture/UI/` 的删除和 `Assets/Plugins/NuGet/` 的 DLL 变更。先备份到分支、具名 stash 或隔离 worktree，不得盲提交或直接丢弃。

## 5. 第一波执行：Gate 0（任务 1 修订版）

阶段 0 完成后，第一波只做任务 1。

### 5.1 文件

- 新建：`Assets/Scripts/Editor/Quality/GameSys.EditorQuality.asmdef` 及 `.meta`
- 新建：`Assets/Scripts/Editor/Quality/QualityGate.cs` 及 `.meta`
- 新建：`Assets/Scripts/Editor/Quality/AssemblyInfo.cs`（`InternalsVisibleTo`）及 `.meta`
- 修改：`Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef`（引用 `GameSys.EditorQuality`）
- 新建：`Assets/Scripts/Tests/Editor/Quality/QualityGateTests.cs`
- 新建：`Assets/Scripts/Tests/Editor/Architecture/PlayModeAssemblyContractTests.cs`
- 移动并保留 `.meta`：`Assets/Scripts/Tests/PlayMode/KcpNet.PlayModeTests.asmdef` → `Assets/Scripts/Tests/PlayMode/GameSys.PlayModeTests.asmdef`；程序集名改为 `GameSys.PlayModeTests`，移除 `KcpNet` 引用和 `!UNITY_WEBGL`，保留纯 Unity 通用测试
- 新建：`Assets/Scripts/Tests/PlayMode/NativeNetwork/KcpNet.NativePlayModeTests.asmdef` 及 `.meta`；保留 `KcpNet` 引用和 `!UNITY_WEBGL`
- 新建：`Assets/Scripts/Tests/PlayMode/NativeNetwork/TestServerEndpoint.cs` 及 `.meta`
- 新建：`Assets/Scripts/Tests/PlayMode/NativeNetwork/NativeServerProfileSentinelTests.cs` 及 `.meta`
- 移动到 `NativeNetwork/` 并保留各自 `.meta`：`AdapterConnectTests.cs`、`AoiWorldTests.cs`、`BlockMitigationE2ETests.cs`、`ClientRespawnWaitTests.cs`、`CombatSyncTests.cs`、`EchoSmokeTests.cs`、`HitJudgmentAlignmentTests.cs`、`LoginEnterWorldTests.cs`、`MonsterWorldTests.cs`、`MovementSyncTests.cs`、`ThreeClientCombatConsistencyTests.cs`、`TwoClientWorldTests.cs`
- 修改上述 Native 测试：移除硬编码 `127.0.0.1:8000`，通过 `TestServerEndpoint` 读取 `COOL_TEST_SERVER_HOST`/`COOL_TEST_SERVER_PORT`；按 `native-clean`、`native-bots`、`native-monsters` 标记 category/profile
- 新建：`tools/quality-gate.ps1`（Windows 权威 runner）
- 新建：`tools/tests/quality-gate-selftest.ps1`（不依赖外部服务的 runner 负向自测）
- 修改：`tools/unity-tests.sh` 与新建 `tools/quality-gate.sh`（调用 PowerShell 权威 runner 的薄包装）
- 新建：`tools/unity-tests-mcp.sh`（便利入口）
- 新建：`production/quality/test-manifest.json`
- 新建：`production/quality/gate-exceptions.json`
- 修改：`docs/agent-token-hygiene.md`

所有位于 `Assets/` 下的新建或移动文件都必须在任务清单和提交中包含对应 `.meta`；移动使用 Unity AssetDatabase 或等价的“文件 + `.meta`”原子移动，禁止重新生成 GUID。

### 5.2 实现要点

1. `QualityGate.cs` 只做 Editor 内 preflight：验证 active target、包名、asmdef 契约和配置，并提供可被 EditMode 测试直接调用的纯验证器；不调用 `SwitchActiveBuildTarget`，也不判断当前 NUnit run 的最终发现数或失败数；后者由 `quality-gate.ps1` 解析 XML，`quality-gate-selftest.ps1` 用 fixture XML 负向验证。
2. `quality-gate.ps1` 在 Unity 退出后解析 XML，验证 discovered、failed、skipped、manifest、进程退出码、timeout、产物新鲜度与 hash。
3. 原生 runner 启动独立 Unity 进程，传 `-buildTarget StandaloneWindows64`；Editor 内验证 `BuildTarget.StandaloneWindows64`。EditMode、通用 PlayMode和每个 Native server profile 分别输出 XML 与日志。同一 project 已被 Unity 打开时 fail-fast，并提示使用独立 worktree。
4. 服务器先执行 `dotnet build Server/Server3C.csproj -c Debug`，再以 `dotnet Server/bin/Debug/net8.0/Server3C.dll <port> <profile args>` 启动。runner 记录 PID，使用动态 TCP/UDP 端口，必须先同时观察现有日志 `Server3C started on port ... with KCP protocol` 与 `WebSocket listener started on ...`，再执行对应传输 probe；timeout 或失败时只终止本次进程树。若未来改用统一 `SERVER_READY` marker，必须把 `Server/Program.cs` 和对应服务端测试显式加入任务 1。
5. Native profile 固定为：`native-clean` 不带 bots/monsters，运行除下列特殊 profile 外的 Native 测试；`native-bots` 运行 `AoiWorldTests`，使用 `--bots 100 --bots-radius 500`；`native-monsters` 运行 `MonsterWorldTests` 与 `HitJudgmentAlignmentTests`，使用 `--monsters 4` 及测试要求的地图参数。新增导航专用测试时再启用 `native-navigation`，不得建立空 profile。profile 只描述服务器配置，runner 根据 `test-manifest.json` 按 fixture/cohort 多次调用 Unity `-testFilter`；默认每个 test fixture 使用全新服务器进程，只有被显式证明状态互不影响的 fixture 才能组成共享进程 cohort。禁止跨 profile 或未声明 cohort 共享世界状态。
6. `test-manifest.json` 保存每个 assembly/profile 的预期测试全名；测试新增、删除或迁移必须显式更新 manifest。未解释的测试数下降、未知 skip/ignore/inconclusive 均失败。
7. WebGL 使用独立 `-buildTarget WebGL` 进程。Gate 0 明确区分“通用测试程序集 WebGL compile contract”“WebGL Player Build”和“WebGL runtime smoke”，不得用其中一种替代另外两种。移除 `GameSys.PlayModeTests.asmdef` 的直接 `KcpNet` 引用只隔离测试程序集，不代表生产传输已隔离；`GameSys.asmdef` 当前仍引用 `KcpNet` 与 `WebSocketNet`，任务 13 必须用 platform factory、预处理编译守卫、PluginImporter/产物检查和 WebGL compile contract 证明 KCP 玩家路径不可达，任务 30 仅维护这组已通过 P0 Gate 的守卫。
8. `quality-gate-selftest.ps1` 使用 stub 进程和 fixture XML 证明以下情况必定返回非零并保留证据：server ready 超时、Unity 非零退出、XML 缺失/损坏、manifest 数量下降、过期 exception、stale artifact、teardown 超时和残留子进程。
9. `--infra` 与 `--baseline` 在 dirty worktree 上只能输出 `DIAGNOSTIC_ONLY`，并保存 patch hash；`--p0`、`--p1` 与 Mobile Release Readiness 默认要求 clean worktree，dirty 时不得产生 PASS。

### 5.3 验收

- 清洁 worktree 执行 `bash tools/quality-gate.sh --infra`，每个 assembly/profile 至少运行一个确定性 sentinel，退出码 0；这只证明 runner、发现、XML、服务器编排和失败传播可信。
- `tools/tests/quality-gate-selftest.ps1` 全部通过，确认负向场景不会假绿。
- 随后执行 `bash tools/quality-gate.sh --baseline` 运行全量测试并生成基线报告。产品测试可以在此阶段失败，但每个失败必须分类并映射到任务 2–13；runner、发现、profile、teardown 或证据失败仍阻塞任务 2。
- P0 Gate 执行 `bash tools/quality-gate.sh --p0`，此时所有 blocking tests 必须全绿，不能沿用 Gate 0 baseline 的失败。
- EditMode、通用 PlayMode和每个 Native profile 的 XML 均可解析，并与 test manifest 一致；仅 `discovered > 0` 不足以通过。
- 每个 server profile 结束后无残留服务端进程；再次执行结果一致。
- dirty worktree 运行只能生成 `DIAGNOSTIC_ONLY`；清洁 worktree 的正式 Gate 报告记录 release scope 与 manifest/exception 文件 hash。
- WebGL compile contract 与 Player Build 使用独立结果。未执行 runtime smoke 时只能报告 `WebGL compile/build verified`，不能报告 runtime verified。
- 不依赖 MCP `tests-run` 作为证据。

### 5.4 Gate 0 风险

- headless-safe suite 固定使用 `-nographics`；UI、渲染或需要图形设备的 suite 固定使用有图形 runner。不得在失败后临时改变参数并沿用同一 run ID；lane 变更必须新建 run ID 并完整重跑。
- `-buildTarget StandaloneWindows64` 首次切换会触发资源重导入，属预期；把耗时写入 Gate 报告。
- Gate 永远启动新 Unity 进程并写独立 `-logFile`，不调用 Console clear。新进程产生的 Error/Exception 默认失败；确需豁免时使用 `gate-exceptions.json` 精确匹配并设置到期日。
- 当前 629/630 与 PlayMode 0 只作为历史快照。Gate 0 必须重新测量并保存本轮证据，不能通过清历史日志证明问题消失。

## 6. 风险与回滚

| 风险 | 缓解 | 回滚 |
|---|---|---|
| 全量周期长（原计划 49–86 人天；Revision 2 新增移动 KCP 安全层、IPv6/NAT64、公网 WebGL 部署与 release governance，阶段 0 必须重估） | 波次 Gate；每波次独立可交付 | 只回滚当前波次分支 |
| 阶段 0 文档修订漂移 | 复核第 7 节作为验收清单；逐项映射 | 重新修订，不进入 Gate 0 |
| KCP 前移扩大 P0 | 任务 13 独立提交；soak 失败不阻塞其他 P0 波次，但 P0 Gate 不通过 | 回滚到 last-green KCP；若无 last-green，则 Android/iOS 网络就绪状态为 BLOCKED，不得改成 WSS 冒充通过 |
| KCP 安全 provider/密钥配置失败 | ADR、client/server provider、配置与轮换演练独立提交；非 Development 明文/降级守卫为 blocking | 原子回滚 client/server/provider/config 到同一 last-green；无完整配对版本时禁止生成公开候选包，不得临时启用明文 |
| 移动/CI 依赖 runner | 仓库变量控制 iOS job；按里程碑发布范围判定 blocking | 缺少目标平台环境时标记 `BLOCKED_NOT_VERIFIED`，不修改为 PASS |
| 当前脏工作树 | 阶段 0 前逐项分类并备份 | 使用备份分支、具名 stash 或隔离 worktree；未经确认不丢弃 |
| 性能/覆盖率门禁误报 | 按 U-P1-10 固定设备、采样和参数 | 保留原始数据，调整方法而不是放宽门禁 |
| 协议与客户端/服务端版本不匹配 | 同一波次提交、同一 Gate 验证协议版本与 roundtrip | 客户端、服务端和协议定义按 last-green 组合原子回滚 |
| Addressables catalog 与 bundle 不匹配 | 保存 content manifest/hash，Player Build 引用同一 manifest | catalog、hash、bundle 与 Player Build 按同一版本组合回滚 |

每个波次 Gate 通过后创建 last-green tag，并在报告中记录客户端、服务端、协议、Addressables content 和 Player Build 的版本组合。子分支回滚后必须在合并后的集成分支重新运行对应 Gate。

## 7. 完成判定

### 7.1 P0 完成

原 Spec 10.1 + 复核 7.1 全部满足，且任务 1–14 的 Gate 报告存在。关键项：

- 服务端全量测试通过；Unity EditMode、通用 PlayMode、Native PlayMode 各自被发现且通过。
- Native suite 可从清洁环境自行启动和停止服务端。
- 登录响应不被状态判断阻塞；Unity 主线程不存在测试延迟造成的阻塞。
- EventSubscription 重复和跨副本 Dispose 幂等；生产移动上报路径没有 `Thread.Sleep`。
- KCP 生命周期已修复并有自动化测试；Android/iOS 的 KCP 与 WebGL 的 WebSocket/WSS 选择策略、factory 边界和 WebGL KCP plugin 排除已验证；Win64 KCP soak 与 WebGL browser WS 往返通过；每个平台只启用一个主传输。P0 不得据此宣称 Android/iOS 真机或公网安全已验证。
- 所有结果由本轮 XML、日志、进程退出码、test manifest 和产物 hash 证明。

### 7.2 P1 Engineering Gate 完成

原 Spec 10.2 中可由 CI、Editor 或 Standalone 自动验证的工程项，以及复核 7.2 中非设备/公网部署专属的自动化项全部满足；任务 15–33 的工程 Gate 报告存在。设备专属要求由 7.3 判定，公网 WebGL 要求由 7.4 判定。关键项：

- 两个 WorldInstance 隔离；200 bots 压测 tick p95 < 50ms、p99 < 66ms。
- Android ARM64 IL2CPP Development Build 成功；非 Development 候选包及设备 runtime smoke 独立归入 Mobile Release Readiness。缺设备不伪造 Engineering 结果，但对应 Mobile Release Readiness 必须为 `BLOCKED_NOT_VERIFIED`。
- Addressables Use Existing Build 验证通过；Analyze 无阻塞级问题；catalog/hash/bundle 产物存在。
- 热路径性能数据可复现；场景退出后 VFX 与 Monster 资源归零。
- 经 ADR 批准的 KCP security provider 在 `release-scope.yaml` 的所有 blocking mobile platforms 的 IL2CPP Player Build 中编译通过，非 Development 配置禁止明文/降级；factory、endpoint、DNS、IPv4/IPv6 与篡改/重放拒绝的自动测试通过。被排除平台的兼容性只能列 advisory 或平台级 `NOT_IN_SCOPE`，不得反向阻塞整个 Engineering Gate。Engineering Gate 不替代候选包真机网络证据。
- WebGL 为 blocking platform 时，Engineering Gate 必须完成 release 配置的 WebGL Player Build、禁止 `ws://` 公网 endpoint 的静态检查、反向代理配置语法检查与 artifact digest；真实证书、DNS、公开代理和浏览器往返由 7.4 判定。
- CI 对 0 tests、测试清单异常下降、未知 skip、默认包名、协议漂移、配置错误、架构护栏和覆盖率下降明确失败。
- 关闭 Domain Reload 后连续两轮运行无静态状态与资源泄漏。
- iOS 是否 blocking 由批准的 `release-scope.yaml` 决定；被明确排除时为 `NOT_IN_SCOPE`，纳入 blocking platforms 但缺 macOS/Xcode/runner/设备证据时为 `BLOCKED_NOT_VERIFIED`，不能写成 iOS verified。

### 7.3 Mobile Release Readiness 完成

- 当前 blocking 平台已经从 Engineering Gate 相同 Git SHA、相同 Addressables content manifest 生成非 Development 候选包；候选包记录 BuildReport、非敏感 signing identity 指纹、版本号、Managed Stripping Level、Scripting Backend、Development/Debuggable 标志与 ABI，任何一项不匹配均失败。
- 当前目标 Android 平台的 ARM64 IL2CPP 候选包已在真机或明确 ARM64 emulator 完成安装与 runtime smoke，并保存主动完成标志、`adb logcat`、设备信息、包版本、协议 fixture 结果和 ABI。
- 候选包通过已批准的 KCP 安全 provider 连接受控公网测试端点；按 3.4 把客户端包、实际服务端 artifact/deployment/config、provider/协议/ADR、非敏感 key ID、endpoint/DNS 与测试窗口绑定到同一 run ID，任一端重新部署后旧证据失效；保存域名、IPv4、IPv6-only/NAT64、服务端身份、AEAD、重放/篡改/降级拒绝、重连与 rekey/密钥轮换证据，且未记录密钥、票据或明文敏感载荷。仅 emulator 证据不能替代物理设备网络切换与 NAT64 验证。
- Android 参考设备档位已经配置；帧时间、GC、内存和 30 分钟 soak 达到固定阈值并保留原始数据。
- 冷/热启动、前后台恢复、网络切换、断网重连、Safe Area、返回键、低内存和场景切换取消均有 PASS 证据。
- 本次测试窗口内未观测到崩溃、ANR、未观察异常或超过已定义容差的资源泄漏。
- 当前里程碑包含 iOS 时，iOS 必须完成对应的非 Development 候选包、Xcode/archive 与设备验证，证据缺失则为 `BLOCKED_NOT_VERIFIED`；被批准范围明确排除时为 `NOT_IN_SCOPE`。
- 任一目标平台 blocking 项缺证据时，本 Gate 不得 PASS，也不得标记“公开测试/商店发布准备”。

### 7.4 WebGL Public Deployment Readiness 完成

- WebGL 不在批准范围时，本 Gate 为 `NOT_IN_SCOPE`；纳入 blocking platforms 时，下列项目全部为 blocking，不能用 P0 的本地 `ws://` browser smoke 替代；
- Engineering Gate 生成的 WebGL artifact digest 与实际 CDN/站点部署 artifact 一致；Addressables catalog/hash/bundle 来自同一 Git SHA 和 content manifest；
- 公开 `https://` 页面只连接 release scope 中批准的完整 `wss://host:port/path`，DNS、有效证书链、hostname、有效期、反向代理路由/Upgrade/超时/消息上限配置均通过；浏览器控制台无 mixed-content、证书或握手错误；
- 在 `requiredBrowserLanes` 指定的浏览器/版本上完成冷启动、登录、消息往返、断线/重连、页面隐藏/恢复和至少一个场景进入 smoke；保存 HAR/代理日志/浏览器日志与主动完成标志；
- 按 3.4 把 WebGL artifact、实际服务端/代理 artifact 或镜像 digest、配置 hash、部署时间、endpoint/DNS/证书指纹和测试窗口绑定到同一 run ID；任一部署、DNS、证书或代理配置变化后旧证据失效；
- 任何 blocking 项缺失时为 `BLOCKED_NOT_VERIFIED` 或 `FAIL`，不得宣称 WebGL 公网发布准备。

## 8. 阶段 0 退出与基线激活

阶段 0 只有在以下条件全部满足后才能结束：

- [ ] 原 Spec 已写回平台传输矩阵、Gate 状态机、资源/消息泵/UI 启动器/静态生命周期契约。
- [ ] 原 33 任务计划已按 U-P0-01 至 U-P0-07、U-P1-01 至 U-P1-13 更新文件清单、命令、测试和验收条件。
- [ ] 原计划已把任务 13 前移到 P0 并设为任务 14 的显式依赖；原 Spec §6.0 与 ADR-0001 已删除“所有平台默认 WSS/KCP 实验禁用”的旧默认值并改写为本文平台矩阵；任务 30/32 的职责与本文一致。
- [ ] 任务 1 已包含完整 Native 测试迁移清单、server profile、`TestServerEndpoint`、runner 职责和 WebGL lane。
- [ ] `docs/technical-preferences.md` 的权威来源和两个工具链同步策略已经写明；`.codex/` 小写契约、Git 大小写变体检查、`AGENTS.md`/`CLAUDE.md` 入口及 Windows/Linux consumer sentinel 均通过。
- [ ] 三个基础 ADR 已创建并通过一致性检查，平台传输不再存在 WSS/KCP 全局默认值冲突，移动公网 KCP 已锁定标准安全协议/库与密钥生命周期。
- [ ] ADR-0003 的 POC 已完成并通过：Android IL2CPP 客户端与 .NET 8 服务端握手、AEAD 往返、篡改/重放拒绝；失败时已写回退方案。
- [ ] `KcpNet` WebGL 编译守卫清单（7 个未带守卫文件）已逐项处理；排除 `Kcp.dll` 后 `KcpNet` 仍可在 WebGL 编译。
- [ ] `Assets/Packages/` 的归属（生成目录 vs 版本控制）已决定，并写入 `.gitignore` 或任务 13 文件清单。
- [ ] 端点重构清单（`WebSocketClientAdapter`、`KcpNetClientAdapter`、`KcpClient`、endpoint resolver）已写入任务 13。
- [ ] 阶段 0 已按 Revision 2 范围重估工期，不再引用 49–86 人天作为完整范围估算。
- [ ] Gate 状态、run ID、timeout、test manifest、skip/flaky、产物新鲜度和原子回滚规则已写回原计划。
- [ ] `release-scope.yaml` 已由明确 approver 批准，Android/iOS/WebGL 的 blocking/NOT_IN_SCOPE 状态及 device/browser lanes 不再由执行者临时决定。
- [ ] Gate 0 的 cohort manifest、profile sentinel 与 runner 负向自测已经写入任务 1。
- [ ] 任务 13 已独立拥有 P0 所需的 platform factory、`NetworkEndpoint`、两份 Kcp.dll 去重/Importer 排除、DNS candidate policy、IPv4/IPv6/NAT64、factory/plugin/产物守卫与完整文件/测试清单；任务 30 只接管长期守卫维护；任务 32 包含安全 provider、release 明文 fail-closed、部署与轮换演练。
- [ ] 全局 `ResourceManager.ShutdownAsync` 与 owner 级 `ResourceScope.CloseAsync` 已分离，CoreUI application/UI scope 与 DemoDay world scope 的往返测试已写入任务 21/23/30。
- [ ] P1 Engineering Gate 的 Development Build 与 Mobile Release Readiness 的同 SHA、同 content manifest、非 Development 候选包已分别写入任务 29/33。
- [ ] P1-B1/P1-B2/P1-C1/P1-C2 波次和依赖已经写回原计划。
- [ ] 当前工作树已安全备份或隔离，没有未授权丢弃。
- [ ] 修订后的 Spec 与计划通过文档复核，并记录批准 Git SHA。

激活记录：

| 项目 | 值 |
|---|---|
| 批准后的 Spec Git SHA | `c773a5b180b04c033e8472bf2a073052b375856d` |
| 批准后的计划 Git SHA | `5383a413701fe5d1f54336d7e5aa3c2367319f30` |
| 平台传输 ADR | `c64457f15041245fa072d098dcbc58cc3ebdf720` |
| Gate 证据 ADR | `8dadf176473617179e311a67f6f926b4c74df0a6` |
| KCP 会话安全 ADR | `c24eeafdb05d1c62576d4d4a43acea6174e75e21` |
| Release scope 提交（包含已批准内容） | `c739dfbd5b1115ec7f53292f79d5de8ad5e9348b` |
| 激活日期 | 2026-11-07 |

Spec/计划/ADR 行为基线提交 `c739dfbd5b1115ec7f53292f79d5de8ad5e9348b` 上对应路径的 Git blob SHA（`git rev-parse c739dfbd:<path>`）；该提交包含全部被批准内容。Release scope 行为包含已批准 release scope 内容的提交 SHA（该提交加入 `production/quality/release-scope.yaml`），不是父提交或提交前 `HEAD`，与 `release-scope.yaml` 的 `sourceGitSha` 一致。

激活状态：**待裁决**。文档/治理文档已就绪；§8 仍有 1 项 `BLOCKED_NOT_VERIFIED`（Linux consumer/hash），另有 3 项执行类 DEFERRED（KCP WebGL 守卫、Assets/Packages 归属、WebGL compile contract）到 task 13/Gate 0。Codex consumer sentinel 已通过（独立 Codex 会话读取 `.codex/docs/technical-preferences.md` 并回报 `source-sha256 670a084a...`）；`production/quality/technical-preferences-sentinel.md` 的 Linux consumer/hash 仍为 `BLOCKED_NOT_VERIFIED`（本机 `wsl.exe` 存在但未安装 Linux distribution），因此本文状态不改为“已确认，执行基线”，执行基线激活等待用户裁决。

仅当上述清单全部满足后，才将本文状态改为“已确认，执行基线”，然后进入 Gate 0。清单未完成时，只允许修改文档、测试基础设施设计和治理文件，不允许开始任务 2–33 的运行时代码实施。

## 9. 参考

- [原设计规格](2026-11-05-p0-p1-quality-improvement-design.md)
- [原实现计划](../plans/2026-11-05-p0-p1-quality-improvement.md)
- [Unity 专家复核报告](../reviews/2026-11-06-p0-p1-unity-expert-review.md)
- [跨模块架构指南](../../agent-architecture.md)
- [Agent Token 使用指南](../../agent-token-hygiene.md)
- [Unity 2022.3 Editor 命令行参数](https://docs.unity3d.com/2022.3/Documentation/Manual/EditorCommandLineArguments.html)
