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

## 测试

- Editor：`Assets/Scripts/Tests/Editor/Sys3C/`、`FSMManagerHitFeedbackTests`、`JumpTransitionTableTests`、
  `HitAnimatorContractTests`、`CharacterSelectionServiceTests`、`MobileInputAdapterTests`。
- PlayMode：`PlayerSpawnerFeedbackWiringTests`、`PlayerDeathRespawnTests`、`MovementSyncTests`、`HitJudgmentAlignmentTests`。

```bash
bash tools/unity-tests.sh EditMode FSMManagerHitFeedbackTests
bash tools/unity-tests.sh PlayMode MovementSyncTests
```

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
