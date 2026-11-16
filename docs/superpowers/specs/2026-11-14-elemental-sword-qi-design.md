# 属性剑气设计

日期 2026-11-14

设计草案，已补联机一致性审核。2026-11-14 修订：补齐施法取消、状态投影、DoT 表现分流、`CastId`/攻速下发、PvP 位移预算输入与同 tick 死亡边界。2026-11-14 复审修订：怪物动作阶段接通 `NpcAction` 全链；`CombatProjection` 收敛为 `CombatStateUpdate` 单一变更通道；跳跃采用客户端起跳 + 服务端观察；麻痹保留已有空中水平惯性。2026-11-14 full 复审修订：B1 动作合并键改为 `(EntityId, LifeId, InstanceId, Revision)` 并补客户端动作存储与 `CancelAttack(CastId)`；B2 优化了血量与状态投影的发送频率；B5 预算改为分项公式并只信服务端接收 tick；PvP HP 调整为 720，PvP 控制规则包生效；元素改为同次施法首个有效命中附加；攻速使用攻击 state 的 `AttackSpeed` 参数。玩家可在交战间隙换剑，预先选用减速、持续伤害、麻痹或易伤，并通过剑气外观识别本次攻击的属性。武器模板固定元素，入场按表发放可换武器，会话内保留。属性状态覆盖怪物和同战斗场景的敌对玩家。武器基础攻击力和攻速作用于普攻，剑气有独立伤害倍率。

## 实施状态（2026-11-17）

| 范围 | 状态 |
|---|---|
| Plan 01：共享表、权威换装、普攻参数、`CastId`、发射/停点、场景隔离 | ✅ 已完成（服务端 512/512、Unity EditMode/PlayMode 相关测试通过） |
| Plan 02：取消广播/NpcAction、战斗投影/带宽 | ✅ 已完成（Part A/B 全部完成；服务端 552/552、Unity 相关测试通过） |
| Plan 03：位移/跳跃预算、PvP 控制 | ❌ 未开始 |
| Plan 04：状态结算/DoT/元素 VFX | ❌ 未开始 |
| Plan 05：三客户端验收/表现收口 | ❌ 未开始 |
| 简易武器背包与八把持剑模型（2026-11-16 跟进） | ✅ 代码与自动测试完成；视觉/交互/出包验收待人工 |

下文“验收场景”是 01–05 的完整目标，不是当前已全部验证的结果。

## 玩法规则

持有元素武器时，普攻第二段 `10002` 的整次施法携带该武器的元素。同次施法对每个目标只在首个有效命中时附加元素状态：近身挥砍先命中则由挥砍附加，剑气先命中则由剑气附加；后续命中只结算伤害，不重复附加。同次普攻的近身挥砍仍单独结算。同一目标在同一 tick 已被前一段伤害打死时，后续段不再产生伤害事件，也不附加状态——死亡优先于同 tick 的后续命中。格挡吸收剑气时保留原有减伤和停点。被挡的剑气不会给格挡者或其后方目标新附加元素，之前由独立的有效挥砍施加的状态仍有效。

| 元素 | 命中后的效果 | 配置字段 |
|---|---|---|
| 无 | 普通剑气表现，无元素附加状态 | 武器表 `ElementId = 0` |
| 冰 | 目标移动速度 × `(1 - SlowRatio)` | `DurationSeconds`、`SlowRatio` |
| 火 | 每到一个间隔扣一次血，使用施加时解析的每跳伤害 | `DurationSeconds`、`IntervalSeconds`、`DamagePerTick` |
| 雷 | 目标无法主动移动或开启新施法。首次施加时打断可打断的动作，已经发射的独立投射物继续飞行。霸体只免除打断：怪物现有前摇霸体按首版规则直接跳过麻痹状态；玩家技能霸体仍会被挂上麻痹，当前动作播完后再受移动/施法锁 | `DurationSeconds` |
| 风 | 状态生效后，目标从任意来源承受的战斗伤害乘倍率；PvE 包含灼烧，PvP 不吃灼烧 | `DurationSeconds`、`IncomingDamageMultiplier`，PvE 初始 `2.0`，PvP 首版 `1.25–1.3` |

首轮联调建议值为冰减速 30% 持续 2 秒，火持续 3 秒、每秒 8 点，雷麻痹 PvE 0.6 秒 / PvP 0.8 秒，风易伤 PvE 2 倍 / PvP 1.25–1.3、持续 1.5 秒。PvP 控制规则包：控制结束 0.5 秒免疫，同组易伤 4 秒内不刷新。数值通过配置调节。

风在同次施法首个有效命中结算后附加；该次施法后续段（含剑气）读取新倍率；目标已经易伤时沿用已有倍率。PvP 首版风不放大灼烧。倍率作用于现有暴击和减伤计算之后、扣血之前。灼烧按每跳记伤害和余血，不进入受击硬直、不触发命中特效；客户端只按 `DamageKind=Dot` 更新血量与飘字。直接命中仍驱动原有受击反应。冰影响的是实际移动速度，施法节奏和冷却按原计时运行。雷首版沿用现有前摇霸体规则，命中仍有霸体的怪物时跳过麻痹状态；玩家技能霸体只免除打断，麻痹状态仍会挂上，当前动作播完后再锁移动/施法。无霸体时取消尚未提交的前摇，清理对应待判定段，保持已提交的攻击与自然恢复流程。麻痹限制主动行为，死亡优先，击退等外部位移沿用各自规则。同 tick 顺序固定为：解除到期修饰 → 结算灼烧跳 → 处理输入 → 命中段；风在 `EndTick` 到期后不再放大同一 tick 的灼烧最后一跳，同 tick 新施加的风只影响其后的直接命中。

同一目标的同组状态最多一条，不同组可共存。首版同组重施使用最近一次有效施加的参数刷新结束时间，灼烧仇恨来源保留首次施加者，避免低伤重施抢仇恨；客户端保持正在播放的循环特效，不重启。灼烧下一跳取原下一跳与按新间隔算出的下一跳中较早的一个，避免连续命中推迟伤害。首次灼烧从第一个间隔起结算，恰好在到期 tick 的那一跳先结算再移除状态。目标死亡、Evade 到家重置、离开战斗场景或回池时移除状态；Evade 途中保留状态但不再产生仇恨。武器切换后，在途剑气和已挂状态保留原参数。已挂的灼烧靠快照结算，来源离场时仍能结算伤害，仇恨沿用服务端现有有效攻击者校验；每跳按 `damage × ThreatPerDamage` 计仇恨，不叠加 `ThreatFlat`，避免每跳固定 +10 抢仇恨。

首版采用自由 PvP。玩家基础 HP 调整为 720；按近身挥砍 + 剑气约 180 计，TTK 约 4 次交手，首轮实测后在 720–1080 区间调。怪物伤害、治疗和 HUD 按同一基准校准，保持现有 PvE TTK。服务器按施法者的战斗场景筛选剑气候选，场景内的其他正式登录玩家均视为敌对，怪物须归属同一战斗场景。场景使用服务端房间或地图实例标识，不能只按坐标或能否收到 AOI 广播判定。测试 bot 保留现有压测基础伤害路径；首版压测前至少接收 `SpeedScale`/麻痹/DoT，以覆盖位移预算与跳伤，完整元素特效可后置。单机模式只有怪物目标。

