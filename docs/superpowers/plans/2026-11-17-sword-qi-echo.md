# 剑气回荡实现计划

> **面向 AI 代理的工作者：** 使用 `executing-plans` 或 `subagent-driven-development` 逐任务实现。使用复选框记录验证进度。

**目标**　交付支持单机、PvE 和 PvP 的「剑气回荡」，使选敌、首次穿过、伤害与施法无敌在同一时间轴上运行。

**架构**　共享规则生成确定性的目标名单和首次接触时间表。服务端先选敌再接受施法，按逻辑 tick 提交命中及无敌状态。客户端使用确认后的实例播放动作和剑气，并通过现有 `DamageResult` 显示联机战斗结果。

**技术栈**　Unity 2022.3.25f1、C# 9 共享程序集、protobuf-net、.NET 8 服务端、NUnit 与 xUnit。

**规格**　[剑气回荡设计规格](../specs/2026-11-17-sword-qi-echo-design.md)

2026-11-18 审核修订，将五项审核意见分别落实到生命周期、请求状态、逐目标去重、路线和远端恢复的实现与回归步骤。

2026-11-18 二次审核修订：补齐既有回归更新、单机怪物注册表、服务端判定入口解耦、特性协商、生命与无敌状态存储、格挡方向、协议 tag 固定、客户端表现失效和数字验收。

## 全局约束

- 默认 `20006`、`12 m`、`8` 个、`2.0` 倍、`2 s`（15 Hz 下 `30 tick`）、`4` 道、穿过后 `0.75 m`、冷却 `12 s`。键盘 `4` 和移动技能栏共用输入通路。客户端 `SkillID.SwordQiEcho = 20006` 必须与资产、技能目录、DemoDay 技能栏和测试期望同步。
- 状态机、施法锁、无敌与动画共用结束时刻；Echo 的 Execution 必须忽略攻击动画完成回调，避免临时剪辑提前结束施法。允许普通移动和转向。确认等待的工程默认值为 `SwordQiEchoSkillData.ConfirmationTimeoutSeconds = 3`，资产可独立调节。
- 服务端权威状态新增 `PlayerState.CombatLifeId` 与 `[InvulnerableStartTick, InvulnerableEndTick)`；无窗口时两值相等。复活时自增生命代次并清空无敌窗口，死亡、离场、断线时清空。
- 开始每个任务前读对应模块 `README.md` 与 `.claude/docs/technical-preferences.md`（Claude 入口；`.codex` 入口仅 Codex 使用）。同一任务更新受影响模块 README，包括 `SyncCore/README.md`。共享层保持 C# 9、无 `UnityEngine`，协议只追加字段和消息。
- 各任务使用先红后绿的回归用例。服务端 `testhost` 遇到 `Win32Exception(5)` 时，用 `bash tools/test-server-inprocess.sh` 并保留原 `--filter`。既有回归 `MobileSkillHudTests`、`SkillPresentationCatalogTests` 必须随本次按钮/目录变更更新并加入命令。每项验证通过后可作为独立提交，按用户选择的执行与提交方式交接。

## 审查与回归覆盖

| 输入或失败模式 | 正确结果 | 任务 |
|---|---|---|
| 客户端看到敌人，服务端复核时已出圈 | 返回拒绝，本地无冷却、动画或无敌 | 2、3 |
| 旧选中对象在命中时已换命，或同 ID 重新入场 | 新生命不承接旧剑气，施法者离场时提前结束对应实例 | 2、4 |
| 一帧同时有 NPC 和 PvP 伤害，恰好处在无敌边界 | 起始 tick 免伤，结束 tick 可受伤，同 tick 顺序可复现 | 2 |
| 30 tick 内只命中一个敌人，多道剑气重复穿过 | 共用一个名单与一次命中结果，拖尾仍分向错时 | 1、3、4 |
| 重复请求、迟到的接受或旧服握手 | 最多一次正式施法，既有技能与旧协议正常工作 | 2、3、4 |
| 临时剪辑短于或长于 2 秒 | 技能在配置结束时刻完成，期间移动和转向可用 | 3 |
| 请求 A 超时，B 等待时收到 A 的拒绝 | 只结清 A 的状态，B 继续等待 | 3 |
| 旧绑定回包到达，或有效接受到达时正在执行另一动作 | 丢弃旧绑定结果，有效接受通过协调器切换到剩余权威窗口 | 3 |
| 同实例多个目标结果，其中一条重复 | 各目标都能消费，各自只触发一次 HP 和表现更新 | 2、3 |
| 空闲剑气提前经过尚未排到的目标 | 路线绕开该目标，首次穿过和排定接触一致 | 4 |
| 模型在施法期间禁用后重新启用，或切换 Full/Proxy | 重新绑定当场恢复剩余姿势及进度，保持原结束时刻 | 4 |
| 新增第 6 个技能按钮或目录条目 | `MobileSkillHudTests` 与 `SkillPresentationCatalogTests` 更新后仍绿 | 3、4 |
| 老客户端不支持 `SwordQiEcho`，或新客户端连老服务端 | 不发送 305，按钮与快捷键禁用，单机路径不受影响 | 2、3 |
| 目标在接触 tick 处于无敌或格挡，或施法者已移动 | 该次零伤害且不补排期，格挡方向仍按施法起点 | 2 |
| 单机需要枚举当前存活怪物 | 通过 `MonsterSpawner` 公开快照取候选，死亡/池中对象被过滤 | 3 |
| 动画完成回调早于 2 s 结束 | Echo Execution 忽略完成回调，仍在统一结束时刻完成 | 3、4 |
| 目标出圈或换命后的客户端表现 | 同一圆心重查，不画穿过，不追复活后的新生命 | 4 |

