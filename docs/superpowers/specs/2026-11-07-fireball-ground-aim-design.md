# 火球术移动端地面瞄准——设计

日期：2026-11-07

## 目标

把火球术（20003）从「按住蓄力、松手朝角色前方发射」改为移动端 MOBA 式地面瞄准：

- 按住火球按钮进入瞄准：图标变成摇杆，地面出现爆炸中心圈。
- 拖动同时控制方向与距离，最大施法距离 15m（可配置）。
- 任意方向拉远进入取消态，松手取消；精力耗尽自动取消。
- 瞄准期间角色不可移动、可转向，转向由按钮控制。
- 点按（未拖动）优先打当前选中目标（含施法瞬间自动锁定），否则朝前打最远。
- 客户端与服务端判定一致；本期只做功能，表现使用占位资源。

## 非目标

- 不做蓄力伤害/范围缩放；火球仍固定 1 倍。
- 不做「锁定目标自动瞄准」之外的自动瞄准。
- 不做 PC 鼠标地面瞄准；PC 维持「朝向前方最远」。
- 不做新美术资源；地面圈用占位圆柱/陨石预警材质。
- 不改其他技能的蓄力/精力/打断语义（20004、30001 行为不变）。
- 不新增防作弊机制；沿用现有落点有限性与距离上限校验。

## 交互语义

### 按住与阶段

- 按下火球按钮：UI 手势立即开始；技能沿用现有 `Casting`（0.1s）→ `Charging`。
- 地面圈只在进入 `Charging` 后显示；`Casting` 期间已锁移动。
- 点击/拖动事件由 `MobileSkillAimJoystick` 与 `MobileSkillButton` 在同一按钮对象上并存处理；按钮根节点不动，只有 `Icon` 子物体跟手。

### 瞄准映射

- 方向：以相机**水平 yaw** 为基准（摇杆上 = 相机前方）。
- 距离：拖杆幅度 `m`（0–1，按 `aimRadius` 归一）映射到 `[minAimDistance, ProjectileRange]`，默认 `2m–15m`。
- 死区：`m < 0.1` 视为未拖动，采用「默认落点」规则（见下）。
- 拖动原点：按下点。`delta = 当前点 − 按下点`；图标位置 = `clamp(delta, aimRadius)`；取消阈值用未钳制的 `delta.magnitude`。
- `aimRadius` 使用 **Canvas 单位**（与按钮同坐标系），默认 90；不是屏幕像素。

### 默认落点（死区内 / 点按）

1. 当前有选中目标 → 落点 = 目标 XZ 向下 raycast 得到的地面点；水平距离超过 15m 时沿「玩家→目标」方向钳到 15m（网络上报按 14.95m 留余量）。
2. 没有选中目标 → 角色当前水平朝向 × 最大施法距离。
3. 瞄准期间每帧重算：目标移动时地面圈跟随；目标死亡 / 清锁 / 出 AOI → 回退规则 2。
4. 点按（未拖动）松手 = 按上述默认落点发射。
5. `Sys3CEntry` 每帧在**释放判定之前**刷新瞄准点：快速点按时，激活瞬间刚由 `EnsureTargetFor` 自动锁定的目标必须在释放前被读到，否则会退回「前方最远」。

选中目标来源：

- 联网：`NetworkTargetSelector.LockedTargetId` 对应远程视图位置；包含 `EnsureTargetFor` 在施法激活时的自动锁定（最近候选）。
- 单机：`CharacterAttackHandler` 当前选中目标（目标面板 / 选中环显示的那个）。
- `ClientNetworkHost` 联网装配时向 `Sys3CEntry` 注入位置提供者；单机由 `Sys3CEntry` 已有的 `_attackHandler` 兜底。

### 取消

- 指针相对**按下点**的未钳制位移超过 `aimRadius × 1.4` → 进入可取消态：图标与地面圈变红（占位表现）。
- 回拉到 `aimRadius × 1.2` 以内 → 退出取消态（带回滞）。
- 取消态松手 → 取消蓄力：不发射、不占冷却、不上报 `CastRequest`；图标复位。
- 阈值/回滞/颜色全部 Inspector 可配。

### 精力与打断

- 瞄准期间沿用 `StaminaDrainPerSecond = 15` 持续扣精；上限 100 ⇒ 约 6.7 秒。
- 精力耗尽 → 自动取消（不发射、不冷却、不上报）。
- 受击打断、死亡、切角色、断线：沿用现有技能中断；UI 复位。

### 角色

