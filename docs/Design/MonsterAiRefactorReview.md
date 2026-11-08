# 怪物 AI、资源与联网一致性评审

日期：2026-11-08。范围：`Assets/Monstor`（排除 Bon）、客户端 Monster、服务端 Monster、NPC 表现与同步协议。

用户确定的优先方向：战斗决策——选招、走位、追击、脱战。Bon 在重构和验收完成后再接入。本轮仅评审，不修改生产代码、场景或怪物资源。

后续更新：已完成 [Unity 专家复核](../superpowers/reviews/2026-11-08-monster-ai-unity-expert-review.md)，并形成 [重构 spec 草案](../superpowers/specs/2026-11-08-monster-ai-runtime-refactor-design.md)。用户后续明确：普通怪攻击前摇被命中可打断，Boss／指定技能按霸体配置。本文的原始证据保留，设计以待评审 spec 的明确合同为准。

## 结论

现有架构可以作为重构基础：服务端单线程 tick 决策、仇恨与目标选择、寻路/ORCA、服务端命中裁决、客户端插值表现，方向正确。当前实现适合少量怪物的基础战斗，但还不足以作为持续扩展智能行为的稳定基础。

推荐保留“模式机 + 行为树”，增加独立动作执行器、统一配置导出与可恢复的动作同步。优先解决资源与配置漂移、动作生命周期和联网时间轴，再加入战术选招及站位。暂不引入 GOAP，也不重写为一个包含所有行为的巨大 FSM。

这里的联网一致性是：同一服务器时间点、同一动作实例、同一目标和判定结果；各客户端沿统一渲染时间轴呈现，允许受网络及帧率影响存在有界显示延迟。无需各客户端重复运行 AI 或保证逐像素、逐帧相同。

## 现状与值得保留的部分

- 联网本地怪物由 `ClientNetworkHost.cs:798` 对场景 Spawner 执行 DespawnAll 并停用。当前没有证据表明正常联网路径在各客户端分别运行怪物 AI。
- 本地 `AIBrain + AIStateMachine` 服务单机；服务端 `MonsterBrain + BT` 服务联网；`NpcEntityAnimator` 是表现状态驱动。三者职责不同，不应直接合并为同一个 Animator 状态机。
- 服务端模式为 Patrol / Combat / Evade / Dead。仇恨切换有 20% 迟滞、同分按 playerId 定序、前摇锁目标、死亡与脱战清理，适合继续保留。
- 寻路失败时停住，不直线穿墙；预算拒绝与真正失败已有区分。攻击起手及判定帧检查 LOS。
- `NpcAnimator_*` 四个控制器采用一致的 MonsterState / AttackVariant / Attack 参数，各有 7 个状态和 9 条转换；Root Motion 关闭。没有发现必须推翻这组基础表现控制器的理由。
- 没有证据支持仅凭控制器使用 AnyState 或统一 Write Defaults=1 就判为错误；需要围绕真实中断、循环及恢复行为验证。

## 问题与方案

### 1. P1：当前模型半径与服务端配置已经不一致，测试仍使用旧目录

证据：

- `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity:9027` 的 NPC 目录已包含 1 Slime、2 Wolf→TurtleShell、3 DogKnight、4 SpiderBoss。
- `Server/Config/monster_config.json` 中模板 1、2 的 bodyRadius 仍为 0.5。
- Unity 读取当前预制体 SkinnedMeshRenderer bounds：Slime XZ 最大跨度 1.71556m，半宽约 **0.858m**；TurtleShell 最大跨度 1.66419m，半宽约 **0.832m**。
- `NpcBodyCollider.cs:82` 使用同一“XZ 最大半宽”口径生成客户端 BodyRadius。
- `Assets/Scripts/Tests/Editor/SyncLayer/MonsterBodyRadiusTests.cs:36` 的 Catalog 仍将 1、2 写为 null，以球体 0.5 校验，因此现有测试通过不能证明当前场景一致。

