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
| 05 | 三客户端验收 + 表现收口 | 三端一致性、带宽实测、麻痹惯性/推挤纠偏、KCP/WebSocket 注入延迟、Addressables 元素特效 | 01–04 |

## 当前状态（2026-11-17）

| # | 计划 | 状态 | 说明 |
|---|---|---|---|
| 01 | 共享表、权威换装、普攻参数与剑气快照 | ✅ 已完成 | 50/50 步骤；服务端 512/512、Unity EditMode/PlayMode 相关测试全部通过；模块 README 已回写 |
| 02 | B1 取消 + NpcAction 全链 + B2 战斗投影/带宽 | ❌ 未开始 | 尚无实现计划与代码 |
| 03 | B5 位移/跳跃预算 + PvP 控制 | ❌ 未开始 | 依赖 02；尚无实现计划与代码 |
| 04 | 状态结算 + DoT + 表现 | ❌ 未开始 | 元素状态/DoT/元素 VFX 未实现；本地持剑模型已由换装跟进补齐 |
| 05 | 三客户端验收 + 表现收口 | ❌ 未开始 | 依赖 01–04 |

**换装跟进（2026-11-16）**：简易武器背包、男女八把元素剑校准变体、权威确认对账与
交互边界已完成，自动测试全绿；握姿/穿模/拖尾、DemoDay 交互冒烟与发布构建冒烟仍待人工验收。
详见 [换剑与持剑适配设计](../specs/2026-11-16-weapon-bag-sword-fit-design.md) 与
[实现计划](2026-11-16-weapon-bag-sword-fit.md)。

Plan 01 的完成只覆盖共享表、权威换装、普攻参数、`CastId` 与发射/停点；设计规格中的
取消、战斗投影、位移/PvP、状态/DoT、元素 VFX 与三客户端验收仍属于 Plan 02–05。

## 执行顺序与并行度

1. **01 必须先做**：它定义共享 DTO、协议字段与 `CastId`，后续计划的类型都以它为准。
2. **02 完成后接入 03**。01 完成后，03 可先用测试状态做纯位移预算和跳跃轨迹测试。控制版本、客户端锁定与解除接入须等 02 的 `ControlSummary` 和生命代次契约落地。03 用注入的权威控制状态验证输入/投影闭环，实际元素命中统一在 04 接入。
3. **04 依赖 02**。状态变更走 `CombatStateUpdate`，DoT 事件走 `DamageResult/HitEntry`。PvE 链路可先验收，真实 PvP 冰雷对局须合入 03 的移动与施法控制。
4. **05 最后做**：它验证 01–04 的三端一致性，不再新增机制。

## 每条计划的完成定义

- 新增/修改的测试全部通过（服务端 xUnit + Unity EditMode/PlayMode）。
- 对应模块 README 已更新（协议、SyncLayer、SyncCore、Server、Skills、3C）。
- 没有把未启用字段写成已打通；未启用的通道在 README 与代码注释中标注。
- 提交信息按模块拆分，不用一个 commit 覆盖多份计划。