- 瞄准期间 `LockMovement = true`、`LockRotation = false`。
- **移动端瞄准手势激活时**：朝向跟目标/拖动方向；无目标且无拖动时保持当前朝向；跳过现有「蓄力时朝相机前方转」的分支，避免两个旋转源。平滑转向沿用现有 10/s Slerp。
- **无移动端手势时（PC/键鼠）**：保持现有行为——角色继续跟随相机朝向，落点 = 角色当前朝向 × 最大距离，不显示地面圈。
- 瞄准期间屏蔽跳跃、防御、攻击与其他技能输入（其他技能沿用 `Charging` 既有缓冲语义）。
- `Casting` 阶段也不可移动（仅 `HoldToAim` 技能特判）。
- PC/键鼠的 `HoldToAim` 同样不再 2s 自动释放：松手结束，或精力耗尽取消；方向仍为角色当前朝向。

## 分层与数据流

```
MobileSkillAimJoystick (AimGestureState)
  → MobileInputBinder (LateUpdate)
    → InputManager (aim-active / aim 向量 / cancel-armed / release-kind)
      → Sys3CEntry (世界落点、角色转向、地面圈)
        → SkillCoordinator.UpdateAimTarget / TryReleaseCharge / CancelCharge
          → SkillExecutor → FireballSpawner → FireballProjectile

选中目标：
  NetworkTargetSelector (联网，ClientNetworkHost 注入位置提供者)
  CharacterAttackHandler.CurrentTarget (单机兜底)

服务端：
  CastRequest(ImpactX/Y/Z) → CombatRules / CombatResolver(CircleAtPoint)
    → CastBroadcast / DamageResult → RemoteCastPresenter
```

UI 不直接调用 `SkillCoordinator`，沿用现有唯一输入通路。

## 数据与配置

### ProjectileSkillData

新增字段：

| 字段 | 默认 | 含义 |
|---|---:|---|
| `_holdToAim` | false | 按住瞄准；不自动释放；精力耗尽改为取消；释放上报落点 |
| `_minAimDistance` | 2 | 最小落点距离；最大距离复用 `_projectileRange` |
| `_aimIndicatorPrefab` | null | 地面落点圈占位 prefab；仅本地瞄准使用 |

### 火球资产

`Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset`：

- `_holdToAim: 1`
- `_minAimDistance: 2`
- `_canMoveWhileCharging: 0`
- 其余不变：`_maxChargeTime: 2`（仅作进度归一化，不再触发自动释放）、
  `_staminaDrainPerSecond: 15`、`_explosionRadius: 1.5`、`_projectileRange: 15`、`_projectileSpeed: 18`。

Unity 对缺失序列化字段使用默认值（false/0），不会报错；必须显式写入资产。

### 服务端表

`CombatRules` 20003 表项：

```
ProjectileKind.CircleAtPoint, radius: 1.5, maxImpactDistance: 15, speed: 18, stages: 1
```

- `ProjectileSpec` 新增可选 `Speed`，仅 `CircleAtPoint` 且 `Speed > 0` 时用于判定延迟。
- shape 上限保持 0（客户端投射物发零形状，现有 `ValidateShape` 通过）。

### UI / 场景配置

`MobileSkillAimJoystick`（挂在火球按钮上）：

- `_buttonRect`、`_icon`（场景已有 `Icon` 子物体）、`_aimRadius`（Canvas 单位，90）。
- `_deadZone`（0.1）、`_cancelEnterScale`（1.4）、`_cancelExitScale`（1.2）、`_cancelColor`、可选取消标记。
- Icon 的 `RaycastTarget` 保持 0；取消变色改 Icon 的 `Image.color`（Button 的 ColorTint 只作用根 Image）。

`InputManager` 新增状态：

| 状态 | 写入方 | 读取方 | 说明 |
|---|---|---|---|
| `aim-active` | `MobileInputBinder`（每帧） | `Sys3CEntry` | 移动端手势是否按下；决定走移动端瞄准还是 PC 键鼠路径 |
| `aim` 向量 | `MobileInputBinder`（每帧） | `Sys3CEntry` | 按下点相对位移，模长 0–1 |
| `cancel-armed` | `MobileInputBinder`（每帧） | `Sys3CEntry` / 指示器 | 取消回滞后的状态 |
| `release-kind` | `MobileInputBinder`（一次性） | `Sys3CEntry` | `Fire` / `Cancel`；沿用现有「值 + consumed 锁存」模式 |

`Sys3CEntry`：

