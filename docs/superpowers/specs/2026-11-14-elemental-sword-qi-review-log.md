# 属性剑气设计评审日志

## Review — 2026-11-14 — Verdict: NEEDS REVISION

Scope signal: XL
Specialists: 主审（lean 模式；未派生子代理）
Blocking items: 7 | Recommended: 10
Prior verdict resolved: First review

Summary: 服务端单一结果、版本化投影与确定性 tick 顺序方向正确；但按现有代码接线，表现层不能保证三端画面一致。阻塞项集中在：施法被打断没有下行取消、状态投影没有 AOI 通道/脏位、灼烧跳伤会走普通受击路径、`CastId`/事件 ID/攻速未进协议、PvP 位移预算缺少输入模型、状态移除时钟归属不清、同 tick 死亡边界未闭合。

### 阻塞项与修订

| # | 问题 | 修订 |
|---|---|---|
| B1 | 麻痹/死亡打断施法时，旁观者仍把攻击动作播完、剑气继续飞；`NpcActionEvent` 协议存在但 Server 从未发送，`AoiBroadcaster` 不填 `NpcAction` | 新增 `CastCancelBroadcast`；怪物复用 `NpcActionPhase.Cancelled`/`NpcActionCancelReason`；客户端按 `CastId` 停止攻击表现，未发射剑气不再生成；`AoiBroadcaster` 进入基线带动作快照 |
| B2 | `EntitySnapshot` 无状态/战斗版本，`AoiBandScheduler` 无状态脏位；状态开始/刷新/结束无法可靠下发 | 新增 `CombatStateUpdate` + `CombatProjection`，用 `EntityFieldMask.Combat = 1 << 10` 承载；AOI 进入/复活基线携带完整投影；`AoiBandScheduler` 把 `CombatVersion` 变化视为立即脏位；4Hz 保活不重复携带完整状态列表 |
| B3 | 灼烧每跳走普通受击路径，触发受击硬直、命中火花、格挡反馈 | `HitEntry` 增加 `DamageKind/EventId/EventSeq/CombatVersion`；客户端 DoT 分支只更新权威血量/死亡与飘字，跳过受击硬直、格挡、击退和命中特效 |
| B4 | `CastBroadcast` 无 `CastId`、攻速、元素；远端按 `(casterId, skillId)` 找投射物，连续两发会错 | `CastBroadcast` 增加 `CastId/AttackSpeedMultiplier/ElementId`；新增 `ProjectileLaunchBroadcast`/`ProjectileStopBroadcast`；远端 `PlayAttack` 按攻速缩放；本地 `SkillExecutor.OnSwordQiFrame` 联网不再生成剑气视觉 |
| B5 | `MovementValidator` 只用 15 m/s 上限且瞬移门槛 50m；`PositionSyncRequest` 无移动模式/跳跃阶段/位移技能/控制版本；服务端用客户端时间戳算预算 | `PositionSyncRequest` 增加 `MoveMode/JumpPhase/DisplacementSkillId/ClientControlVersion/LifeId`；服务端只按接收 tick 给预算，同一 tick 共用、长间隔有上限；本地速度单点合成为 `Base × Defend × Skill × Status` |
| B6 | 状态特效若各自按本地 `EndTick` 移除，会因时钟偏移在不同时刻消失 | 状态挂载/刷新/移除以 `CombatStateUpdate`（或携带同一版本的死亡/离场事件）为准；本地 `EndTick` 只做倒计时和预期淡出 |
| B7 | `ApplyHit` 扣血前不复查存活；挥砍致死 + 剑气仍补一条 `HitEntry`；风到期/灼烧最后一跳/灼烧致死同 tick 边界未定义 | `ApplyHit` 扣血前复查 `target.IsAlive`，已死跳过且不生成 `HitEntry`；新增同 tick 顺序表与风/灼烧/死亡边界测试 |

### 建议项

- DoT 仇恨按 `damage × ThreatPerDamage` 计，不叠加 `ThreatFlat`，避免每跳固定 +10 抢仇恨。
- 怪物 DoT 必须走 `MonsterModule.OnDamageTaken(kind: Dot)` 更新 `LastHp`，不进入 `HitStartTick`/`ActionState=Hit`。
- 明确玩家技能霸体只免除打断、麻痹仍挂上；怪物前摇霸体按首版规则跳过麻痹。
- 候选、AOI、广播收件人统一按 `CombatSceneId` 过滤，不能只按坐标/AOI 判定。
- 剑气速度以 `ProjectileSpec.Speed` 为权威，构建校验 `Length / Speed == TotalFlightSeconds`。
- 第一段剑气延迟（段末距离/速度约 0.22s）需实测，决定按目标实际距离结算或细分 1m 子段。
- 同一 tick 出站事件按 `EventSeq` 排序，避免异步发送导致客户端伤害数字顺序不一致。
- 联网时关闭本地剑气生成，避免本地预测与服务端发射描述重影。
- `NpcActionEvent`/`NpcActionHistory` 要么接入并测试，要么标注未启用，避免被误认为已打通。
- 状态投影/装备摘要纳入 AOI 带宽预算测试，避免完整状态列表进入 4Hz 保活。

### 修订状态

