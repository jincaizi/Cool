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
| 04 | [状态结算 + DoT + 表现](2026-11-19-elemental-sword-qi-04-status-dot-vfx.md) | ✅ 已完成 | 8/8 任务；服务端全部 654/654；Unity 目标 EditMode 类通过（`ElementalApplicationTests` 仅服务端存在；`RemoteEntityRegistryTests` 仅既有 `Bot_Radius` PPtr 环境失败，基线可复现）；PlayMode 五类回归完成（`MonsterWorldTests` 导航模式既有 Slime 巡逻卡死为已知导航债）；模块 README 已回写。在线 PvE/PvP 权威状态已完成；**离线单机共享运行时未接入，留给 backlog（Plan 06 未包含；2026-11-20 修正）** |
| 05 | 三客户端验收 + 表现收口 | ✅ 已完成 | 6/6 任务；服务端 668/668（基线 654 → AOI 10 + 带宽 2 + 服务端预算 2；含最终审查补充的 NPC 目标反查用例）；Unity EditMode 目标类 `ElementVfxFallbackTests` 5/5、`SwordQiElementTintTests` 4/4、`StatusVFXSystemTests` 3/3、`NetworkConsumptionInjectionTests` 14/14、`ProtocolShapeGuardTests` 17/17（`RemoteEntityRegistryTests` 16/17，仅既有 `Bot_Radius` PPtr）；PlayMode 9/9（三客户端元素 1/1、三客户端战斗一致性 1/1、`MovementSyncTests` 4/4、`PlayerControlInertiaTests` 3/3，真实 Server3C:8000）；带宽 100 实体 ×15Hz×60s 平均 53.4 KB/s、100 实体同时 DoT 最坏 76.9 KB/s（预算 100 KB/s，未上调）；收包注入目标类合计 72/72；AOI 反查 N=1000 ≤1ms、索引与暴力逐项相等。**未新增战斗机制/协议字段；离线共享运行时与 Addressables 美术资源签收移至 backlog（Plan 06 未包含）。设计规格中未执行的三端 DoT/末跳致死、麻痹取消前摇 `Cancelled` 链、状态结束时钟偏移、AOI 快速出入/复活版本连续、KCP/WS 延迟注入、断线重进旧包与模型未加载场景已登记到 Plan 06 并在 Plan 06 收口（见下）：DoT/麻痹取消/AOI 版本连续/消息级注入已验收；状态结束时钟偏移、KCP/WS 传输层延迟、真实会话代次仍未执行；三端末跳致死时刻为 Plan 07 gap** |
| 06 | [验收缺口与稳定性](2026-11-20-elemental-sword-qi-06-acceptance-gaps.md) | ✅ 已完成 | 6/6 任务；服务端 **699/699**；EditMode `NetworkConsumptionInjectionTests` **24/24**；PlayMode `ThreeClientElementalAcceptanceTests` **3/3**、`PlayerControlInertiaTests` **4/4**（真实 Server3C:8000）。生产仅改 `CombatNetworkBridge` 的 LifeId 代次门。边界：移动 NpcBody 推挤只在测试 stub 下验收机制，生产推挤仍 backlog；三端末跳致死时刻为 Plan 07 gap；状态结束时钟偏移、KCP/WebSocket 传输层延迟注入、真实连接代次/旧位移预测仍为 backlog（见下） |

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
元素 VFX 资源化属于 backlog（**Plan 06 明确未包含**）。Plan 06 只补齐验收缺口
（DoT、麻痹取消、消息注入、AOI 版本连续、移动 NpcBody 机制、ORCA `maxStep`），未新增战斗机制；
未执行项、stub 边界与 Plan 07 gap 见下节。

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

## 完整首发范围（2026-11-20）

