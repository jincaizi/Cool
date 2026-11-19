# VFX —— 武器、技能与受击特效

## 组成

| 文件 | 作用 |
|---|---|
| `WeaponVFXController.cs` | 武器特效总控 |
| `WeaponTrailRenderer.cs` / `WeaponMistParticles.cs` | 武器拖尾与雾气粒子 |
| `SlashTrailVFX.cs` / `SlashBloodTrail.cs` | 挥砍与血迹 |
| `SwordGlowVFX.cs` / `FrostAuraVFX.cs` / `IceBurstVFX.cs` / `IceDecalVFX.cs` | 技能与元素特效 |
| `HitFeedbackSystem.cs` / `HitSpark.cs` | 受击与格挡反馈；订阅 `BlockSucceededEvent` 播放金色火花 |
| `HitStopManager.cs` / `CameraShakeManager.cs` | 顿帧与震屏 |
| `HitFlashVFX.cs` / `BodyTintRenderer.cs` / `PlayerBodyTint.cs` / `HitTintState.cs` | 受击闪白与染色 |
| `StatusVFXSystem.cs` / `SkillFreezeEffector.cs` | 状态效果表现 |
| `AuthoritativeStatusVfx.cs` | 权威状态投影 → 按组 diff 的表现事件（Plan 04） |
| `WeaponElementConfig.cs` / `WeaponMaterialProxy.cs` / `WeaponSurfaceShader.cs` | 武器元素与材质 |
| `PlayerHealVFX.cs` | 治疗表现 |
| `SkillWatcher.cs` | 技能事件观察 |

事件来源：`Skills/Events/` 与 `Skills/EventBus.cs`。格挡火花链路见 [Combat README](../Combat/README.md)。

## 权威状态与元素表现（Plan 04，2026-11-19）

- **`AuthoritativeStatusVfx`**：纯逻辑类，由 `SyncLayer/ClientNetworkHost` 驱动。
  按 `(EntityId, GroupId)` 维护已应用状态表并 diff `CombatProjection.Statuses`：
  新增/同组替换/移除各发一次 `StatusEffectAppliedEvent`/`StatusEffectRemovedEvent`；
  同一投影重复到达零事件。EffectId 映射：`1xx` 冰 → `EffectType.Freeze`、
  `2xx` 火 → `Burn`、`3xx` 雷 → `Stun`、`4xx` 风 → `Vulnerable`。
  实体换渲染根（Proxy↔Full / 本地角色重建）时先对旧根发全部 Removed、再对新根发
  Applied，染色不会留在旧模型。
- **四色状态染色**：`StatusVFXSystem` 对四种权威状态做身体染色——
  冰 `(0.55,0.75,1)`、雷 `(0.7,0.4,1)`、火 `(1,0.35,0.1)` 呼吸闪烁、风 `(0.3,1,0.9)`；
  同实体多状态按 `Freeze > Stun > Burn > Vulnerable` 取优先级，计数归零后 `Clear`。
  染色用 `BodyTintRenderer`（MaterialPropertyBlock），不污染共享材质。
- **剑气元素色**：`SwordQiElementTint`（`Skills/Runtime/SwordQi/SwordQiProjectile.cs`）
  在生成时按 `ElementId` 给拖尾/粒子 `startColor` 与材质 `_Color` 上色
  （冰蓝白、火橙红、雷紫白、风青白；`0` = 保留预制体原色）。联机剑气读
  `ProjectileLaunchBroadcast.ElementId`；离线 `SkillExecutor` 的元素 provider 留 Plan 05。
- 当前是程序化染色/简化特效，不依赖新增美术资源；`ViewKey` 的资源化留给 Plan 05。

## 测试

- Editor：`Assets/Scripts/Tests/Editor/VFX/`（`BodyTintRendererTests`、`HitTintStateTests`、`PlayerBodyTintTargetTests`、
  `AuthoritativeStatusVfxTests`）、`StatusVFXSystemTests`；剑气染色在
  `Assets/Scripts/Tests/Editor/Skills/SwordQiElementTintTests.cs`。
- PlayMode：`BlockFeedbackSparkTests`、`PlayerHealVFXTests`、`PlayerBodyTintTests`。

```bash
bash tools/unity-tests.sh EditMode BodyTintRendererTests
bash tools/unity-tests.sh PlayMode BlockFeedbackSparkTests
```

## 相关模块

- [Skills](../Skills/README.md) —— 技能事件
- [Combat](../Combat/README.md) —— 命中与格挡
- [Nameplate](../Nameplate/README.md) —— 飘字与屏效
- [Monster](../Monster/README.md) —— 怪物受击目标
