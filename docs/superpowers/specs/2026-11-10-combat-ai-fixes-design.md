# 战斗与 AI 修复规格

日期：2026-11-10

状态：Draft v3（2026-11-10 评审补记 + B1–B5 实施），已核对源码、服务端基线与 PlayMode 复现，真机/双客户端验收待执行。

前序：[体验改进规格](2026-11-09-experience-polish-design.md)。本篇覆盖原先推后的四项战斗和 AI 问题，以及新增的贴怪跳跃失败。

## 1. 现象与证据

| 编号 | 现象 | 当前证据 | 还需确认 |
|---|---|---|---|
| B1 | 跳跃躲不开怪物攻击。PvP 是玩家推测 | 联网 `HitResolver` 的圆形、锥形和线形几何只比较 XZ。怪物和玩家施法均经 `CastHandler` 裁决。怪物判定帧的 LOS 另行检查，不能表达攻击高度 | 用同一招对照站地与跳跃，分别记录怪物和玩家攻击。客户端单机另有本地形状与碰撞链，单独复现 |
| B2 | 空中死亡复活后角色漂移，再跳一次恢复 | 已核对真实 `Character3C.controller`。JumpStart 只接受 BaseState=4 进入 JumpAir，JumpAir 只接受 BaseState=5 进入 JumpEnd，Base 层没有 AnyState 出口。死亡锁实际是 `BaseFSM.LockState(Idle)`（不是 Death），`ForceState` 只写 Animator 参数；`Unlock` 的 Death 分支不会进入。参数可变回 Idle，实际状态却仍留在跳跃节点，`ForceState`/`Unlock` 也未同步清理 `IsJumping` | 用真实控制器分别从 JumpStart、JumpAir 复活并移动，核对基础层实际状态与腿部混合值 |
| B3 | R 旋转数圈无伤害 | `Spin_SkillR`（20002）单机每 0.2 秒有一次判定，每目标上限 5 次。联网 `SkillCastReporter` 激活时只发一次；`CombatRules` 的 20002 延迟为 0 且无多段配置，`PendingCast` 只安排首段。目标在首段后进圈会整次无伤害，这是可直接从代码推出的用例 | 对“起手时已在圈内但无伤害”的样本另查施法拒绝、服务端位置、候选和 `DamageResult`，目前不能归于同一原因 |
| B4 | 玩家贴近怪物后，怪物停手。玩家离开后也可能不动 | 已有两个具体候选。动作 V1 的 `Attack` 先做 `TrySetupSkill`，符合血量和冷却的远距招式可能优先于可立即施放的近战。导航 `PathFollower` 每次成功重寻重置停滞计数，正常重寻周期 15–29 tick，小于累计三次 10 tick 窗口所需的 30 tick | 按怪物、地图位置、动作 V1 开关分辨等待冷却、站位准备、LOS、无路、预算、实际无位移和视图失步。以上缺口不等于已还原玩家那一次停手 |
| B5 | 贴近怪物或 Boss 容易跳不起来 | `RequestJump` 与实际消费都要求 `IsGrounded`。移动先执行一次 `CharacterController.Move`，再读取该控制器的接地值，之后才消费跳跃请求。客户端 `NpcBody` 使用实心盒体，怪物移动可与玩家产生交叠 | 通过实际碰撞复现区分输入门控、接地丢失、起跳速度未写入、起跳后被几何阻挡和联网纠偏 |

## 2. 修复合同

### B1 攻击高度与跳跃规避

