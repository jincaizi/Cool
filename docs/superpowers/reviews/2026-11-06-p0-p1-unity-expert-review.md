# P0 P1 质量改进计划 Unity 专家复核报告

## 文档信息

| 项目 | 内容 |
|---|---|
| 复核日期 | 2026-11-06 |
| 复核对象 | `2026-11-05-p0-p1-quality-improvement-design.md` 与 `2026-11-05-p0-p1-quality-improvement.md` |
| 项目环境 | Unity 2022.3.25f1、移动平台、IL2CPP、Addressables、KCP、Protobuf |
| 复核结论 | **CHANGES REQUIRED** |
| 文档用途 | 作为修订 P0/P1 Spec、实施计划及后续验收门禁的执行依据 |

## 1 结论摘要

现有设计和实施计划的总体方向正确，不需要推倒重写。不可变运行时数据、主线程消息泵、资源租约、类型化 UI 注册、P0/P1 分段门禁，以及“无法验证即标记为 not-verified”的原则都值得保留。

当前计划仍不能直接作为实施基线。Unity 专项复核发现：

- 至少 7 项 P0 计划阻塞；
- 10 余项 P1 Unity 落地缺口；
- 部分方案在普通 C# 设计上成立，但会被 Unity 的程序集边界、序列化规则、Domain Reload、BuildTarget 切换、Addressables 生命周期或 IL2CPP AOT 约束破坏；
- 若不先修订计划，实施后可能出现无法编译、测试空跑、登录永久等待、资源重复打包、场景句柄泄漏或移动包构建成功但运行失败等问题。

因此，后续应先修订 Spec 和实施计划，再按本文第 5 节的顺序执行。P0 Gate 通过前，不应开始大规模 P1 重构。

## 2 优先级定义

### 2.1 P0 计划阻塞

必须在执行对应 P0 任务前修订。否则计划可能无法编译、无法形成可信测试证据，或直接破坏核心登录与网络流程。

### 2.2 P1 计划阻塞

可以在 P0 Gate 通过后处理，但必须在执行对应 P1 任务前写入 Spec、文件清单、测试方案和完成条件。

### 2.3 完成判定原则

单纯完成代码修改不代表任务完成。每项修复至少需要同时满足：

1. Unity 编译通过；
2. 对应 EditMode 或 PlayMode 测试被实际发现且通过；
3. 测试结果由独立 XML、构建产物或设备证据证明；
4. 失败、取消、销毁、重复初始化和关闭 Domain Reload 等生命周期路径有明确行为；
5. 不以历史 Console 日志、手工预启动服务或未验证的平台状态作为通过证据。

## 3 P0 必须修订项

### U-P0-01 质量门禁程序集设计无法编译

**对应任务：** 任务 1。

**问题：** 计划把 `QualityGate.cs` 放在 `Assets/Scripts/Editor/Quality/`，同时要求 `GameSys.EditorTests` 调用其 `internal` API。该文件会进入预定义的 `Assembly-CSharp-Editor`，asmdef 测试程序集不能反向引用预定义 Editor 程序集。

**修订要求：**

- 新建 Editor-only 的 `GameSys.EditorQuality.asmdef`；
- `GameSys.EditorTests.asmdef` 显式引用该程序集；
- 若验证 API 保持 `internal`，增加 `InternalsVisibleTo("GameSys.EditorTests")`；
- 把 asmdef、`.meta` 和程序集引用修改加入任务 1 的文件清单与提交清单；
- 纯验证逻辑不得依赖运行时切换 BuildTarget。

**完成条件：**

- Unity 完整编译无程序集引用错误；
- EditMode 测试能直接调用质量门禁验证器；
- 测试程序集不依赖 `Assembly-CSharp-Editor`。

### U-P0-02 测试过程中切换 BuildTarget 不可靠

**对应任务：** 任务 1、任务 14、任务 31。

**证据：**

- 计划：`docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md:109-113`；
- 现有说明：`Assets/Scripts/Editor/WebGLBuildScript.cs:52-60`；
- 当前测试入口：`tools/unity-tests.sh:15-20`。

**问题：** `SwitchActiveBuildTarget` 会触发资源重新导入、脚本重编译和 Domain Reload。测试进程内执行 `EnsurePlayModeTarget()` 后，当前测试方法和后续语句不能保证继续运行。当前原生 Unity CLI runner 又被延后到任务 31，无法支撑任务 14 的 P0 Gate。

**修订要求：**

- 把原生 Unity CLI runner 前移到任务 1；
- 在启动独立 Unity 进程时传入 `-buildTarget StandaloneWindows64`；
- `QualityGate` 只验证当前目标，不在 NUnit 测试中执行切换；
- MCP `tests-run` 仅作为本地便利入口，不作为 P0 Gate 的唯一证据；
- EditMode、通用 PlayMode、Native PlayMode 分别输出独立 XML 和日志文件。

