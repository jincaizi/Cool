# 属性剑气实现路线图

日期：2026-11-14
规格：[2026-11-14-elemental-sword-qi-design.md](../specs/2026-11-14-elemental-sword-qi-design.md)

规格跨越协议、服务端裁决、客户端表现、动画资产与 PvP 规则，按“每个计划能独立产出可工作、可测试软件”的原则拆成 5 份计划。按顺序执行；每份计划结束时都应能通过自己的验证命令，并更新对应模块 README。

| # | 计划 | 交付物 | 依赖 |
|---|---|---|---|
| 01 | [共享表、权威换装、普攻参数与剑气快照](2026-11-14-elemental-sword-qi-01-foundation.md) | 同源配置与入场指纹、登录发放与背包装备闭环、武器参数驱动实际普攻、全局 `CastId` 与本人确认、联机剑气统一发射/终止、跨场景命中隔离 | 无 |
| 02 | [B1 取消 + NpcAction 全链 + B2 战斗投影/带宽](2026-11-17-elemental-sword-qi-02-cancel-projection.md) | `CastCancelBroadcast`、`NpcActionEvent`、客户端动作存储、`CombatStateUpdate`/`CombatProjection`、血量 `LifeId`/`CombatVersion`、AOI 基线与在途剑气补发、带宽门禁 | 01 |
| 03 | B5 位移/跳跃预算 + PvP 控制 | `LocomotionMode/JumpPhase/JumpSeq/VerticalVelocity/DisplacementSkillId/CastId`、服务端接收 tick 预算、Dash 活动窗、跳跃惯性、PvP HP/控制规则包 | 01、02 |
| 04 | 状态结算 + DoT + 表现 | PvE/PvP 共用冰/火/雷/风状态运行时、同次挥砍和剑气只附加一次、同 tick 顺序、DoT 末跳、`DamageKind` 分流、元素 VFX | 01、02；PvP 端到端验收还需要 03 |
| 05 | [三客户端验收 + 表现收口](2026-11-20-elemental-sword-qi-05-acceptance.md) | 三端一致性、带宽实测、麻痹惯性/推挤稳定性（静态盒）、KCP/WebSocket 收包注入（重复/乱序）、元素表现回退、AOI 观察者反查索引（`GetObserversOf` O(N²)） | 01–04 |

## 当前状态（2026-11-20）

| # | 计划 | 状态 | 说明 |
|---|---|---|---|
| 01 | 共享表、权威换装、普攻参数与剑气快照 | ✅ 已完成 | 50/50 步骤；服务端 512/512、Unity EditMode/PlayMode 相关测试全部通过；模块 README 已回写 |
| 02 | [B1 取消 + NpcAction 全链 + B2 战斗投影/带宽](2026-11-17-elemental-sword-qi-02-cancel-projection.md) | ✅ 已完成 | Part A/B 全部完成并回归：取消/NpcAction、战斗投影、AOI 基线/条件字段、在途剑气补发与 100 KB/s 门禁；模块 README 已回写 |
| 03 | [B5 位移/跳跃预算 + PvP 控制](2026-11-18-elemental-sword-qi-03-displacement-pvp.md) | ✅ 已完成 | 10/10 任务；服务端 587/587；Unity 目标测试类通过（`RemoteEntityRegistryTests` 仅既有 `Bot_Radius` PPtr 环境失败，基线可复现，不计回归）；模块 README 已回写 |
| 04 | [状态结算 + DoT + 表现](2026-11-19-elemental-sword-qi-04-status-dot-vfx.md) | ✅ 已完成 | 8/8 任务；服务端全部 654/654；Unity 目标 EditMode 类通过（`ElementalApplicationTests` 仅服务端存在；`RemoteEntityRegistryTests` 仅既有 `Bot_Radius` PPtr 环境失败，基线可复现）；PlayMode 五类回归完成（`MonsterWorldTests` 导航模式既有 Slime 巡逻卡死为已知导航债）；模块 README 已回写。在线 PvE/PvP 权威状态已完成；**离线单机共享运行时未接入，留给 Plan 06/backlog（2026-11-20 修正）** |
| 05 | 三客户端验收 + 表现收口 | ✅ 已完成 | 6/6 任务；服务端 668/668（基线 654 → AOI 10 + 带宽 2 + 服务端预算 2；含最终审查补充的 NPC 目标反查用例）；Unity EditMode 目标类 `ElementVfxFallbackTests` 5/5、`SwordQiElementTintTests` 4/4、`StatusVFXSystemTests` 3/3、`NetworkConsumptionInjectionTests` 14/14、`ProtocolShapeGuardTests` 17/17（`RemoteEntityRegistryTests` 16/17，仅既有 `Bot_Radius` PPtr）；PlayMode 9/9（三客户端元素 1/1、三客户端战斗一致性 1/1、`MovementSyncTests` 4/4、`PlayerControlInertiaTests` 3/3，真实 Server3C:8000）；带宽 100 实体 ×15Hz×60s 平均 53.4 KB/s、100 实体同时 DoT 最坏 76.9 KB/s（预算 100 KB/s，未上调）；收包注入目标类合计 72/72；AOI 反查 N=1000 ≤1ms、索引与暴力逐项相等。**未新增战斗机制/协议字段；离线共享运行时与 Addressables 美术资源签收移至 Plan 06/backlog。设计规格中未执行的三端 DoT/末跳致死、麻痹取消前摇 `Cancelled` 链、状态结束时钟偏移、AOI 快速出入/复活版本连续、KCP/WS 延迟注入、断线重进旧包与模型未加载场景已登记到 Plan 06（见下），不得视为已验收** |

