# 3C 系统当前设计文档

> **状态:** 已实现
> **更新时间:** 2026-05-04
> **架构:** 双 FSM 架构 (BaseFSM + AttackFSM)

---

## 1. 系统概览

### 1.1 架构演进

```
旧架构 (已废弃):
  CharacterData → Animator 参数 → Animator Controller 转换

新架构 (当前):
  CharacterData → FSMManager → BaseFSM + AttackFSM → AnimationDriver → Animator
```

### 1.2 核心模块

| 模块 | 职责 |
|------|------|
| **FSMManager** | FSM 协调者，管理 BaseFSM 和 AttackFSM |
| **BaseFSM** | 基础移动状态机 (Idle/Move/Sprint/Jump/Death) |
| **AttackFSM** | 攻击状态机 (普攻连击/技能) |
| **StateTransitionTable** | 外部化状态转换规则 |
| **CharacterController** | 物理/移动/状态控制 |
| **AnimationDriver** | Animator 参数驱动 |
| **ThirdPersonCameraController** | 第三人称相机 |
| **NetworkPrediction** | 客户端预测 (待集成) |

---

## 2. FSM 架构

### 2.1 BaseFSM — 基础状态机

管理角色移动和跳跃，驱动 Base Layer 动画。

#### 状态定义 (BaseState)

| 值 | 状态 | 描述 |
|----|------|------|
| 0 | Idle | 静止 |
| 1 | Move | 行走 |
| 2 | Sprint | 冲刺 |
| 3 | JumpStart | 跳跃起跳 |
| 4 | JumpAir | 空中 |
| 5 | JumpEnd | 跳跃落地 |
| 6 | Death | 死亡 |

#### 状态转换规则 (StateTransitionTable)

```
Idle:
  → Move     [MoveDir.sqrMagnitude > 0.01f, priority=1]
  → Sprint   [MoveDir.sqrMagnitude > 0.01f && IsSprint, priority=2]
  → JumpStart [RequestJump, priority=10]
  → Death    [IsDead, priority=100]

Move:
  → Idle     [MoveDir.sqrMagnitude < 0.01f, priority=1]
  → Sprint   [IsSprint, priority=2]
  → JumpStart [RequestJump, priority=10]
  → Death    [IsDead, priority=100]

Sprint:
  → Idle     [MoveDir.sqrMagnitude < 0.01f, priority=1]
  → Move     [!IsSprint, priority=2]
  → JumpStart [RequestJump, priority=10]
  → Death    [IsDead, priority=100]

JumpStart:
  → JumpAir  [Always, priority=0]  // 动画播放时自动转换
  → Death    [IsDead, priority=100]

JumpAir:
  → JumpEnd  [IsGrounded && Velocity.y <= 0, priority=0]  // 落地检测
  → Death    [IsDead, priority=100]

JumpEnd:
  → Idle     [Always, priority=1]
  → Move     [MoveDir.sqrMagnitude > 0.01f, priority=2]
  → Sprint   [MoveDir.sqrMagnitude > 0.01f && IsSprint, priority=3]

Death:
  (无转换)
```

#### BaseFSM 特性

- **状态锁定:** `LockState()` 可锁定状态，阻止自动转换
- **强制同步:** 锁定时强制同步到 Animator
- **事件:** `OnStateChanged` 状态变化通知

### 2.2 AttackFSM — 攻击状态机

管理普通攻击连击和技能释放，驱动 Attack Layer 动画。

#### 状态定义 (AttackState)

| 值 | 状态 | 描述 |
|----|------|------|
| 0 | Idle | 空闲 |
| 1 | Attack1 | 普通攻击第一击 |
| 2 | Attack2 | 普通攻击第二击 (连击) |
| 3 | SkillQ | 技能Q (空中可用) |
| 4 | SkillR | 技能R (仅地面) |

#### 攻击流程

```
普通攻击 (鼠标左键):
  Attack1 → (5帧后可连击) → Attack2 → Idle

技能Q (Q键):
  Idle/Attack1/Attack2 → SkillQ → Idle

技能R (R键, 仅地面):
  Idle/Attack1/Attack2 → SkillR → Idle
```