**推荐命令形态：**

```text
Unity.exe -batchmode -nographics -projectPath <project> \
  -buildTarget StandaloneWindows64 \
  -runTests -testPlatform PlayMode \
  -assemblyNames <assembly> \
  -testResults <result.xml> -logFile <unity.log>
```

**完成条件：**

- runner 不在测试进程内切换 BuildTarget；
- 结果 XML 存在、可解析且测试发现数大于 0；
- Unity 异常退出、结果缺失或 0 tests 均返回非零。

### U-P0-03 Native PlayMode 缺少服务端编排与套件隔离

**对应任务：** 任务 1、任务 14、任务 31。

**证据：** `Assets/Scripts/Tests/PlayMode/EchoSmokeTests.cs:15,80,97` 当前依赖已运行的 `127.0.0.1:8000` 服务端。

**问题：** 当前计划没有定义服务器启动、ready probe、动态端口、日志收集和 finally 停止流程。通用与 Native PlayMode 也没有固定程序集过滤、独立结果文件和各自的 0-tests 检查。

**修订要求：**

- Native suite 前启动独立测试服务器；
- 使用动态端口，通过环境变量或测试参数传给 Unity；
- 等待健康检查或端口 ready 后再运行测试；
- 服务端启动失败、提前退出、端口冲突或 teardown 残留均使门禁失败；
- 无论测试成功或失败，都在 finally 中停止服务端并保存日志；
- 通用与 Native suite 分别传入 `-assemblyNames`，分别生成 XML；
- 明确迁移的测试文件及 `.meta` 清单。

**完成条件：**

- 清洁环境下无需手工预启动服务器即可运行；
- 通用与 Native suite 各自发现至少 1 个测试；
- 两套结果可独立判定；
- 测试结束后不存在遗留服务端进程。

### U-P0-04 KCP 修复优先级与 P0 测试依赖冲突

**对应任务：** 任务 13、任务 14。

**证据：** 任务 13 被标为 P1，但以下 P0 验证相关测试直接实例化 `KcpNetClientAdapter`：

- `Assets/Scripts/Tests/PlayMode/BlockMitigationE2ETests.cs:30`；
- `Assets/Scripts/Tests/PlayMode/ThreeClientCombatConsistencyTests.cs:27`；
- `Assets/Scripts/Tests/PlayMode/CombatSyncTests.cs:26`；
- `Assets/Scripts/Tests/PlayMode/LoginEnterWorldTests.cs:46`；
- `Assets/Scripts/Tests/PlayMode/MovementSyncTests.cs:18,305`。

**问题：** 任务 14 只要求确认生产默认禁用 KCP，不能消除 P0 Native suite 对未修复 KCP 生命周期的实际依赖。

**修订要求：**

- 推荐将任务 13 前移为 P0，并放在任务 14 前；
- 若 KCP 坚持留在 P1，则 P0 suite 必须改成传输无关、WSS 或 in-memory transport，并明确排除 KCP；
- 无论采用哪种方案，生产默认禁用 KCP 的检查仍需保留。

**完成条件：**

- P0 Gate 不依赖一个尚未完成生命周期修复的传输实现；
- KCP 连接、断开、重复释放和接收循环停止均有自动测试；
- 生产配置保持实验性默认禁用。

### U-P0-05 EventSubscription 不能使用无共享状态的 readonly struct

**对应任务：** 任务 4。

**证据：** `docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md:340-342`。

**问题：** `readonly struct` 可被复制。复制后的多个 token 都可以调用 `Dispose`，无法天然保证跨副本幂等退订。

**修订要求：**

- 推荐改为 `sealed class EventSubscription : IDisposable`；
- 使用 `Interlocked.Exchange` 或等价原子状态确保只退订一次；
- 若必须保留 struct，内部必须持有共享的 `SubscriptionState` 引用；
- EventBus 派发热路径不应在每次 `Emit` 时调用 `ToArray()`，优先采用订阅变更时更新的 copy-on-write 快照。

**完成条件：**

- 增加 `Copied_Tokens_Dispose_Only_Once` 或 class 等价测试；
- 重复 Dispose 不抛异常、不重复修改订阅表；
- Emit 热路径无临时数组分配。

### U-P0-06 MovementReporter 在 Unity 主线程阻塞

**对应任务：** 任务 7。

**证据：**

- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/MovementReporter.cs:65-66` 使用 `Thread.Sleep`；
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs:192` 在主线程调用 reporter。

**问题：** 测试延迟注入进入了生产运行类。即使生产参数通常为 0，这种设计仍允许 Unity 帧线程被阻塞，并且任务 7 只处理服务器时钟，没有移除该风险。

**修订要求：**