- 按判定帧的攻击体积与受击体积是否相交裁决。地面近战在水平命中后检查高度重叠。起跳、落地或高位攻击仍可命中，只要攻击体积触及受击体积。高打击与特殊技能由各自技能配置表达。
- 高度规则随技能明确定义。第一批覆盖可复现的怪物地面近战和玩家基础近战，保留既有 XZ 形状、目标体型扩展、怪物 LOS、格挡与伤害顺序。投射物、落点技能和其他招式逐类验收，避免一次更改全部形状语义。
- 联网以服务端判定帧的可信高度为前提。当前 `MovementValidator` 对异常垂直增量只标记纠偏，服务端不能直接用当前客户端自报 Y 作为新的规避依据。实施前确定并验证服务端认可的垂直位置或跳跃轨迹，再据实测的起跳高度、角色受击范围和招式动作配置高度边界。单机按同一几何合同使用本地有效位置。
- 复现以固定 XZ 的站地、起跳、最高点、落地四组为对照。玩家攻击从两客户端记录，明确区分服务端是否扣血和客户端是否显示飘字。
- **评审补记（2026-11-10，代码核对）**：`HitResolver` 的圆/锥/线全部只算 XZ（`SurfaceDistance`/`DistXZ`/`InCone`/`ResolveLineAll`/`ResolveLineNearest`），`CombatCandidate` 只有 Position + Radius，没有高度；`MovementValidator` 对垂直增量只标纠偏、不钳制 Y，权威位置仍是客户端自报值。PvE 与 PvP 共用同一套服务端裁决，跳跃规避失效对两者同时成立。
- 数据模型缺口（实现前必须补）：`CombatCandidate` 需要目标垂直范围（脚/头或半高），技能表需要攻击高度上下限，并保持双端 `CombatRules` 同源；单机本地链走 `Physics.OverlapSphere`（3D 碰撞体），高度可能已部分生效，须单独复现确认是否与联网同因。
- 服务端可信高度方案（阻塞项，实施前先选定）：A. 采信客户端上报 Y（与既有位置同步同一信任面，登记作弊风险）；B. 服务端按起跳 tick + 移动配置轻量模拟竖直轨迹；C. 只接受有限高度档位（地面/空中），由客户端声明并限幅。未选定前 B1 不进入实现。
- **决策与实施（2026-11-10）**：采用 **A（客户端 Y + 服务端限幅）**。`CombatCandidate` 增加 `Height`（玩家 1.8m，NPC = max(1m, bodyRadius×2)）；`SkillEntry` 增加 `AttackHeightSpec`；第一批只给玩家基础近战 10001/10002（仅挥击实例）与怪物地面近战 50001–50005 配置 `[-0.3, 1.2]` 高度带，投射物/AOE 保持纯 XZ。有 navmesh 时 `CombatResolver.ResolveCombatHeight` 把玩家 Y 钳到 `[地面−1m, 地面+MaxJumpHeight]`，无导航宿主沿用客户端 Y 并在文档登记信任面。**已知信任面**：限幅只能挡住离谱高度，挡不住客户端始终上报可达跳高；这与既有“客户端权威位置 + 部分校验”一致，登记为方案 A 的风险。单机本地链未改，仍需单独复现。

### B2 空中死亡后的复活复位

- 单机 `TickRespawn` 和联网 `ApplyServerRespawn` 在各自合法复活时刻进入同一基础状态复位路径。现有服务端落点、传送同帧位置同步与预测清理继续由当前接线负责。
- 同步清理本条命的跳跃请求、延迟离地标记、空中状态和垂直速度。基础 FSM、`CharacterData`、Animator 的 BaseState 与 `IsJumping` 必须一致。复活只复位基础层到 Locomotion，保留 Hit 层已定义的起身表现和权威复活时序。
- 实际基础层必须退出 JumpStart / JumpAir。通过控制器的明确复活出口或复活路径定向切到 Locomotion 实现，验证时读取 Base 层当前状态，单查参数不能覆盖本问题。复活后的第一个合法移动阶段应出现走跑动画，之后的正常跳跃与落地仍完成。
- **评审补记（机制校正）**：死亡锁的是 **Idle 不是 Death**——`StateCoordinator.HandleDeath` 调用 `BaseFSM.LockState(0)`（`BaseState.Idle = 0`），`ForceState` 只写 Animator 参数；`BaseFSM.Unlock` 的 `_currentState == Death` 分支实际永远不会进入。因此病灶是"参数已写成 Idle、动画层仍停在 JumpAir"，复位必须对 Animator 做实际状态切换，不能只改参数。
- 正常死亡路径不调用 `CharacterController.ApplyDeath`（`TakeDamage`/`ApplyDeath` 当前无实际调用方，属死代码，且 `_data.IsDead` 没有复位点）。B2 的复位不要依赖 `_data.IsDead` 或这条旧路径。
- 实施入口：单机 `TickRespawn` 与联网 `ApplyServerRespawn` 已共同经过 `FSMManager.RequestResurrect`，复位逻辑收口在该方法：`BaseFSM` 强制切到 Locomotion 并写齐 `IsJumping`/`BaseState`，`CharacterController` 清 `_jumpRequested`/`_leftGroundTriggerTime`/`_data.HasLeftGround`/`_data.RequestJump`。验收用 `Animator.GetCurrentAnimatorStateInfo(0)` 断言实际状态，不只看参数。