#### AttackFSM 特性

- **连击窗口:** 5帧后可输入下一击
- **技能可中断:** 普攻可被技能Q/R中断
- **事件:** `OnAttackCompleted`, `OnSkillCompleted`, `OnSkillOrAttackEnded`

### 2.3 输入映射

| 按键 | 输入方法 | 行为 |
|------|----------|------|
| 鼠标左键 | RequestNormalAttack() | 普通攻击 (Attack1↔Attack2 交替) |
| Q | RequestSkillQ() | ���能Q，可空中释放 |
| R | RequestSkillR() | 技能R，仅地面 |
| Space | RequestJump() | 跳跃 |
| Shift (按住) | IsSprint | 冲刺 |

---

## 3. 数据结构

### 3.1 CharacterData

```csharp
struct CharacterData
{
    Vector3 Position;
    Quaternion Rotation;
    Vector3 Velocity;
    Vector3 MoveDir;           // 世界空间移动方向
    bool IsGrounded;          // 是否着地
    float VerticalVelocity;   // 垂直速度
    BaseState BaseState;      // 基础状态
    AttackState AttackState;  // 攻击状态
    bool IsSprint;            // 冲刺中
    bool IsDead;              // 死亡标记
    bool RequestJump;         // 跳跃请求
}
```

### 3.2 MoveCommand

```csharp
struct MoveCommand
{
    Vector3 MoveDir;      // 世界空间方向
    float Speed;          // 当前速度
    Quaternion Rotation;  // 目标朝向
    bool IsSprint;        // 冲刺标记
}
```

---

## 4. 动画系统

### 4.1 AnimationDriver

统一管理 Animator 参数，是 FSM 与 Animator 之间的桥梁。

#### 参数定义

| 参数 | 类型 | 驱动对象 |
|------|------|----------|
| BaseState | Int | Base Layer 状态 |
| AttackState | Int | Attack Layer 状态 |
| IsJumping | Bool | 跳跃中标记 |
| IsHit | Bool | 受击标记 |
| Attack | Trigger | 触发普攻 |
| SkillQ | Trigger | 触发技能Q |
| SkillR | Trigger | 触发技能R |
| Hit | Trigger | 触发受击 |

#### 动画层

| 层索引 | 层名 | Avatar Mask | 说明 |
|--------|------|-------------|------|
| 0 | Base | - | 移动/跳跃动画 |
| 1 | Attack | AnimLayer.mask | 上半身普攻/技能 |
| 2 | Hit | - | 受击动画 |

### 4.2 StateMachineBehaviour

| 类 | 挂载 | 触发条件 | 回调 |
|----|------|----------|------|
| BaseStateBehaviour | JumpEnd | normalizedTime ≥ 0.9 | OnAnimationCompleted("JumpEnd") |
| AttackStateBehaviour | Attack1/2, SkillQ/R | normalizedTime ≥ 0.8 | OnAnimationCompleted(stateName) |
| HitStateBehaviour | Hit | - | OnHitCompleted |

### 4.3 动画播放策略

| 场景 | 方式 |
|------|------|
| 基础状态变化 | `SetInteger(BaseState, value)` → Animator Controller 转换 |
| 跳跃阶段变化 | `SetInteger(BaseState, value)` → 触发 Base Layer 转换 |
| 攻击/技能 | `SetInteger(AttackState, value)` → 触发 Attack Layer 转换 |

---

## 5. 角色控制器

### 5.1 CharacterController

纯 C# 类，处理物理和状态控制。

#### 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| MoveSpeed | 5.0f | 移动速度 |
| SprintSpeed | 8.0f | 冲刺速度 |
| RotationSpeed | 10.0f | 旋转平滑系数 |
| Gravity | -30f | 重力加速度 |
| JumpForce | 12f | 跳跃初速度 |

#### Update() 流程

