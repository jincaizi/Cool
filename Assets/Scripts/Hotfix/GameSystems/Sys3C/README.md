# Sys3C —— 角色、控制与相机

本地角色入口为 `Sys3CEntry.cs`，负责组件装配，以及输入、技能、战斗、表现之间的接线。

## 状态架构

状态层有 3 个，但**只有两个 FSM 类**：

| 层 | 实现 | 状态 |
|---|---|---|
| Base | `FSM/BaseFSM.cs` | Idle / Move / Sprint / JumpStart / JumpAir / JumpEnd / Death |
| Attack | 无 `AttackFSM` | `FSM/AttackState.cs` 只有 `Idle` / `Attacking`，仅作 Animator 参数；由 `Core/StateCoordinator.cs` + `Skills/Runtime/SkillCoordinator.cs` 驱动 |
| Hit | `FSM/HitFSM.cs` | None / Hit / Knockback / Launched / Dizzy / Down / GetUp / Death / DefendHit |

`FSM/FSMManager.cs` 只协调 BaseFSM 与 HitFSM，攻击层不在其中。普攻连击、霸体、技能超时与突进都在技能系统内实现。

Animator 有四层：Base 0、Attack 1、Hit 2、Defend 3。动画完成回调由 `Animation/StateBehaviours/` 下的
`StateMachineBehaviour` 触发；参数哈希集中在 `Animation/AnimHashes.cs`。动画完成回调与受击去重标志按
**Animator 实例**注册在 `AnimationCallbackRegistry`，远程实体不注册，`Sys3CEntry` 销毁时注销。

### Hit 表现覆盖

Hit 层只有 `Hit` / `DefendHit` / `Dizzy` 三态可达。`Knockback` / `Launched` 复用 `Hit` 剪辑；
`Death` / `Down` / `GetUp` 没有入边转移。倒地、死亡、浮空的表现层仍是 backlog。

## 关键文件

| 文件 | 作用 |
|---|---|
| `Sys3CEntry.cs` | 本地角色装配与模块接线 |
| `FSM/FSMManager.cs` | 协调 BaseFSM / HitFSM，初始化 Animator 层权重 |
| `Core/StateCoordinator.cs` | 层间切换、锁定与能力判定 |
| `Character/CharacterController.cs` | 移动、重力、跳跃、伤害处理 |
| `Character/GroundDetector.cs` | 地面检测 |
| `Character/PlayerSpawner.cs` | 出生与重建；出生点 `(-15, 0.3, -30)` |
| `Character/CharacterAdapters.cs` | `CharacterStatsAdapter` / `ShieldSystemAdapter` / `PhysicsSystemAdapter` / `StatusControllerAdapter` |
| `Input/InputManager.cs` | 统一键鼠、摇杆、触屏输入，输出 `MoveCommand` |
| `Camera/ThirdPersonCameraController.cs` | 第三人称相机与防穿墙 |
| `Core/Combat/` | 攻击形状、伤害接口、Hitbox 数据（见 [Combat README](../Combat/README.md)） |
| `Network/MovementPolicy.cs`、`Network/NetMovementPolicy.cs` | 本地 / 联网移动策略 |
| `Skill/SkillDashComponent.cs` | 技能突进 |

## 复活传送

联网复活按服务端下发的坐标传送。单机和旧版服务端通知使用场景出生点。
传送同步刷新角色移动数据，确保复活同一帧的位置上报读到新坐标。

**复活基础层复位（B2，2026-11-10）**：`FSMManager.RequestResurrect` 在解锁后调用
`CharacterController.ResetAirborneState`（清跳跃请求/延迟离地标记/空中状态）并
`BaseFSM.ResetToLocomotion`（`Animator.Play` 实际切到 Locomotion）。死亡锁只写 `BaseState`
参数，而 JumpAir 的出口只认参数 5——只改参数不会退出跳跃节点。单机 `TickRespawn` 与联网
`ApplyServerRespawn` 共用这条路径。

**接地检测与贴怪起跳（B5，2026-11-10）**：`GroundDetector` 优先用 `CharacterController.isGrounded`；
为 false 时用只含 `GroundLayer` 的脚下球体补接地证据（覆盖 skinWidth 间隙），并在胶囊与
NpcBody（层 10）贴边时提供跳跃支撑（0.25m 容差）——贴边/交叠不再被接地门控拒绝，普通空中
二段跳不受影响。`CharacterController` 暴露 `JumpRequests` / `JumpRequestsRejectedUngrounded` /
`JumpsConsumed` / `LastMoveFlags` 供真机采样。

## 测试

- Editor：`Assets/Scripts/Tests/Editor/Sys3C/`、`FSMManagerHitFeedbackTests`、`JumpTransitionTableTests`、
  `HitAnimatorContractTests`、`CharacterSelectionServiceTests`、`MobileInputAdapterTests`、
  `InputBlockGateTests`、`InputManagerAttackSourceTests`。
- PlayMode：`PlayerSpawnerFeedbackWiringTests`、`PlayerDeathRespawnTests`、`MovementSyncTests`、`HitJudgmentAlignmentTests`。

```bash
bash tools/unity-tests.sh EditMode FSMManagerHitFeedbackTests
bash tools/unity-tests.sh PlayMode MovementSyncTests
```

### 攻击来源与输入阻断

- **普攻只认触控 HUD 的普攻按钮（所有平台一致）**：`InputManager.Update` 只消费
  `MobileActionButtons.AttackHeld → MobileInputBinder → SetAttackHeld` 的通路。
  鼠标左键不再读取，场景与 UI 上的点击都不产生普攻；按住/松开的轻击、重击语义保持不变。