## 配置与运行时数据

| 数据 | 字段与职责 |
|---|---|
| 武器模板表 `WeaponDefinition` | `TemplateId` 对应背包模板，配置 `WeaponViewKey`、固定 `ElementId`、`BaseAttack`、`AttackSpeedMultiplier`、`SwordQiDamageMultiplier`。两个倍率默认 `1.0`，攻击力以当前普攻基线的默认武器为基准。首版换同类剑，技能仍提供动作、基础时序、命中形状和剑气飞行几何。 |
| 元素表 `ElementDefinition` | 稳定 `ElementId`、剑气表现键、状态定义 ID 列表。首版每个元素指向一个状态，后续可为新元素组合已有状态。 |
| 状态表 `StatusEffectDefinition` | 稳定 `EffectId`、`EffectKind`、`StackGroupId`、`DurationSeconds`、表现键和按类型校验的参数。首版参数分别为移速比例、灼烧每跳伤害与间隔、麻痹、受伤倍率。 |
| 武器实例 `WeaponInstance` | `InstanceId`、`TemplateId`，元素由模板查询。如果以后引入随机词条，可用可空的实例覆写字段扩展，空表示继续使用模板，显式 `0` 表示无元素。 |
| 装备状态 | 每个角色一个武器槽，保存已装备实例 ID 和递增版本。集成背包行的装备交互与 3C 武器槽。联机换装只选择实例 ID，并带请求序号。服务端核对归属、槽位和是否可装备，回传确认后的版本与武器摘要。 |
| 施法快照与活动状态 | 普攻记录 `CastId`、来源、接受施法时的武器实例、装备版本、解析后的攻击力和攻速，剑气额外带元素 ID 与解析后的元素参数。活动状态记录目标、来源、效果 ID、组、结束 tick、下一跳 tick 与参数。协议和运行时均使用稳定 ID，Unity 资源引用留在表现目录。 |

武器模板和状态数值建议沿用 `SyncCore/Npc` 已有的共享配置模式，用纯 C# DTO 与同一份 JSON 数据供服务端和离线客户端读取。技能位移参数（`DashDistance`、`DashDuration`、起手/收尾窗口）也进入共享表，服务端用同一份数据计算位移预算；构建校验 `DashDistance > 0`、`DashDuration > 起手 + 收尾`、`DashDistance / (DashDuration - 窗口)` 有限且在合理上限内。跳跃参数（`JumpForce`、`Gravity`、上升时长上限）同样共享给服务端做轨迹观察。PvP 战斗参数（玩家 `MaxHp`、风易伤 PvP 倍率、控制结束免疫 0.5 秒、同组易伤 4 秒不刷新、雷 PvP 0.8 秒）也进共享表。首版初始武器列表也用表配置，显式指定默认剑与可试用的冰、火、雷、风模板。表现键在 Unity 端映射到 Addressables 资源。构建校验重复 ID、缺失引用、非有限值；`BaseAttack > 0`、`AttackSpeedMultiplier ∈ [0.25, 4]`、`SwordQiDamageMultiplier > 0`、`0 < SlowRatio < 1`、`IncomingDamageMultiplier > 0`；持续时间/间隔向上取整后 ∈ [1, 上限]，灼烧 `DurationTicks ≥ IntervalTicks`；tick 加法饱和，禁止 `(uint)` 回绕。灼烧持续时间至少容纳一跳。`DurationSeconds` 和 `IntervalSeconds` 在权威时钟上统一向上取整，使用环绕安全的 tick 比较。后续新增一个复用现有机制的元素主要填表、挂资源。新增一种结算机制时，在状态模块加一种参数类型与处理规则。

### 下行消息与战斗投影

首版新增/扩展以下下行消息；全部走可靠通道，收件人为实体本人、受击者与能看见该实体的观察者：

| 消息 | 关键字段 | 用途 |
|---|---|---|
| `CastBroadcast` | `CastId`、`SkillId`、`CasterPlayerId`、`CasterLifeId`、`AttackSpeedMultiplier`、`ElementId`、`NpcAction?` | 开始施法动作；所有施法共享服务端 `CastId` 分配器，远端按 `CastId` 建唯一实例，按攻速倍率播放动画 |
| `CastConfirm` | `ClientCastSeq`、`Accepted`、`RejectReason`、`CastId`、`SkillId`、`CasterLifeId`、`EquipmentRevision`、`AttackSpeedMultiplier`、`AcceptedTick` | 给本人确认或拒绝施法。按请求序号幂等重发，与观察者动作广播分开消费 |
| `CastCancelBroadcast` | `CastId`、`CasterLifeId`、`EffectiveTick`、`Reason` | 麻痹/死亡/离场取消尚未提交的前摇或持续施法；`Reason` 复用 `NpcActionCancelReason`（麻痹=Hit、死亡=Death、强制/离场=Forced、目标无效=TargetInvalid）；客户端按 `CastId` 停止动作与未发射剑气 |
| `NpcActionEvent` | `EntityId`、`NpcActionSnapshot`、`ServerTimestamp` | 怪物动作阶段广播（Accepted/Windup/Active/Recovery/Completed/Cancelled）；客户端按 `(InstanceId, Revision)` 合并（仅同实例比较 Revision），与 AOI 基线 `NpcAction` 同源 |
| `ProjectileLaunchBroadcast` | `CastId`、`CasterPlayerId`、`CasterLifeId`、`CombatSceneId`、`LaunchTick`、`OriginX/Y/Z`（仅 XZ 参与判定）、`Yaw`、`Speed`、`Range`、`ElementId` | 剑气权威发射描述；新进 AOI 补当前进度 |
| `ProjectileStopBroadcast` | `CastId`、`CasterLifeId`、`CombatSceneId`、`StopTick`、`Reason`、`StopX/Z` | 格挡/死亡/离场提前终止；正常到射程可由发射描述推导。本人与远端用同一消费路径 |
| `CombatStateUpdate` | `EntityId`、`CombatProjection`、`ServerTimestamp` | 状态/血量/死亡/装备/控制/当前施法的可靠完整投影；状态变更的唯一来源 |
| `DamageResult` / `HitEntry` | 增加 `CastId`、`EventSeq`、`EventId`、`DamageKind`、`TargetLifeId`、`CombatVersion` | 每次直接命中与灼烧跳数一条稳定事件；客户端按 `EventSeq` 单调消费、按 `EventId` 去重、按 `DamageKind` 选择反馈 |