影响：客户端锁定距离/身体阻挡与服务端命中半径/ORCA 不一致，可能出现边缘可选却打不到、视觉贴合但服务端仍认为有空间等问题。这里是跨端配置口径不一致，不等于已经实测到所有具体症状。

专家复核补充：这些数值证明现行算法口径漂移，不是已确认的设计碰撞尺寸。SMR 默认边界可能包含导入动画范围，世界 AABB 也受旋转/缩放影响；发布 CollisionProfile 前须固定测量姿态、根旋转与缩放，人工校准后导出。逻辑圆与客户端盒体保持有记录的近似关系。

方案：生产目录统一到 MonsterCatalog，编辑期按实际模型导出半径与引用并校验；测试遍历真实目录，不维护手写副本。NavRadius 与 BodyRadius 保持分离，但 NavRadius 的含义必须包含烘焙侵蚀口径。模型 bounds 作为编辑期测量依据，运行时消费明确、稳定的碰撞配置。

### 2. P1：缺少可恢复的当前动作，AOI 新观察者不能可靠还原攻击

证据：

- `RemoteEntityRegistry.cs:402` 在施法广播到达但 NPC 视图尚未存在时直接丢弃。
- `WorldSnapshot.cs` 的 EntitySnapshot 有 ActionState，但没有当前技能实例、技能 ID、起止 tick 或动作阶段。
- `NpcEntityAnimator.cs:95` 只写状态整数；攻击动画需要 `PlayAttack` 的 Trigger。单独收到 Attack 快照无法重建已错过的那次攻击。
- `NpcAttackSchedule.cs:62` 将迟到事件立即触发，未按已过去的时间恢复动画进度。
- `NpcEntityAnimator.cs:100` 的 awaitingConfirm 依赖后续看到 Attack/Hit/Death 才释放。若广播与状态窗口错过，存在等待没有期限的边界风险；本轮未做该条件的网络复现。

影响：持续观察与中途进入 AOI 的客户端可能看到不同的技能过程；迟到客户端从第 0 帧挥刀时，其他客户端可能已到命中/后摇。

方案：将持久动作状态放进快照，事件用于及时触发。动作字段至少包含 entity/generation、actionInstanceId、skillId/presentationId、startTick、phase/phaseStartTick、endTick；按技能补目标和落点。用实例号去重、拒绝过期事件，取消动作带实例号与原因。AOI 重建从快照恢复到当前阶段，不能依赖重新收到历史 Trigger。迟到事件按 renderTick 追帧或跳过已结束的动作。

Unity 2022.3 的 `Animator.Play` 支持 normalizedTime，可用于恢复播放进度；恢复仍需处理跨阶段、已结束及正在混合的情况，不能只加一次 Play 调用就认为同步闭环。

参考：[Unity 2022.3 Animator.Play 官方文档](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Animator.Play.html)。

### 3. P1：受击表现与攻击取消没有统一合同

证据：`MonsterModule.cs:290` 以 HP 下降进入 Hit 并暂停 BT；`CastHandler.cs:187` 的待结算施法只复查施法者存在/存活，没有读取动作实例是否已取消或受击中断。

本轮隔离诊断结果：

```text
castTick=0 resolveTick=6 npcState=Hit hits=1 playerHp=85
```

即起手后第 2 tick 受到普通伤害，第 6 tick 仍处于 Hit，原攻击仍命中玩家。诊断复用当前服务端测试程序集，不连接或停止正在运行的服务器。

这确认了现有行为；是否允许“普通受击不打断”属于设计选择，不能一概把所有受击都改成取消。问题在于当前没有显式、可扩展的中断合同：表现已经显示 Hit，出伤仍继续。

后续用户决策已消除这里的普通怪前摇歧义：普通怪前摇可打断，Boss／指定技能走霸体配置；Active、Recovery、同 tick 顺序和冷却处理见后续 spec。

方案：技能配置声明可打断阶段、韧性/霸体、死亡处理；动作执行器统一处理 Interrupt。可打断动作取消对应 PendingCast 并广播取消；不可打断动作继续执行，受击反馈遵守霸体规则。死亡、眩晕、击退、脱战分别定义优先级；投射物发射后是否随施法者状态取消要单独定义。严禁只清 AttackResolveTick 而不处理裁决队列。

