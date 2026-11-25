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
| `SwordQiEchoPresenter.cs` | 剑气回荡待发环绕、飞行、回旋收束与拖尾回收 |

事件来源：`Skills/Events/` 与 `Skills/EventBus.cs`。格挡火花链路见 [Combat README](../Combat/README.md)。

## 剑气回荡

- 待发和回收时，飞剑在角色上胸附近环绕。出击时仅分配到目标的剑离开环绕，按排期穿过一个或多个目标后平滑返回。
  待发取消与出击结束后，环绕半径收小，剑头与拖尾一起淡出，根节点清拖尾回池。
- 联网使用服务器的环绕起点、释放位置和接触时间表。不同到包时间的客户端按同一时间轴定位，晚进入视野时照此推进。
- `SwordQiEcho_Skill4` 为三条运行入口提供同一个 `SwordQiEchoWhite` 预制体。剑头贴图随飞行朝向，
  绕运动轴朝向镜头。拖尾使用不循环的横向 UV；接触裂光由实际伤害触发，联网在去重后播放。
- 测试：`SwordQiEchoPresenterTests`（EditMode，注入时钟）、`SwordQiEchoPresentationTests`（PlayMode）。

## 火球与蓄力炎爆

- `ChargeFire_SkillR` 的蓄力预制体用聚火火丝贴图，跟随施法者并在退出蓄力时消失。
  `ChargeBlastShockRing` 从出手时角色的躯干中心水平扩张并淡出，满蓄力放大光圈直径。
- `Fireball` 的飞行阶段用 `FireballCore.shader` 生成随时间流动的金橙色火核，
  独立 `FireballTrail` 材质复用现有火焰尾迹贴图。
- `Explosion_Fireball` 复用 16 帧火焰、贴地扩张环与四种随机余烬，由火球的落地逻辑触发。
- 原始 PNG 位于 `Assets/PreRes/SkillsCfg/Textures/`。特效材质使用 `SkillTextureVfx.shader`
  和直通 Alpha。序列帧在导入时向格内收边，避免采样邻格。

## R 拖尾、陨石和防御

- 男女角色的 `WeaponTrailRenderer` 共用 `SpinFrostTrail` 材质。旋转斩拖尾起始宽度乘以 1.85。
  白亮前缘过渡到冰蓝细尾，更密的路径采样让弧线连贯，停止发射后尾迹自然消散。
- `MeteorStrike` 预警末段显示下坠岩体和尾焰。岩体按半径和地面坡度预留间隙，
  `Explosion_Meteor` 在校正后的地面落点播放 16 帧冲击。
- `DefendHaloVfx` 同时控制脚边金色底圈与半透明半球。格挡结果短暂提亮，解除防御时两层一起淡出。

## 状态贴图

- DemoDay 的 `StatusVFXSystem` 为冰霜、灼烧、麻痹、易伤各实例化一层。以已有状态应用和移除事件为准。
  同类状态共用一层，最后一条移除时隐藏，目标销毁、停用或更换表现根时清理。

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