## 文件与职责

- `Assets/Scripts/Hotfix/GameSystems/SyncCore/SwordQiEchoRules.cs` 与 `CombatRules.cs` 提供配置、选敌和首次接触时序。`SwordQiEchoRules.Spec` 是独立静态配置；`CombatRules` 只为 `20006` 注册 `SkillEntry`（圆形、12 m、12 s、100 基础伤害、15% 暴击），测试守护 `10001 × 2.0` 的来源。
- `Server/Handler/SwordQiEchoCast.cs` 跟踪单次施法。`Server/Handler/CastHandler.cs` 在现有 `CombatResolver` 中接入接受、到期命中、共用伤害入口及消息扩播；必须抽出与 `PendingCast` 解耦的伤害结算入口，定义 Echo 与普通 pending 的同 tick 顺序，并在 20006 分支跳过 `ValidateShape` 与 `BroadcastSwingAsync`。`Server/Player/PlayerState.cs`（新增 `CombatLifeId` 与无敌窗口）、`Server/Server3C.cs`、`Server/Handler/LoginHandler.cs`、`Server/Monster/MonsterModule.cs` 和 `Server/Entity/EntityStore.cs` 提供生命、区域、能力与会话数据。
- `Assets/Scripts/AOT/Protocol/Messages/SwordQiEchoState.cs`、`Messages/Combat.cs`、`Messages/NpcAction.cs`（现有 `ProtocolFeatures` 定义）、`MessageId.cs`、`MessageCodec.cs` 提供追加协议和注册。协议 tag 与 feature bit 用 `ProtocolShapeGuardTests` 固定。
- `Assets/Scripts/Hotfix/GameSystems/Skills/Data/SwordQiEchoSkillData.cs` 提供资产类型，`Skills/Definition/SkillType.cs` 增加 `SkillID.SwordQiEcho = 20006`，`Skills/Runtime/SkillCoordinator.cs`、`Skills/Runtime/SkillExecutor.cs`、`Skills/Runtime/SkillStateMachine.cs`、`Skills/Runtime/SkillInterruptionMatrix.cs`、`Skills/Runtime/CooldownManager.cs`、`Sys3C/Sys3CEntry.cs` 与 `Sys3C/Input/InputManager.cs` 接通本地施法和伤害。请求归属与连接代次由 `ClientNetworkHost` 持有，`SkillCoordinator` 只发请求/提交事件，避免 Skills 直接依赖 SyncLayer。`UI/Mobile/MobileSkillButton.cs`、`MobileActionButtons.cs` 和 `MobileSkillBarPresenter.cs` 构建触控桥及冷却读数。`SyncLayer/CombatNetworkBridge.cs`、`SkillCastReporter.cs`、`ClientNetworkManager.cs` 和 `ClientNetworkHost.cs` 处理发送、主线程路由、逐目标结果去重与确认。
- `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterSpawner.cs` 暴露单机活跃怪物快照（`AliveMonsters` 或 `SnapshotAlive()`），供单机选敌与接触伤害使用；死亡、非激活和池中对象不得进入候选。
- `Assets/Scripts/Hotfix/GameSystems/VFX/SwordQiEchoPresenter.cs` 负责实例和剑气池，`SyncLayer/RemoteCastPresenter.cs` 接入远端动作，`SyncLayer/RemoteEntityRegistry.cs` 和 `SyncLayer/RemoteEntityAnimator.cs` 接入视图升档及重新启用后的恢复。动画、技能资产、角色预制件和 DemoDay HUD 在任务 3、4 列明。
- 测试同步修改：`Assets/Scripts/Tests/PlayMode/MobileSkillHudTests.cs`、`Assets/Scripts/Tests/Editor/Skills/SkillPresentationCatalogTests.cs`、`Assets/Scripts/Tests/Editor/Protocol/ProtocolShapeGuardTests.cs`。`MobileHudLayoutFitTests` 只覆盖 CharacterSelectPanel，不作为本次 HUD 布局测试。