```
1. 死亡检测 → 返回
2. 眩晕检测 → 只处理重力
3. 设置 RequestJump 标记
4. 应用水平移动 (MoveCommand.MoveDir × Speed)
5. 地面检测
6. 走下悬崖检测 (wasGrounded && !isGrounded → velocity.y = 0)
7. 跳跃请求处理
8. 重力应用
9. 跳跃阶段转换 (Start→Air→End)
10. 基础状态更新 (Idle/Move/Sprint)
11. 数据同步到 CharacterData
12. 护盾/状态更新
```

#### 适配器模式

CharacterController 通过适配器集成其他系统：

| 适配器 | 职责 |
|--------|------|
| CharacterStatsAdapter | 属性系统 (血量等) |
| ShieldSystemAdapter | 护盾系统 |
| PhysicsSystemAdapter | 物理系统 |
| StatusControllerAdapter | 状态系统 (眩晕等) |

#### 公共方法

| 方法 | 说明 |
|------|------|
| RequestJump() | 请求跳跃 |
| FinishJump() | 完成跳跃 |
| ApplyDeath() | 应用死亡 |
| TakeDamage(damage, type) | 应用伤害 |
| Heal(amount) | 治疗 |
| LockRotation | 技能期间锁定旋转 |

---

## 6. 相机系统

### 6.1 ThirdPersonCameraController

纯 C# 类，非 MonoBehaviour。

#### 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| Distance | 5.0f | 相机距离 |
| Height | 2.0f | 高度偏移 |
| PositionDamping | 5.0f | 位置平滑系数 |
| RotationDamping | 8.0f | 旋转平滑系数 |
| MinPitch | -30f | 最小俯仰角 |
| MaxPitch | 60f | 最大俯仰角 |
| MouseSensitivityX/Y | 2.0f | 鼠标灵敏度 |

#### 工作原理

1. 鼠标移动 → HandleRotationInput() → 更新角度
2. 球坐标计算目标位置
3. Lerp 平滑跟随
4. Slerp 平滑看向角色

---

## 7. 输入系统

### 7.1 架构

```
InputManager
  ├── KeyboardInputAdapter (WASD + 鼠标)
  └── JoystickInputAdapter (手柄，未使用)

  ├── GetMoveCommand(cameraForward) → MoveCommand
  ├── GetCameraRotationInput() → Vector2
  └── 单帧事件: IsJumpPressed / IsNormalAttackPressed / IsSkillQPressed / IsSkillRPressed
```

### 7.2 输入转换

```
WASD → GetAxisRaw → 标准化 Vector3
     → ConvertToWorldDirection(cameraForward)
     → 输出世界空间 MoveDir + Rotation
```

---

## 8. 网络同步

### 8.1 NetworkPrediction

客户端预测与服务端校验。

```csharp
class NetworkPrediction
{
    // 预测帧记录
    void RecordPredictedFrame(sequence, position, rotation)

    // 服务端校验
    bool ValidateAndCorrect(serverSeq, serverPos, serverRot,
                            out correctedPos, out correctedRot)
}
```

### 8.2 MovementPolicy

移动策略接口，支持本地和网络模式。

```csharp
interface IMovementPolicy
{
    void Update(MoveCommand command);
    void ApplyServerCorrection(position, rotation);
}

// 本地模式
class LocalMovementPolicy : IMovementPolicy

// 预测模式
class PredictionMovementPolicy : IMovementPolicy
```

### 8.3 NetworkBridge

网络桥接，待与 AOT 层 KCP 集成。

---

## 9. 模块关系图

```
Sys3CEntry
  │
  ├── InputManager ──→ MoveCommand
  │     └── KeyboardInputAdapter
  │
  ├── CharacterController ──→ CharacterData
  │     ├── GroundDetector
  │     └── 适配器 (Stats/Shield/Physics/Status)
  │
  ├── FSMManager ──→ BaseFSM + AttackFSM
  │     └── StateTransitionTable
  │
  ├── AnimationDriver ──→ Animator
  │
  ├── ThirdPersonCameraController ──→ Camera
  │
  └── MovementPolicy (Local/Prediction)
        └── NetworkBridge (待集成)
```

### 数据流