**换装跟进（2026-11-16）**：简易武器背包、男女八把元素剑校准变体、权威确认对账与
交互边界已完成，自动测试全绿；握姿/穿模/拖尾、DemoDay 交互冒烟与发布构建冒烟仍待人工验收。
详见 [换剑与持剑适配设计](../specs/2026-11-16-weapon-bag-sword-fit-design.md) 与
[实现计划](2026-11-16-weapon-bag-sword-fit.md)。

Plan 03 补齐位移/跳跃预算与 PvP 基线（720 HP、接收 tick 预算、突进窗口、控制投影消费）；
Plan 04 补齐服务端权威四状态（冰/火/雷/风）、DoT 末跳、`DamageKind` 分流与客户端
状态/剑气表现。Plan 05 完成 AOI 观察者反查索引、三客户端元素一致性、带宽/注入验收、
麻痹惯性/推挤稳定性（静态盒）与元素表现回退（`ElementVfxFallback` 只锁定“资源命中 → 资源自带颜色，
否则程序化调色板”的判定顺序；生产 Addressables loader/Host 未接入）。
**明确边界**：在线 PvE/PvP 权威状态与三客户端验收已完成；离线单机仍使用本地
`StatusEffectManager` 的冰冻/灼烧（仅怪物、独立计时），四元素共享运行时、离线去重与
元素 VFX 资源化属于 Plan 06/backlog。

## mainline 合并集成（2026-11-20）

`codex/elemental-sword-qi-foundation` 已合入 mainline 的“剑气回荡”（`2026-11-17-sword-qi-echo`）。
两条线都改了协议、`CastHandler`、`PlayerState`、`CombatNetworkBridge` 与 `ClientNetworkHost`，集成裁定如下：

- **协议编号**：mainline `SwordQiEchoState = 305` 保持不变；属性剑气 7 条消息由 305–311 顺延到
  **312–318**（符号名不变）。`CastRequest` 的 tag 8/9 被回声占用，属性剑气 `ClientCastSeq /
  EquipmentRevision / CasterLifeId` 顺延到 **10/11/12**。能力位 `SwordQiEcho = 1u << 2` 保持不变，
  `ElementalSwordQiV1` 由 bit2 改为 **`1u << 4`**（`PvpMovementBudgetV1` 仍为 `1u << 3`）。
- **生命代次统一**：mainline 的 `PlayerState.CombatLifeId` 与属性剑气的 `LifeId` 是同一概念，
  合并后只保留 `LifeId`；回声的锁定/无敌窗口与伤害包统一读 `LifeId`。
