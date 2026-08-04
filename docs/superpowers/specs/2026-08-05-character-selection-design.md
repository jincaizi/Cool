# 男女角色选择系统（双 prefab 复用 3C）设计

- 日期: 2026-08-05
- 状态: 已批准（方案 B：双 prefab）
- 目标: 游戏启动 → 选人（男女预览）→ 进世界，所选角色完整复用现有 3C 系统

## 1. 背景与结论

RPG Duo 资产包自带男女角色，共用同一骨架（`ModularCharacterPBR.fbx`）、同一套动画 clip（`Animation/SwordAndShield/`）和同一 `Character3C.controller`。3C 系统（`Sys3CEntry` + FSMManager + AnimationDriver）完全由 Animator 参数驱动，与角色模型零耦合。

**结论：3C 代码零改动。** 女角色通过镜像男角色 prefab 接线获得完整可玩性（移动/跳跃/攻击/技能Q/R/受击/死亡全部复用），选人流程为新增的纯客户端模块（PlayerPrefs 持久化，后续网络化时再扩展协议）。

## 2. 核心设计原则

1. **完全同构**：女 prefab 与男 prefab 组件接线完全一致，行为天然一致，不引入任何新绑定（nameplate/HUD/相机绑定方式与男角色完全相同）。
2. **相机自动绑定**：`Sys3CEntry.Start()` 已自动 `FindObjectOfType<ThirdPersonCameraController>()` 并设置 `Target` + `SnapToTarget()`（Sys3CEntry.cs:120-125）—— 生成器**不需要**相机引用。
3. **事件驱动**：选人面板与生成器通过 `EventBus` 通信（项目层间通信约定，与 `HitFeedbackSystem` 模式一致）。
4. **禁止运行时 `GetComponent`**：预览体用剥离脚本的专用 prefab，面板序列化引用（符合项目 C# 语法规则）。

## 3. 资产清单

### 3.1 `FemalePlayer.prefab`（核心资产工作）

从 `Assets/RpgDuo/Prefab/FemaleCharacterPBR.prefab` 基础上，镜像男模玩家 prefab（`Assets/RpgDuo/Prefab/MaleCharacterPBR.prefab`，场景中的玩家实例）的完整接线：

- 组件：`Sys3CEntry`、`UnityEngine.CharacterController`、`Animator`（指定 `Character3C.controller`）、`MeleeWeapon`、`CharacterAttackHandler`、`HitFlashVFX`、`FrostAuraVFX`、`IceDecalVFX`、`IceBurstVFX`、`SkillFreezeEffector`
- 序列化字段：`Sys3CEntry._characterSkills`（**共享引用**同一组 SkillData 资产，不复制资产）、`MeleeWeapon` 武器配置、GroundLayer、VFX 引用、挂点引用（骨架相同，`weapon_r`/`weapon_l`/手部挂点通用）
- 部件可见性：启用女部件组合，禁用男部件；**以 stock `FemaleCharacterPBR.prefab` 的默认活动部件为准**核对外观
- `RingVisual`（地面指示圈）一并复制
- 实施时需 diff 场景中男模玩家实例与 prefab 的差异，把场景侧新增部分也纳入女 prefab（若有）

### 3.2 预览 prefab（2 个，剥离脚本）

- 从玩家 prefab 复制并**删除全部 MonoBehaviour 脚本**（无 Sys3CEntry/无 VFX/无物理），仅保留模型部件 + 部件可见性状态（男预览 = 男部件，女预览 = 女部件）
- **禁用 Animator**（静态摆姿，不跑 Locomotion 树；MVP 不做动画预览）
- `CharacterSelectPanel` 序列化引用，运行时零 `GetComponent`

## 4. 新增代码