`CastId` 在 `CombatResolver.Decide` 接受施法时由整个服务端世界共享的单调 `ulong` 分配器生成，写入 `PendingCast`，`0` 保留给旧协议或无施法。玩家和 NPC 共用此分配器。NPC 的 `InstanceId` 仍由其动作运行器生成，供该实体的动作时间轴使用；施法广播和动作事件各自保留对应 ID。`CastCancelBroadcast`、`ProjectileLaunchBroadcast`、`ProjectileStopBroadcast`、`DamageResult/HitEntry` 全部以同一 `CastId` 关联；客户端跟踪表以 `CastId` 为键。服务端 `PendingCast` 同时记录 `CastId` 与已发射段号，取消只删除未提交段；`Broadcast.NpcAction` 在入队前 Clone，禁止持有 runner 活引用。客户端 `CancelCastRequest` 增加可选 `CastId`：携带时服务端只取消该实例，缺省时取消该技能最新实例（兼容旧客户端，不再删除全部实例）。客户端动作存储以 `(EntityId, LifeId, InstanceId, Revision)` 合并；视图未建时事件进入有界缓存，缓存键含 `CasterLifeId`，重连清空，视图建立后按 `CastId` 回放；`CancelAttack(CastId)` 只作用于当前实例。`NpcActionHistory` 首版不启用，`NpcAction` 过期/回池时清空。

`CombatProjection` 包含 `CombatVersion`、`LifeId`、`Hp/MaxHp/Dead`、`IsDefending`、`Statuses[]`（`EffectId`、`GroupId`、`EndTick`、`SourceId`、按类型的参数）、`EquipmentSummary`（`WeaponInstanceId`、`TemplateId`、`ElementId`）、`ControlSummary`（`SpeedScale`、`ActionLock`、`ControlVersion`；`ControlVersion` 与位置包的 `ClientControlVersion` 同源）和 `ActiveCast`（`CastId`、`SkillId`、`Phase`、`EndTick`，用于晚进观察者补玩家长施法）。协议用 `EntityFieldMask.Combat = 1 << 10` 承载该子消息；`EntityFieldMask` 是 `ushort`，状态、装备、控制都收进同一个 `CombatProjection`，不各占一位。`EntitySnapshot` 另加 `CombatVersion`/`LifeId` 两个标量字段（不依赖 `Combat` 位）：`CombatVersion` 随基线以及携带 `Health` 的普通快照发送，`CombatStateUpdate` 的投影也包含该版本；`LifeId` 随基线、换代及所有携带 `Health` 的增量发送，纯移动/保活省略。`Statuses[]` 使用 `IList<StatusProjection>`，`StatusProjection` 与 `CombatProjection` 全 `[ProtoContract]`；按类型参数用平铺字段，不用字典/多态，避免 IL2CPP 序列化缺口。

下发模型固定为单一变更通道：`CombatStateUpdate` 是状态/装备/控制变更的唯一来源；血量/死亡仍随普通快照走，但只在 `Health` 位置位（血量/死亡/防御变化或基线）时携带 `CombatVersion`。AOI 只在进入/复活基线携带完整 `CombatProjection`；普通移动快照与 4Hz 保活不置 `Health`/`Defending`/`Combat` 位，只带位置与动作；每个含 `Health` 的快照都携带当前 `CombatVersion` 和 `LifeId`。`Dead` 与 `Health` 同步置位。`AoiBandScheduler.ShouldSend` 返回发送原因（`Baseline`/`Keepalive`/`Changed`），`Baseline` 仅指首次发送或离场重见，带变化不算；`AoiBroadcaster` 只在 `Baseline` 时附投影；`Changed` 用于血量/死亡/防御/动作翻转，不带完整状态列表。复活时服务端先发一条带新 `LifeId` 的完整 `CombatStateUpdate`，并重置该 (observer, entity) 节拍槽（等价 `MarkOutOfSight`）以触发 `Baseline`。`FieldMask.Combat` 置位且 `Statuses` 为空表示全部清除；不置位表示保留已有状态——因此保活/移动快照必须省略该位。带宽按 100 KB/s 预算核算：按实测编码长度分别计纯位置包和带 `CombatVersion`/`LifeId` 的血量包，再计可靠事件和帧头。此前 64B/实体和 96 KB/s 只作规划参考，不能用于代替门禁；`CombatProjection` 只出现在入场/复活基线与变更消息，`Statuses[]` 首版最多 4 组。`BandwidthBudgetTests` 增加投影字节、入场突发、状态变更观察者扇出与每观察者/单 tick 上界断言；若实测超过 100 KB/s，优先降近带实体数或保活频率。

## 普攻参数

- 基础伤害建议用 `现有普攻段基础伤害 × 武器 BaseAttack / 默认武器 BaseAttack`，保留轻击 `10001` 与重击 `10002` 原有比例、暴击和减伤规则。剑气从该次重击基础伤害出发，保留技能原有剑气伤害系数，再乘武器的 `SwordQiDamageMultiplier`。伤害输入统一为共享表：`base × weaponRatio × swordQiCoeff × crit × block × wind`，客户端 `DamageBlock` 与服务端 `SkillEntry` 同源；`SwordQiConfig.DamagePercent` 作为共享 `swordQiCoeff` 下发。挥砍和剑气按现有独立命中实例结算。主动技能和怪物技能继续用各自配置。默认剑设为当前基线，避免重新调平衡。
- 攻速倍率同时应用于普攻动画状态播放、判定帧、可接续窗口及冷却。攻击 Animator state 增加 `AttackSpeed` 参数，本地与远端 `PlayAttack` 都按 `AttackSpeedMultiplier` 写入，禁止全局 `Animator.speed`。时长统一除以倍率，服务器向上取整为 tick；所有窗口 `max(1, ceil(...))`，倍率限定在 `[0.25, 4]`。`10001` 和 `10002` 的判定帧、接续窗口和冷却需要从同一套校验过的普攻时序数据解析，客户端动画事件只驱动本地预测表现。剑气发射时刻跟对应判定帧，发出后的速度和射程由技能配置决定。剑气速度以 `ProjectileSpec.Speed` 为权威，构建校验 `Speed > 0`、`Stages ≥ 1`、`Length > 0`，并要求 `Length / Speed` 等于 `TotalFlightSeconds`（带 epsilon），锁定 `Stages`、`Length`、`Speed` 三者的关系；现有 `10002` 的 `ProjectileSpec.Speed` 未配置，实施时先补 `speed:18f` 再启用校验。联网时 `SkillExecutor.OnSwordQiFrame` 不再生成剑气视觉；本地剑气统一由 `ProjectileLaunchBroadcast` 创建，避免本地预测与服务端发射描述重影。远端 `PlayAttack` 把 `CastBroadcast.AttackSpeedMultiplier` 写入攻击 state 的 `AttackSpeed` 参数，判定帧/接续窗口/冷却由同一快照派生；连续两发以 `CastId` 区分，禁止按 `SkillId` 猜。
- 联机普攻时序由 `SkillCoordinator` 和服务端 `CombatResolver` 从同一个已解析的武器快照派生。服务端记录该次普攻的最早可再次出手 tick，后续换装不重置该窗口。对 `10001` 这类基础冷却为零的普攻，使用按技能配置得到的最早接续窗口约束接受下一次攻击；同一 tick 同技能只接受一次，避免 cd=0 无限接包。客户端远端表现使用广播携带的已确认倍率。禁止跨模块修改共享 SkillData 资产或全局 Animator 播放速度。

## 架构接线