- **包路径决策 tick 统一**：mainline 旧注释假设包处理时 `CurrentTick` 落后一 tick（`+1`）；
  属性剑气 Plan 04 已让 `GameLoop` 在包处理前推进 `CurrentTick`。合并后普通施法与回声都直接取
  `_server.CurrentTick`，不再 `+1`。
- **验证**：服务端 **689/689**；Unity EditMode **955/959**（4 个环境失败：`NpcCatalogAssetTests`
  缺发布资产、`PlayerBodyTintTargetTests`/`ResourceAsyncLifecycleTests` 的 batchmode PPtr 与异步
  完成时序）；PlayMode 定向 **剑气回声表现 3/3、三客户端元素验收 1/1（真实 Server3C:8000）、
  麻痹惯性 3/3、移动端技能 HUD 1/1**。
- 合并提交为 merge commit；`backup/elemental-pre-merge-20261120` 保留合并前分支头。

## 执行顺序与并行度

1. **01 必须先做**：它定义共享 DTO、协议字段与 `CastId`，后续计划的类型都以它为准。
2. **02 完成后接入 03**。01 完成后，03 可先用测试状态做纯位移预算和跳跃轨迹测试。控制版本、客户端锁定与解除接入须等 02 的 `ControlSummary` 和生命代次契约落地。03 用注入的权威控制状态验证输入/投影闭环，实际元素命中统一在 04 接入。
3. **04 依赖 02**。状态变更走 `CombatStateUpdate`，DoT 事件走 `DamageResult/HitEntry`。PvE 链路可先验收，真实 PvP 冰雷对局须合入 03 的移动与施法控制。
4. **05 最后做**：它验证 01–04 的三端一致性，不再新增机制。（2026-11-20 已完成）

## Plan 05 收口项结果（2026-11-20 完成）

- **AOI 观察者反查 O(N²)**（2026-11-18 登记，Plan 05 任务 1 完成）：`AoiGrid` 维护
  `entityId → 观察者` 反向索引，`GetObserversOf` 只扫观察者集合；`Server3C` 在索引路径
  按 `LeaveRadius`（90m）剔除陈旧边，首 tick 尚无观察者快照时保留暴力兜底。
  `AoiReverseIndexTests` 9/9（任务交付时）：随机 50 实体 × 30 tick 与暴力结果逐项相等、80/90m 迟滞、
  `RemoveEntity`/`RemoveObserver` 清理、重登不残留；N=1000、100 观察者单次反查 ≤1ms
  （Debug 本机中位数）。服务端全量 654 → 663（AOI）→ 667（含带宽/预算）；最终审查补
  1 个 NPC 目标反查用例后 668/668。
- **三客户端元素验收**（2026-11-19 登记，Plan 05 任务 2 完成）：
  `ThreeClientElementalAcceptanceTests` 1/1（真实 Server3C:8000）——A 雷剑施法，A/B/C
  对齐同一 `HitEntry` 的 `CastId/EventSeq/CombatVersion/TargetRemainingHp`（严格相等），
  B 本人与 C 旁观视图的 311/301 施加-移除各恰好一次，施加→清空服务器时间差 0.781s
  （窗口 0.700–1.050s）；`ThreeClientCombatConsistencyTests` 1/1 回归通过。
- **带宽门禁**（2026-11-19 登记，Plan 05 任务 3 完成）：100 实体 ×15Hz×60s，常规回放
  3,204,397 B/60s（平均 53.4 KB/s，单 tick 峰值 3,980 B，投影基线 227 B）；100 实体
  同时 DoT 最坏变体 4,615,939 B/60s（76.9 KB/s，单 tick 峰值 27,542 B）。预算
  `BandwidthBudgetBytesPerSec = 100_000` 未上调；状态/DoT 流下限与最坏变体均有断言。
- **收包重复/乱序注入**（2026-11-19 登记，Plan 05 任务 4 完成）：
  `NetworkConsumptionInjectionTests` 14/14；加既有目标类合计 72/72。桥层按 `EventId`
  去重命中、按 `CastId` 去重 `CastBroadcast`，旧版本 `CombatStateUpdate` 丢弃，停点先到
  跳过补建视觉；`ClearDedupe` 在断线/重建路径清空窗口。