- 从生产 `MovementReporter` 删除 `injectedSendDelayMs` 和 `Thread.Sleep`；
- 将延迟、抖动、丢包模拟放到 fake transport 或可控 test scheduler；
- 时间读取继续通过可注入时钟完成，避免生产逻辑直接依赖 `DateTime.UtcNow`。

**完成条件：**

- 生产移动上报代码不存在 `Thread.Sleep`；
- 增加 `Injected_Network_Delay_Does_Not_Block_MainThread`；
- 延迟模拟期间 Unity 主线程仍可持续推进帧更新。

### U-P0-07 主线程消息泵会让登录响应永久得不到消费

**对应任务：** 任务 12。

**证据：** `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkManager.cs:160-163` 在状态不是 `InWorld` 时立即返回。

**问题：** 计划把 `LoginResponse`、错误和断线事件都放入消息泵，但没有要求在状态判断前排空控制消息。按当前结构实现后，状态进入 `LoggingIn`，登录响应入队，`Update` 却直接返回，登录将永久等待。

计划使用的 `ClientEnvelope`、`DrainResult`、`LoginResult` 也没有对应创建文件或嵌套类型说明。`ClientNetworkHost` 的连接生命周期、异常观察和销毁取消同样未完整定义。

**修订要求：**

- `Update` 先 drain 登录、错误、断线和其他 control 消息；
- control drain 完成后，再根据状态决定是否执行 InWorld tick；
- 明列 `ClientEnvelope.cs`、`DrainResult.cs`、`LoginResult.cs`，或说明其所属嵌套类型；
- `ClientNetworkHost` 持有连接 lifetime CTS 和正在执行的 Task；
- `Disconnect` 与 `OnDestroy` 取消任务并观察所有异常；
- 禁止用未观察的 fire-and-forget 包装新的连接流程。

**完成条件：**

- `Pump_Drains_LoginResponse_While_LoggingIn` 通过；
- `Pump_Drains_Error_And_Disconnect_Before_InWorld` 通过；
- Host 销毁期间连接任务能取消且没有未观察异常。

## 4 P1 必须修订项

### U-P1-01 IDamageable 迁移清单不完整

**对应任务：** 任务 19。

**问题：** 计划主要列出 `IDamageable`、`MonsterEntity` 和待删除旧脚本，但接口调用实际分布于 `Sys3CEntry`、`SkillExecutor`、`FireballProjectile`、`SwordQiProjectile`、怪物攻击状态及多个测试 fake。直接替换接口会造成大面积编译失败。

**修订要求：**

- 定义唯一运行时伤害模型；
- 推荐让 `DamageRequest` 包含不可变 `SkillDamageContext`、方向及 `HitContact`；
- 建立生产调用者、实现者、测试 fake 的迁移矩阵；
- 可先保留 `[Obsolete] DamageBlock` 适配 overload，迁移完成后再删除；
- 每个迁移阶段先执行 Unity 编译和目标测试，再扫描 Prefab GUID。

**完成条件：**

- 全项目只有一条客户端伤害输入模型；
- 所有生产调用者和测试 fake 完成迁移；
- 删除旧脚本后无 Missing Script、无旧 GUID 引用、无旧接口调用。

### U-P1-02 Sys3C 依赖注入忽略 Prefab 不能引用场景对象

**对应任务：** 任务 20。

**证据：**

- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:200-222`；
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs:243,764,770`。

**问题：** 同一 Prefab 上的组件可以序列化，但场景中的 `ThirdPersonCameraController` 不能保存为 Prefab 引用。仅修改两个玩家 Prefab 无法消除 Camera 查找，也无法满足项目禁止非内部 MonoBehaviour 使用 `GetComponent` 的规则。

**修订要求：**

- 将 `PlayerSpawner`、`PlayerSpawnedEvent` 和场景组合根纳入任务 20；
- PlayerSpawner 使用类型化 Prefab 根，例如 `Sys3CEntry` 或 `PlayerCompositionRoot`；
- 实例化后、`Start` 前，由场景组合根注入 Camera 和其他 scene dependency；
- `PlayerSpawnedEvent` 直接携带 `Sys3CEntry`，避免 Host 再执行 `GetComponent`；
- `Sys3CEntry` 在注册 Physics、EventBus 前一次性验证全部依赖；
- 初始化失败不得留下半注册对象。

**完成条件：**

- 两个玩家 Prefab 和真实入口场景均有 PlayMode 覆盖；
- 相关 MonoBehaviour 不再使用 `FindObjectOfType` 或 `GetComponent` 获取运行依赖；
- 缺失依赖时快速失败，并清理所有已完成注册。

### U-P1-03 ResourceManager 仍保留 WebGL 不兼容和同步等待路径

**对应任务：** 任务 21。

**证据：**

