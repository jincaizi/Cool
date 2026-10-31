# Skills —— 技能系统

技能系统负责技能数据、输入缓冲、状态机、执行、效果与事件。攻击层由 `Runtime/SkillCoordinator.cs`
接入 3C，不使用 AttackFSM。

## 目录结构

| 目录 | 内容 |
|---|---|
| `Definition/` | `SkillState` / `SkillType` / 技能子状态枚举 |
| `Data/` | `SkillData` 与技能类型数据：`InstantSkillData` / `ProjectileSkillData` / `ComboSkillData` / `ChargedSkillData` / `ChanneledSkillData` / `SpinSkillData`；`MeteorConfig` / `SwordQiConfig`；`EffectBlock` / `ShapeBlock` / `DamageBlock` / `PresentationBlock`；`SkillPresentationCatalog`（7 个同步玩家技能，与本地职业无关的远程表现目录） |
| `Runtime/` | `SkillCoordinator` / `SkillStateMachine` / `SkillExecutor` / `SkillInputBuffer` / `CooldownManager` / `SkillInterruptionMatrix` / `SkillDamageHelper` / `SpinHitTracker` |
| `Effect/` | `StatusEffectManager` / `StatusEffectRegistry` / `EffectData` / `BurnEffectData` |
| `Events/` | 火球、陨石、剑气、状态效果与 VFX 事件 |
| `EventBus.cs` | 静态事件总线；跨模块事件都走这里 |

## 主流程

`InputManager` → `SkillCoordinator`（输入、冷却、打断）→ `SkillStateMachine`（阶段与 Hitbox 窗口）
→ `SkillExecutor`（目标检测、伤害、效果）→ `EventBus` 事件 → Nameplate / VFX / 状态效果。

- 蓄力、旋转、冲刺、投射物各有独立数据类与测试。
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

- [Sys3C](../Sys3C/README.md) —— 攻击层、角色状态与突进
- [Combat](../Combat/README.md) —— 命中检测与伤害接口
- [VFX](../VFX/README.md) —— 技能特效
- [Stamina](../Stamina/README.md) —— 技能精力消耗
- [SyncLayer](../SyncLayer/README.md) —— 施法上报