### 4. P1：联网技能时间轴仍是占位配置，没有完整后摇/恢复期

证据：`SyncCore/CombatRules.cs:148` 起的 50001–50006 命中延迟均为 0.35s；15Hz 向上取整实际为 6 tick，约 0.4s。服务端 AttackResolveTick 管前摇锁定，冷却管下一次起手，没有独立的 Active / Recovery 动作状态。

本地资产另配置 AttackWindupTime=0.2s、AttackRecoveryTime=0.3s，它们不驱动联网判定。客户端按 skillId 写死映射到 Attack01/02，50005/50006 复用既有动画且没有专属 NPC VFX 接线。

Unity 读取实际剪辑还发现：DogKnight、Slime、TurtleShell 的 Attack01/02 和 GetHit 长约 0.833s，导入的 isLooping=true；Spider 的 attack1/2、hit1 长约 0.667s，isLooping=false。前者不自动等于播放错误，因为控制器仍能通过转换退出，但当前缺少统一的动作时长与循环合同。建议行走/待机循环，普通攻击/受击按一次播放配置；持续攻击单独声明，并实测命中标记和退出条件。仅凭剪辑总长不能确定伤害应在哪一帧发生。

专家复核补充：一次播放应由联网独立表现合同保证，必要时使用独立资产；不批量修改共享 FBX 的 loopTime 或控制器设置，以免改变本次冻结的单机行为。

影响：逻辑结束点、剪辑结束点、命中时点缺乏共同来源；增加突进、多段技、蓄力或可取消后摇会继续向 BT、裁决器和动画驱动添加特例。

方案：SkillDefinition 定义 Windup → Active（可多次）→ Recovery → Completed/Cancelled，以及转向、移动、目标锁定、中断窗口。服务端 tick 决定命中与动作结束；客户端用同一份导出的表现元数据对齐动画速度、阶段和 VFX。AnimationEvent 可以做表现或编辑期标记，不能成为联网伤害真值。

### 5. P1：追击不可达目标没有向上反馈的退出机制

证据：`PathFollower.cs:68` 只返回是否移动，真正失败退避 30 tick 后重试；无路径、预算等待、路径走完和 ORCA 暂停对调用方都可能只是 false。`MonsterBrain.Update` 的脱战主要看离巢距离、目标有效性/距离、无目标超时。当前有效且在圈内的不可达目标不会因长期寻路失败自然触发这些条件。

已知地图存在互不连通的导航区域；当前组合存在目标仍有效而怪物长期原地重试的设计缺口。本轮没有另做隔岸双客户端实景复现。

方案：导航返回 Moving / Arrived / PendingBudget / TemporarilyBlocked / Unreachable 等明确结果。战斗层统计真正失败和有效进展，进行替代接近点、短时搜索、换目标、最终 Evade。预算等待不计失败；短暂拥堵不立刻丢仇恨。失去 LOS 的持续时间、追击无进展上限、不可达目标冷却分别配置。

### 6. P2：当前选招是确定性优先级过滤，尚未形成战术决策

证据：`SkillSelector.cs:34` 按距离、血量、冷却过滤后选固定最高 priority；`:57` 的停步忽略冷却；`MonsterBtActions.Attack:109` 无可用技能时原地等待。当前模板都有覆盖近距离、minRange=0 的常驻基础技能，NeedsReposition 很难成为当前配置的实际战术分支。

这不是算法错误，但“加技能列表”不会自动获得灵活走位。怪物缺少优选作战距离、绕侧、冷却期间动作、目标趋势、站位安全性与连续选招约束。

方案：先保留硬条件，再对少量候选行动评分，例如：技能收益、优选距离、玩家正在施法、目标侧后方、路径代价、近期已用技能。输出 Attack / Approach / Strafe / Reposition / Wait / Search / Evade 意图。加入最短意图保持时间和切换阈值，避免每 tick 抖动。若需要随机变化，由服务端选择并同步最终动作，客户端不重新抽签。