- `Assets/Scripts/AOT/Core/Resource/ResourceManager.cs:50,149,169` 使用 `AsyncOperationHandle.Task`；
- `Assets/Scripts/AOT/Core/Resource/ResourceManager.cs:99` 使用 `WaitForCompletion()`。

**问题：** `AsyncOperationHandle.Task` 不适用于 WebGL，`WaitForCompletion()` 也不应保留在玩家资源加载路径。计划只把旧 `Load/Release` 标记为 obsolete，没有明确删除这些等待方式或限制其仅在 Editor 使用。

**修订要求：**

- `AddressablesResourceBackend` 使用 UniTask Addressables awaiter 或 Completed callback；
- 所有 Addressables API 从 Unity 主线程进入；
- 删除玩家运行路径中的 `WaitForCompletion()`；
- 同步兼容接口若必须保留，只允许 Editor 工具调用并提供明确守卫；
- 增加 WebGL Player 编译门禁和源码守卫。

**完成条件：**

- 生产资源层不包含 `AsyncOperationHandle.Task` 与 `WaitForCompletion()`；
- WebGL、Android 目标均可编译；
- 加载失败、取消和 shutdown 不泄漏 handle。

### U-P1-04 SceneLease 需要异步卸载契约

**对应任务：** 任务 21。

**问题：** 计划让 `AssetLease<T>` 和 `SceneLease` 都使用同步、幂等 `Dispose`，但 Addressables 场景通过 `UnloadSceneAsync` 卸载。同步 Dispose 容易变成未观察的 fire-and-forget，产生场景竞态或句柄泄漏。

**修订要求：**

- `SceneLease` 保存 `AsyncOperationHandle<SceneInstance>`；
- 提供幂等 `UniTask UnloadAsync()` 或等价 async-dispose 契约；
- 加载取消后，若底层操作已经开始，应等待完成后再卸载和释放；
- 明确 Additive、Single、重复卸载和 ResourceManager shutdown 的所有权规则。

**完成条件：**

- 重复卸载、加载中取消、Single 切换和 shutdown 中卸载测试全部通过；
- 不存在未观察的场景卸载任务；
- 句柄计数最终归零。

### U-P1-05 UIBootstrap 的序列化方案不可实现

**对应任务：** 任务 23。

**证据：**