1. **生成和换装**。联机由服务器按表生成会话武器列表及默认装备，背包显示权威结果并发送换装请求。离线初始化本地武器实例并沿用同一槽位接口。装备保持至本次会话结束，复活保留。客户端收到装备确认后更新手持模型与拖尾，远端在装备变更及首次进 AOI 时收到武器模板和元素摘要。武器资源加载完成时核对装备版本，避免旧请求覆盖新模型。`CharacterAttackHandler.EquipWeapon` 可作为旧表现或组件衔接点。实际普攻参数由 `SkillCoordinator` 和服务端的施法快照消耗。
2. **剑气发射**。联机在 `CombatResolver.Decide` 接受施法时冻结武器参数，写入 `PendingCast`。在权威发射帧捕获发射位置、方向和发射 tick，发送 `ProjectileLaunchBroadcast`，并以同一快照驱动服务端判定与客户端剑气实例。取消走 `CastCancelBroadcast`，提前终止走 `ProjectileStopBroadcast`。离线在启动同次技能时冻结武器快照，由 `SkillExecutor.OnSwordQiFrame` 传给 `SwordQiProjectile`。联机的施法确认、取消、发射与停点按下节对账。
3. **命中与伤害**。`CombatResolver.ResolveStage` 保留现有挥砍和投射物两组结果，两个分支共用 `PendingCast` 上按 `(TargetId, TargetLifeId)` 记录的元素附加集合。每次独立伤害结算后检查有效命中，首个有效命中登记一次并尝试挂状态。有效命中要求正伤害、未格挡且目标仍存活；控制免疫等状态资格另行判断。`ApplyHit` 扣血前复查 `target.IsAlive`；目标已死则跳过，不生成 `HitEntry`，不附加状态。将现有扣血、余血结果、NPC 伤害通知和观察者广播收口成可接受直接伤害和持续伤害的入口。灼烧通过 `MonsterDamageKind.Dot` 走伤害归属、死亡与飘字，沿用 `DamageResult` 的收件人规则，并带 `DamageKind` 供客户端选择反馈。`HitEntry` 同时带 `CastId`、`EventId`、`TargetLifeId`；客户端 `ApplyServerDamage(kind)` 按 `DamageKind=Dot` 走 DoT 分支，只更新权威血量/死亡与飘字，跳过受击硬直、格挡、击退和命中特效。离线沿用投射物检测、怪物伤害管线和状态事件，补一个能回传格挡与实际扣血结果的命中适配器。
4. **状态运行时**。`SyncCore` 共享元素状态的校验、应用、刷新和时序。服务端和离线各提供时钟与目标适配器。服务端状态变更写 `CombatVersion` 并触发 `CombatStateUpdate`。怪物移动读取 `SpeedScale`，作用于期望速度、ORCA 最大步长和快照 `Speed`。怪物动作门读取麻痹标志，锁定主动移动和施法，清理被打断前摇对应的待判定段。怪物 DoT 必须走 `MonsterModule.OnDamageTaken(kind: Dot)` 更新 `LastHp`，不进入 `HitStartTick`/`ActionState=Hit`；DoT 仇恨按 `damage × ThreatPerDamage` 计，不叠加固定值。离线在怪物适配器中接入移速与伤害修饰，可复用现有眩晕门和状态事件。已有火球的本地灼烧按独立状态组运行，其伤害跳入同一易伤计算点，原参数和触发范围继续由火球提供。旧的 `StatusEffectRegistry`/`StatusEffectManager`（冰冻/灼烧，本地且仅怪物）保持独立：本地灼烧进入同一易伤计算点，冰冻仍是本地定身表现；权威状态与旧本地状态按效果组去重，不重复扣血。
5. **PvP 控制**。冰降低玩家主动水平移动速度，覆盖普通移动、冲刺与可控位移技能。客户端在 3C 和技能位移入口应用同一倍率。`PositionSyncRequest` 增加正交字段：`LocomotionMode`（Walk=0/Sprint=1）、`JumpPhase`（None=0/Start=1/Air=2/End=3）、`JumpSeq`、`VerticalVelocity`、`DisplacementSkillId`、`CastId`、`ClientControlVersion`、`LifeId`；`IsDefending` 沿用既有字段，不再重复进 `MoveMode`。服务端只按接收 tick 给预算，客户端 `Timestamp` 不参与：普通移动 `budget = Σ(BaseSpeed[locomotion] × Defend × StatusSpeedScale × dt)`；技能位移只在已确认 `CastId` 的活动窗口发放 `DashSpeed × StatusSpeedScale × dt`，`DashSpeed = DashDistance/(DashDuration - 起手 - 收尾)`，按 `DashDistance + 1 帧` 封顶，起手/收尾不发预算，Dash 期间 `Defend` 视为 1（不可防御）；跳跃惯性按起跳时服务端认可的水平速度 × 受限滞空时间发放，不乘 `StatusScale`。同一 tick 多个包共用普通移动预算，长间隔累积上限 `MaxAccumulatedSeconds`；技能位移与跳跃惯性不进入该 bank。服务端只承认与已确认施法阶段匹配的位移技能；客户端速度合成固定为 `Base × Defend × Skill × Status`，由 `CharacterController` 单点应用，`SkillDashComponent` 也必须消费合成后的速度，起手/收尾窗口取自共享表而非硬编码 0.05 秒。跳跃采用客户端起跳 + 服务端观察：服务端在首次收到新 `JumpSeq` 的 `JumpPhase.Start` 时校验接地并记录接收 tick，结合共享 `JumpForce`/`Gravity` 夹取 Y 与上升时长；重发同一 `JumpSeq.Start` 不重置；`VerticalVelocity` 只用于区分上升/下落。状态变化时按新倍率更新预算，状态版本跟随位置确认，旧版位置包不得跨控制窗口补位移。雷将主动水平位移预算设为零、禁止新的上升跳跃和施法；已有空中轨迹保留麻痹前的水平惯性，按上述跳跃惯性预算继续到落地，不接受麻痹后的新水平输入；服务端授权的外部位移沿用各自规则。PvP 首版控制规则包：控制结束 0.5 秒免疫，同组易伤 4 秒内不刷新；雷 PvP 麻痹 0.8 秒。客户端清空尚未消费的移动和施法输入，并沿现有技能打断入口停止未完成动作。服务器 `Decide` 拒绝新施法，取消未提交的前摇或持续施法后续段，已发射的独立投射物继续飞行。解除以 `CombatStateUpdate` 的权威版本为准；本地结束 tick 只用于倒计时和预期淡出，不单独销毁特效。死亡和复活优先。
6. **特效**。剑气按本次发射的 `ElementId` 选颜色、拖尾和粒子，回池时清理元素、材质参数和命中缓存。受击者的元素表现从权威状态衍生。相同效果刷新时更新结束 tick，保持正在播放的循环特效。状态去除只由 `CombatStateUpdate` 或死亡/离场事件触发；本地 `EndTick` 只用于倒计时和预期淡出。若权威移除消息晚于结束 tick，保持循环特效直到消息到达；超时请求新基线。首版建议蓝白冰晶、橙红火尾、紫白电弧、青白风痕，通过轮廓和粒子运动区分四种效果。

重构集中在共享状态规则、伤害提交、普攻时序和权威装备，复用现有命中几何、普攻动画资产与剑气飞行。实施顺序为共享表与权威换装、普攻参数与剑气快照、PvE 状态结算、PvP 权威限制与远端表现。每一步用默认剑对照现有伤害、判定帧和格挡停点。