- `_aimGroundMask`（序列化；排除 `NpcBody` 层索引 10，场景中指到地面层）。
- 选中目标位置提供者（联网注入；单机走 `_attackHandler`）。
- `IsSkillFireballAimActive()` 为 true 时走移动端瞄准；否则走 PC 键鼠路径（角色跟随相机、前方最远、无地面圈）。
- 新增 `CancelAimingSkill()`：内部调用 `SkillCoordinator.CancelCharge`，供 teardown 使用。

## 技能层

### SkillStateMachine

- 缓存 `_holdToAim = _projectileData?.HoldToAim ?? false`。
- `UpdateCharging`：`_holdToAim` 时跳过 `chargeTime >= MaxChargeTime` 的自动 `Execution`，停在 `Charging` 等松手/取消/精力耗尽。
- `CanBeInterrupted(PlayerCancel) => true`；`Casting`/`Charging` 可取消，`Execution` 仍拒绝。

### SkillCoordinator

- `UpdateAimTarget(Vector3 point)`：仅当前 `HoldToAim` 技能活跃时写入 executor。
- `CancelCharge(int skillId)`：仅在 `Casting`/`Charging` 生效，走 `PlayerCancel`，**不调用** `ReportChargeReleased`。
- `CanMove`：
  - `Casting`：`HoldToAim` 投射物返回 false，其余保持现有 true。
  - `Charging`：`HoldToAim` 投射物返回 false；否则沿用 `ProjectileSkillData.CanMoveWhileCharging`。
- `HandleStaminaDepleted`：`HoldToAim` 的 `Charging` 改为 `TryInterrupt(StaminaExhausted)`；
  取消分支按 `CurrentSubState == Cancelled` 判定（不能只信 `TryInterrupt` 返回值），其余技能保持自动释放。
- `ReportChargeReleased` 改用 `SkillExecutor.ReportedImpactPoint`。

### SkillExecutor

- `SetTargetPosition` 记录 `_hasTargetPosition`。
- `ReportedImpactPoint`：

```
HoldToAim ? (_hasTargetPosition ? _targetPosition
                                : owner.position + owner.forward * ProjectileRange)
          : TargetPositionOrOwner
```

兜底**绝不能**是 owner 位置，否则服务端会把落点算在施法者脚下。

- `SpawnFireball`：`HoldToAim` 时用落点算水平方向并传 `targetPoint`；否则沿用「朝向 × ProjectileRange」。

### FireballSpawner / FireballProjectile

- `Spawn` / `OnSpawn` 增加可选 `targetPoint`。
- `OnSpawn` 必须重置新增状态（`_hasTarget` / `_targetPoint` / `_flightTime` / `_released`），防池复用串味。
- 有落点：水平距离反解飞行时间 `t = distance / speed`，`vy0 = (target.y − origin.y) / t + 0.5·g·t`，落点 = `target`。
- `HoldToAim` 时不做飞行途中的 `DetectAndExplode`（不撞怪提前爆），只在地面点爆炸。
- 落地判定：`_velocity.y < 0 && _flightTime > 0.05f && position.y <= _groundY`（防上坡/高台一生成就爆）。
- 目标距离 < 0.1m 时不生成投射物。

### 打断枚举

`InterruptionSource` 末尾追加 `PlayerCancel`；`SkillInterruptionMatrix` 与 `SkillStateMachine` 都放行。

## 网络与服务端

### 上报

- `SkillCastReporter.ReportChargeRelease` 落点条件扩展为
  `ChargedSkillData || ProjectileSkillData.HoldToAim`。
- 取消路径不发任何消息；冷却只在 `ReportChargeReleased` 起算，与服务端「收到 `CastRequest` 才 `RecordCast`」同源。

### 服务端判定

- `ValidateProjectileRequest` 自动开始校验 20003：落点有限 + 距施法者 XZ ≤ 15m；
  非法落点拒收（`impact-not-finite` / `impact-too-far`）。
- 首个判定 tick 增加飞行延迟：`ceil(水平距离 / Speed × 15Hz)`；
  新增纯函数 `CombatRules.ProjectileTravelTicks(...)`（非有限/负值/零距离钳制）。
- 判定形状改为落点圆；候选集不变（玩家施法仍含玩家 + 怪物）。
- 客户端落点钳制到 `14.95m` 留余量，避免位置同步抖动触发 `impact-too-far`。

### 远程表现

- `RemoteCastPresenter` 火球视觉用 `broadcast.Impact` 作为飞行目标；
  `Tracked` 记录落点，`DamageResult` 到达时在该点爆炸（不再用 `Hits[0]` 的受击者位置）。