- 计划：`docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md:1747`；
- 当前实现：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBootstrap.cs:19-35`。

**问题：** 当前 `UIBootstrap` 是 `static class`，并通过 `RuntimeInitializeOnLoadMethod` 动态创建 `UIManager`。静态类不能拥有 Unity 序列化字段，因此无法按计划序列化 `BuiltinUiLoadFailurePresenter`。

**修订要求：**

- 推荐让场景中的 Presenter 在 `Awake` 调用 `UIBootstrap.RegisterFailurePresenter(this)`；
- `AfterSceneLoad` 再将已注册 Presenter 注入 `UIManager`；
- 或改成真实的 `UIBootstrapBehaviour`，但需要同步修改所有入口场景和 UI README；
- 增加 `SubsystemRegistration` 清理静态注册；
- 同时覆盖 `CharacterSelect` 和直接进入 `DemoDay` 的启动路径；
- 没有 Presenter 时必须有非递归、非 Addressables 依赖的降级行为。

**完成条件：**

- `CharacterSelect` 正常显示失败 Presenter；
- 直接进入 gameplay 场景不会空引用或递归加载失败页；
- 连续两次 PlayMode 不残留失效静态引用。

### U-P1-06 Addressables 门禁没有验证真实 bundle 路径

**对应任务：** 任务 22、任务 31。

**问题：** 仅执行普通 PlayMode 时，Active Play Mode Data Builder 可能仍是 Use Asset Database。测试即使全部通过，也不能证明 catalog 和 bundle 能在真实 Player 路径加载。计划只描述“另执行 BuildPlayerContent”，没有可调用的静态 Editor 入口和产物校验。

**修订要求：**

- 增加 `AddressablesBuildScript.BuildPlayerContent` batchmode 入口；
- 快速测试使用 Use Asset Database；
- 生产验证使用“切换目标平台 → BuildPlayerContent → Use Existing Build → PlayMode 生命周期测试”；
- 运行 Addressables Analyze；
- 校验重复 address、缺失依赖、重复 bundle dependency，以及 Resources/Addressables 双重打包；
- 固定 Core UI、Combat VFX 等 group/schema、Build Path、Load Path、压缩和打包策略；
- 验证 catalog、hash、bundle 产物实际存在。

**完成条件：**

- 清洁 checkout 可重复构建 Addressables content；
- Use Existing Build 下 UI/VFX 生命周期测试通过；
- Player Build 使用同一份已验证 catalog；
- Analyze 无阻塞级问题。

### U-P1-07 VFX 迁移对象和真实引用链不一致

**对应任务：** 任务 26。

**证据：**

- 计划迁移 `Assets/VFX/HitSparkParticle.prefab`，但真实战斗场景引用 `Assets/Prefabs/VFX/HitSparkBurst.prefab`；
- `Assets/Scripts/AOT/DataDefinition/GameSettings.cs:60` 直接持有 `HealVfxPrefab`；
- `Assets/Resources/Setting/GameSettings.asset:27` 直接引用 Heal Prefab；
- `Assets/Scripts/Hotfix/GameSystems/VFX/PlayerHealVFX.cs:31-34` 直接 Instantiate。

**问题：** 迁移错误资源不能覆盖真实命中路径。Heal Prefab 若同时被 Resources asset 直接引用并标为 Addressable，会产生 Player 数据与 bundle 的重复内容。同步 `IVfxPool.Rent(prefab)` 也没有说明 prefab 的异步预加载时机和 lease 所有者。

**修订要求：**

- 明确迁移 `HitSparkBurst`，或明确未使用 `HitSparkParticle` 的处置；
- 将 `GameSettings.cs`、`GameSettings.asset`、`DemoDay.unity` 和实际引用 Prefab 加入任务文件清单；
- 将 Heal 的直接 `GameObject` 引用改为 address、`AssetReference` 或 VFX descriptor；
- 定义完整链路：World/Combat loading → 获取 prefab lease → 预热 pool → 启用事件订阅；
- 场景退出时先回收或销毁 active 与 idle instance，再释放 prefab lease；
- 明确未 Ready 时丢弃、排队或降级的策略；
- CI 按 GUID 检查 Addressable VFX 不再被 Scene、Resources asset 或非 Addressable Prefab 直接引用。

**完成条件：**

- 首次战斗不触发临时 Addressables 懒加载卡顿；
- 真实 Hit 与 Heal 路径都使用统一 VFX pool；
- 场景退出后实例数和 lease 数归零；
- 无重复打包依赖。

### U-P1-08 Android iOS 构建入口可能空跑或使用错误平台

**对应任务：** 任务 29、任务 31。

**证据：** `docs/superpowers/plans/2026-11-05-p0-p1-quality-improvement.md:2097-2101`。

**问题：** 当前命令未传 `-buildTarget Android` 或 `-buildTarget iOS`。若 build method 内部切换目标，会遇到同样的 Domain Reload 问题。Windows 又不应无条件调用 iOS Xcode 导出。GitHub 缺少匹配的 self-hosted macOS runner 时，job 会排队，不会自动 skipped。

**修订要求：**

- Android 进程启动时指定 `-buildTarget Android`；
- macOS iOS job 启动时指定 `-buildTarget iOS`；
- build method 只验证 active target，不负责切换；
- Windows 仅执行 `ValidateIosConfiguration`，不调用 Xcode export；
- 使用仓库变量，例如 `HAS_IOS_RUNNER`，控制 iOS job；
- 没有 runner 时生成 `ios-status = not-verified` artifact/summary；
- 固定 runner labels，并检查 Unity 版本、许可证、Build Support、SDK/NDK/JDK 与 Xcode；
- 写明唯一 BuildScenes 清单；
- 校验 BuildReport、APK/AAB 或 Xcode 工程确实存在，而不是只看退出码。

**完成条件：**

- Android 缺少构建环境时明确失败；
- iOS 缺少 macOS runner 时明确 not-verified，且不会无限排队；
- 每次成功构建均产生可验证产物和日志；
- 构建脚本的设置修改由 finally 恢复。

### U-P1-09 缺少 IL2CPP AOT 和代码裁剪运行时 Smoke

**对应任务：** 任务 29、任务 33。

**证据：** `Assets/Scripts/AOT/Protocol/MessageCodec.cs:50,113,132` 使用 protobuf-net 的 `Serializer.NonGeneric` 路径，并已有 IL2CPP 限制说明。

**问题：** Android/iOS 构建成功不能证明反射反序列化、代码裁剪和 AOT 泛型实例在设备上可运行。当前计划没有设备启动、协议编解码和 Addressables 初始化的运行时 smoke。

**修订要求：**

- 增加 Android IL2CPP 设备或模拟器 smoke；
- 至少覆盖冷启动、CharacterSelect、Addressables 初始化、内建 UI 加载、代表性协议编解码和进入 DemoDay；
- 根据 strip diagnostics 和 smoke 证据配置 `link.xml` 或 `[Preserve]`；
- 固定 Managed Stripping Level；
- 对 MessageTypeRegistry 中的消息建立可执行的 AOT 保留契约。

**完成条件：**

- Android IL2CPP 包在目标设备启动并完成核心 smoke；
- 没有协议反序列化、MissingMethod、AOT 或裁剪异常；
- 只有在真机或模拟器证据存在时，才可声明 Android runtime verified。

### U-P1-10 性能与覆盖率门禁不可复现

**对应任务：** 任务 24、任务 27、任务 31、任务 33。

**问题：** 普通 Unity Editor Test Runner 本身会产生分配，直接断言“warm path 0 bytes”容易误报。30 分钟 soak 也没有固定设备、分辨率、Quality Level、预热、采样源、采样窗口、p95 算法和输出格式。Coverage 缺少可执行参数、程序集白名单和排除规则。

**修订要求：**

- 纯 C# 热路径可使用隔离后的 `GC.GetAllocatedBytesForCurrentThread`；
- Unity 对象和帧性能使用 Unity Performance Testing package 或 Standalone Player 性能测试；
- 为设备 soak 固定设备档位、分辨率、Quality Level、预热时间、采样窗口和机器可解析输出；
- Coverage 固定 `-enableCodeCoverage`、`-coverageResultsPath`、`-coverageOptions`、assembly filter 和排除规则；
- Coverage XML 缺失、损坏或目标程序集未出现时门禁失败；
- 不把一次 Editor 内的零分配断言作为移动端性能结论。

**完成条件：**

- 性能结果可在同档设备重复；
- p95 与 GC 指标有原始数据文件；
- Coverage baseline 按稳定程序集名称记录；
- 缺少真实设备证据时，移动性能状态保持 not-verified。

### U-P1-11 静态状态没有覆盖关闭 Domain Reload 的情况

**对应任务：** 任务 4、任务 21、任务 23、任务 30。

**问题：** `EventBus`、`ResourceManager`、`UIManager`、`BagManager` 等持有静态状态。计划虽然提到部分 reset 和泄漏检查，但没有形成统一的 SubsystemRegistration、shutdown 和连续运行策略。关闭 Domain Reload 后，测试和下一次 PlayMode 可能继承旧订阅、句柄或失效 Unity 引用。

**修订要求：**

- 为静态入口定义 `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` 重置策略；
- 资源系统提供可等待的 `ShutdownAsync`；
- 测试提供受控的 `ResetForTests`；
- UI 销毁顺序固定为：取消加载 → 销毁实例 → 释放 descriptor lease → 清空 Instance；
- 连续运行两轮 PlayMode，并在关闭 Domain Reload 的配置下验证无残留。

**完成条件：**

- 连续两轮运行结果一致；
- 静态订阅、对象引用、Addressables handle 和 lease 均归零；
- 正常取消不会产生未观察异常或错误日志。

### U-P1-12 Monster 池需要分阶段恢复 NavMeshAgent

**对应任务：** 任务 28。

**问题：** `MonsterEntity` 依赖 `NavMeshAgent`。计划要求对象在 inactive 状态完成复位后再 `SetActive(true)`，方向正确，但 NavMeshAgent 的启用、投影和 `Warp` 需要考虑对象激活状态及是否位于 NavMesh。若顺序不明确，`OnEnable` 可能观察旧状态，AI 也可能在 agent 尚未落到 NavMesh 时开始运行。

**修订要求：**

1. inactive 阶段执行 `PrepareForSpawn`：清除订阅、状态效果、目标、计时器和旧数据，并设置 transform；
2. 激活对象；
3. 执行 NavMesh 投影、启用 agent 和 `Warp`；
4. 确认 `isOnNavMesh` 后，才注册 Physics、感知和 AI tick；
5. 任一步失败都清理并退回池。

**完成条件：**

- `OnEnable` 不读取上一次生命周期数据；
- AI 首帧前 `NavMeshAgent.isOnNavMesh == true`；
- 连续租出和归还不累积订阅、碰撞注册或状态效果；
- 投影失败的对象不会进入活跃怪物集合。

### U-P1-13 移动生命周期 Smoke 不完整

**对应任务：** 任务 23、任务 29、任务 33。

**修订要求：** 在 Android 设备验收中补充：

- 冷启动与热启动；
- 前台进入后台再恢复；
- Wi-Fi 与移动网络切换；
- 断网、重连和服务端断开；
- Safe Area、分辨率和方向变化；
- Android 返回键与 modal back stack；
- 低内存回调；
- 场景切换期间取消 Addressables 加载。

`SafeArea_Updates_On_Orientation_And_Resolution_Change` 应通过 `IScreenMetrics` 或等价接口注入屏幕参数，避免桌面 PlayMode 直接依赖难以稳定修改的 `Screen.safeArea`。

**完成条件：**

- 每条 smoke 都有自动结果或明确的人工验收证据；
- 崩溃、ANR、未观察异常和资源泄漏均为 0；
- 没有物理设备证据时，P1 发布状态不得标为通过。

## 5 修订与实施顺序

### 5.1 依赖主线

```text
任务 1
  ├─ 任务 4
  ├─ 任务 7
  ├─ 任务 12 → 任务 13
  └────────────────→ 任务 14 P0 Gate