## 联机一致性约定

同一战斗事件使用服务端生成的一份结果。施法者、受击者和有视野的旁观者最终看到相同的元素、命中、格挡、伤害及状态。网络延迟可以推迟表现。客户端预测仅用于本地动画，血量、死亡、命中和持续状态由服务端结果驱动。

### 服务端顺序与场景边界

- `LoginHandler` 目前向房间和 `EntityStore` 分别登记玩家，后者为当前战斗和 AOI 真值。入场时将同一个服务端 `CombatSceneId` 写进权威实体。房间登记引用该身份，怪物生成也显式绑定场景。命中候选、AOI 可见性、广播收件人使用同一场景过滤，先筛场景再算坐标或距离。切场景时先关闭旧场景下的未完成施法、状态和观察关系，再发布带新入场代次的基线，防止返回同一个场景时接受上次入场的迟到包。旧场景中已挂在其他目标上的灼烧保持原归属。
- 每个服务端逻辑 tick 固定顺序为场景及实体生命周期变更 → 解除已到期的修饰并处理到期的灼烧 → 按服务器入队顺序处理本 tick 的换装、位置、施法输入 → 推进怪物动作和到期命中段 → 复活与同步。现有 `GameLoop` 在 `_onTick` 前消费网络队列，实施时把相关战斗输入落到 tick 内消费，避免某个包走到期前的门禁、另一个计算却走到期后。所有世界状态变更在主循环内提交，异步发送只消费提交后的结果。
- 状态作用区间为 `[ApplyTick, EndTick)`。灼烧允许在 `EndTick` 结算已排定的最后一跳：到期 tick 先结算排定跳，再移除状态；风在 `EndTick` 先失效，不放大同一 tick 的灼烧最后一跳；跳伤害只读取尚未到 `EndTick` 的风。同 tick 多个 DoT 按目标 ID、效果组 ID 定序。直接命中按判定 tick、`CastId`、段号处理。同一道剑气按沿程距离由近及远，距离相同按实体 ID 定序。一次命中依次读取旧状态、复查存活、结算伤害、判定存活、提交新状态；已死目标直接跳过。第一个风命中使用旧倍率，同 tick 之后的命中读取已提交的新倍率。死亡立即清状态并拒绝后续命中和跳伤害。
- 麻痹按已提交阶段处理。服务端为被取消的前摇或持续施法发送 `CastCancelBroadcast`（带 `CastId`）。已经提交发射的剑气在施法者被麻痹时继续飞行。施法者死亡、离场或断线时沿用当前联机取消后续判定的规则，同时向已知观察者结束对应视觉。取消待发射攻击和结束已发射剑气都以具体 `CastId` 定位。

### 换装、发射和结果身份

| 消息或数据 | 权威字段与消费方式 |
|---|---|
| 换装请求和确认 | 请求携带会话内的请求序号与实例 ID。相同序号的重试返回原结果，旧请求不能覆盖新装备。确认后客户端才切换背包选中项和手持模型。装备版本不随角色复活重置。 |
| 普攻请求和确认 | 客户端发送 `ClientCastSeq`、已确认的 `EquipmentRevision` 和当前生命代次，服务器按该版本校验并返回接受或拒绝。版本不匹配时拒绝并补发当前装备摘要。重复施法序号只返回原结论，不再扣资源或排新判定。确认包含服务端 `CastId`、技能 ID、装备版本、攻速和 `AcceptedTick`。重试结果保留在有界窗口，过旧序号拒绝执行。客户端在换装待确认时暂缓发起新普攻；已发出的请求仍按服务端处理顺序确认或拒绝。 |
| 剑气发射及终止 | `CastBroadcast` 用来开始施法动作。服务端在首个剑气判定段前发布 `ProjectileLaunchBroadcast`，包含 `CastId`、施法者生命周期、场景、`LaunchTick`、共同的 XZ 判定原点和方向、速度、射程及元素。表现允许固定高度偏移，判定与表现使用同一水平起点和轴线。`ProjectileStopBroadcast` 含 `CastId`、`StopTick`、原因与沿程停点。正常到达射程可以从发射数据推导，格挡、死亡和离场造成的提前终止可靠下发。连续两发分别定位，格挡端不能按技能 ID 猜一发。 |
| 伤害与状态 | 每个直接命中和灼烧跳数有稳定事件 ID，附服务器结算 tick、目标生命周期、伤害类别、扣血值、格挡结果、余血及 `CombatVersion`。实体的血量、死亡和元素状态使用同一个单调的战斗版本。命中和附加状态在服务端一起提交。`HitEntry` 与 `WorldSnapshot` 的血量条目都携带对应 `CombatVersion` 和目标 `LifeId`。状态改变时以 `CombatStateUpdate` 可靠消息传完整投影，包含同版本的血量、死亡、装备、控制及活动状态。 |

本地可预测挥砍动作，剑气飞行和元素特效从权威发射描述开始。同一 `CastId` 只创建一个实例。客户端根据同步的服务端时钟和渲染时间轴补进度，延迟收到发射包时从剩余轨迹播放。终止先到时按 `CastId` 缓存，随后合并；已结束或超出有效飞行窗口时跳过生成。结果可以先于施法或模型加载到达。命中表现按事件 ID 去重，只在目标仍属当前生命周期、当前可见、且事件仍处于表现窗口时播放。超时、拒绝或取消只清理对应预测。缓存按会话、生命周期和飞行窗口限界。

现有联机在首个判定 tick 直接检查第一整段剑气，动画此时才发出。建议保留原沿线判定形状与分段长度，将发射和命中断开。第 `i` 段在 `LaunchTick + ceil(段末距离 / 速度 × TickRateHz)` 结算，段索引从 0 起，段末距离 = `(i + 1) × Length / Stages`；现有 `BuildStageTicks` 按段首 0/4/7 tick，首段语义差异列入实现前验证，以实测延迟决定是否细分判定段。沿线接触点使用权威段的结果。近身挥砍仍在发射帧结算。这样伤害不会早于剑气沿程到达，但会存在最多一段飞行时间的批处理延迟，使用 `10002` 默认参数时约 0.22 秒，另有至多一次 tick 取整。客户端收到格挡结论后向同一权威停点收束，已越过时结束飞行并在停点播放反馈。该调整对默认剑也生效，需要以近身和远距离命中回归确认手感。首轮按段末判定实测近身延迟。若需降低延迟，细分判定段并继续按各段末飞行时间调度，保留挥砍在发射帧的基线。

### 取消、状态投影与同 tick 顺序