- 未命中（无 `DamageResult`）时视觉自然落在 Impact 点并回收。
- 不改变测试注入工厂签名 `(data, origin, forward)`；默认路径单独走带 Impact 的重载。

## UI 与表现

### MobileSkillAimJoystick

- 纯逻辑抽成 `AimGestureState`（仿 `SprintGestureState`）：按下点原点、半径归一、死区、取消回滞、释放类型。可 EditMode 直接断言。
- 指针：`OnPointerDown` 记按下点；`OnDrag` 算 delta、移动 Icon、更新 `AimValue`；`OnPointerUp` 写释放类型（`CancelArmed ? Cancel : Fire`）。
- `OnDisable` / `ResetGesture()`：释放类型记为 `Cancel`；`OnEnable` 清干净，防重入残留。
- `ConsumeReleaseKind()` 一次性消费；图标回中、颜色复原。
- `SetInteractable(bool)`：冷却/精力不足时按钮由 `MobileSkillBarPresenter` 置为不可交互，图标不动。

### 地面落点圈

- `FireballAimIndicator.prefab`（占位）：扁平圆柱，直径 1、高 2 的 primitive → 半径 1.5 时 `localScale = (3, 0.02, 3)`；贴地 +0.05~0.1m。
- 无 Collider；阴影关闭（`shadowCastingMode = Off`）。
- 材质复用 `WarnDecal.mat`（Built-in `Sprites/Default`，`_Color`）；用 `MaterialPropertyBlock` 设 `_Color`，
  **不要**直接改共享材质（会污染所有陨石预警圈）。
- 由技能数据直接引用 prefab（与 `ReleaseVFX` 同模式），不走 Addressables/运行时 `Shader.Find`。
- 只创建一次，`SetActive` 切换；仅**移动端瞄准手势激活的** `HoldToAim` `Charging` 显示（PC 不显示）；取消态变红；随玩家销毁清理。
- 每帧写 world pose；无分配。

### 技能栏

- `MobileSkillBarPresenter`：`HoldToAim` 技能在 `Charging` 时隐藏蓄力环；
  `Charging` 结束但摇杆仍按下（精力耗尽/受击打断）→ `ResetGesture()`。
- 冷却遮罩/倒计时不变。

## Unity 工程注意事项

1. 地面射线：`Physics.Raycast(..., QueryTriggerInteraction.Ignore)`，掩码显式排除 `NpcBody`（层 10）与判定触发器；不用默认 `Physics.Raycast`。
2. 指示器 primitive 自带 `CapsuleCollider`：先 `enabled = false` 再 `Destroy`。
3. 共享材质染色用 `MaterialPropertyBlock`（Built-in RP，属性 `_Color`），避免污染 `WarnDecal.mat`。
4. 相机方向只用水平 yaw，不把带 pitch 的 `camera.transform.forward` 直接喂给 `Quaternion.LookRotation`。
5. `InputManager.Update` 同时是 Unity 消息与显式调用（每帧可能两次）：aim/cancel/release 沿用现有「值 + consumed 锁存」模式，不在 Update 做边沿翻转。
6. UGUI 指针捕获在根节点；Icon 的 `RaycastTarget = 0` 不要打开；拖出按钮仍能收到 Drag/Up。
7. `aimRadius` 是 Canvas 单位；CanvasScaler 参考分辨率 1920×1080、Match 0.5。
8. 新脚本放对 asmdef：`MobileSkillAimJoystick`/`AimGestureState` 在 UI 程序集，`GroundAimIndicator` 在 Skills/VFX。
9. 移动端 UI 在 `DemoDay.unity`（不是 prefab）：加组件/绑 Icon 是场景改动，补 PlayMode 断言防丢。
10. 池对象 `OnSpawn` 重置全部新增状态；`MaterialPropertyBlock`/事件订阅在销毁时清理。

## 边界与清理

| 场景 | 行为 |
|---|---|
| 失焦 / UI 停用 / 绑定释放 / 断线 / 切角色 | 取消，不发射、不冷却、不上报 |
| 死亡 / 受击打断 / 精力耗尽 | 技能取消；摇杆回中、取消态清空、地面圈隐藏 |
| 冷却 / 精力不足 | 不进入摇杆态；预输入缓冲沿用现状（0.5s） |
| 目标死亡 / 清锁 / 出 AOI | 回退前方最远 |
| 按住期间目标移出 15m | 锁定保持到 20m，落点沿方向钳到 15m（客户端 14.95m 留余量） |
| 地面 raycast 失败 | 回退角色/目标 Y |
| 上坡 / 下坡落点 | 下降段 + 最小飞行时间后才判落地 |
| 目标距离 < 0.1m | 不生成投射物 |
| 多指 | 火球按钮只认自己的 pointerId；观察区另一指可同时转相机 |
| 远程视图不存在 | provider 返回 false，回退前方最远 |