任务 19 → 任务 20
任务 21 → 任务 22 → 任务 23 → 任务 29
任务 21 + 任务 25 → 任务 26
任务 19 + 任务 27 → 任务 28
任务 29 + 任务 30 → 任务 31 → 任务 33 P1 Gate
```

### 5.2 阶段 0 修订文档基线

在写实现代码前，先修改原 Spec 和实施计划：

1. 将本文 U-P0-01 至 U-P0-07 写入对应 P0 任务；
2. 将任务 13 前移为 P0，或明确 P0 Gate 不运行 KCP；
3. 把缺失的文件、asmdef、`.meta`、测试程序集和构建脚本加入文件清单；
4. 为每个 Gate 固定输入、命令、产物、失败条件和 not-verified 条件；
5. 将本文 U-P1-01 至 U-P1-13 写入对应 P1 任务；
6. 更新设计规格中的资源、消息泵、启动器和静态生命周期契约。

**阶段退出条件：** Spec 与计划不存在本文指出的架构矛盾，任务文件清单足以支持实现者完成工作而无需自行猜测关键设计。

### 5.3 阶段 1 建立可信 P0 Gate

1. 修订并实施任务 1：EditorQuality asmdef、原生 CLI runner、独立 XML、0-tests 检查、服务端编排；
2. 修订任务 4：可靠 EventSubscription 和无分配派发快照；
3. 修订任务 7：移除 Unity 主线程 `Thread.Sleep`；
4. 修订任务 12：control 消息优先 drain、连接 CTS、任务异常观察；
5. 将任务 13 前移并完成 KCP 生命周期修复，或替换 P0 transport；
6. 执行任务 14 P0 Gate。

**P0 Gate 必须证明：**

- 服务端全量测试通过；
- Unity EditMode、通用 PlayMode、Native PlayMode 各自被发现且通过；
- Native suite 可以从清洁环境自行启动和停止服务端；
- 登录响应不会被状态判断阻塞；
- Unity 主线程不存在测试延迟造成的阻塞；
- KCP 风险已修复或已从 P0 Gate 明确隔离；
- 所有结果由本轮 XML、日志和进程退出码证明。

### 5.4 阶段 2 收敛 P1 核心架构

1. 任务 19：先完成伤害接口迁移矩阵，再删除旧管线；
2. 任务 20：通过场景组合根完成 Sys3C 类型化依赖注入；
3. 任务 21：完成资源 backend、asset lease、async scene lease、shutdown 和静态 reset；
4. 任务 22：建立真实 Addressables content build 和 Use Existing Build 门禁；
5. 任务 23：重做 UIBootstrap 与失败 Presenter 接线；
6. 横向验证关闭 Domain Reload、取消、重复初始化与销毁顺序。

**阶段退出条件：** 编译、场景启动、资源加载、场景卸载和 UI 失败恢复均有可重复的生命周期测试，且没有静态状态与 Addressables handle 泄漏。

### 5.5 阶段 3 完成内容与性能改造

1. 任务 24、任务 27：建立可重复的性能测量方式；
2. 任务 25：完成局部 HitStop；
3. 任务 26：迁移真实 VFX 引用并实现 pool/lease 所有权；
4. 任务 28：按 Prepare → Activate → NavMesh → AI 的顺序实现 Monster 池；
5. 在目标设备档位上收集帧时间、GC 和内存证据。

**阶段退出条件：** 热路径性能数据可复现，场景退出后 VFX 与 Monster 资源归零，没有首次战斗加载尖峰或对象复用污染。

### 5.6 阶段 4 建立移动构建与 CI

1. 任务 29：使用进程级 `-buildTarget` 建立 Android/iOS 构建入口；
2. 增加 Android IL2CPP runtime smoke；
3. 任务 30：在架构守卫稳定后拆分 Bag/Stamina asmdef；
4. 任务 31：建立 CI、Coverage、Addressables Existing Build 和构建产物校验；
5. 明确 iOS runner 不存在时的 not-verified 流程；
6. 执行任务 33 P1 Gate。

**阶段退出条件：** Android 构建及设备 smoke 有证据；iOS 状态真实反映 runner 和 Xcode 验证程度；Coverage、Addressables、性能和移动生命周期结果均可追溯。

## 6 原计划任务修订映射

| 原任务 | 必须补充的内容 |
|---|---|
| 任务 1 | EditorQuality asmdef、原生 CLI runner、程序集过滤、独立 XML、服务端编排、0-tests 门禁 |
| 任务 4 | EventSubscription 共享幂等状态、copy-on-write 派发、SubsystemRegistration |
| 任务 7 | 删除主线程 Thread.Sleep，将网络延迟模拟移入测试 transport |
| 任务 12 | control 消息优先 drain、缺失类型文件、Host CTS、任务异常观察 |
| 任务 13 | 前移为 P0，或从 P0 suite 明确隔离 KCP |
| 任务 14 | 使用本轮原生 XML、服务端日志和独立 suite 结果作为证据 |
| 任务 19 | 完整伤害调用迁移矩阵、分阶段兼容适配、编译检查 |
| 任务 20 | 场景组合根、类型化玩家 Prefab 根、Camera 注入、真实场景测试 |
| 任务 21 | WebGL 兼容 await、async SceneLease、ShutdownAsync、静态 reset |
| 任务 22 | batchmode content build、Use Existing Build、Analyze、group/schema |
| 任务 23 | 可实现的 Presenter 注册或 MonoBehaviour bootstrap、双入口场景、移动生命周期 |
| 任务 24/27 | 可复现性能测试环境与原始数据，不依赖普通 Editor 零分配断言 |
| 任务 26 | 真实 Hit/Heal VFX 引用、AssetReference、预加载与 pool lease 所有权 |
| 任务 28 | 分阶段 NavMeshAgent 激活与失败回池协议 |
| 任务 29 | 进程级 build target、环境 preflight、产物校验、IL2CPP runtime smoke |
| 任务 30 | asmdef 显式引用、Domain Reload 关闭场景、架构守卫基线 |
| 任务 31 | Coverage 参数、Addressables 生产验证、runner availability、artifact 校验 |
| 任务 33 | 真实设备证据、移动生命周期 smoke、明确 not-verified 条件 |

## 7 最终验收清单

### 7.1 P0 完成清单

- [ ] EditorQuality 程序集结构可编译且可测试。
- [ ] Unity 测试进程内不执行 BuildTarget 切换。
- [ ] EditMode、通用 PlayMode、Native PlayMode 各自有 XML，且测试数大于 0。
- [ ] Native suite 自动启动、探测并停止测试服务器。
- [ ] 测试不依赖固定 `127.0.0.1:8000` 的残留进程。
- [ ] KCP 已修复或已从 P0 Gate 明确隔离。
- [ ] EventSubscription 重复和跨副本 Dispose 幂等。
- [ ] 生产移动上报路径没有 `Thread.Sleep`。
- [ ] LoggingIn 状态可以消费登录、错误和断线消息。
- [ ] Host 销毁时连接任务可取消且异常被观察。
- [ ] 任务 14 报告只引用本轮测试证据。

### 7.2 P1 完成清单

- [ ] 客户端只有一个运行时伤害请求模型。
- [ ] Sys3C 场景依赖由组合根注入，不依赖运行时查找。
- [ ] ResourceManager 玩家路径不使用 `.Task` 或 `WaitForCompletion()`。
- [ ] SceneLease 可等待地卸载并正确处理取消与 shutdown。
- [ ] UIBootstrap 与失败 Presenter 的接线符合 Unity 序列化规则。
- [ ] Addressables 在 Use Existing Build 模式下验证通过。
- [ ] Analyze 没有阻塞级重复依赖或 Resources/Addressables 重复打包。
- [ ] Hit 与 Heal VFX 使用真实资源、统一 pool 和明确 lease 所有权。
- [ ] Monster 复用时先完成 NavMesh 恢复，再启动 AI。
- [ ] Android 构建命令显式指定目标并产生可验证产物。
- [ ] Android IL2CPP 包完成设备或模拟器 runtime smoke。
- [ ] iOS 缺少 runner 时明确标为 not-verified，不无限排队。
- [ ] Coverage XML、程序集过滤和 baseline 口径固定。
- [ ] 性能 soak 固定设备档位、分辨率、Quality Level 和采样方法。
- [ ] 后台恢复、断网重连、Safe Area、返回键和低内存 smoke 有证据。
- [ ] 关闭 Domain Reload 后连续两轮运行无静态状态与资源泄漏。
- [ ] 无物理设备证据时，不宣称移动性能或发布准备已通过。

## 8 复核边界

本文审核的是设计规格和实施计划的 Unity 可执行性，不代表上述代码问题已经修复，也不代表 Android、iOS、Addressables content build 或真机性能已经验证。

仓库中未发现 `.Codex/docs/technical-preferences.md`，两份被审文档也没有引用 ADR，因此本次无法执行项目级 technical preferences 与 ADR 合规检查。后续若补充这些约束，应再次检查计划是否与其冲突。

## 9 相关文档

- [P0 P1 质量改进设计规格](../specs/2026-11-05-p0-p1-quality-improvement-design.md)
- [P0 P1 质量改进实施计划](../plans/2026-11-05-p0-p1-quality-improvement.md)
- [项目全局约定](../../../AGENTS.md)
- [跨模块架构指南](../../agent-architecture.md)
- [Agent Token 使用指南](../../agent-token-hygiene.md)