| 文件 | 职责 |
|---|---|
| `Sys3C/Character/CharacterGender.cs` | `enum CharacterGender { Male, Female }` |
| `Sys3C/Character/CharacterSelectionService.cs` | 静态类：`Current`、`Save(gender)`、`Load()`；PlayerPrefs key `character.gender`，无存档/非法值 → `Male` |
| `Sys3C/Character/PlayerSpawner.cs` | MonoBehaviour：序列化 `_malePrefab`/`_femalePrefab`/`_spawnPoint`；EventBus 订阅 `CharacterSelectedEvent`，生成前销毁旧玩家实例（防重复）；不持有相机引用 |
| `Sys3C/Core/Events/CharacterSelectedEvent.cs` | 事件：携带 `CharacterGender` |
| `UI/Panel/CharacterSelect/CharacterSelectPanel.cs` | UIPanel：序列化引用 2 个预览 prefab，**打开时 Instantiate、关闭时销毁**（面板持有预览体，场景只负责相机取景位置）；上次选择高亮、确认按钮 → Save + `EventBus.Publish(CharacterSelectedEvent)` + 关闭自身 |
| `UI/Panel/CharacterSelect/PreviewRotator.cs` | 挂在预览体上的纯旋转组件（无 GetComponent） |

## 5. 改动清单

- `UIManager.LoadBuiltInPanels()`（UIManager.cs:31-40）：+2 行 —— 注册 `CharacterSelectPanel`（Resources/Prefabs/UI/CharacterSelectPanel）并 `OpenAsync`；HUD 自动显示逻辑**不动**（选人面板全屏覆盖，确认后关闭）
- 场景（DemoDay）：移除烘焙的 `MaleCharacterPBR` 实例；新增 `PlayerSpawner` 对象（序列化 2 个玩家 prefab + 出生点 `PlayerSpawnPoint` 空对象）；Main Camera 位置调整为选人视角（能看见两个预览体）
- **接线基准**：以场景中当前正常工作的男模玩家实例（prefab + 可能的场景侧新增）为基准 —— diff 出的场景侧组件**先并入男玩家 prefab**（保证生成后与现状行为一致），再镜像到女 prefab

## 6. 数据流

1. 场景启动 → `UIManager.LoadBuiltInPanels` 注册并打开 `CharacterSelectPanel`（HUD 同时显示，被面板覆盖）
2. 面板：男/女卡片 + 3D 预览（`PreviewRotator` 慢转），`CharacterSelectionService.Load()` 高亮上次选择（默认男）
3. 确认 → `CharacterSelectionService.Save(gender)` → `EventBus.Publish(new CharacterSelectedEvent(gender))` → 面板关闭
4. `PlayerSpawner` 收到事件 → 销毁旧玩家（若有）→ 在 `PlayerSpawnPoint` 实例化对应 prefab
5. `Sys3CEntry.Start()` 自动完成：PhysicsRegistry 注册、相机 Target 绑定、技能注册 —— 游戏开始，与烘焙玩家行为完全一致

## 7. 错误处理与边界

| 场景 | 处理 |
|---|---|
| 无存档 / 读档失败 / 非法值 | `Load()` 返回 `Male`，面板默认高亮男 |
| prefab 引用缺失 | Spawner 报 `LogError`，玩家不生成，面板保持打开可重选 |
| 重复选人（重复确认） | Spawner 销毁旧实例再生成，不产生双玩家 |
| 快速连点确认 | 事件幂等：Spawner 每次收到事件先销毁旧实例 |

## 8. 测试

### 8.1 单元测试（EditMode）

`CharacterSelectionService`：
- Save(Male) → Load == Male（往返）
- Save(Female) → Load == Female（往返）
- 无存档 → 默认 Male
- 存档写入非法值 → 默认 Male
- 测试后清理 PlayerPrefs key

### 8.2 场景验证清单（手动，男/女双路径）

- 启动显示选人面板，预览正常（部件组合正确、慢速旋转）
- 选女确认 → 女角色生成：移动/跳跃/冲刺/防御
- 普攻连击（Attack1/Attack2）、技能 Q、技能 R（含突进/旋转）
- 受击/击退/击倒/死亡动画与状态、HitFlashVFX 泛红
- 相机跟随、HUD 正常
- 选男回归：与选女完全同路径，行为不受影响
- 再次启动 → 上次选择正确高亮（持久化生效）

## 9. 决策记录

- 2026-08-05：方案 B（双 prefab）经评审后确认。评审修正：① 相机绑定由 `Sys3CEntry` 自动完成，生成器不持有相机引用；② 启动入口挂在 `UIManager.LoadBuiltInPanels`，HUD 逻辑不动；③ 预览体用剥离脚本 prefab，杜绝运行时 `GetComponent`；④ 面板与生成器走 `EventBus`。