### B3 R 的联网多段判定

- 每次获准施法只产生一个服务端施法实例。使用与单机一致的首判时间（起手剪辑时长加一个 0.2 秒间隔）、后续 0.2 秒间隔和最多 3 秒持续时间。时间参数由服务端认可的技能配置提供，服务端 tick 向上取整，并对客户端真实资产做一致性验证。
- 每段按当时的施法者位置和存活目标重新判定 2.5 米圆形范围。每目标计数独立，最多 5 次。目标在起手后进圈可被后段命中，出圈立即停止吃后续段。同一施法的冷却只登记一次，各段各自结算和广播权威伤害。
- 首先补齐实例的结束语义。死亡、换命、退出和技能取消后丢弃未到期段，客户端播放与服务端判定基于同一实例收尾。保留已发生的命中。若当前协议不能表达合法取消，先补齐取消接线与重复消息测试，再启用多段结算。
- 单机资产的基准伤害是 160，当前联网最多结算一次。恢复多段会提高全段命中的联网伤害。实施计划需记录本次采用单机每段伤害或维持当前联网总量的决定，再把数值写入回归断言。
- **评审补记（实现约束）**：`CombatResolver.ResolveStage` 对玩家施法有 `GroupHitIds` 每目标每施法只命中一次的去重（`Server/Handler/CastHandler.cs:275`）。旋转多段必须按技能豁免该去重并改为每目标独立计数（≤5），`LineAll`/`NearestSplash` 的组内去重语义保持不变——否则只加 StageTicks 会静默丢掉第 2–5 段。
- `SkillEntry` 当前无法表达非投射物多段，需要扩展（首判延迟、间隔、持续时长、每目标上限）；首判延迟 = 起手剪辑时长 + 0.2s，数值来源须与客户端资产一致，并在客户端 EditMode 增加资产→表一致性测试。
- 结束语义：施法者死亡/换命或技能取消时，必须把未到期 `PendingCast` 从 `_pending` 移除，不能只靠逐段 `ResolveStage` 的 alive 检查静默失败；否则复活后仍可能继续结算。
- 取消接线：客户端 `SkillCoordinator.Cancel` 存在，但协议没有取消消息。启用多段前必须补齐取消上报与服务端丢弃，否则已取消的旋转仍会继续扣血。
- 数值决定（2026-11-10 评审）：采用与单机一致的**每段 160、每目标最多 5 段**；`CombatRules` 的 `baseDamage` 不改，后续平衡调整单独进行。回归断言按该口径写。

### B4 贴身攻击与停滞恢复