## 任务 1　共享配置、选敌与时间表

**文件**　新建 `SyncCore/SwordQiEchoRules.cs`（位于上节 GameSystems 路径），扩展 `SyncCore/CombatRules.cs`，把 `HitResolver.SurfaceDistance` 暴露为 `internal`/`public` 共享方法。测试置于 `Assets/Scripts/Tests/Editor/SyncCore/SwordQiEchoRulesTests.cs` 与 `Server/Server.Tests/SyncCore/SwordQiEchoRulesTests.cs`（与既有共享规则测试同目录）。

- [ ] 写测试。`SelectNearest_MixedTargets_UsesSurfaceDistanceAndId` 使用 9 个有效候选与无效候选，断言只选最近 8 个，边界等距时 ID 小的优先。`BuildContacts_ThreeSwordsTenTargets_EachOnceBeforeEnd` 断言 10 个唯一目标使用 3 道剑气在 30 tick 内各安排一次，首轮目标互异；该用例只验证纯调度器，显式超过规格默认 `MaxTargets = 8`。`BuildContacts_TwoRounds_UsesExactTicks` 断言 4 道剑气、2 轮的精确 tick，固定公式为 `offsetTick = clamp(round((0.2 + roundIndex × 0.6 / max(1, rounds − 1)) × durationTicks), 1, durationTicks − 1)`（2 轮时为 20%/80%，4 轮时为 20%/40%/60%/80%）；对 1 s/2 s、1/2/4 轮、3/4/5 道剑气覆盖严格递增。`DefaultDamage_UsesBasicAttackBase` 断言 `50 × 2` 为 `100`。
- [ ] 运行 `bash tools/unity-tests.sh EditMode SwordQiEchoRulesTests` 和 `bash tools/test-server.sh --filter "FullyQualifiedName~SwordQiEchoRulesTests"`，确认新增规则的用例初次失败。
- [ ] 新建 `SwordQiEchoSpec`（放在 `SwordQiEchoRules` 内，独立于 `SkillEntry`），含 `Radius`、`MaxTargets`、`DamageMultiplier`、`DurationSeconds`、`SwordCount`、`OvershootMeters`。在 `CombatRules` 为 `20006` 注册圆形 `SkillEntry`：`ShapeType = 1`、`MaxAreaRadius = 12`、冷却 `12 s`、`BaseDamage = 100`、`CriticalRate = 0.15`；测试守护 `20006` 的基础伤害与暴击参数等于 `10001` 的对应值，不引入第二份可随手改的常量。`SwordQiEchoRules.SelectTargets(Vec3 origin, float radius, int limit, IReadOnlyList<CombatCandidate> eligible)` 返回按距离和 ID 排序的 `List<CombatCandidate>`。调用方在建候选时处理己方、区域、正式玩家和区域归属，`SelectTargets` 负责存活与几何过滤。复用已暴露的 `HitResolver.SurfaceDistance`，不复制公式。
- [ ] 实现 `SwordQiEchoRules.BuildContacts(Vec3 origin, IReadOnlyList<CombatCandidate> targets, int swordCount, uint durationTicks)`，返回 `List<SwordQiEchoContact>`。元素含 `TargetId`、`SwordIndex`、`OffsetTicks`、`Position`。将已入选名单按相对起点的极角排序，同角按表面距离与 ID 排序；目标与圆心重合时用 ID 兜底。第 `i` 个目标由第 `i % swordCount` 道剑气负责，第 `i / swordCount` 轮抵达。每轮按上面的唯一公式落在 20% 至 80%，抵达 tick 夹在 `[1, durationTicks − 1]`。校验配置能为同一道剑气上的不同目标分配严格递增的 tick；`swordCount ≤ 0`、`durationTicks < 2` 或无法分配时抛出明确异常，由调用方在配置校验阶段拦截。选敌名单锁定后才做极角重排。
- [ ] 重跑本任务两条命令，预期新增规则测试均通过，记录参数与共享程序集约束。更新 `SyncCore/README.md`。