首批只做：技能优选距离、冷却中绕行/防御/等待策略、追击失败退出。群体包围和协同进攻留后续。

### 7. P2：资源、身份和本地/联网配置缺少一致的发布入口

证据：

- 模板 2 的服务端身份与显示名仍是 Wolf，DemoDay 却使用 TurtleShellPBR。是否作为临时占位需明确，不能据此假定联网已有龟防御。
- DogKnight 本地资产 MaxHP=100 / MoveSpeed=3.5；服务端为 300 / 2.8。Spider 本地为 100 / 3.5；联网 SpiderBoss 为 1500 / 3.2。可以是刻意分模式设计，但目前不是同一配置入口。
- Slime 本地 EnableTaunt=true，TurtleShell 本地 EnableDefend=true；服务端模式/动作没有相应行为。服务端 Taunt API 是强制仇恨，与 Slime 的嘲讽表演是两种语义。
- Unity 实测 SlimePBR/TurtleShellPBR 的 NavMeshAgent 为 enabled=true、updatePosition=true、updateRotation=true；`RemoteEntityRegistry.cs:734` 的 StripGameplay 只移除 MonsterEntity，保留该 Agent。它违反联网视图只消费服务端移动的预期边界；实际帧内 Transform 冲突仍需 PlayMode 验证。

方案：稳定 templateId 绑定 Catalog；服务端数值、客户端表现配置从同一编辑源导出，允许明确命名的模式覆写。拆成纯视图 Prefab 与本地玩法装配，不靠运行时逐项剥离长期维持边界。联网视图禁本地 NavMeshAgent/自主移动/伤害接口；保留 NpcBodyCollider 和只读 Targetable。

本地 AI 的后续选择：如果仅供资源预览，收敛为预览适配器；若仍要求完整单机玩法，优先复用不依赖 Unity 的决策/动作规则，通过离线宿主和适配器提供世界信息。不要在两套实现分别继续添加相同战斗行为。

### 8. P2：BT 缺少动作生命周期，控制状态与大规模调度需要补设计

`BtNode.cs` 只有 Tick；Selector/Sequence 每 tick 从首个子节点评估，是轻量响应式实现。对现有短动作合理；对持续蓄力、跳跃、突进、连段而言，没有统一 Enter / Tick / Exit / Abort 容易留下导航目标、施法队列和计时器。

方案：根节点保留响应式的死亡/控制/脱战优先级，持续动作由 ActionRunner 持有，只有明确中断或完成才退出。不要把所有 Selector 简单改为记忆型，否则高优先级中断反而可能无法及时执行。控制状态单列，动作能力通过 CanMove / CanTurn / CanCast 等规则判断，避免行为节点各写一份控制判断。

感知现状：`MonsterBrain.cs:191` 只查距离/视野锥，首次获取没有障碍 LOS；若怪物设定为视觉感知，应补视线检测和感知记忆。若允许隔墙感知，应作为感知类型配置，避免与“看见”混用。

移动平台扩展风险：`RemoteEntityRegistry.cs:293` 明确排除 NPC 分档；当前 Full/Proxy 数量不代表怪物的模型/动画预算。服务端感知逐怪遍历实体，部分 tick 路径仍分配 List。尚无本轮同屏大数量实测，不能仅凭 4–6 只怪判定满足 MMO 性能目标。

后续可将感知/战术思考错峰降频，动作计时、移动及判定仍沿服务器固定 tick。客户端分别管理视觉细节和碰撞/目标数据生命周期；不能因某个客户端距离远就改变服务器怪物逻辑。

## 推荐架构