- DemoDay 武器背包共用 Popup 安全区。换装期间缓存最多一次普攻。角色重建保留连接级确认队列和请求序号，重连按新会话发放默认剑。
- 服务端按动作窗口限制换装。动作结束可换剑，已独立发射的投射物保留快照和裁决。已死目标不重复出伤，过期施法序号拒绝。怪物前摇霸体免疫麻痹，普通 PvE 麻痹 0.6 秒，PvP 0.8 秒且保留当前霸体动作。
- 远端 Full 使用对应角色预制体的持剑外观，在 AOI 重建及分档升档恢复。离线冰、火、雷、风复用共享状态规则、同一施法的元素快照和本地怪物生命周期。四种剑气与四种状态特效随 Addressables 内容出包，资源不可用时沿用染色。
- 已复跑服务端全量 705/705、Unity EditMode 984/988、定向 PlayMode 7/7。EditMode 的四项与先前全量记录相同。PlayMode 覆盖真实 KCP 和 WebSocket、同账号重登、三端雷和火、DemoDay 男女五剑交互及八个 Addressables 键的加载释放。
- WebGL 实包含八个元素资源键。在 960×540 视口强制触控并联网，五把剑逐一确认。在同尺寸普通桌面视口验证画布完整。启动带导航的 DemoDay 服务后确认联机角色保留配置出生位置、重击收到权威广播，元素特效 bundle 由实包加载。
- 默认重击剑气改为银白刃芯和柔光。冰、火、雷、风按各自的弧面纹理和轮廓区分，尾迹随距离收窄淡出。已渲染五种重击预制体，剑气和元素表现专项 EditMode 65/65、PlayMode 4/4。
- 新版 WebGL 联机换上火剑并触发重击，浏览器成功加载新版元素 bundle。Android ARM64 IL2CPP 开发包包含八个元素地址，已在 Android 模拟器安装并冷启动到 UI 初始化。
- 公网原生移动候选仍受 ADR-0003 约束。当前生产 Android/iOS 入口使用 WebSocket。移动 KCP 的安全 provider 尚未接入生产入口。

以下 Plan 05/06 记录保留各阶段当时的验收范围。当前实现状态见本节。

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
  `NpcBody` + 位置上报/纠偏联测登记到 Plan 06，已完成机制验收（见下）。
- **元素表现回退**（Plan 05 任务 6 完成）：`ElementVfxFallback.ResolveColor` 锁定
  “ViewKey 非空且 loader 命中资源 → `Color.white`（资源自带颜色，Host 实例化），否则
  `ElementPalette.ColorOf(elementId)`”；`ElementVfxFallbackTests` 5/5。未实际调用
  Addressables；生产 loader/Host 接入与美术资源签收已由完整首发 `66812544` 完成。

## Plan 06 收口项结果（2026-11-20 完成）

Plan 06 六个任务全部完成；服务端全量 **699/699**。生产代码只改一处：任务 3 的
`CombatNetworkBridge` `LifeId` 代次门（旧世代 `DamageResult`/`CastBroadcast` 丢弃），由失败断言驱动。

- **三端 DoT 每跳 + 死亡投影**（任务 1）：`ThreeClientElementalAcceptanceTests` 3/3（真实
  Server3C:8000）。`FireDot_Ticks_Are_Consistent_And_Death_Projection_Is_Consistent` 断言
  A/B/C 按 `EventId` 逐条相等（`Damage/TargetRemainingHp/TargetLifeId/CombatVersion/EventSeq/CastId/SourcePlayerId`）、
  3 跳、间隔 0.85–1.15s、`IsBlocked=false`、201 施加/移除各一次、死讯
  `Dead/CombatVersion/LifeId` 三端严格相等、死后 1.6s 无 DoT。服务端末跳致死由
  `DotDamageTests.Burn_Tick_That_Kills_Player_Clears_Statuses_And_Stops` 证明；
  **三端同一次末跳致死的死亡时刻仍未验收，登记 Plan 07**（需服务器 HP 注入/测试钩子）。
- **麻痹取消前摇三端 `Cancelled` 链**（任务 2）：`CastCancelDuringWindupTests` 3/3
  （`EffectiveTick`=命中 tick、按 `CastId` 精确取消、已发射只停表现不终止结算）+
  `ThreeClientElementalAcceptanceTests.Thunder_Cancels_Windup_Cast_On_All_Clients`
  （三端同一 `CastId`/`EffectiveTick`、B 未发射且无命中、A 雷仍 2 条命中、旁观者 presenter 停止且不重播）。
- **延迟/断线重进/模型未加载**（任务 3）：`NetworkConsumptionInjectionTests` **24/24**。
  覆盖换装确认/施法交错、Stop 后迟到 Launch 不重建、旧 `LifeId` 伤害不扣血、旧投影不回退、
  重复 `EventId` 只消费一次、换命旧包丢弃、模型未建时 318 缓存回放与 DoT 基线补齐不双扣。
  边界：注入在桥层 `OnMessage`/`Update`，**KCP/WebSocket 传输层延迟注入未执行**；真正的跨会话
  隔离仍靠宿主（新 playerId/新桥），桥只按 `LifeId` 识别同会话旧世代，旧位移预测不在桥职责内。
