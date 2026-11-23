# Skills —— 技能系统

技能系统负责技能数据、输入缓冲、状态机、执行、效果与事件。攻击层由 `Runtime/SkillCoordinator.cs`
接入 3C，不使用 AttackFSM。

## 目录结构

| 目录 | 内容 |
|---|---|
| `Definition/` | `SkillState` / `SkillType` / 技能子状态枚举 |
| `Data/` | `SkillData` 与技能类型数据：`InstantSkillData` / `ProjectileSkillData` / `ComboSkillData` / `ChargedSkillData` / `ChanneledSkillData` / `SpinSkillData` / `SwordQiEchoSkillData`；`MeteorConfig` / `SwordQiConfig`；`EffectBlock` / `ShapeBlock` / `DamageBlock` / `PresentationBlock`；`SkillPresentationCatalog`（同步玩家技能远程表现目录） |
| `Runtime/` | `SkillCoordinator` / `SkillStateMachine` / `SkillExecutor` / `SkillInputBuffer` / `CooldownManager` / `SkillInterruptionMatrix` / `SkillDamageHelper` / `SpinHitTracker` / `SwordQiEchoLocalRunner` |
| `Effect/` | `StatusEffectManager` / `StatusEffectRegistry` / `EffectData` / `BurnEffectData` |
| `Events/` | 火球、陨石、剑气、状态效果与 VFX 事件 |
| `EventBus.cs` | 静态事件总线；跨模块事件都走这里 |

## 主流程

`InputManager` → `SkillCoordinator`（输入、冷却、打断）→ `SkillStateMachine`（阶段与 Hitbox 窗口）
→ `SkillExecutor`（目标检测、伤害、效果）→ `EventBus` 事件 → Nameplate / VFX / 状态效果。

- 蓄力、旋转、冲刺、投射物各有独立数据类与测试。
- 旋转取消上报（B3）：`SkillCoordinator.OnSkillCancelled` 在同键取消/被受击眩晕打断时触发；
  `SkillCastReporter.ReportCancel` 只对 `SpinSkillData` 发 `CancelCastRequest`，服务端据此
  丢弃未到期判定段。单段技能没有残留段，不上报。
- 远程施法表现使用 `Data/SkillPresentationCatalog`，不依赖本地 `SkillCoordinator`。
- 技能对 3C 的影响通过 `Sys3CEntry` 与 `StateCoordinator` 的接口完成。
- 格挡成功事件 `BlockSucceededEvent` 定义在 `Sys3C/Core/Events/`，由战斗与同步层发布，表现层订阅。

## 属性剑气与攻速（2026-11-14）

- 单机装备由 `SkillCoordinator.OfflineWeaponProvider` 提供。10001/10002 接受施法时冻结武器
  元素、基础攻击、攻速和剑气倍率。挥击乘武器基础攻击相对默认铁剑的倍率，重击剑气
  再乘共享剑气系数及武器剑气倍率。切换装备只影响下一次施法。
- 仅 10002 的挥击和剑气附加武器元素。两者各自结算伤害，共用同一次施法的每目标附加记录。
  首个有效且目标存活的命中占用记录。格挡、无效伤害和击杀均不附加元素。

- 普攻 10001/10002 的攻速来自已确认装备：`SkillCoordinator.AttackSpeedProvider` 注入倍率
  （0.25–4），`SkillExecutor.SetTimeScale` 驱动 `SkillStateMachine.ElapsedTime`，
  冷却按 `Cooldown / attackSpeed` 起算；每段时序只缩放一次。蓄力/旋转/引导保持原计时。
- `SkillStateMachine` 只缩放 `_elapsedTime`，不写全局 `Animator.speed`；Animator 由
  `CharacterAttackHandler` / `RemoteEntityAnimator` 写 `AttackSpeed` 参数。
- 联机启用属性剑气后，`NetworkSwordQiGate.SuppressLocalSpawn=true`：
  `SkillExecutor.OnSwordQiFrame` 不生成剑气视觉，由 `ProjectileLaunchBroadcast` 统一创建。
  单机由 `SkillExecutor` 生成并使用施法快照的元素。 `SwordQiSpawner` 仍支持
  `startDistance` 供联网晚到补进度。
- `SwordQiProjectile` 把已飞距离和停点余量写入波面材质参数。补进度、元素资源替换及回池
  复用时，波面形态跟随本次飞行进度。命中和停止距离仍由弹体按原规则计算。

## 突进位移（Plan 03，2026-11-18）

- 突进的起手/活动/收尾窗口来自共享表 `ElementalCatalogDefinition.Displacements`
  （首版 20001 = 2m / 0.3s / 起手 0.05s / 收尾 0.05s），不在技能资产里各写一套。
- `SkillExecutor` 进入 `Execution` 时用 `ElementalCatalogDefaults.ResolveDashDistance(skillId, 资产值)`
  解析距离——**共享表优先，资产字段只在表里没有该技能时回退**；`DashDuration` 共享表优先、
  缺表回退资产 `DashDuration`，`Startup/Recovery` 缺表回退默认 `0.05s`，再交给
  `SkillDashComponent.StartDash`。
- `SkillDashComponent` 的有效活动窗 = `duration − startup − recovery`，`DashSpeed = distance / 活动窗`；
  `CurrentFrameSpeed(dt) = DashSpeed × StatusSpeedScaleProvider() × dt`，即突进乘权威状态倍率
  （冰减速等）；普通移动的 `Base × Defend × Skill × Status` 合成见
  [Sys3C](../Sys3C/README.md)。状态倍率由 `Sys3CEntry` 注入
  `() => CharacterController.StatusSpeedScale`。