- `InputBlockGate` 合并竖屏遮罩（`Landscape`）与失焦（`FocusLost`）两个原因，静态状态跨角色重建
  与场景切换。阻断生效时立即 `SuspendForBlock`：清 pressed/held/release/瞄准锁存，不合成释放；
  解除后移动、视角、防御、冲刺、火球/炎爆按住都要求先释放再接受新按下。普攻按钮状态在阻断时
  由 Binder 清除，解除后不会恢复旧按住。
- `MobileLandscapeGate` 是 WebGL 下的常驻 `DontDestroyOnLoad` 组件，逐帧轮询页面
  `window.CoolLandscape`。页面状态未就绪时保持阻断（fail-closed）；使用
  `Assets/WebGLTemplates/MobileLandscape` 模板出包，`WebGLBuildScript` 会检查模板与产物。
- `MobileInputBinder` 在阻断期间释放绑定、隐藏 `MobileUiRoot`，并跳过 `LateUpdate` 的状态回写；
  解除后重新发现当前本地角色再绑定，旧按住不会漏给新角色。

## 相机视角输入

- **PC / Editor：只有按住鼠标右键拖拽才旋转视角**（`KeyboardInputAdapter.GateLookToRightDrag`）；
  单纯移动鼠标不转视角。右键仅用于拖拽视角，不再举盾；**PC/Editor 防御键为 F**。
- `TouchLookArea` 只接收真实触摸（`pointerId >= 0` 且左键；鼠标事件完全忽略）；按下后的起始死区
  按 160dpi 基准 12px × `Mathf.Max(1, Screen.dpi/160)`（`Screen.dpi` 无效时按 160 处理）。
- 俯仰限位：桌面 `[-30, 60]`；视角采样来自触屏（`InputManager.IsLookFromTouch`）时切换为
  `[-10, 25]`（`ThirdPersonCameraController.UseMobilePitchLimits`）。
- **触摸活跃时鼠标视角兜底为零**（2026-11-11）：`Input.touchCount > 0` 时
  `KeyboardInputAdapter.GetCameraRotationInput` 返回零向量。手机浏览器会把触摸合成为鼠标事件；
  摇杆按住时观察区 `Delta` 为零，若无此门控，第二根手指落下会让合成 `Mouse X`
  以 `2°/px` 灌进相机，表现为一次大角度跳转。PC/编辑器在 `touchCount == 0` 时右键拖拽不变。

## 相关模块

## 移动技能输入

- `InputManager` 合并键鼠与手机技能输入：Q/R/陨石是一次性按下沿，火球/炎爆同时提供 pressed 与 held/release 语义。
- `MobileInputBinder` 每帧末汇入按钮状态，并通过 `BoundEntry` 让 HUD 与同一个本地角色绑定；角色重建后会重新发现并绑定。
- Binder 停用、关闭触控、角色销毁重建或切换绑定时会**同时释放** `InputManager` 与 `BoundEntry`，并清空按钮 held/pressed 后再重绑，避免旧角色的普攻/蓄力状态漏给新角色。
- 失焦/关触控会无释放事件地取消攻击按住（`CancelAttackHold`），不会合成“松开 = 重击”；失焦、死亡或进入防御还会清除 `CanFireHeavy`，避免金边残留或解除状态后补放重击。
- `Sys3CEntry.CanFireHeavy` 暴露现有 `_canFireHeavy` 真值，仅用于普攻按钮剑气就绪表现；UI 不按固定长按时间推算。

### 火球地面瞄准刷新（移动端）

- `HandleInput` 每帧**先**刷新瞄准、再做释放判定：`UpdateFireballAim` 必须在
  `TryReleaseCharge` 之前，否则激活帧的默认落点会被真发射出去。
- 移动端瞄准只认 `Charging` 帧（`IsHoldToAimCharging`）：摇杆 `Aim` 经
  `SkillAimMath.CameraRelativeDirection` 转成相机相对方向，再用
  `DistanceFromMagnitude(MinAimDistance, ProjectileRange)` 映射距离，`SnapToGround` 后写入
  `SkillCoordinator.UpdateAimTarget`；地面圈随落点与 `ExplosionRadius` 更新。
- 松手帧的 `Aim` 已归零，但释放锁存可能下一帧才被消费：`ShouldKeepLastAim` 保留上一帧拖拽落点；
  点按（未拖动）才重新解析默认落点（自动锁定在激活后才可见）。真值表由 `FireballAimReleaseTests` 覆盖。
- 移动端瞄准期间角色朝向由摇杆方向驱动（`UpdateFireballAim` 内 Slerp）；非移动端蓄力仍走原
  相机朝向跟随分支（`!IsMobileAimActive()` 且 `CanRotate()`）。
- **移动锁**：`ProjectileSkillData.HoldToAim` 在 `Casting` / `Charging` 都返回 `CanMove=false`
  （`SkillCoordinator.CanMove`），`Sys3CEntry` 每帧写入 `CharacterController.LockMovement`；
  资产 `CanMoveWhileCharging=false` 与之一致。

- [Skills](../Skills/README.md) —— 攻击层与技能执行
- [Combat](../Combat/README.md) —— 命中检测
- [SyncLayer](../SyncLayer/README.md) —— 联网移动与远端实体
- [Stamina](../Stamina/README.md) —— 冲刺与格挡精力
- [Nameplate](../Nameplate/README.md) —— 飘字与精力环