- **AOI 快速出入/复活版本连续**（任务 4）：`AoiVersionContinuityTests` 6/6（Aoi 命名空间 58/58）。
  80/90/91m 迟滞往返、死亡复活/Evade 到家重见基线逐项等于服务器状态、回池重生按新实体 id
  （`LifeId=1/CombatVersion=0`，不承诺跨 id 单调）、观察者重进首帧完整投影、
  旧 `LifeId` 的 `CombatStateUpdate` 出界后真正停止。
- **移动 NpcBody + 位置上报/纠偏**（任务 5）：`PlayerControlInertiaTests` 4/4（真实
  Server3C:8000）。2s 窗口相对 XZ 净变化 ≤0.1m（raw）、方向翻转 ≤1（0.25s 均值，0.03m 死区）、
  raw 幅度 ≤0.15m、上报与权威差 ≤0.5m、纠偏数 ≤ 超预算包数、怪物停止后残余速度 ≤0.01m/s，
  并以故意超预算传送做正向对照。**边界：推挤门槛 `IDamageable` 由测试本地 stub 满足，
  只证明推挤/纠偏机制；生产 `NpcBodyCollider` 仍无该组件，`_pushVector` 对远程 NpcBody
  的产品行为保持 backlog。**
- **ORCA 减速 `maxStep` 邻居用例**（任务 6）：`MonsterStatusControlTests` 8/8。新增
  `Slow_Scales_Orca_MaxStep_With_Monster_Neighbor_And_Keeps_Avoidance_Side`：冰减速
  `SpeedScale=0.7` + X 轴正向 1.8m 静止怪物（期望方向 `(1,0,0.7)` 与之约 35°），断言实际步长
  = `moveSpeed×0.7×dt`（解恰好截断在缩放后的 `maxStep` 圆上；未缩放对照 = `moveSpeed×dt`），
  慢/快步长比 0.7，解出朝向偏离期望方向（点积 <0.95 ≈0.82，证明约束确实生效，而非 `v_pref`
  原样返回）且避让横向分量同向（不反转）；无邻居对照组仍逐字返回期望位移。命令：
  `bash tools/test-server-inprocess.sh --filter "FullyQualifiedName~MonsterStatusControlTests"`。

全量验证：`bash tools/test-server-inprocess.sh` → **699/699**；EditMode
`NetworkConsumptionInjectionTests` **24/24**；PlayMode `ThreeClientElementalAcceptanceTests`
**3/3**、`PlayerControlInertiaTests` **4/4**（真实 Server3C:8000）。Unity batchmode 副作用已还原，
`TestResults/` 未提交。

## Plan 06 / backlog 登记（2026-11-20）

以下项**不属于** Plan 01–05 已交付范围，登记到 Plan 06/backlog（Plan 06 已收口：已验收项在
条目内标注证据，汇总见上节；**未标注的项归 Plan 09/backlog，不得写成已打通**）。