- **取消**：麻痹、死亡、离场取消未提交的前摇或持续施法时，服务端向施法者与观察者发送 `CastCancelBroadcast`（`CastId` + `EffectiveTick` + `Reason`）。玩家与怪物 `CastId` 全局唯一；怪物动作阶段另外用自己的 `InstanceId` 和 `Revision`。客户端按 `CastId` 停止攻击表现：玩家路径由 `RemoteCastPresenter`/`RemoteEntityAnimator` 消费，怪物路径由 `RemoteEntityRegistry` 的 `NpcAttackSchedule`/`NpcEntityAnimator` 消费；两条路径都按 `CastId` 键控跟踪项，禁止再按 `(casterId, skillId)` 猜。已发射剑气继续飞，未发射剑气不再生成。取消消息先到则缓存，等 `CastBroadcast` 到达后合并；`CastBroadcast` 先到则立即停止。服务端 `Decide` 分配 `CastId` 并在入队前 Clone `NpcAction` 快照；`CancelPending` 按 `CastId` 只删未提交段；`CancelCastRequest` 缺省 `CastId` 时取消最新实例，不再删除全部。怪物侧把 `NpcActionPhase.Cancelled`/`NpcActionCancelReason` 写进动作快照并发送 `NpcActionEvent`；`AoiBroadcaster` 的进入基线填 `EntitySnapshot.NpcAction` 并置 `EntityFieldMask.NpcAction`；客户端按 `(EntityId, LifeId, InstanceId, Revision)` 合并动作快照（仅同实例比较 Revision），先写入按 `InstanceId` 键控的动作存储，再进入插值时间轴；视图未建时事件进入有界缓存；`Cancelled` 阶段释放攻击窗口。玩家晚进观察者的长施法由 `CombatProjection.ActiveCast` 补进度。
- **状态投影**：`CombatStateUpdate` 是状态挂载、刷新、移除的权威来源；血量/死亡仍可随普通快照走，但必须带同一 `CombatVersion`，普通快照在 `Health` 位置位时携带同一条目的 `CombatVersion` 和 `LifeId`。死亡/离场事件可直接清理，但必须携带同一 `CombatVersion`/`LifeId`，客户端仍按版本合并。收到版本更高的投影时应用完整状态；版本更低时按字段分别合并（血量/死亡与状态各自记录已应用版本；装备/控制随完整投影原子应用）。`ControlSummary` 带 `ControlVersion`，客户端据此丢弃旧控制窗口的输入。状态移除只由更高版本投影触发；本地 `EndTick` 只做倒计时和预期淡出，不能单独销毁循环特效。状态投影的收件人 = 目标本人 + 能看见目标的观察者，与伤害收件人分开计算。状态字段置位且列表为空表示全部清除；省略字段时保留已有状态。
- **同 tick 顺序**：场景/生命周期变更 → 解除到期修饰 → 结算到期灼烧 → 处理输入 → 怪物动作与命中段 → 复活与同步。命中段内按判定 tick、`CastId`、段号定序；同一道剑气按沿程距离、再按实体 ID 定序。每次命中依次读取旧状态、结算伤害、复查存活、提交新状态；目标已死则跳过，不产生 `HitEntry`，不附加状态。风在 `EndTick` 到期后不再放大同 tick 的灼烧最后一跳；同 tick 新施加的风只影响其后的直接命中，不影响已在更早阶段结算的灼烧跳。
- **事件顺序**：同一 tick 的出站事件按服务端裁决顺序写入 `EventSeq`；`EventSeq` 按 `(LifeId, EntityId)` 序列单调，客户端允许序号空洞、不按连续序号阻塞；`EventId` 用于跨重试去重，去重窗口有界。迟到或重复事件不重复扣血、不重复触发硬直和格挡精力。
- **死亡**：灼烧跳或直接命中致死时立即清状态并拒绝同 tick 后续命中；复活只由新生命周期基线开始。

### 快照、重入和恢复

- 接收端在主线程按场景、实体生命周期和版本合并战斗数据。血量投影和活动状态投影分别记录已应用版本，带完整战斗投影的消息同时检查并合并两项。例如已应用血量版本 12，之后收到带状态的版本 11，血量留在 12，状态仍可推进到 11，前提是状态版本尚小于 11 且本代仍存活。血量快照携带 `LifeId` 和 `CombatVersion`。旧 `LifeId` 的条目直接丢弃。更高 `LifeId` 的条目在新生命基线到达前有界暂存，同一生命按 `CombatVersion` 合并。装备与控制随完整投影原子应用，不单独接受低版本覆盖。死亡清除状态并使本代保持死亡，复活只由新生命周期基线开始。先到的新生命数据可短暂等待基线，超时请求恢复。复活和第一次进入视野的全量基线含当前战斗版本、血量、死亡标志、装备、活动状态的生效和结束 tick。状态字段置位且列表为空表示全部清除，省略字段时保留已有状态。本人通过可靠变更与权威初始/复活基线同步，不依赖 AOI 快照包含自己。
- 活动状态和装备只在变更、初次入场、恢复或进入 AOI 时发送完整投影；移动包只携带移动与校验字段，血量或死亡变化的包按 `Health` 位携带 `CombatVersion` 和 `LifeId`，纯移动包省略这些字段，不置 `Combat` 位，也不无条件置 `Health`/`Defending` 位。可靠的 AOI 进入基线、变更和退出都携带观察者与实体之间本次可见期的版本，快速出入时旧增量不能复活旧实体视图。基线前的本期变更有界暂存。模型升降档和池化只重建表现，要从本地当前基线重新应用装备与状态。未出现在本次消息中的实体不能被误删。
- 新进 AOI 的观察者补当前仍在飞、且轨迹进入视野的剑气，含同一个发射描述与当前进度。施法者处在视野外时，剑气按自身位置进入视野，命中反馈按目标可见性播放。服务器维护每发剑气的已通知会话集合，保证提前停止也会发送给仍持有该视觉的客户端。已结束的剑气与历史飘字不补播。会话断开后清理旧状态、预测和异步加载结果。当前会话内武器设计下，重新登录由新会话基线重新发放武器。
- 客户端按同步的服务端 tick 和结束 tick 只估算倒计时与预期淡出；状态特效的挂载、刷新和移除以 `CombatStateUpdate` 为准。本人控制投影携带权威移速倍率和动作锁标志，客户端直接应用。解除以权威版本确认，过期后没有收到解除则请求新基线。重复或迟到的伤害事件不能回写新版本血量，也不能重复消耗格挡精力或触发硬直。PvP 的位置确认携带当前控制版本；位置序号只用于包顺序，服务器接收 tick 和实际位移预算决定能移动多远。
- 协议通过现有功能位协商，对战场景内统一启用。共享战斗配置，包括武器、状态、普攻时序和剑气飞行几何，在入场前比对内容指纹，未通过则无法进入该功能场景。Addressables 表现资源加载失败使用同元素的预置简化表现并记录诊断，战斗状态继续消费。新增 Protobuf 字段使用新 tag，按现有 IL2CPP 容器规则做双端序列化与真包验证。

### 控制与反馈的落地约束