- **麻痹空中惯性 / 推挤稳定性（静态盒）**（Plan 05 任务 5 完成）：`PlayerControlInertiaTests` 3/3
  （空中麻痹 0.3s 位移 1.521m、落地累计 3.650m、地面麻痹 0；贴 `NpcBody` 往返幅度
  0.0000m、最大 z 0.410）；服务端 `PvpMovementBudgetTests` 9/9（空中锁定仅发放跳跃惯性
  6.0m = 6 m/s × 1.0s，普通预算为 0）。**只覆盖静态盒贴附的本地稳定性**；移动
  `NpcBody` + 位置上报/纠偏联测登记到 Plan 06（见下）。
- **元素表现回退**（Plan 05 任务 6 完成）：`ElementVfxFallback.ResolveColor` 锁定
  “ViewKey 非空且 loader 命中资源 → `Color.white`（资源自带颜色，Host 实例化），否则
  `ElementPalette.ColorOf(elementId)`”；`ElementVfxFallbackTests` 5/5。未实际调用
  Addressables；生产 loader/Host 接入与美术资源签收属 Plan 06。

## Plan 06 / backlog 登记（2026-11-20）

以下项**不属于** Plan 01–05 已交付范围，登记到 Plan 06/backlog，不得写成已打通：

- **Plan 05 未执行的设计验收场景**（2026-11-20 登记；Plan 05 已交付的是子集，以下不得写成已验收）：
  - **三端 DoT/末跳致死**：三端每跳伤害/余血/`DamageKind=Dot` 一致，受击者不被跳伤打断攻击，
    观察者不播命中火花/受击硬直（Plan 06 任务 1 验收）；**末跳致死**由服务端
    `DotDamageTests.Burn_Tick_That_Kills_Player_Clears_Statuses_And_Stops` 证明（`MaxHp=8`，
    一跳致死、只一条 DotHit、死后不再跳）；**三端同一次末跳致死的死亡时刻**在 720 HP 联机场景
    无法确定性构造（直伤 90×2/次、灼烧 8/跳、暴击 15%），需服务器 HP 注入/测试钩子，登记 Plan 07。
  - **麻痹取消前摇三端 `Cancelled` 链**：A 的雷剑气命中 B 进行中的前摇，三端按同一 `CastId` 记录
    `CastCancelBroadcast.EffectiveTick`；B/C 停止攻击动作，不生成未发射剑气与后续 `DamageResult`，
    已发射的独立投射物继续飞行；玩家路径断言 `RemoteCastPresenter`/`RemoteEntityAnimator.CancelAttack(CastId)`，
    怪物路径断言 `NpcAttackSchedule`/`NpcEntityAnimator` 停止且 `NpcActionEvent` 按 `(InstanceId, Revision)`
    合并、`Phase=Cancelled` 生效（`design.md:167`）。
  - **状态结束时钟偏移注入**：给两台客户端注入不同时钟偏移和渲染延迟，断言特效移除由同一条
    `CombatStateUpdate` 触发；权威移除晚于 `EndTick` 时保持到消息到达（`design.md:169`）。
  - **AOI 快速出入/复活版本连续**：AOI 快速出入、死亡复活、Evade 到家、回池与断线重进时，
    进入基线与后续 `CombatStateUpdate` 的 `CombatVersion`/`LifeId` 连续，旧版本增量不能复活
    旧状态或旧装备（`design.md:170`）。
  - **KCP/WebSocket 延迟注入**：重复/乱序已由 `NetworkConsumptionInjectionTests` 覆盖；延迟注入
    （含 KCP 与 WebSocket 正常流程、换装确认/施法交错、发射晚于格挡结果、旧伤害晚于复活、
    较新血量早于状态消息）未执行（`design.md:166`）。
  - **断线重进旧包**：按新会话初始化，旧包、旧模型回调与旧位移预测均不生效（`design.md:166`）。
  - **收到状态时模型尚未加载**：模型加载完成前的状态投影/事件不得丢失或错挂（`design.md:166`）。