- **Plan 05 未执行的设计验收场景**（2026-11-20 登记；Plan 05 已交付的是子集，Plan 06 验收状态
  见每条标注，未标注者不得写成已验收）：
  - **三端 DoT/末跳致死**：三端每跳伤害/余血/`DamageKind=Dot` 一致（Plan 06 任务 1 验收）；
    受击者不被跳伤打断、观察者不播命中火花/受击硬直由 `DotDamageRoutingTests`（Plan 04）覆盖；
    **末跳致死**由服务端
    `DotDamageTests.Burn_Tick_That_Kills_Player_Clears_Statuses_And_Stops` 证明（`MaxHp=8`，
    一跳致死、只一条 DotHit、死后不再跳）；**三端同一次末跳致死的死亡时刻**在 720 HP 联机场景
    无法确定性构造（直伤 90×2/次、灼烧 8/跳、暴击 15%），需服务器 HP 注入/测试钩子，登记 Plan 07。
    **Plan 06 任务 1 已完成**三端每跳/死亡投影验收：
    `ThreeClientElementalAcceptanceTests.FireDot_Ticks_Are_Consistent_And_Death_Projection_Is_Consistent`
    （三端 3/3）；**三端末跳死亡时刻仍为 Plan 07 gap，未验收。**
  - **麻痹取消前摇三端 `Cancelled` 链**：A 的雷剑气命中 B 进行中的前摇，三端按同一 `CastId` 记录
    `CastCancelBroadcast.EffectiveTick`；B/C 停止攻击动作，不生成未发射剑气与后续 `DamageResult`，
    已发射的独立投射物继续飞行；玩家路径断言 `RemoteCastPresenter`/`RemoteEntityAnimator.CancelAttack(CastId)`，
    怪物路径断言 `NpcAttackSchedule`/`NpcEntityAnimator` 停止且 `NpcActionEvent` 按 `(InstanceId, Revision)`
    合并、`Phase=Cancelled` 生效（`design.md:167`）。**Plan 06 任务 2 已完成**：
    `CastCancelDuringWindupTests` 3/3 + `ThreeClientElementalAcceptanceTests.Thunder_Cancels_Windup_Cast_On_All_Clients`
    （三端 3/3；B 未发射且无命中、A 雷仍 2 条命中、旁观者 presenter 停止）。
  - **状态结束时钟偏移注入**：给两台客户端注入不同时钟偏移和渲染延迟，断言特效移除由同一条
    `CombatStateUpdate` 触发；权威移除晚于 `EndTick` 时保持到消息到达（`design.md:169`）。
    **Plan 07 任务 1 口径修正**：生产没有本地 `EndTick` 移除路径（`AuthoritativeStatusVfx`
    只按 318 diff，`StatusVFXSystem.Update` 不按时间移除；火焰脉冲仍读 `deltaTime`），
    时钟偏移注入没有被测对象。改为验收
    “施加/移除只由 318 驱动、迟到 318 保持到到达、推进时间不自行移除”，并把消费端
    `StatusVFXSystem` 纳入回路；时钟偏移本身不适用，不再伪造。
  - **AOI 快速出入/复活版本连续**：AOI 快速出入、死亡复活、Evade 到家与断线重进时，
    进入基线与后续 `CombatStateUpdate` 的 `CombatVersion`/`LifeId` 连续，旧版本增量不能复活
    旧状态或旧装备（`design.md:170`）。回池重生按生产语义是**新实体 id**（新实体 `LifeId=1`、
    `CombatVersion=0`）：验收旧 id 停止、新 id 全量基线，不承诺跨 id 的 `CombatVersion` 单调。
    **Plan 06 任务 4 已完成**：`AoiVersionContinuityTests` 6/6（Aoi 命名空间 58/58）。
  - **KCP/WebSocket 延迟注入**：重复/乱序已由 `NetworkConsumptionInjectionTests` 覆盖；
    消息级延迟/交错（换装确认/施法交错、发射晚于格挡结果、旧伤害晚于复活、较新血量早于状态消息、
    延迟但同版本只消费一次）已由 **Plan 06 任务 3** 的 `NetworkConsumptionInjectionTests`
    （当时 24/24）在桥层 `OnMessage`/`Update` 覆盖；**Plan 07 任务 2 完成消费层延迟/乱序注入**
    （`TransportDelayInjectionTests` 5/5，测试本地 `DelayedNetworkClient` 装饰器包住 fake client，
    不改生产传输层）；**真实 KCP/WebSocket 传输 socket 级延迟注入仍未执行**（`design.md:166`）。
  - **断线重进旧包**：按新会话初始化，旧包、旧模型回调与旧位移预测均不生效（`design.md:166`）。
    **Plan 06 任务 3 部分覆盖**：桥按 `LifeId` 丢弃同会话旧世代 `DamageResult`/`CastBroadcast`。
    **Plan 07 任务 3 完成宿主级会话隔离**：`SessionIsolationTests` 5/5（final review 补 NpcAction
    缓存跨会话隔离用例）；真实 `ClientNetworkHost` 断线重连下旧包/旧位移预测/旧 presenter/
    旧 playerId 均不生效，`registry.Clear()` 同步清空旧视图、战斗投影、NpcAction 缓存与
    last-known vitals。
  - **收到状态时模型尚未加载**：模型加载完成前的状态投影/事件不得丢失或错挂（`design.md:166`）。
    **Plan 06 任务 3 已完成**：视图未建时 318 缓存回放、DoT 由基线补齐且不双扣
    （`NetworkConsumptionInjectionTests` 24/24）。