- 位置和施法输入带生命代次、`LocomotionMode`、`JumpPhase`/`JumpSeq`/`VerticalVelocity`、`DisplacementSkillId`/`CastId` 及客户端最后确认的控制版本。控制版本只用于辨别状态切换时产生的旧输入，服务端始终按当前权威状态校验；客户端时间戳不参与预算。PvP 减速碰到正常移动、冲刺或技能位移时，按各自允许的速度和已确认施法阶段统一计入预算。麻痹期间减少的主动位移当场纠偏，解锁后从新的服务端位置和接收 tick 起计。客户端按当前权威状态预测，丢弃对应旧版本的待重放输入。本地速度在 `CharacterController` 单点合成为 `Base × Defend × Skill × Status`，技能与状态不得互相覆写 `MoveSpeedScale`；`SkillDashComponent` 走同一合成结果。
- 麻痹禁止新起跳，但保留已有跳跃轨迹的水平惯性（作为有上限的授权位移）和服务器授权的位移。现有 `MovementValidator` 对 Y 只标记异常，不能用于兑现这项规则。控制窗先按状态生效时最后接受的位置、`JumpSeq` 和服务端记录的起跳接收 tick 限定上升，再允许重力下落；`VerticalVelocity` 只用于区分上升/下落，不参与预算。碰撞推挤和技能击退需要来自服务端认可的来源与方向范围。当前仅在客户端发生的 `NpcBody` 推挤需要与冻结纠偏联测，避免两边反复移动同一角色。认可已有跳跃与碰撞位移的最小实现需先做 PvP 技术验证，验收同时覆盖地面起跳被挡、空中正常落地、麻痹空中保留水平惯性落地和贴着移动怪物时没有持续往返纠偏。
- 格挡使用命中当时服务端已接受的位置、朝向与防御姿态，同一 tick 的位置输入先于命中阶段。客户端以事件 ID 消费一次格挡反馈，旁观者对同一事件使用相同的 `IsBlocked`。飘字按同一事件值显示。同场景收件人可以只收到其可见的命中条目，每个条目的数值和身份来自同一次提交。
- PvP 首版控制规则包已生效：控制结束 0.5 秒免疫，同组易伤 4 秒内不刷新，雷 PvP 麻痹 0.8 秒。首轮联调用两名雷剑施法者验证最长连续控制时间，再校准免疫窗口与雷时长；服务端仍对所有客户端使用同一规则。

## 依赖与实施验证

- [Bag](../../../Assets/Scripts/Hotfix/GameSystems/Bag/README.md)、[3C](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md) 和 [Skills](../../../Assets/Scripts/Hotfix/GameSystems/Skills/README.md) 接入换装、普攻参数和本地控制。
- [Server](../../../Server/README.md)、[怪物](../../../Server/Monster/README.md) 和 [SyncCore](../../../Assets/Scripts/Hotfix/GameSystems/SyncCore/README.md) 管理场景、裁决、状态与位移预算。
- [协议](../../../Assets/Scripts/AOT/Protocol/README.md)、[SyncLayer](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md) 和 [VFX](../../../Assets/Scripts/Hotfix/GameSystems/VFX/README.md) 消费版本、AOI 基线与事件，驱动各端表现。
- 先用现有 `CastResolutionSchedulingTests`、`CombatResolverSwordQiBlockTests`、`PositionSyncEntityStoreTests`、协议形状与 AOI 带宽测试锁定基线。新增 `CastCancelBroadcastTests`、`NpcActionChainTests`、`NpcActionRevisionMergeTests`、`CombatStateProjectionTests`、`CombatVersionMergeTests`、`DotDamagePresentationTests`、`DotTerminalTickTests`、`SameTickCombatOrderTests`、`PvpMovementBudgetTests`、`MovementBudgetFormulaTests`、`AttackSpeedAnimatorTests`；三客户端测试加入麻痹取消前摇、DoT 跳伤、状态结束时钟偏差。状态同步比较变更前后快照字节数和多人同屏峰值，并绑定 `GlobalGameSettings.BandwidthBudgetBytesPerSec`（100 KB/s）；补每观察者 60 秒回放与单 tick 上界断言。活动状态按目标与状态组索引，只推进有活动状态的目标。AOI 下发按观察者与当前状态版本合并，避免对每个状态做全世界观察者扫描；`CombatProjection` 只进入基线与变更消息。协议形状守卫同步 `EntityFieldMask.Combat` 位与新增 tag。
- 文档实现前验证项为 PvP 位移控制、默认剑发射时序和包字段/预算公式。前者先验证空中轨迹、麻痹水平惯性与碰撞推挤，中者先量近身剑气的首段延迟，再确定需要细分判定段或保持现有段数，后者先确认 `LocomotionMode/JumpPhase/JumpSeq/VerticalVelocity/DisplacementSkillId/CastId/ClientControlVersion/LifeId` 能覆盖现有移动、冲刺、跳跃与技能位移。三者都用现有场景验证，可与配置表和换装模块并行验证。

## 验收场景