2026-11-14 已按上表修订 [2026-11-14-elemental-sword-qi-design.md](2026-11-14-elemental-sword-qi-design.md)，新增「下行消息与战斗投影」「取消、状态投影与同 tick 顺序」两节及对应验收场景。后续复审重点：B1 的取消广播三端验证、B2 的状态投影带宽、B5 的 PvP 位移预算包字段与公式。

## Review — 2026-11-14 — Verdict: NEEDS REVISION（复审 B1/B2/B5）

Scope signal: XL
Specialists: 主审（lean 定向复审；未派生子代理）
Blocking items: 12 | Recommended: 8
Summary: 修订稿已补齐上次 B3/B4/B6/B7；B1/B2/B5 仍有落地缺口：取消广播缺少客户端双路径消费与玩家 CastId 分配，NpcAction 从 AOI 到客户端整条链未接通；CombatVersion 未进入普通 EntitySnapshot，投影下发存在双通道且未绑定 100 KB/s 带宽预算；技能位移缺少服务端 DashDistance/DashDuration，SkillDashComponent 绕过单点速度合成，JumpPhase 无法区分上升/下落。用户裁决：接通 NpcAction 全链；投影按单一变更通道；跳跃客户端起跳 + 服务端观察；麻痹保留空中水平惯性。修订后已补 NpcActionEvent/AOI 基线消费、CastId 分配与按 CastId 取消、EntitySnapshot.CombatVersion/LifeId、AoiBandScheduler 发送原因、ActiveCast 基线、共享位移/跳跃参数与对应验收场景。
Prior verdict resolved: No — B3/B4/B6/B7 已闭合，B1/B2/B5 部分闭合，复审后已按裁决补齐。

## Review — 2026-11-14 — Verdict: MAJOR REVISION NEEDED（full 专家复审）

Scope signal: XL
Specialists: network-programmer、systems-designer、game-designer、qa-lead、unity-specialist、creative-director
Blocking items: 20 | Recommended: 8
Summary: 5 位专家一致认为上一轮“B1/B2 已闭合”判断过于乐观。硬阻塞：NpcAction 的 Revision 每动作实例重置，必须按 `(EntityId, LifeId, InstanceId, Revision)` 合并；客户端无 NpcAction 消费层/取消 API，事件会被插值时间轴回滚；快照强带 CombatVersion/LifeId 把 60B 推到 74B、111 KB/s 超预算；B5 的 SkillFactor 量纲算重（m/s × BaseSpeed≈100m/s）且仍信客户端时间戳；攻速没有 per-clip 动画通道；PvP 90+90 对 100 HP 两刀定生死。用户裁决：玩家 HP 改 720、采纳 PvP 控制规则包（风 1.25–1.3 且不吃 DoT、控制结束 0.5s 免疫、同组易伤 4s 不刷新、雷 0.8s）、元素改为首个有效命中附加、攻速走 Animator `AttackSpeed` 参数。修订后已补合并键/动作存储/CastId 全链、LifeId 与 CombatVersion 条件携带、ControlVersion、分项位移预算、服务端接收 tick、跳跃接地校验与 Y 夹取、Dash 活动窗口、DoT 末跳、10002 Speed>0、参数区间、EventSeq 空洞与 AC 数值断言。
Prior verdict resolved: No — 定向复审的 B1/B2/B5 被 full 复审判定未闭合；B3/B4 文档意图已闭合、B7 末跳与 10002 Speed=0 仍缺，已在本次修订补齐。

## 2026-11-14 实现计划复核与回写

以下问题来自对现有代码和 Plan 01 的交叉检查。修订已写入规格和计划，运行时验收随实施进行。

| 优先级 | 复核问题 | 文档修订 |
|---|---|---|
| P0 | 登录没有武器发放或库存基线，客户端没有背包请求，协议未登记到编解码注册表 | Plan 01 任务 1–3、5 补运行时共享表、功能位和指纹、编解码登记、从正式登录到背包换装、拒绝恢复与重试幂等 |
| P0 | `CastId` 只写施法广播，服务端命中和本地停点没有贯通 | Plan 01 任务 2、4、7 补本人 `CastConfirm`、服务端 `HitEntry.CastId`、权威发射与终止；本地和远端都按同一 `CastId` 播放和停止 |
| P0 | 武器只影响广播和动画，未参与伤害、判定和冷却；默认射程与线上基线冲突 | Plan 01 任务 1、4、6 纠正为 12 米，补共享时序、伤害消费、实际控制器和在途换装验证 |
| P1 | NPC 动作实例 ID 跨实体重复；旧血量快照到达复活后无法辨别生命代次 | 设计采用全世界唯一 `CastId`，NPC 动作继续用实体内 `InstanceId`；Plan 02 每个血量/死亡增量带 `LifeId` 和 `CombatVersion`，以实际序列化数据重算带宽 |
| P1 | 元素首个有效命中与架构接线冲突；路线图的 02/03 并行口径冲突 | 设计规定同次施法的挥砍和剑气共用目标附加记录；路线图明确 03 可预先做独立位移测试，真实控制投影集成依赖 02，元素驱动由 04 接入 |

对应文档见[设计规格](2026-11-14-elemental-sword-qi-design.md)、[实现路线图](../plans/2026-11-14-elemental-sword-qi-roadmap.md)及 [Plan 01](../plans/2026-11-14-elemental-sword-qi-01-foundation.md)。
