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

## 测试

Editor：`SkillCoordinator*Tests`、`SkillExecutor*Tests`、`SkillStateMachine*Tests`、`SkillDataMeteorTests`、
`SkillInterruptionMatrixSpinTests`、`StatusEffect*Tests`、`Fireball*Tests`、`Meteor*Tests`、`SwordQi*Tests`、
`BurnEffectDataTests`、`ProjectileSkillDataTests`、`SkillPresentationCatalogTests`、`RemoteProjectileApiTests`。

```bash
bash tools/unity-tests.sh EditMode SkillExecutorMeteorTests
```

## 相关模块

## 剑气回荡（20006，2026-11-18）

- `SwordQiEchoSkillData` 承载确认超时、飞剑预制体和命中裂光引用；半径/目标上限/剑气数/伤害倍率在共享
  `SyncCore.SwordQiEchoRules.Spec`。资产 `_skillType=Instant`、`_canBeInterruptedByMovement=false`。
- 第一次按键布置 4 把飞剑并打开 5 秒待发窗口。角色可以移动，待发本身不扣血也不授予无敌。
  第二次按键按施法者当时的位置选取 12 米内最近的 8 个目标。无目标时飘字，保留剩余待发时间。
- 成功出击或待发超时时开始 12 秒冷却。出击保留 2 秒战斗窗口，结束后由表现层独立回旋收束。
- 联网请求携带阶段、序号、角色绑定代次以及准备实例 ID。单机由 `SwordQiEchoLocalRunner` 驱动同一规则，
  按接触点进入本地伤害。待发确认前的重复按键最多缓存一次释放。

## 蓄力炎爆（20004）

- `ChargedSkillData.ChargeVfx` 随本地角色进入蓄力时启动，释放、完成和打断时清理。
  20004 在释放时记录施法者位置，供本地圆形判定和上报中心共用。
  释放光圈从该位置上方 0.74m 的躯干中心向外扩张，光圈留在出手位置。
  半径随蓄力从 4m 增至 6m，单机命中沿爆心向外水平击退，初始推力从 8m/s 增至 12m/s。

## 陨石术（30001）

- 陨石的本地执行和施法上报共用地面采样。旁观端在广播落点处生成陨石时也采样地面。
  预警贴花沿地面法线放置，下坠给岩体预留离地间隙，落地冲击略高于地表。

## 移动端显示契约

- 技能冷却 UI 只读取 `SkillCoordinator.GetCooldownInfo(skillId)`；遮罩使用 `Remaining / Total`，不得维护第二套计时器。
- 火球与炎爆在实际蓄力释放时才开始冷却；取消蓄力不占冷却。
- 蓄力环只在当前技能处于 `Charging` 时读取 `CurrentSkill.GetChargeProgress()`。
- R 的再次按下继续进入现有“旋转中同键取消”分支，移动按钮不得绕过 `InputManager` 直接施放。

## 火球地面瞄准（HoldToAim）

- 本地和远端使用同一个 `Fireball` 飞行预制体。池中取出的弹体在重新定位后清空旧拖尾。
  火焰、地环和余烬由火球的 `ReleaseVFX` 在实际落点播放。

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