- 先复现两个独立场景。开阔地贴住一只怪用于排查攻击决策，墙角、窄缝和离开仇恨区域用于排查移动与回巢。记录怪物模板、动作 V1 开关、目标存活、距离、候选技能与冷却、LOS、移动结果与真实位移、动作阶段和拒绝原因。用服务端动作及位置区分怪物确实停住和客户端表现停住。
- 若确认是站位准备抢占，优先施放合法就绪的贴身招式；仅在没有可用近战且存在合法站位时准备有最小距离的招式。为失败和无进展设置明确的退出与退避，下一次重评近战或追击。覆盖半血以下 DogKnight 的 50005 与 50003 竞争，普通怪与 SpiderBoss 作为对照。
- 若确认是移动停滞，对同一目标和目的地持续观察真实进展，成功返回路径本身不能清除停滞证据。使用合法攻击条件区分“已经能打”与“虽近但隔墙”。预算等待与真正不可达分别计数。持续无进展后进入既有重寻、搜索、回巢流程，并遵守墙体、导航网格、脱战和 ORCA 约束。
- **评审补记（已坐实）**：`Attack` 在正常选招前调用 `TrySetupSkill`，而 `SkillSelector.SelectPotential` 完全不看距离；半血 DogKnight 的 50005（priority 10、minRange 2、hpBelow 0.5）会在玩家贴身 <2m 时抢占，进入后撤 setup（最长 30 tick）并跳过本可施放的 50003，失败后还有 30 tick 退避。`SpiderBoss` 的 50006 minRange 0，不会触发 setup，适合作对照。
- `PathFollower` 每次成功重寻都会清零 `_stuckWindows` 与 `_stuckCheckTick`，重寻周期 15–29 tick 永远凑不满 3×10 tick，停滞检测在有路可寻时形同虚设。另有两个次级缺口：`CheckStuck` 只在跟随路径时调用，`Stop`/`Arrived` 分支不检测；`inAttackRange` 用模板 `attackRange` 而非当前技能射程。
- 实现规则：先 `Select` 合法就绪且在射程内的技能，命中才施放；没有可用技能时才评估 `TrySetupSkill`（仅当存在“太近”的潜在招式）。停滞证据只由真实位移或目标点显著变化重置，成功重寻不清零。
- **次级项实施（2026-11-10）**：路径走完（Arrived）分支也累计停滞证据；`Chase` 把当前血量条件下最大技能射程（`SkillSelector.MaxRangeFor`）作为停滞豁免半径，`PathFollower.CheckStuck` 用它替代模板 `attackRange`；无路径分支仍由失败退避/预算重试负责，不叠加检测。测试：`PathFollowerTests`、`SkillSelectorTests`。

### B5 贴怪起跳

- 在平地记录 `IsJumpPressed`、请求是否进入队列、跳跃消费前后接地值、两次 `Move` 的 `CollisionFlags`、垂直速度、实际 Y 变化和联网纠偏。空场、贴普通怪、贴 Boss 三组各测原地与朝目标移动时起跳，再对照怪物主动压入、侧面贴边和场景墙角。
- 若证实侧面接触使接地值丢失，使用能够区分脚下地面与 `NpcBody` 侧面的接地证据消费本次跳跃，保留防御、眩晕、瞄准蓄力和空中二段跳限制。若证实竖直位移被盒体交叠阻挡，修正交叠或竖直位移的处理，保持水平阻挡。若本地已经起跳而服务端拉回，则修正位置同步链。
- 保留 `NpcBody` 层 10 与本地玩家层 9 的碰撞关系，保留相机对该层的排除。可正常起跳的贴身条件与低天花板等物理阻挡分别验收，避免用放宽空中门控或关碰撞掩盖问题。
- **评审补记**：`RequestJump` 读取的是**上一帧**的 `_data.IsGrounded`，`ProcessJumpRequest` 读取的是本次 `ApplyHorizontalMovement` 之后的新值；接地闪烁会同时影响输入门控与消费。复现时需记录这两个时刻的接地值，避免把“上一帧拒绝”误判为“交叠挡住竖直位移”。
- **复现与修复（2026-11-10）**：PlayMode 用真实 `CharacterController` + layer 10 实心盒体复现——贴边/交叠时 `isGrounded=false`（`flags=Sides/None`），`RequestJump` 被接地门控拒绝（`rejectedUngrounded=1`）。修复：`GroundDetector` 在 `isGrounded=false` 时用只含 `GroundLayer` 的脚下球体补接地证据（覆盖 skinWidth 间隙）；再增加 NpcBody（层 10）贴边支撑证据（胶囊膨胀 0.25m），仅在确实贴怪时放行跳跃，普通空中门控不变。复现用例 `PlayerJumpBodyCollisionTests`（空场/贴边/交叠 3/3）；真机与 Boss 侧面仍需复测。