- **移动 NpcBody + 位置上报/纠偏联测**（2026-11-20 登记）：Plan 05 的 `PlayerControlInertiaTests`
  只验证静态盒贴附的本地稳定性（往返幅度 0.0000m）；`design.md:144` 要求“碰撞推挤需要与服务端
  冻结纠偏联测，避免两边反复移动同一角色”。需构造会移动的怪物 `NpcBody` 与服务端位置上报/纠偏
  同场运行，断言无持续往返纠偏。
- **离线单机四元素共享运行时**（原 Plan 04 前瞻误写为“Plan 05”，2026-11-20 修正）：
  服务端权威状态已完成，但离线仍用 `Skills/Effect/StatusEffectManager`（本地冰冻/灼烧，
  仅怪物、独立计时）。需把 `SyncCore.StatusRuntime` 接入离线怪物/玩家适配器（时钟、
  目标、伤害修饰），复用 `ElementalCatalogDefinition` 四元素表；权威状态与本地旧状态按
  效果组去重，本地灼烧进入同一易伤计算点、冰冻保留定身表现。验收：同表配置下离线四元素
  行为（减速/灼烧末跳/麻痹/易伤）与服务端 `StatusRuntimeTests` 断言一致。
- **Addressables 元素资源、远端武器模型与美术签收**：`ElementVfxFallback` 只锁定键解析/
  颜色回退的判定顺序，未接入 Addressables；需在 Plan 06 的 Host 侧实现生产 loader、
  填表 `ViewKey`，并完成四元素特效资源与远端武器模型的人工美术验收（远端武器模型目前
  仍只有本地换剑外观）。资源缺失时必须继续回退程序化颜色，不阻塞战斗。
- **`_pushVector` 对远程 NpcBody 不生效**（Plan 05 任务 5 登记）：`Sys3CEntry.OnControllerColliderHit`
  以 `IDamageable` 为门槛，而 `NpcBodyCollider.BuildCollider` 在子物体只挂 `BoxCollider`，
  无该组件，因此贴身推离反馈对远程 `NpcBody` 不触发。若产品需要“被怪身体推开”，需给
  NpcBody 暴露可识别标记（非 `IDamageable` 语义）或在玩家侧按层 10 处理。
- **Plan 04 final review 遗留项**：M2 同段尸体检定、M4 多 DoT 定序、M6 来源类型建模、
  M7 未用参数、M8 死 API、M10 死亡 tick、M11 非 ActionV1、M16 离线 provider、
  M17 ThunderDuration、M18 bot 控制、M19 断开/换模型陈旧表现（M9 `EventId` 去重已在
  Plan 05 任务 4 完成；M3 的 `StatusTickService.Tick` 列表缓冲复用仍未做，一并归入）。
- **NpcAction 驱动的攻击变体/时间轴**（2026-11-20 登记）：`NpcActionEvent` 目前只负责
  阶段/取消，NPC 攻击起手仍由 `CastBroadcast → NpcAttackSchedule` 驱动；若后续需要
  `NpcAction` 直接驱动攻击变体/时间轴，需在 Plan 06 单独设计（Plan 05 未新增该机制）。
- **ORCA 减速 `maxStep` 邻居用例**（M12 遗留）：`MonsterStatusControlTests` 目前只验证
  无邻居期望位移；Plan 06 补邻居构造用例，验证 ORCA `maxStep` 同样按状态倍率缩放。
- **已知环境/夹具失败（非 Plan 05 回归）**：`RemoteEntityRegistryTests.Bot_Radius`
  的 PPtr 环境失败（16/17，基线可复现）；`MonsterWorldTests` 导航模式 Slime 巡逻卡死
  （服务端导航债）；`PlayerJumpBodyCollisionTests` 在 batchmode 下 0/3（夹具自身
  PPtr 伪报 + 固定 180 帧落地等待不足，2026-11-20 复现，未修改该文件）。

## 每条计划的完成定义

- 新增/修改的测试全部通过（服务端 xUnit + Unity EditMode/PlayMode）。
- 对应模块 README 已更新（协议、SyncLayer、SyncCore、Server、Skills、3C）。
- 没有把未启用字段写成已打通；未启用的通道在 README 与代码注释中标注。
- 提交信息按模块拆分，不用一个 commit 覆盖多份计划。