- **移动 NpcBody + 位置上报/纠偏联测**（2026-11-20 登记）：Plan 05 的 `PlayerControlInertiaTests`
  只验证静态盒贴附的本地稳定性（往返幅度 0.0000m）；`design.md:144` 要求“碰撞推挤需要与服务端
  冻结纠偏联测，避免两边反复移动同一角色”。需构造会移动的怪物 `NpcBody` 与服务端位置上报/纠偏
  同场运行，断言无持续往返纠偏。**Plan 06 任务 5 已完成机制验收**：`PlayerControlInertiaTests` 4/4
  （真实 Server3C:8000；raw 净变化 ≤0.1m、翻转 ≤1、raw 幅度 ≤0.15m、上报差 ≤0.5m、
  纠偏数 ≤ 超预算包数、停止后残余速度 ≤0.01m/s，并含超预算传送正向对照）。**边界：推挤门槛
  `IDamageable` 由测试本地 stub 满足，只证明推挤/纠偏机制；生产 `NpcBodyCollider` 无该组件，
  生产 NpcBody 推挤仍为 backlog。**
- **离线单机四元素共享运行时**（原 Plan 04 前瞻误写为“Plan 05”，2026-11-20 修正）：
  **完整首发已完成（`66812544`）**：`OfflineMonsterStatuses`/`OfflineWeaponCast` 把
  `SyncCore.StatusRuntime` 接入离线怪物/玩家适配器，复用 `ElementalCatalogDefinition`
  四元素表、同一施法元素快照与本地怪物生命周期；`OfflineElementalWeaponTests` 4/4
  （EditMode）。离线四元素仍以共享状态规则为准，旧 `StatusEffectManager` 不作为权威。
- **Addressables 元素资源、远端武器模型与美术签收**：
  **完整首发已完成（`66812544`）**：新增 `AddressableAssetsData/Elemental VFX` 组、
  `ElementVfxAssets` 生产 loader、八个元素/状态键（冰/火/雷/风各一剑气+一状态）与
  `ElementVfxAttachment` 回退接线；资源不可用时继续回退程序化颜色。远端 Full 在 AOI 重建/
  升档恢复对应角色预制体的持剑外观（`RemoteEquipmentVisualTests` 男/女 2/2）。
  美术人工签收证据见“完整首发范围”节的 WebGL/Android 实包记录；未在此重复声明自动验收。
- **`_pushVector` 对远程 NpcBody 不生效**（Plan 05 任务 5 登记）：`Sys3CEntry.OnControllerColliderHit`
  以 `IDamageable` 为门槛，而 `NpcBodyCollider.BuildCollider` 在子物体只挂 `BoxCollider`，
  无该组件，因此贴身推离反馈对远程 `NpcBody` 不触发。若产品需要“被怪身体推开”，需给
  NpcBody 暴露可识别标记（非 `IDamageable` 语义）或在玩家侧按层 10 处理。
  **Plan 06 任务 5 只证明 stub 下的推挤/纠偏机制；本条产品行为仍未修，保持 backlog。**
- **Plan 04 final review 遗留项**：M2 同段尸体检定、M4 多 DoT 定序、M6 来源类型建模、
  M7 未用参数、M8 死 API、M10 死亡 tick、M11 非 ActionV1、M16 离线 provider、
  M17 ThunderDuration、M18 bot 控制、M19 断开/换模型陈旧表现（M9 `EventId` 去重已在
  Plan 05 任务 4 完成）。**Plan 07 任务 4 已逐项审计（2026-11-24）：M3/M7/M10 机械修复，
  M16/M17 已由完整首发解决；M19 断开侧已由 Plan 08 任务 2 解决；M4/M6/M8/M11/M18
  保留 Plan 09/backlog；见下节。**
- **NpcAction 驱动的攻击变体/时间轴**（2026-11-20 登记）：`NpcActionEvent` 目前只负责
  阶段/取消，NPC 攻击起手仍由 `CastBroadcast → NpcAttackSchedule` 驱动；若后续需要
  `NpcAction` 直接驱动攻击变体/时间轴，需在 Plan 09/backlog 单独设计（Plan 05/06 均未新增该机制）。
- **ORCA 减速 `maxStep` 邻居用例**（M12 遗留，**Plan 06 任务 6 已完成**）：
  `MonsterStatusControlTests` 8/8；新增 `Slow_Scales_Orca_MaxStep_With_Monster_Neighbor_And_Keeps_Avoidance_Side`
  用冰减速 `SpeedScale=0.7` + X 轴正向 1.8m 静止怪物（期望方向 `(1,0,0.7)` 与之约 35°）断言
  实际步长 = `moveSpeed×0.7×dt`（未缩放对照 = `moveSpeed×dt`），慢/快步长比 0.7，解出朝向
  偏离期望方向（点积 <0.95，约束确实生效）且避让横向分量不反转；无邻居对照组仍按原期望位移。