## 任务 2　权威施法、无敌和协议

**文件**　按文件清单中的 Server 和协议路径实现。测试新建 `Server/Server.Tests/Combat/SwordQiEchoCombatTests.cs` 与 `Server/Server.Tests/Protocol/SwordQiEchoProtocolTests.cs`。客户端握手消费在任务 3。

- [ ] 写测试。`EmptyCast_DoesNotRecordCooldownOrInvulnerability` 先空放，再在下一 tick 加入有效怪物并成功接受。`MixedTargets_ResolveOnceAndDoNotReplaceInvalidTargets` 覆盖怪物、敌对玩家、跨区玩家、bot、同 ID 换命，逐目标断言同一次最多一个结果。`InvulnerableAtStartAndLastTick_ExpiresAtEnd` 以接受 tick 100 为起点，断言 tick 100 和 129 的 NPC、PvP 命中均没有 `HitEntry` 或 HP 变化，130 恢复扣血。`DuplicateRequest_CommitsOnce_AndDisconnectEndsCast` 验证重复接受只排一次，断线清除后续段和无敌。`TargetInvulnerableOrBlocked_SkipsContactAndDoesNotReschedule` 在目标接触 tick 置目标无敌或格挡，断言零伤害且后续回穿无补排期。`BlockDirection_UsesCastOrigin_WhenCasterMoves` 接受后移动施法者，断言格挡锥仍按接受时的圆心计算。`ZoneAndFeature_FilterCandidatesAndRecipients` 断言跨区和 bot 不进入候选；老客户端仍可成为目标并收到既有 `DamageResult`，但不接收 305。
- [ ] 写协议测试。`State_RoundTripsWithContactOrderAndTiming` 校验三种状态、10 个接触、`EchoRequestId`、`EchoBindingId` 与施法身份字段往返。`DamageResults_ShareCastIdButKeepDistinctTargets` 校验同实例不同目标分批序列化后仍保留各自目标 ID 和同一 `EchoCastId`。`LegacyCombat_WithoutEchoFields_ReadsDefaults` 校验旧请求、旧结果与无能力握手。`EchoTagsAndFeatureBit_ArePinned` 在 `ProtocolShapeGuardTests` 固定 `CastRequest` tag 8/9、`DamageResult` tag 9 与 `ProtocolFeatures.SwordQiEcho = 1u << 2`。
- [ ] 运行 `bash tools/test-server.sh --filter "FullyQualifiedName~SwordQiEcho"`，确认新增用例初次失败。
- [ ] 在 `CombatResolver.Decide` 引入 `20006` 专用分支，复用注册、存活、冷却检查；**跳过 `ValidateShape`**（客户端新数据类型发零形状，服务端只用共享半径和服务端名单）。空名单返回 `no-target`，通过后才 `RecordCast`。`HandleCastAsync` 在 20006 分支不得调用 `BroadcastSwingAsync`，改走 `SwordQiEchoState`。为在线角色沿登录接线把服务器采用的 `ZoneId` 写入权威 `EntityStore`（在 `RegisterEntity` 前计算 zoneId 或通过参数传入，避免写进 RoomManager 的另一个 `PlayerState` 实例），当前地图生成的 NPC 明确归属 `Global` 战斗区。按已登记区域建候选并过滤非正式玩家，发送时再按同区和已协商能力筛收件人。拒绝施法者在窗口内提交新主动施法，已提交的其他攻击保留当时行为。
- [ ] 把 `CombatResolver.ApplyHit` 从 `PendingCast` 解耦：抽出显式接收施法者、`SkillEntry`/伤害快照、目标、固定起点和 `CastResolution` 的共享结算入口，普通 PvP/PvE 与本技能共用；`DrainResolved` 同时排空普通 `_pending` 与 Echo 施法列表，顺序固定为“普通 pending 在前、Echo 在后”，由测试锁定同 tick 可复现。`SwordQiEchoCast` 保存施法者身份、固定圆心、基础轻击伤害及暴击参数快照、`StartTick`、`EndTick`、单调 `CastId` 和带生命身份的接触。用 `PlayerState` 对象身份与增量 `CombatLifeId`（每次复活递增）识别入选生命；`ApplyHit` 在掷骰、格挡、扣血或发受击之前检查目标在同一生命下的有效无敌窗口，普通 PvE/PvP 与本技能共用这道检查。Echo 结算把固定圆心作为 `attackerPos` 传给格挡判断。正常到期、离场、断线及换命清掉对应实例，并接入 `UnregisterEntity`/断线路径。普通 `CancelCastRequest` 的旋转语义不适用于本技能。
- [ ] 追加 `MessageId.SwordQiEchoState = 305` 和 `ProtocolFeatures.SwordQiEcho = 1u << 2`，在 `MessageTypeRegistry` 注册。用 `LoginRequest.SupportedFeatures` 和 `LoginResponse.EnabledFeatures` 协商：服务端新增 `PlayerState.Features`（uint，保存协商位），`LoginHandler` 计算 `zoneId` 与 `features`，`RegisterEntity` 接收 `features` 写入该字段，并把 `request.SupportedFeatures & (ProtocolFeatures.AuthoritativeRespawn | ProtocolFeatures.SwordQiEcho)` 写入 `LoginResponse.EnabledFeatures`；客户端登录请求追加该位，任务 3 保存 `EnabledFeatures` 并用它限制按钮与快捷键，服务端按 `PlayerState.Features` 筛收件人。`CastRequest` 追加 tag 8 `EchoRequestId` 和 tag 9 `EchoBindingId`，均为 `ulong`。`DamageResult` 追加 tag 9 `EchoCastId`（`ulong`）。新 `SwordQiEchoState` 以追加标签携带 `Phase`（`Rejected=1, Started=2, Ended=3`）、`CasterId`、`CastId`、`RequestId`、`EchoBindingId`、`RejectReason`、`StartTick`、`EndTick`、服务器起始时间戳、`CooldownTicks`、圆心 XYZ、朝向、半径、剑气数、穿越距及 `IList<SwordQiEchoContactMessage>`。服务端回显请求的绑定代次，角色和生命的权威校验仍由服务端实体状态负责。接触消息携带目标 ID、剑气编号、相对 tick 和初始 XYZ。每次命中通过通用 `DamageResult` 发权威 HP 与受击，并携带同一 `EchoCastId`。
- [ ] 在 `CastHandler` 按会话缓存最近请求序号及决定。重复序号只向请求者回放决定，较旧序号拒绝且不改变状态。每个返回携带原 `RequestId` 和 `EchoBindingId`，施法已结束时重放 `Ended`。开始事件可靠发送给施法者、名单内玩家及双方同区 AOI 观察者；只向已协商 `SwordQiEcho` 的客户端发送 305。提前结束按同一 `CastId` 发给仍在线的已知收件人。消息发送复用现有队列，结算在逻辑 tick 提交。
- [ ] 重跑 `bash tools/test-server.sh --filter "FullyQualifiedName~SwordQiEcho|FullyQualifiedName~CombatResolver|FullyQualifiedName~CastResolutionScheduling"`，预期回环、新裁决、原投射物和旋转回归均通过。更新 Server、Monster（若生成接线变化）、Protocol 和 SyncCore README。

