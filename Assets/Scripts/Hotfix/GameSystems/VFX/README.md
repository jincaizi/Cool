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
| `ElementVfxAssets.cs` / `ElementVfxAttachment.cs` | 元素资源地址、异步加载和显示生命周期 |
| `ElementVfxFallback.cs` | 资源命中优先的颜色回退规则 |
| `WeaponElementConfig.cs` / `WeaponMaterialProxy.cs` / `WeaponSurfaceShader.cs` | 武器元素与材质 |
| `PlayerHealVFX.cs` | 治疗表现 |
| `SkillWatcher.cs` | 技能事件观察 |
| `SwordQiEchoPresenter.cs` | 剑气回荡待发环绕、飞行、回旋收束与拖尾回收 |

事件来源：`Skills/Events/` 与 `Skills/EventBus.cs`。格挡火花链路见 [Combat README](../Combat/README.md)。

## 剑气与状态的元素表现

- **`AuthoritativeStatusVfx`**：纯逻辑类，由 `SyncLayer/ClientNetworkHost` 驱动。
  按 `(EntityId, GroupId)` 维护已应用状态表并 diff `CombatProjection.Statuses`：
  新增/同组替换/移除各发一次 `StatusEffectAppliedEvent`/`StatusEffectRemovedEvent`；
  同一投影重复到达零事件。EffectId 映射：`1xx` 冰 → `EffectType.Freeze`、
  `2xx` 火 → `Burn`、`3xx` 雷 → `Stun`、`4xx` 风 → `Vulnerable`。
  实体换渲染根（Proxy↔Full / 本地角色重建）时先对旧根发全部 Removed、再对新根发
  Applied，染色不会留在旧模型。
  **断开清理**：`ClientNetworkHost.Disconnect()` 在 `ResetAttachment` 之后、
  `manager.Dispose()` 销毁视图之前调用 `Clear()`，对每个活动组各发一次 Removed 并清表；
  该路径只用于会话结束，角色重建不调用（换根仍走 `Apply` 的 target diff）。
- **状态表现**由 `StatusVFXSystem` 消费线上与离线的同一组状态事件。同实体多状态按
  `Freeze > Stun > Burn > Vulnerable` 选择贴身特效。冰使用霜纹脚环，火用八帧小火循环，
  雷用短促紫色电弧，风用青色流带。材质直接引用 `Assets/PreRes/SkillsCfg/Textures/` 的生成图。
  异步加载期间和资源缺失时由 `BodyTintRenderer` 用
  MaterialPropertyBlock 做身体染色。换渲染根、状态优先级切换、状态清空或退场时回收旧效果。
- **剑气表现**在 `SwordQiProjectile.OnSpawn` 按施法冻结的 `ElementId` 读取共享表的
  `SwordQiViewKey`。五个预制体共用 `SwordQiWave.asset` 的网格和 `SwordQiEnergy.shader`。
  亮刃与尾缘属于同一片弯曲网格，沿全弧向后渐隐并在两翼收尖。材质根据飞行进度改变展开宽度、
  尾缘长度及倾角。默认银白，冰有晶纹，火有起伏的焰纹，雷有折线电丝，风有柔和回流。
  首帧回退使用同元素轮廓与染色。Addressables 实例继承当前飞行进度，再替换回退渲染器。
- `ElementVfxAttachment` 通过 `Res.LoadAsync` 加载，仅活跃表现持有资源引用。回池、换根或
  移除状态时取消待完成请求，迟到的资源仅归还引用。已生成的表现先隐藏再销毁，销毁后释放引用。
- **资源登记**由编辑器 `ElementalVfxRegistration` 在导入后与 Player Build 前执行。四种元素
  各登记 `swordqi/{ice,fire,thunder,wind}` 和 `status/{ice,fire,thunder,wind}`，对应
  `Assets/Prefabs/VFX/Elemental/` 的八个纯表现预制体。Player Build 从本地
  `Elemental VFX` 组构建和打包。可用菜单 **Cool → VFX → Register Elemental Addressables** 重新登记。

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

- Editor：`Assets/Scripts/Tests/Editor/VFX/`（`BodyTintRendererTests`、`HitTintStateTests`、`PlayerBodyTintTargetTests`、
  `AuthoritativeStatusVfxTests`、`ElementVfxFallbackTests`、`ElementVfxAttachmentTests`）、`StatusVFXSystemTests`；剑气染色在
  `Assets/Scripts/Tests/Editor/Skills/SwordQiElementTintTests.cs`。
- PlayMode：`ElementalAddressableLifecycleTests` 加载八个元素预制体并验证资源引用回收。
  受击与治疗回归包括 `BlockFeedbackSparkTests`、`PlayerHealVFXTests`、`PlayerBodyTintTests`。

```bash
bash tools/unity-tests.sh EditMode BodyTintRendererTests
bash tools/unity-tests.sh PlayMode BlockFeedbackSparkTests
```

## 相关模块

- [Skills](../Skills/README.md) —— 技能事件
- [Combat](../Combat/README.md) —— 命中与格挡
- [Nameplate](../Nameplate/README.md) —— 飘字与屏效
- [Monster](../Monster/README.md) —— 怪物受击目标