- **未执行项（Plan 09 Block，见下节）**：真实 KCP/WebSocket 传输 socket 级延迟注入
  （消费层延迟/乱序已由 Plan 07 任务 2 覆盖，不是 socket 级）、三端同一次末跳致死时刻、
  生产 `NpcBody` 推挤、`NpcAction` 攻击变体、M4/M6/M8/M11/M18。Plan 07 已完成状态结束
  消息驱动契约与宿主级会话隔离；Plan 08 已完成 echo 会话复位与 M19 断开清理；
  状态结束时钟偏移注入仍为不适用（生产无本地 `EndTick` 移除路径）。
- **已知环境/夹具失败（非 Plan 05 回归）**：`RemoteEntityRegistryTests.Bot_Radius`
  的 PPtr 环境失败（16/17，基线可复现）；`MonsterWorldTests` 导航模式 Slime 巡逻卡死
  （服务端导航债）；`PlayerJumpBodyCollisionTests` 在 batchmode 下 0/3（夹具自身
  PPtr 伪报 + 固定 180 帧落地等待不足，2026-11-20 复现，未修改该文件）。

## Plan 07 任务 1–3：网络硬化（2026-11-24）

| 任务 | 处置与证据 |
|---|---|
| 任务 1 状态结束消息驱动契约 | ✅ 范围裁定：生产无本地 `EndTick` 移除路径，时钟偏移注入不适用，不伪造；改为消息驱动契约（施加/移除只由 318 `Statuses` diff 触发、迟到 318 保持到到达、推进时间不自移除，`StatusVFXSystem` 纳入回路）。`StatusRemovalOnlyOnAuthoritativeProjectionTests` PlayMode 5/5；回归 `StatusVFXSystemTests` + `AuthoritativeStatusVfxTests` 12/12。提交 `53bd8d94`（文档 `166513bc`/`4bc9c07e`）。 |
| 任务 2 传输消费层延迟/乱序注入 | ✅ 测试本地 `DelayedNetworkClient : INetworkClient` 装饰器包住 fake client（延迟/重复/乱序按测试时间释放，不改生产传输层）。`TransportDelayInjectionTests` 5/5（换装/施法交错、发射晚于终止、旧 `LifeId` 伤害晚于复活、旧 318 晚于新版本、延迟同版本只消费一次）。提交 `4eedbd4e`。**真实 KCP/WebSocket socket 级延迟注入仍未执行。** |
| 任务 3 宿主级会话隔离 | ✅ 真实 `ClientNetworkHost` + 新 manager/新 playerId；旧包、旧位移预测、旧 presenter、旧 playerId 均不生效。`SessionIsolationTests` 5/5（final review 补 NpcAction 缓存跨会话隔离）；回归 36/36。生产最小修复 `ClearEchoRequest` 空守卫。提交 `a8cb8c8b` + `fix(sync): clear npc action caches on session reset`。 |

最终审查修复（2026-11-24）：`RemoteEntityRegistry.Clear()` 补清 `_pendingNpcActions`/`_npcActions`
（旧会话缓存不得回放到新会话同 Id/LifeId 视图）；复核 `SessionIsolationTests` 5/5、
`NetworkConsumptionInjectionTests` 25/25、`TransportDelayInjectionTests` 5/5 全绿。

## Plan 08：会话复位与断开清理（2026-11-26）

| 任务 | 处置与证据 |
|---|---|
| 任务 1 Echo 最后应用状态跨会话复位 | ✅ `ClientNetworkHost.ClearEchoRequest()` 在 `Connect`/`Disconnect`（以及角色重绑路径）复位 `_lastAppliedEchoCastId/_lastAppliedEchoStartTick/_endedEchoCastId/_lastAppliedEchoRequestId`。服务器重启后的低 `StartTick`/`RequestId=1` 回包不再被旧会话永久判旧；同会话 `(StartTick, CastId)` 单调语义不变。`EchoSessionResetTests` 4/4（含 RequestId=5 → 断线 → 新 RequestId=1 红→绿），回归 `SessionIsolationTests` + `SwordQiEchoPresenterTests` 8/8。提交 `0edd7239` + `50754869`。 |
| 任务 2 断开清空状态 VFX（M19 断开侧） | ✅ `AuthoritativeStatusVfx.Clear()` 对每个活动组发一次 `StatusEffectRemovedEvent` 并清表；只在 `ClientNetworkHost.Disconnect()`（`ResetAttachment` 之后、`manager.Dispose()` 之前）调用，角色重建不清。`StatusVfxDisconnectCleanupTests` 2/2（真实 host 断开 + registry/EventBus/StatusVFXSystem；旧染色清空、新会话同 id 无残留、角色重建保留），回归 12/12。提交 `d561ad3c`。 |