```text
统一编辑配置 → 校验与导出 → 服务端逻辑配置 / 客户端表现目录

服务端世界与伤害事件
        ↓
感知记忆 + 仇恨/目标选择
        ↓
模式机：Patrol / Combat / Evade / Dead
        ↓
行为树：优先级、流程与回退
        ↓
战术选择：硬条件 + 小规模效用评分
        ↓
ActionRunner：动作实例、阶段、移动/转向约束、中断
        ↓
导航/ORCA + CombatResolver
        ↓
动作快照 + 带实例号的施法/取消事件 + 权威伤害结果
        ↓
客户端统一 renderTick：插值、动作恢复、动画、VFX、UI
```

行为树决定“做什么”，执行器保证“完整地做完或明确地取消”，Animator 负责“如何呈现”。共享决策代码必须不依赖 UnityEngine。

方案比较：

| 方案 | 适配当前项目 | 取舍 |
|---|---|---|
| 继续扩充现有 BT 条件和静态动作 | 短期容易 | 时间轴、中断、持续动作问题继续扩散，不推荐作为重构终点 |
| 模式机 + BT + ActionRunner，选招逐步加入评分 | 推荐 | 复用现有仇恨/导航/裁决，改动边界清楚，适合战斗扩展 |
| 全部改成单一 HFSM | 可行但无明显收益 | 技能、移动、控制、Boss 阶段的组合会扩大状态/转移数量 |
| 完整 GOAP/复杂规划器 | 当前不优先 | 当前问题首先在动作与同步合同；增加规划器无法直接解决 |

## 重构顺序与验收

1. **配置和资源基线**：统一目录、模板身份、实际半径、纯视图 Prefab、真实目录驱动的测试。先保留现有战斗行为建立对照。
2. **动作执行与同步**：动作实例、Windup/Active/Recovery、中断策略、快照恢复、过期与取消事件处理。协议字段与掩码只追加，保持现有 tag/数值契约。
3. **选招与走位**：优选距离、技能评分、冷却期策略、追击无进展处理、搜索和脱战；加入意图保持/迟滞。
4. **多人及负载验收**：完成下列测试，再扩 Boss 阶段、控制效果与更多怪物内容。Bon 此后再接入。

必需验收：

- 三客户端：A 引怪，B 持续观察，C 在前摇/命中/后摇分别进入 AOI；用 actionInstanceId 对齐同一 renderTick 的阶段和技能。
- 加延迟、抖动、短暂停帧，验证晚到广播、广播先于视图、取消先后顺序、旧事件重放不导致重播或卡忙位。
- 普通受击、霸体、强控、死亡、目标死亡/离线、切目标、Evade 与待结算技能的组合矩阵。
- 墙后/隔岸/岩石上目标、寻路预算耗尽、临时拥堵；真正不可达最终有恢复路径，预算等待不触发错误脱战。
- 怪物血量、命中结果由服务端下发，各客户端不重算；本地角色被怪物碰撞后的纠偏仍做多人实景验收。
- 自动遍历 Catalog，验证 templateId 唯一、Prefab/Animator 合同、技能表现映射、半径、导航参数与配置版本；客户端视图不含自主导航或伤害入口。
- 按约定 Android 设备与同屏目标数量测量 p95、动画/蒙皮、网络开销、服务端 tick 和 GC。本轮不能给出“已达标”的结论。

## 本轮验证与边界

- 服务端：Monster 120、BrainTests 8、ThreatTableTests 8、SkillSelectorTests 6、PathFollowerTests 15，共 **157 通过，0 失败**。
- Unity EditMode：NpcEntityAnimator、NpcAttackSchedule、MonsterConfigNumbers、MonsterBodyRadius、NpcViewGameplayStrip 相关查询共 **22 通过，0 失败**。
- Unity 读取实际资源：模型 bounds 与 NavMeshAgent 开关；静态审查四套联网控制器、本地配置、生产场景目录、服务端行为及同步字段。
- 隔离诊断确认 Hit 状态下既有 PendingCast 仍造成伤害；诊断文件在 `.tmp/monster-ai-review/`，未改生产实现。
- 未执行本轮三客户端怪物端到端、弱网测试、NavMeshAgent 帧内冲突实测、Android 性能验收。现有测试全绿不能覆盖这些边界，也不能排除旧测试目录与场景漂移。