teardown 机制：

- `MobileSkillAimJoystick.OnDisable` / `ResetGesture()` 把释放类型记为 `Cancel`。
- `MobileInputBinder` 失焦 / 停用 / `ReleaseBinding`：先 `_boundEntry.CancelAimingSkill()`，再清 UI 与 InputManager；顺序不能反。

## 测试矩阵

命名沿用 `test_[system]_[scenario]_[expected_result]`，Arrange/Act/Assert。

### EditMode

- `ProjectileSkillDataTests`：`HoldToAim` 默认 false；火球资产开关与最小距离。
- `SkillStateMachineTests`：`HoldToAim` 满蓄不自动释放；`PlayerCancel` 在 `Casting`/`Charging` 生效、`Execution` 拒绝。
- `SkillCoordinatorChargeTests`：`HoldToAim` 释放恰好一条带落点的 `CastRequest`；取消零上报零冷却；未设落点时兜底为前方最远。
- `SkillCoordinatorStaminaTests`：`HoldToAim` 精力耗尽 → `Cancelled` 无释放；`ChargedSkillData` 仍保持自动释放（回归）。
- `FireballProjectileTests`：变距落点、不撞怪提前爆、上坡不原地爆、下坡正常落地、近距保护。
- `AimGestureStateTests`：按下点原点、半径钳制、死区、取消回滞、释放类型、复位。
- `MobileInputManagerUiHooksTests`：aim/cancel/release 三组状态透传与清理。
- `SkillCastReporterTests`：`HoldToAim` 带落点；非瞄准投射物不传落点（回归）。
- `NetworkTargetSelectorTests`：`TryGetLockedPosition` 有锁返回视图位置、无锁/死亡返回 false。
- `FireballAssetTableTests`：火球资产 `ProjectileRange`/`ProjectileSpeed`/`ExplosionRadius` 与服务端 20003 表一致（仿 `MonsterConfigNumbersTests`）。

### Server

- `CombatResolverProjectileTests`：20003 落点圆命中、超 15m 拒收、零形状通过、飞行延迟 tick、蓄力进度不缩放。
- `CombatRulesTests`：`ProjectileTravelTicks` 对非有限/负值/零距离的钳制。

### Sync

- `RemoteCastPresenterTests`：远程火球飞向 `broadcast.Impact`；`DamageResult` 到达在 Impact 爆炸，不用 `Hits[0]`。

### PlayMode

- `MobileTouchInputTests`：拖动改变落点并发射；拉远松手取消且不进冷却；失焦取消不发射；精力耗尽取消并复位 UI；无目标点按打前方最远；有锁定目标点按打目标。
- `MobileSkillHudTests`：瞄准期间隐藏蓄力环；取消后图标回中；火球按钮存在 `MobileSkillAimJoystick` 且 Icon 引用非空。

### 手工验收

- WebGL 真机：按住拖动、点按、拉远取消、切后台再回来均不留锁定状态。
- 联机：客户端落点与服务端受击位置一致（半径 1.5m 内）。

## 文档同步

改动模块行为后同步更新模块 README：

- `Assets/Scripts/Hotfix/GameSystems/Skills/README.md`：瞄准/取消/落点上报、蓄力环隐藏。
- `Assets/Scripts/Hotfix/GameSystems/UI/README.md`：`MobileSkillAimJoystick`、地面圈、动效复位。
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`：瞄准输入、转向与移动锁。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`：目标锁定点按语义、远程火球 Impact。
- `Server/README.md`：20003 判定从线形改落点圆、飞行延迟。

## 已知取舍

- 20003 从 `LineNearestSplash` 改为 `CircleAtPoint`：不再有飞行途中命中，地面圈即真实爆炸中心；本地与服务端同源。
- `EnsureTargetFor` 按角色当前朝向筛选（±90°、15m）；敌人在背后时施法瞬间不会自动锁定，玩家可手动切换或拖动瞄准。
- 点按目标超过 15m 时会被 `EnsureTargetFor` 清锁并回退前方最远；「钳到 15m」只覆盖按住期间目标移出与大体型（表面距离 ≤15m、中心 >15m）两类。