## 元素表现信号（Plan 04，2026-11-19）

- `Effect/EffectData.cs` 的 `EffectType` 只追加 `Vulnerable`（末尾），既有枚举序列化值
  不变；`StatusVFXSystem` 用显式 `case` 处理以免未来类型被静默归类。
- `SwordQiSpawner.Spawn(..., int elementId = 0)` 透传给 `SwordQiProjectile`，由
  `SwordQiElementTint` 按元素给拖尾/粒子/材质 `_Color` 上色（冰蓝白/火橙红/雷紫白/
  风青白，0 = 原色）；联机剑气读 `ProjectileLaunchBroadcast.ElementId`。
- 单机武器元素与联网共用 `StatusRuntime`；单机怪物的状态推进、实际移动倍率和伤害顺序
  见 [Monster](../Monster/README.md)。独立技能资产中的 `EffectData` 继续由
  `StatusEffectManager` 处理。

## 测试

Editor：`SkillCoordinator*Tests`、`SkillExecutor*Tests`、`SkillStateMachine*Tests`、`SkillDataMeteorTests`、
`SkillInterruptionMatrixSpinTests`、`StatusEffect*Tests`、`Fireball*Tests`、`Meteor*Tests`、`SwordQi*Tests`、
`BurnEffectDataTests`、`ProjectileSkillDataTests`、`SkillPresentationCatalogTests`、`RemoteProjectileApiTests`、
`OfflineElementalWeaponTests`。
突进回归在 Sys3C：`SkillDashComponentTests`（共享窗口距离/起手收尾/状态倍率）。

```bash
bash tools/unity-tests.sh EditMode SkillExecutorMeteorTests
```

## 相关模块

## 剑气回荡（20006，2026-11-18）

- `SwordQiEchoSkillData` 只承载 `ConfirmationTimeoutSeconds`；半径/目标上限/剑气数/伤害倍率在共享
  `SyncCore.SwordQiEchoRules.Spec`。资产 `_skillType=Instant`、`_canBeInterruptedByMovement=false`。
- `SkillCoordinator` 两阶段：联网时发 `OnEchoRequested`（宿主负责 requestId/bindingId 与发送），
  单机发 `OnEchoOfflineRequested`（`SwordQiEchoLocalRunner` 选敌并提交）。待确认期间锁新主动施法；
  `CommitEchoRequest` 接迟到有效接受并结束冲突动作，`ApplyAuthoritativeCooldown` 只对齐冷却。
- `SkillStateMachine.StartEcho(endTime, now)` 让 Execution 按权威时刻完成，剪辑长度只影响表现；
  `CanMove`/`CanRotate` 为真，`IsAnimationCompletionIgnored` 为真。
- `SkillCastReporter.ReportActivation` 跳过 20006；`CombatNetworkBridge.NotifyEchoCast` 发送带
  `EchoRequestId/EchoBindingId` 的 CastRequest。

## 移动端显示契约

- 技能冷却 UI 只读取 `SkillCoordinator.GetCooldownInfo(skillId)`；遮罩使用 `Remaining / Total`，不得维护第二套计时器。
- 火球与炎爆在实际蓄力释放时才开始冷却；取消蓄力不占冷却。
- 蓄力环只在当前技能处于 `Charging` 时读取 `CurrentSkill.GetChargeProgress()`。
- R 的再次按下继续进入现有“旋转中同键取消”分支，移动按钮不得绕过 `InputManager` 直接施放。

## 火球地面瞄准（HoldToAim）

- 20003（`Fireball_SkillQ`）的 `ProjectileSkillData`：`HoldToAim=true`、`MinAimDistance=2`、
  `CanMoveWhileCharging=false`；按住不会自动释放，`Charging` 一直持续到松手。
- 松手释放走 `SkillCoordinator.TryReleaseCharge`，落点由 `SkillExecutor.ReportedImpactPoint`
  提供并随 `CastRequest.ImpactX/Y/Z` 上报；**冷却在释放时起算**，取消蓄力不占冷却。
- `PlayerCancel` 在 `Casting` / `Charging` 均放行（`SkillInterruptionMatrix`）：移动端拖远取消、
  失焦、关闭触控、角色解绑都走它；取消不发射、不冷却、不上报。
- 蓄力期间按 `_staminaDrainPerSecond=15` 持续扣精力；精力耗尽由 `StaminaExhausted` 取消，
  同样不发射、不冷却。
- 20003 在 `CombatRules` 表里是 `ProjectileKind.CircleAtPoint`（radius 1.5 / MaxImpactDistance 15 /
  speed 18），服务端按落点圆判定并叠加飞行延迟 `ProjectileTravelTicks`。
- 资产与表逐值对齐由 EditMode `FireballAssetTableTests` 锁定（HoldToAim / MinAimDistance /
  CanMoveWhileCharging / 爆炸半径 / 射程 / 速度）。
- **施法表现**：`Character3C.controller` 的 `Fireball` 条件不再进入 `SkillQ`
  （`Attack03_SwordAndShield` 挥剑）；施法期间 Attack 层保持 `AttackIdle`（战斗待机），
  不再出现像普攻的挥剑动作。Q 技能仍走自己的 `SkillQ` 状态。

- [Sys3C](../Sys3C/README.md) —— 攻击层、角色状态与突进
- [Combat](../Combat/README.md) —— 命中检测与伤害接口
- [VFX](../VFX/README.md) —— 技能特效
- [Stamina](../Stamina/README.md) —— 技能精力消耗
- [SyncLayer](../SyncLayer/README.md) —— 施法上报