## 任务 3　本地入口、单机施法及联网确认

**文件**　按文件清单中的 Skills、Sys3C、SyncLayer、Monster 和 UI 路径实现。新增 `Assets/PreRes/SkillsCfg/SwordQiEcho_Skill4.asset`，注册到 `Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab` 和 `Assets/Prefabs/Character/FemalePlayer.prefab`。触控布局接入 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 中的 `MobileActionButtons`；`SkillID.SwordQiEcho = 20006` 加入 `Skills/Definition/SkillType.cs`，并同步 `MobileSkillButton`、`MobileActionButtons` 与 `InputManager`。新增 `Assets/Scripts/Tests/Editor/SwordQiEchoSkillTests.cs`，扩展 `MobileSkillInputBridgeTests` 与 `MobileSkillBarPresenterTests`；更新 `MobileSkillHudTests`（6 个技能按钮）。`SkillPresentationCatalogTests`（目录 8 个技能）随任务 4 的目录资产一起更新。`MobileHudLayoutFitTests` 只覆盖 CharacterSelectPanel，本次不作为 HUD 布局测试。

- [ ] 写测试。`EmptyTarget_OfflineDoesNotStartCooldown` 验证没目标后加怪可立即释放。`AuthorityReject_DoesNotTriggerActivation` 模拟本地有目标但服务端拒绝。`AuthorityStart_DedupesAndUsesRemainingTime` 重发同 `CastId`，断言只启动一次，迟到开始只播放剩余部分。`OfflineHit_UsesSameMultiplier_AndSkipsInvalidTarget` 固定非暴击，断言扣 100 HP、越界目标零次、回穿无第二笔。`OfflineInvulnerability_ExpiresAtCastEnd` 覆盖单机无敌起止边界。还要覆盖旧服禁用联网入口、失焦/解绑清输入、权威冷却读数、`MobileSkillHudTests` 的 6 个技能按钮，以及新增按钮在现有布局内。
- [ ] 补生命周期测试。`ExecutionDeadline_IgnoresTemporaryClipLength` 分别使用 0.5 s 和 3 s 剪辑，断言 2 s 前 `CurrentSkill.IsActive` 为真且新施法被锁，到 2 s 完成。`EchoExecution_AllowsMovementAndRotation` 断言移动输入保持技能活动，`CanMove()` 和 `CanRotate()` 为真。`LateStart_UsesOriginalEndTime_ExpiredStartOnlyAlignsCooldown` 覆盖剩余 0.5 s 与窗口已结束的确认，断言原结束时刻不顺延、过期确认仅更新剩余冷却。
- [ ] 补请求与消费测试。`PendingRequest_BlocksOtherCastsUntilDecisionOrTimeout` 验证等待时主动施法被锁，3 s 超时释放。`OldReject_DoesNotClearNewPendingRequest` 按 A 超时、B 发出、A 拒绝、B 接受的顺序断言各状态归属。`OldBindingOrConnectionStart_IsIgnored` 验证角色换命/重绑或连接改变后丢弃旧确认。`LateAcceptedStart_ReconcilesConflictingLocalSkill` 验证超时后启动另一动作，迟到有效接受通过协调器结束冲突动作，并执行剩余窗口。`DamageResults_DeduplicatePerCastAndTarget` 向同实例先投递 A、再投递 B、再重复 A，断言两个目标各更新一次 HP、受击、飘字与火花。重复在视觉结束后到达也保持该断言。
- [ ] 补接受乱序测试。`OlderAcceptedCast_DoesNotOverwriteNewerCooldownOrExecution` 先应用较新的接受，再投递旧实例的迟到接受，断言当前执行器、结束时刻、冷却总时长和剩余时长保持较新值，旧请求被结清。
- [ ] 运行 `bash tools/unity-tests.sh EditMode SwordQiEchoSkillTests`，确认新增用例初次失败。
- [ ] 新建 `SwordQiEchoSkillData : SkillData`（`_skillType = Instant`、`_canBeInterruptedByMovement = false`、`_confirmationTimeoutSeconds = 3`）并注册资产。`SkillCoordinator` 增加请求/提交两阶段入口，普通技能继续走原 `TryActivateSkill`；协调器只发出 `EchoRequested`/`EchoCommitted` 事件，**不直接依赖 `CombatNetworkBridge`**。`ClientNetworkHost` 在联网装配时订阅事件并持有带 `EchoRequestId`、`EchoBindingId` 和接收连接代次的归属记录，通过 `SkillCastReporter`、`CombatNetworkBridge` 发送；单机时无人订阅，协调器走本地提交。联网请求前先按现有切换条件结束需要取消的持续动作，并沿现有入口上报取消。`EchoRequestId` 在同一连接中持续递增，角色重绑递增 `EchoBindingId`。待确认期间锁新主动施法，收到匹配拒绝时释放，资产中的超时值也释放。超时保留归属记录，较旧拒绝只结清自身记录。角色解绑或换命清该绑定记录，断线清连接记录，回调处理前校验捕获的连接代次。
- [ ] 处理有效 `Started` 时以 `(StartTick, CastId)` 与本绑定最近一次已应用接受比较，只有较新的实例更新当前执行器和权威冷却，旧接受只结清自身请求。仅剩余窗口大于零时提交技能。若超时后另一动作已经本地开始，通过协调器现有结束/取消入口清理其执行器、相关输入和必要的持续动作上报，再切入本次剩余窗口。独立且已提交的投射物按原规则运行。已过期接受只校准冷却，`Ended` 锁存到该实例，后来的同实例 `Started` 保持结束。`SkillCastReporter.ReportActivation` 跳过 `20006`。用握手结果限制按钮与快捷键。
- [ ] 在 `SkillStateMachine.cs` 增加 `StartEcho(double endTimeSeconds, Func<double> nowSeconds)`，由协调器在正式提交时通过执行器调用。单机结束时刻由本地单调时钟和配置时长生成，联网结束时刻由权威起始时间及 `EndTick − StartTick` 换算，时钟读取现有同步服务器时钟。该执行分支按结束时刻保持活动，到点直接完成，动画剪辑长度只用于表现；在 `SkillCoordinator.IsAnimationCompletionIgnored()` 中把 Echo 的 Execution 纳入忽略，确保攻击动画完成回调不会提前清理技能。在 `SkillCoordinator.CanMove()`、`CanRotate()` 和打断规则中显式支持本技能，移动不打断，窗口内新主动施法不进入缓冲补放，角色清理仍结束该实例。为 `CooldownManager` 增加 `StartCooldown(int skillId, float totalDuration, float elapsedSeconds)` 重载，确认时传入原冷却总时长 12 s 与权威已过时间，原双参数调用等价于已过时间 0。测试迟到 2 s 时 `Total = 12`、`Remaining = 10`。
- [ ] 在 `CombatNetworkBridge` 的 HP 与表现消费之前，以 `(连接代次, EchoCastId, TargetPlayerId)` 过滤重复命中条目，再将该批剩余条目交给现有链路。`EchoCastId = 0` 继续走原技能语义。同实例不同目标全部消费，施法视觉结束后继续保留有界去重窗口和淘汰水位（首版 2 s 或 64 个 `EchoCastId`），重复旧结果直接丢弃。`ClientNetworkManager` 订阅 `MessageId.SwordQiEchoState` 并像 `CastBroadcast` 一样只入主线程队列；`ClientNetworkHost` 在主线程消费并驱动确认、冷却和表现。请求归属记录同样使用有界窗口，角色与连接清理同步清对应记录。
- [ ] 单机通过 `MonsterSpawner.AliveMonsters`（或等价的 `SnapshotAlive()`）取当前存活怪物候选，过滤死亡/非激活/池中对象后复用 `SelectTargets` 和 `BuildContacts`。在 `SkillExecutor` 中按接触点复核并走现有 `IDamageable`，从已注册 `10001` 数据取基准和暴击参数；施法后移动不改变固定圆心。单机无敌检查放在 `Sys3CEntry` 本地受击入口、减伤与 HitFSM 前。联机 HP 消费继续走 `CombatNetworkBridge` 原路径，确认消息只推动动作、冷却与表现，迟到的权威 HP 照常落地。按 `InputManager` → `Sys3CEntry` 接键盘 `4`，按 `MobileActionButtons` → `MobileInputBinder` → `InputManager` 接触控；`MobileSkillButton` 增加 Echo 的按下 case，`MobileActionButtons` 增加对应标志并在 `ConsumeSkillPresses`/`ClearCombatInput` 清理。冷却遮罩从 `SkillCoordinator.GetCooldownInfo(20006)` 读取。
- [ ] 运行 `bash tools/unity-tests.sh EditMode SwordQiEchoSkillTests`、`bash tools/unity-tests.sh EditMode MobileSkillInputBridgeTests`、`bash tools/unity-tests.sh EditMode MobileSkillBarPresenterTests`、`bash tools/unity-tests.sh PlayMode MobileSkillHudTests`。预期新技能、旧技能触控、6 个技能按钮和布局测试通过。更新 Skills、Sys3C、Monster、UI、SyncLayer README。

