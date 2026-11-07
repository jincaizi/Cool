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
| `WeaponElementConfig.cs` / `WeaponMaterialProxy.cs` / `WeaponSurfaceShader.cs` | 武器元素与材质 |
| `PlayerHealVFX.cs` | 治疗表现 |
| `SkillWatcher.cs` | 技能事件观察 |

事件来源：`Skills/Events/` 与 `Skills/EventBus.cs`。格挡火花链路见 [Combat README](../Combat/README.md)。

## 测试

- Editor：`Assets/Scripts/Tests/Editor/VFX/`（`BodyTintRendererTests`、`HitTintStateTests`、`PlayerBodyTintTargetTests`）、
  `StatusVFXSystemTests`。
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