## 3. 交付顺序与验收

| 顺序 | 交付 | 必须通过的验收 |
|---|---|---|
| 1 | B2 复活基础层复位 | 单机、联网各从 JumpStart 与 JumpAir 死亡并复活，出生点正确，当前 Base 层退出跳跃，移动时腿部正常，新一跳可完整落地 |
| 2 | B5 贴怪起跳 | 有可靠地面且头顶有空间时，普通怪和 Boss 贴边原地及前推均能起跳。继续向怪走仍被阻挡，空中重复按跳不起第二次，低天花板仍能挡住 |
| 3 | B3 R 多段 | 起手圈外、旋转中进圈能受伤。持续在圈内每目标上限 5，出圈、死亡或取消后无额外扣血。单机和联网分别验证有效窗口、血量与飘字 |
| 4 | B1 跳跃规避 | 同 XZ 站地正常命中，离开该招式高度范围时未命中，落回范围再次可命中。分别验证 PvE、PvP 和单机，另测高位攻击及格挡回归 |
| 5 | B4 决策与恢复 | 开阔地有合法就绪近战时可攻击。隔墙时保持无伤。墙角暂堵解除后恢复移动或攻击，真正不可达时有界退出，预算不足不误判永久不可达 |

B2/B5 的本地跳跃和复活证据为 B1 的基线。B4 可独立验证，B3 保持施法实例和血量回归单独可测。服务端用例需覆盖当前启用的动作路径，验证 `DamageResult` 与权威 HP，Unity PlayMode 需使用实际 Animator 和碰撞体。最后在 DemoDay 做双客户端及真机复测。

实施进展（2026-11-10 第二轮）：B1–B5 均已实施。服务端 **468/468**；PlayMode
`PlayerJumpBodyCollisionTests` 3/3、`PlayerDeathRespawnTests` 8/8、`MovementSyncTests` 4/4、
`HitJudgmentAlignmentTests` 2/2；EditMode `HitResolverTests` 33/33、`CombatRulesTests` 20/20、
`ProtocolRoundtripTests` 5/5、`SkillCoordinatorSpinTests` 9/9、`SpinAssetTableTests` 1/1、
`FSMManagerHitFeedbackTests` 6/6、`HitAnimatorContractTests` 6/6、`JumpTransitionTableTests` 3/3。
真机、双客户端与 DemoDay 验收待执行；单机本地攻击链的高度行为未改。

## 4. 现有验证与接线索引

已执行服务端定向基线，`HitResolverTests`、`MonsterNavigationTests`、`MonsterActionRunnerTests`、`MonsterSkillCombatTests` 合计 50 项通过。它们未覆盖上述新增复现场景。B2 的 Animator 状态出口已从实际控制器序列化数据核对。B4 和 B5 的用户场景仍需运行时采样。

- [服务端命中裁决](../../../Assets/Scripts/Hotfix/GameSystems/SyncCore/HitResolver.cs)、[移动校验](../../../Assets/Scripts/Hotfix/GameSystems/SyncCore/MovementValidator.cs)、[服务端施法](../../../Server/Handler/CastHandler.cs)
- [基础状态机](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/BaseFSM.cs)、[实际角色 Animator](../../../Assets/RpgDuo/Animator/Character3C.controller)、[角色移动与跳跃](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs)
- [R 技能资产](../../../Assets/PreRes/SkillsCfg/Spin_SkillR.asset)、[技能状态机](../../../Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillStateMachine.cs)、[联网施法上报](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/SkillCastReporter.cs)
- [怪物行为](../../../Server/Monster/MonsterBtActions.cs)、[路径跟随](../../../Server/Monster/Movement/PathFollower.cs)、[客户端怪物身体碰撞](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/NpcBodyCollider.cs)、[服务端移动护栏](../../../Server/Movement/README.md)