## Plan 09 Block（2026-11-26，暂不实现）

以下项在 Plan 08 收口后整体列入 **Block**，等产品/设计决策或测试钩子就绪后再排期。
Block 不等于已完成，不得写成已打通。

| 项 | Block 原因 |
|---|---|
| 真实 KCP/WebSocket socket 级延迟注入 | 工程可做，但需测试自建 8123 服务与真实 adapter 延迟包装；当前不排期 |
| 三端同一次末跳致死时刻 | 需要服务器 HP 注入/测试钩子；当前 720 HP 数值下无法确定性构造末跳唯一死因 |
| 生产 `NpcBody` 推挤 | 产品决策：怪物身体是否应推开玩家；当前 `_pushVector` 对远程 NpcBody 不生效 |
| `NpcAction` 攻击变体/时间轴 | 设计决策：是否由 `NpcAction` 替代 `CastBroadcast → NpcAttackSchedule` |
| M4 多 DoT 定序 | 规则未定义（当前只有火 DoT 201，同组替换保证不并存） |
| M6 来源类型建模 | 工程可做，但需要先定义来源种类快照与协议语义；暂缓 |
| M8 死 API | 审计后无可证明未用的生产 API，待明确目标后再处理 |
| M11 非 ActionV1 | 是否支持非 ActionV1 宿主形态未定 |
| M18 bot 控制 | 产品决策：压测 bot 是否应受冰/雷控制 |

## Plan 07 任务 4：Plan 04 残余 M 项审计（2026-11-24）

逐项处置；**未做/仍需设计的项不得写成已打通**。服务端全量由 705 增至 **707/707**
（新增 M3 缓冲复用、M10 死亡 tick 两个用例）。