- 相同武器模板固定同一属性，普通模板无属性。按表发放后可从背包切换模型和属性，非法实例换装被拒绝。玩家发出冰剑气后换火剑，前一发仍减速，后一发灼烧，旁观者区分两发，断言两发 `ProjectileLaunchBroadcast` 的 `CastId`/`ElementId` 不同。
- 两把攻击力和攻速不同的剑仅改变轻击、重击及关联剑气。攻速同时影响客户端动作和服务端判定点，切武器保留之前已安排的判定与出手限制，剑气飞行速度保持由技能配置决定。
- `10002` 同时命中挥砍和剑气时结算两段基础伤害，元素由首个有效命中附加且只附加一次；挥砍先命中时剑气不再附加状态。再覆盖挥砍被挡、后续剑气未被挡时只由剑气附加，以及两名 NPC 的动作 `InstanceId` 相同时施法关联仍独立。格挡仍按原位置停住。挥砍已打死目标时，剑气不再产生 `HitEntry`、不再附加状态；目标未活过基础伤害时不附加状态。
- 用默认剑对照首段近处、段边界和末段，确认联机剑气只在对应段结算一次，近身挥砍仍在发射帧命中，远端显示同一发射轴线和格挡停点。记录从画面接触到权威伤害的延迟，断言挥砍在发射帧结算。默认三段时，剑气从到达目标沿程距离到服务端段末判定的延迟不超过约 0.22 秒加 1 tick；网络抖动只影响播放时刻，分别用无延迟和 100/200ms 抖动核对权威 tick。
- 冰结束后恢复速度，雷结束后恢复移动和施法，控制结束后 0.5 秒内免控。火重复命中刷新后仍按时跳伤害，最后一跳和死亡清理只处理一次；灼烧每跳不触发受击硬直、命中火花和格挡；到期 tick 先结算排定跳再移除。风在首个有效命中后附加，PvE 后续伤害按 2 倍，PvP 按 1.25–1.3 且不放大灼烧；同组易伤 4 秒内不刷新。状态特效的挂载、刷新和移除由 `CombatStateUpdate` 或携带同一版本/生命代次的死亡/离场事件触发，本地 `EndTick` 只做倒计时。
- 三客户端测试分别担任施法者、受击者和旁观者。记录 `CastId`、`EventSeq`、发射 tick、XZ 起点/方向、事件 ID、伤害值、`DamageKind`、格挡停点、目标生命代次、战斗版本和状态结束 tick。三端同一记录的值逐项相等，客户端状态与服务端最终版本一致。实际特效按各端渲染时间轴校验，状态特效的移除时刻按同一权威投影校验。离线用同一组表配置对照四种效果。
- 同场景 PvP 复现冰减速和雷停手，分别检查角色本地输入、服务端实际位置、施法拒绝与旁观者表现，覆盖空中中招落地、连按技能、状态刷新与复活；玩家 HP=720 时 TTK 落在 4–6 次交手。减速/麻痹期间客户端持续全速上报，断言每 tick `|server − client| ≤ 预算 × dt × 1.05`、纠偏次数不超过超预算包数、三端观察位置差不超过 2 个插值窗口；本地速度合成为 `Base × Defend × Skill × Status`，技能与状态不互相覆写，`SkillDashComponent` 走同一合成结果。另一战斗场景的玩家即使坐标重叠也不受同一道剑气影响。
- 确定性回放覆盖同 tick 多段、先到期后伤害、最后一跳灼烧触发死亡、同 tick 先风后普通命中、风到期与灼烧最后一跳同 tick、灼烧致死与直接命中同 tick、挥砍致死与剑气同 tick，以及麻痹命中时已经发射和尚在前摇的两笔施法。对照权威事件顺序、实际参与计算的倍率、`HitEntry` 数量和是否产生多余命中。
- 在 KCP 和 WebSocket 各验证正常流程，再在收包消费层注入延迟、重复和乱序，覆盖换装确认和施法交错、发射比格挡结果晚、旧伤害比复活晚、较新血量比状态消息早、AOI 快速出入和收到状态时模型尚未加载。断线重进按新会话初始化，旧包、旧模型回调和旧位移预测均不生效。
- 麻痹取消前摇：A 的雷剑气命中 B 正在进行的普攻/技能前摇，三端用同一 `CastId` 记录 `CastCancelBroadcast` 的 `EffectiveTick`；B 和旁观者 C 都停止攻击动作，不生成未发射剑气，不产生后续 `DamageResult`；已发射的独立投射物继续飞行。玩家路径断言 `RemoteCastPresenter`/`RemoteEntityAnimator` 按 `CastId` 停止；怪物路径断言 `NpcAttackSchedule`/`NpcEntityAnimator` 停止，且 `NpcActionEvent` 与 AOI 基线 `NpcAction` 按 `(InstanceId, Revision)` 合并、`Phase=Cancelled` 生效。连续两发同技能时只取消指定 `CastId`，另一发继续结算。
- 灼烧表现：三端每跳伤害、余血、`DamageKind=Dot` 一致；受击者不被跳伤打断攻击，观察者不播命中火花/受击硬直；最后一跳致死时三端死亡时刻一致。
- 状态结束时钟：给两台客户端注入不同的时钟偏移和渲染延迟，断言冰/火/雷特效的移除由同一条 `CombatStateUpdate` 触发；权威移除晚于 `EndTick` 时特效保持到消息到达，而不是各自按本地结束 tick 消失。
- 状态投影恢复：AOI 快速出入、死亡复活、Evade 到家、回池和断线重进时，进入基线与后续 `CombatStateUpdate` 的 `CombatVersion`/`LifeId` 连续；旧版本增量不能复活旧状态或旧装备。
- 战斗版本合并：普通 `EntitySnapshot` 与 `CombatStateUpdate` 交错时，血量/状态按 `CombatVersion`/`LifeId` 合并且不互相回退；4Hz 保活与移动快照不置 `Combat`/`Health`/`Defending` 位，不误清状态；`CombatProjection` 只出现在基线与变更消息。按实际编码回放 100 实体 × 15Hz × 60 秒，覆盖生命代次、血量变化、AOI 基线及可靠事件，每观察者 ≤ 100 KB/s、单 tick ≤ 150 KB；若超标先降近带实体数或保活频率。
- 冲刺/跳跃预算：冰/雷命中冲刺或跳跃中的玩家，断言技能位移只在已确认 `CastId` 的活动窗口发放 `DashSpeed × StatusSpeedScale × dt` 且总位移不超过 `DashDistance + 1 帧`；跳跃惯性 = 起跳水平速度 × 受限滞空时间，麻痹空中保留该惯性到落地，未产生新的水平输入；客户端时间戳不影响预算。
- PvP TTK 与控制：玩家 HP=720，重击+剑气约 180/次，4–6 次交手内击杀；风 PvP 1.25–1.3 且不放大灼烧；控制结束 0.5 秒免疫，同组易伤 4 秒内不刷新。
- 攻速通道：两把不同攻速的剑，攻击 Animator state 的 `AttackSpeed` 参数与判定帧/接续窗口/冷却同源；倍率 0.25 与 4 的边界不出现除零、0 tick 或动画截断。
- 灼烧末跳：火 3 秒/1 秒间隔时，`EndTick` 那一跳结算且只结算一次；风与灼烧同 `EndTick` 时风先失效，不放大末跳。

## 修订记录

- 2026-11-14：按联机一致性评审补齐施法取消（`CastCancelBroadcast`）、状态投影（`CombatStateUpdate` + `EntityFieldMask.Combat`）、DoT 表现分流（`HitEntry.DamageKind`）、`CastId`/`EventSeq`/攻速下发、PvP 位移预算输入（`MoveMode/JumpPhase/DisplacementSkillId/ClientControlVersion/LifeId`）、状态移除时钟与同 tick 死亡边界；新增下行消息表、同 tick 顺序与对应验收场景。
- 2026-11-14（复审）：怪物动作阶段接通 `NpcAction` 全链（`NpcActionEvent` + AOI 基线 `NpcAction` + 客户端按 `Revision` 消费）；玩家 `CastId` 单调分配，`CancelPending`/`CancelCastRequest` 支持按 `CastId` 取消；`CombatVersion`/`LifeId` 进入 `EntitySnapshot`，`CombatProjection` 收敛为 `CombatStateUpdate` 单一变更通道并明确保活不置 `Combat` 位；新增 `ActiveCast` 补晚进观察者长施法；技能位移共享 `DashDistance/DashDuration` 并纳入预算，`SkillDashComponent` 走单点速度合成；跳跃增加 `JumpSeq`/`VerticalVelocity`，服务端按起跳接收 tick 观察；麻痹空中保留水平惯性；补充带宽、版本合并、NpcAction 链与冲刺/跳跃预算测试。
- 2026-11-14（full 复审）：B1 合并键改 `(EntityId, LifeId, InstanceId, Revision)`，补客户端动作存储/插值接入/`CancelAttack(CastId)` 与预视图有界缓存；服务端 `CastId` 分配、`NpcAction` Clone、按 `CastId` 取消与最新实例回退；B2 `LifeId` 仅基线/换代、`CombatVersion` 仅随 `Health`/变更携带，`ControlSummary` 加 `ControlVersion`，带宽口径 60B→64B/96KB/s；B5 预算改分项公式（普通移动/技能活动窗/跳跃惯性），只信服务端接收 tick，跳跃接地校验与 Y 夹取，Dash 分起手/活动/收尾；玩家 HP 720、PvP 控制规则包、元素首个有效命中、攻速 Animator `AttackSpeed` 参数；补 DoT 末跳、10002 Speed>0、参数区间、EventSeq 空洞与 AC 数值断言。
- 2026-11-14（实现计划复核）：玩家与 NPC 使用同一 `CastId` 分配器；携带血量或死亡的快照附 `LifeId`；挥砍和剑气共用一次施法的元素附加记录；明确本人施法确认和权威发射/终止归 Plan 01。
