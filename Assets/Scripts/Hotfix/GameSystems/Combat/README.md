# Combat —— 命中检测与伤害接口

本目录只放客户端命中检测组件；攻击形状与伤害接口位于 `Sys3C/Core/Combat/`，
服务端权威裁决位于 `SyncCore/HitResolver.cs`。

## 本目录

| 文件 | 作用 |
|---|---|
| `AttackHitbox.cs` | 攻击判定器 |
| `HitZone.cs` | 受击区域 |
| `PlayerHitZone.cs` | 玩家受击区域 |

## 攻击形状与伤害接口（`Sys3C/Core/Combat/`）

- 形状：`IAttackShape` + `CircleShape` / `RectShape` / `ConeShape` / `SectorShape`；工厂 `AttackShapeFactory`，调试 `AttackShapeGizmos`。
- 伤害与目标：`IDamageable` / `ITargetable` / `IWeapon` / `IPlayerHpSource` / `IEntityRegistry`。
- 检测：`IAttackHitbox` / `IHitDetector` / `HitZoneClassifier` / `HitContact`。
- 配置：`AttackHitboxData` / `WeaponConfig` / `PhysicsRegistry`。

## 判定链路

- **本地技能**：`SkillExecutor` 用攻击形状筛选目标，命中后调用 `IDamageable`。
- **联网技能**：客户端 `SyncLayer/SkillCastReporter` 上报；服务端 `Server/Handler/CastHandler` 中的
  `CombatResolver` 调 `SyncCore/HitResolver` 做权威裁决。
- **格挡**：本地成功由 `Sys3CEntry` 发布 `BlockSucceededEvent`，远程成功由 `SyncLayer/CombatNetworkBridge`
  发布；`Nameplate/DisplayEventBridge` 显示飘字，`VFX/HitFeedbackSystem` 播放火花。
- **怪物伤害**：`Monster/Damage/DamagePipeline`，含 `DefendModifier` / `IFrameModifier`。

## 测试

- Editor：`HitZoneClassifierTests`、`DamageBlockFluctuationTests`、`CharacterAttackHandlerSpinTests`。
- PlayMode：`HitJudgmentAlignmentTests`、`CombatSyncTests`、`BlockMitigationE2ETests`、`BlockFeedbackDisplayTests`、
  `BlockFeedbackSparkTests`、`BlockStaminaTests`。
- 服务端：`Server/Server.Tests/Combat/`、`Handler/CombatResolver*Tests`。

```bash
bash tools/unity-tests.sh PlayMode BlockMitigationE2ETests
bash tools/test-server.sh --filter "FullyQualifiedName~CombatResolver"
```

## 相关模块

- [Sys3C](../Sys3C/README.md) —— 攻击形状与伤害接口
- [Skills](../Skills/README.md) —— 技能伤害来源
- [Monster](../Monster/README.md) —— 怪物伤害管线
- [SyncCore](../SyncCore/README.md) —— 服务端命中裁决
- [Nameplate](../Nameplate/README.md) / [VFX](../VFX/README.md) —— 命中与格挡表现