```
每帧:
  InputManager.Update()
    ↓
  MoveCommand → MovementPolicy.Update()
    ↓
  CharacterController.Update(command) → CharacterData 更新
    ↓
  FSMManager.Update() → BaseFSM + AttackFSM
    ↓
  AnimationDriver.SetXxx() → Animator 参数更新
    ↓
  CameraController.Update() → 相机跟随
```

---

## 10. 文件清单

| 文件 | 类型 | 职责 |
|------|------|------|
| **FSM** | | |
| FSMManager.cs | 协调者 | 管理 BaseFSM + AttackFSM |
| BaseFSM.cs | 状态机 | 基础移动/跳跃状态管理 |
| AttackFSM.cs | 状态机 | 攻击/技能状态管理 |
| StateTransitionTable.cs | 配置 | 外部化状态转换规则 |
| States/IState.cs | 接口 | 状态接口定义 |
| States/BaseStates.cs | 实现 | 基础状态类 (预留) |
| **Character** | | |
| CharacterController.cs | 控制器 | 物理/移动/状态控制 |
| CharacterData.cs | 数据 | 角色数据结构 |
| CharacterAdapters.cs | 适配器 | 属性/护盾/物理/状态适配 |
| GroundDetector.cs | 组件 | 地面检测 |
| **Animation** | | |
| AnimationDriver.cs | 驱动 | Animator 参数管理 |
| StateBehaviours/BaseStateBehaviour.cs | SMB | 跳跃完成检测 |
| StateBehaviours/AttackStateBehaviour.cs | SMB | 攻击完成检测 |
| StateBehaviours/HitStateBehaviour.cs | SMB | 受击完成检测 |
| HitManager.cs | 管理 | 受击管理 |
| **Input** | | |
| InputManager.cs | 管理器 | 输入管理/命令转换 |
| KeyboardInputAdapter.cs | 适配器 | 键盘输入 |
| JoystickInputAdapter.cs | 适配器 | 手柄输入 |
| **Network** | | |
| NetworkPrediction.cs | 预测 | 客户端预测 |
| NetworkBridge.cs | 桥接 | 网络桥接 (待集成) |
| PositionInterpolator.cs | 插值 | 位置插值 |
| MovementPolicy.cs | 策略 | 移动策略接口 |
| **Camera** | | |
| ThirdPersonCameraController.cs | 控制器 | 第三人称相机 |
| **Skill** | | |
| SkillConfig.cs | 配置 | 技能配置 |
| SkillDefs.cs | 定义 | 技能定义 |
| SkillRegistry.cs | 注册 | 技能注册表 |
| SkillCoordinatorBridge.cs | 桥接 | 技能协调桥接 |
| BuffData.cs | 数据 | Buff 数据 |
| **Entry** | | |
| Sys3CEntry.cs | MonoBehaviour | 入口，绑定所有模块 |

---

## 11. 与设计文档差异

### 11.1 已实现 vs 原始设计

| 设计项 | 状态 | 说明 |
|--------|------|------|
| 状态机架构 | ✅ | 从 Animator 参数改为双 FSM |
| 状态转换表 | ✅ | 外部化转换规则 |
| 攻击连击 | ✅ | AttackFSM 实现 |
| 跳跃系统 | ✅ | 完整实现 |
| 适配器模式 | ✅ | CharacterController 集成其他系统 |
| 相机跟随 | ✅ | ThirdPersonCameraController |
| 网络预测 | ⚠️ | 框架就绪，待 AOT 集成 |
| 其他玩家插值 | ⚠️ | PositionInterpolator 就绪 |

### 11.2 待集成项

- AOT 层 KCP 网络集成
- 其他玩家位置同步
- 碰撞/受击反馈 (HitManager 框架)

---

## 12. 附录：Animator Controller 参数

| 参数 | 类型 | 说明 |
|------|------|------|
| BaseState | Int | 基础状态 (0-6) |
| AttackState | Int | 攻击状态 (0-4) |
| IsJumping | Bool | 跳跃中标记 |
| IsHit | Bool | 受击中标记 |
| Attack | Trigger | 普攻触发 |
| SkillQ | Trigger | 技能Q触发 |
| SkillR | Trigger | 技能R触发 |
| Hit | Trigger | 受击触发 |