| M 项 | 处置 | 证据 |
|---|---|---|
| M2 同段尸体检定 | ✅ 已有守卫 + 测试（无需改动） | `CastHandler.ApplyHit` 在目标不存在或已死时直接返回（不生成 `HitEntry`、不附加状态）；`ElementalApplicationTests.Lethal_Swing_Does_Not_Apply_Element_To_Dead_Target` 断言 50 HP 目标被挥砍击杀后，同次施法的剑气段不再出伤（1 条命中、`EventSeq=1`、无状态） |
| M3 `StatusTickService.Tick` 缓冲复用 | ✅ 机械修复（2026-11-24） | 每实体 `new List<ActiveStatus>()` 改为服务级 `_dueBuffer`，每实体前后 `Clear()`；`DotDamageTests.Tick_Reuses_Due_Buffer_Across_Entities_And_Ticks` 钉住“两实体 × 两 tick 伤害正确 + 同一缓冲实例 + 注入哨兵被清”，回退为每实体分配时该用例红（已实测） |
| M4 多 DoT 定序 | ⏳ 仍需设计（Plan 09/backlog） | 当前状态表只有一种 DoT（201 火，`ElementalCatalogDefaults.cs`），`StatusRuntime._byGroup` 同组替换保证同目标最多一条灼烧；多 DoT 并存时的结算顺序（`Dictionary` 插入序不是契约）没有任何产品规则，未做投机排序 |
| M6 来源类型建模 | ⏳ 仍需设计（Plan 09/backlog） | `ServerStatusManager.Apply` 对全部状态硬编码 `sourceIsPlayer: true`；`StatusTickService.IsNpcSource` 在 DoT 结算时实时查来源实体，来源离场即按玩家处理。需要先把来源种类快照进 `ActiveStatus`/协议语义（NPC→玩家的雷时长、免疫归属）再改 |
| M7 未用参数 | ✅ 机械修复（2026-11-24） | 删除 `ServerStatusManager.Apply(..., ElementalCatalogDefinition catalog)` 的未用参数并同步调用点（生产 1 处、测试 5 文件）；编译与全量测试通过 |
| M8 死 API | ⏳ 审计后无可证明未用的生产 API（Plan 09/backlog） | 复核 Plan 04 新增公开面：`StatusRuntime.Remove`/`ControlImmuneUntilTick`、`ActiveStatus.AppliedTick`、`ServerStatusManager.SyncControl` 均有内部或测试消费；缩窄可见性/移除测试观测面属设计取舍，未删 |
| M10 死亡 tick | ✅ 机械修复（2026-11-24） | DoT 在 preTick 结算时 `MonsterModule._currentTick` 仍是上一 tick，旧实现 `Die(_currentTick)` 让怪物死亡窗口/`DeathTick` 早 66 ms；新增显式 tick 重载并由 `StatusTickService` 传结算 tick。`DotDamageTests.Dot_Kill_Uses_Settlement_Tick_For_Monster_Death_Window` 旧实现实测 `14 != 15` 红、修复后绿 |
| M11 非 ActionV1 | ⏳ 仍需设计（Plan 09/backlog） | 生产 `Server3C.EnableMonsters` 恒 `actionV1: true`；非 ActionV1 宿主下 `MonsterModule.OnDamageTaken` 不更新 `LastHp`，DoT 掉血会在下一怪物 tick 被 `DetectHitOrDeath` 判成受击。是否支持该宿主形态未定，未改 |
| M16 离线 provider | ✅ 完整首发 `66812544` 已解决 | `OfflineMonsterStatuses` 把 `SyncCore.StatusRuntime` 接到单机时钟/怪物 DoT/表现事件；`OfflineWeaponCast` 冻结武器快照并按施法认领元素；`OfflineElementalWeaponTests`（EditMode，含四元素、无效命中不附加、麻痹/减速移动） |
| M17 ThunderDuration | ✅ 完整首发 `66812544` 已解决 | `StatusRuntime.Apply`：PvP 控制取 `Pvp.ThunderDurationSeconds`，PvE 取 `def.DurationSeconds`；配置为 PvP 0.8 s / PvE 0.6 s（`Server/Config/elemental_catalog.json`、`ElementalCatalogDefaults.cs`）；`StatusRuntimeTests.Paralyze_Uses_Configured_Pvp_Duration_And_Pve_Status_Duration` 与 `Pve_Paralyze_Does_Not_Grant_Control_Immunity` |
| M18 bot 控制 | ⏳ 仍需设计（Plan 09/backlog） | bot 是 `EntityKind.PlayerBot`：`BotRunner.Tick` 只按固定步长移动，不读 `ControlSpeedScale`/`ControlActionLock`；10002 会懒配置状态并向观察者投影，但对 bot 无实际控制效果。压测 bot 是否应受状态控制需产品裁定 |
| M19 断开/换模型陈旧表现 | ✅ Plan 04 换模型 + Plan 08 断开清理 | 换模型：`AuthoritativeStatusVfx.Apply` 在 target 变化时（Proxy↔Full 换根/本地角色重建）先对旧根 `EmitRemoved` 全部组、再对新根重放（`test_authoritative_status_vfx_replays_all_groups_when_target_changes`、`test_remote_entity_registry_replays_statuses_when_tier_changes_render_root`）。断开：Plan 08 任务 2 的 `AuthoritativeStatusVfx.Clear()` 在 `ClientNetworkHost.Disconnect()`（`ResetAttachment` 之后、`manager.Dispose()` 之前）发全部 Removed 并清表；`StatusVfxDisconnectCleanupTests` 2/2，新会话同 id 无残留，角色重建路径不清 |

验证：`bash tools/test-server-inprocess.sh` → **707/707**；Unity EditMode 目标类
**54/54**（Plan 07 任务 4 时点；final review 后 `SessionIsolationTests` 增至 5/5，目标类为
**55/55**）。构成：`AuthoritativeStatusVfxTests` 9、`OfflineElementalWeaponTests` 8、
`NetworkConsumptionInjectionTests` 25、`TransportDelayInjectionTests` 5、
`SessionIsolationTests` 4→5、共享 `StatusRuntimeTests` 3；PlayMode
`StatusRemovalOnlyOnAuthoritativeProjectionTests` **5/5**。`TestResults/` 未提交，
batchmode 的 `Assets/Packages/**` 与 coverage 设置副作用已还原。

## 每条计划的完成定义

- 新增/修改的测试全部通过（服务端 xUnit + Unity EditMode/PlayMode）。
- 对应模块 README 已更新（协议、SyncLayer、SyncCore、Server、Skills、3C）。
- 没有把未启用字段写成已打通；未启用的通道在 README 与代码注释中标注。
- 提交信息按模块拆分，不用一个 commit 覆盖多份计划。