## 任务 4　剑气路线、动画与联调

**文件**　新增 `Assets/Scripts/Hotfix/GameSystems/VFX/SwordQiEchoPresenter.cs` 与 `Assets/PreRes/SkillsCfg/SwordQiEchoWhite.prefab`，修改 `Assets/RpgDuo/Animator/Character3C.controller`、`Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset` 以及 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RemoteCastPresenter.cs` 对应接线。新增 `Assets/Scripts/Tests/PlayMode/SwordQiEchoPresentationTests.cs`；同步更新 `Assets/Scripts/Tests/Editor/Skills/SkillPresentationCatalogTests.cs`（目录 8 个技能）。

- [ ] 写测试。`OneTarget_FourTrailsWithDistinctPasses_OneDamageResult` 验证 4 条不同角度的轨迹和一笔已确认伤害。`EightTargets_AllScheduledContactsReachedBeforeEnd` 验证分散目标按时覆盖。`LateStartEndDetach_AlwaysReturnsTrailsToPool` 覆盖缺目标视图用位置快照、晚到确认跳到当前进度、重复状态、提前结束及解绑清理。断言独立动画状态使用 `defend` 剪辑且防御状态值与精力保持原输入语义。
- [ ] 补路线和恢复测试。`FirstPass_MatchesScheduledContact_ForAllSwords` 覆盖单目标四剑气，以及路线经过后续待命中目标的情况，逐帧断言所有剑气的最早穿过与排定接触相差至多一个渲染帧，后续回穿无额外伤害。`TierChangeAndReenable_ResumeRemainingCast` 在开始 0.75 s 后禁用/启用模型并切换 Full/Proxy，断言重绑定同帧恢复施法姿势，结束时刻仍为 2 s、结算次数保持一次，结束后重新升档保持待机。
- [ ] 补失效与性能测试。`OutOfRangeOrDead_ClientsSkipPass` 断言目标在接触 tick 出圈或观察到死亡边沿时不再画穿过，且不追复活后的新生命；跨 AOI 换命的生命代次校验若需要，后续再扩展 `EntitySnapshot` 生命代次（backlog）。`PoolAfterCast_ReturnsAllTrails_NoSteadyAllocation` 预热后连续施法，断言剑气/拖尾全部回池，记录稳定帧分配基线（首版目标 0 B/帧；实机不达标则记录偏差并收紧池策略），并记录池容量与最大活跃拖尾数。
- [ ] 运行 `bash tools/unity-tests.sh PlayMode SwordQiEchoPresentationTests`，确认新增用例初次失败。
- [ ] 在 Attack 层加入名为 `SwordQiEcho` 的状态，引用原 `Defend` 剪辑，根据确认窗口缩放或保持最后姿势，到期回 `AttackIdle`；确认该状态名不触发既有攻击动画完成回调，且 Echo Execution 已在 `IsAnimationCompletionIgnored()` 中忽略完成回调。从同一实例数据启动本地与远端 `SwordQiEchoPresenter`。通过现有 `ComponentPool` 预热可复用剑气，关闭碰撞体，依据起点方位和剑气编号偏移出发和进场角。每条剑气按已分配接触 tick 经过目标，在目标视图可用且仍有效时追踪当前位置，暂不可见时沿下发快照接续；接触 tick 到达时用消息中的圆心/半径重查视图位置，出圈、死亡或离场的视图不画穿过，观察到死亡边沿后不追复活后的新生命。可确认目标失效时，剑气转向本路线下一个有效目标或进入回旋，保留后续接触 tick。离开目标沿切线飞 0.75 m 后平滑换向。生成及更新全部路线时，使用尚未到接触时刻的目标受击体积作为绕行约束，只有负责剑气能在排定时刻首次穿过。空闲剑气先在两侧回旋，已完成接触的目标允许错时回穿。命中火花和飘字从去重后的权威结果消费。
- [ ] 活跃实例由 `ClientNetworkHost` 持有的远端表现宿主管理，独立于 Full/Proxy 模型根节点。`RemoteEntityRegistry` 在模型重新启用/升档的常规 `Reapply()` 之后，同帧通知 `RemoteCastPresenter` 重新绑定。`RemoteEntityAnimator` 接收该实例原结束时刻及当前进度，恢复攻击层权重、技能姿势和剩余计时，剑气沿同一实例恢复。重新启用只恢复表现。角色解绑、死亡、离场及 `Ended` 清宿主实例并回收拖尾，正常到期淡出，已结束实例重新升档保持待机。
- [ ] 运行 `bash tools/unity-tests.sh PlayMode SwordQiEchoPresentationTests`、`bash tools/unity-tests.sh PlayMode CombatSyncTests`、`bash tools/unity-tests.sh PlayMode HitJudgmentAlignmentTests`、`bash tools/unity-tests.sh PlayMode MobileSkillHudTests`、`bash tools/unity-tests.sh EditMode SkillPresentationCatalogTests`，再运行 `bash tools/test-server.sh`。预期新增、6 按钮 HUD、8 技能目录和现有战斗回归通过。
- [ ] 在 `DemoDay` 以触控和键盘各试一次。至少用施法者、受击者、旁观者三个视角复核零目标、单目标、九目标、目标出圈、两秒无敌边界和离场。记录视角、目标 ID、结算次数、时间、截图、池容量、最大活跃拖尾数和预热后稳定帧分配基线。检查受击、格挡与旋转表现，验证移动场景中剑气回收后无持续分配。更新 VFX 和 SyncLayer README，并记录实机表现与任何联调偏差。
