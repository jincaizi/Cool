# 3C 系统现有功能总结

> 用于重构参考，记录当前已实现的功能、数据结构和模块职责。

---

## 1. FSM 状态机

### 1.1 Animator 参数

| 参数名 | 类型 | 用途 |
|--------|------|------|
| State | Int | 驱动 Base Layer 状态转换 |
| AttackPhase | Int | 驱动 Attack Layer 连击 |
| Jump | Trigger | (已弃用，保留) |
| Attack | Trigger | (已弃用，保留) |

### 1.2 Base Layer 状态

| State 值 | 状态名 | 动画 | fileID |
|-----------|--------|------|--------|
| 0 | Idle | guid:423aaf... | -1001 |
| 1 | BattleIdle | guid:0308cf... | -1002 |
| 2 | Move | guid:7d4f9e... | -1003 |
| 3 | Run | guid:5eee3d... | -1004 |
| 4 | JumpStart | guid:c2b2e4... | -1005 |
| 5 | JumpAir | guid:8be8f9... | -1006 |
| 6 | JumpEnd | guid:8b662f... | -1007 |
| 7 | Death | guid:5940bb... | -1008 |

Base Layer 的 Default State = Idle。

### 1.3 Base Layer 转换关系

```
                    ┌──────────────────────────────────────────┐
                    │            AnyState → Death              │
                    │            条件: State == 7               │
                    └──────────────────────────────────────────┘
                                     │ (任意状态可被打断)

  ┌────────┐   State==2   ┌────────┐   State==3   ┌────────┐
  │  Idle  │ ───────────→ │  Move  │ ───────────→ │  Run   │
  │  (0)   │ ←─────────── │  (2)   │ ←─────────── │  (3)   │
  └────────┘   State==0   └────────┘   State==2   └────────┘
       │                     │                     │
       │ State==3            │ State==4            │ State==4
       ↓                     ↓                     ↓
  ┌────────┐            ┌───────────┐         ┌───────────┐
  │  Run   │            │ JumpStart │         │ JumpStart │
  └────────┘            │   (4)     │         │   (4)     │
                        └───────────┘         └───────────┘
                             │
                   [代码: Start→Air 同帧]
                             ↓
                        ┌───────────┐
                        │  JumpAir  │
                        │   (5)     │
                        └───────────┘
                             │
                   [代码: 着地检测]
                             ↓
                        ┌───────────┐
                        │  JumpEnd  │ ──→ [SMB: normalizedTime≥0.9]
                        │   (6)     │         → FinishJump() → Idle
                        └───────────┘
```

#### 转换条件明细

| 起始状态 | 目标状态 | 条件 | fileID |
|----------|----------|------|--------|
| Idle | Move | State == 2 | -3019 |
| Idle | Run | State == 3 | -3020 |
| Move | Idle | State == 0 | -3006 |
| Move | Run | State == 3 | -3007 |
| Move | JumpStart | State == 4 | -3011 |
| Move | Death | State == 7 | -3012 |
| Run | Idle | State == 0 | -3008 |
| Run | Move | State == 2 | -3021 |
| Run | JumpStart | State == 4 | -3022 |
| BattleIdle | Idle | State == 0 | -3013 |
| BattleIdle | Move | State == 2 | -3015 |
| BattleIdle | Run | State == 3 | -3016 |
| JumpStart | JumpAir | State == 5 | -3004 |
| JumpStart | Death | State == 7 | -3005 |
| JumpAir | JumpEnd | State == 6 | -3002 |
| JumpAir | Death | State == 7 | -3003 |
| JumpEnd | Idle | ExitTime=0.9, 无条件 | -3001 |
| AnyState | Death | State == 7 | -4001 |

#### 已知问题
- JumpStart→JumpAir (-3004) 和 JumpEnd→Idle (-3001) 被 Unity 忽略（"doesn't have an Exit Time or any condition"）
- 当前代码用 `Animator.Play()` 绕过这些转换

### 1.4 Attack Layer 状态

| AttackPhase 值 | 状态名 | 动画 | fileID |
|-----------------|--------|------|--------|
| - | Empty | BattleIdle (guid:0308cf...) | -101 |
| 1 | Attack1 | guid:db509a... | -102 |
| 2 | Attack2 | guid:8283fa... | -103 |
| 3 | Attack3 | guid:9a6c35... | -104 |
| 4 | Attack4 | guid:b267a2... | -105 |

- Avatar Mask: AnimLayer.mask（仅上半身，腿部/脚部禁用）
- BlendingMode: 0 (Override)
- DefaultWeight: 1
- Default State: Empty

### 1.5 Attack Layer 转换关系

```
  AnyState ──→ Attack1  条件: Attack(Trigger) && AttackPhase==1
  AnyState ──→ Attack2  条件: Attack(Trigger) && AttackPhase==2
  AnyState ──→ Attack3  条件: Attack(Trigger) && AttackPhase==3
  AnyState ──→ Attack4  条件: Attack(Trigger) && AttackPhase==4

  Attack1/2/3/4 ──→ Empty  条件: ExitTime=0.9, 无条件
```

#### 已知问题
- AnyState 转换依赖 Attack Trigger，但代码已改用 `Animator.Play()` 直接进入
- 当前实际流程: `Animator.Play("AttackX", 1, 0f)` 跳过 AnyState 转换

### 1.6 StateMachineBehaviour

| 类 | 挂载位置 | 触发条件 | 事件 |
|----|----------|----------|------|
| CharacterStateBehaviour | JumpEnd | normalizedTime ≥ 0.9 | OnJumpEndCompletedEvent → FinishJump() |
| AttackStateBehaviour | Attack1/2/3/4 | normalizedTime ≥ 0.8 | OnAttackCompletedEvent(int) → TryComboNext() |

---

## 2. 技能/攻击系统

### 2.1 输入映射

| 按键 | 方法 | 行为 |
|------|------|------|
| 鼠标左键 | IsAttackPressed() | 普通攻击 (Attack1/Attack2 交替) |
| Q | IsSkill2Pressed() | 技能2 (Attack3)，需 JumpPhase==Air 或 None |
| R | IsSkill3Pressed() | 技能3 (Attack4)，需 JumpPhase==None |
| Space | IsJumpPressed() | 跳跃 |
| Shift(按住) | IsSprintHeld() | 冲刺 |

### 2.2 攻击流程

```
鼠标左键 → OnNormalAttack()
  ├── _lastNormalAttackIndex = 1↔2 交替
  ├── SetInteger(AttackPhase, index)
  └── Play("Attack1"/"Attack2", layer=1, 0f)

Q键 → OnSkill2()
  ├── SetInteger(AttackPhase, 3)
  └── Play("Attack3", layer=1, 0f)

R键 → OnSkill3()
  ├── SetInteger(AttackPhase, 4)
  └── Play("Attack4", layer=1, 0f)

AttackStateBehaviour (normalizedTime≥0.8) → OnAttackCompletedEvent(index)
  └── Sys3CEntry → TryComboNext() → OnNormalAttack() (仅普通攻击连击)
```

### 2.3 技能限制条件

- Skill2 (Q): JumpPhase 必须为 Air 或 None
- Skill3 (R): JumpPhase 必须为 None
- 普通攻击: 无限制

---

## 3. 相机系统

### 3.1 ThirdPersonCameraController

纯 C# 类（非 MonoBehaviour），由 Sys3CEntry 在 Update() 中调用。

#### 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| Distance | 5.0f (Entry设为5) | 相机与角色距离 |
| Height | 2.0f | 相机高度偏移 |
| PositionDamping | 5.0f | 位置跟随平滑系数 |
| RotationDamping | 8.0f | 旋转跟随平滑系数 |
| MinPitch | -30f | 最小俯仰角 |
| MaxPitch | 60f | 最大俯仰角 |
| MouseSensitivityX | 2.0f | 水平旋转灵敏度 |
| MouseSensitivityY | 2.0f | 垂直旋转灵敏度 |

#### 工作原理

1. **输入**: 鼠标移动 → `HandleRotationInput(Vector2)` → 更新 `_horizontalAngle` / `_verticalAngle`
2. **位置计算**: 球坐标 → `Quaternion.Euler(pitch, yaw, 0) * (0, 0, -Distance)` + Height 偏移
3. **平滑跟随**: `Vector3.Lerp(当前位置, 目标位置, damping * deltaTime)`
4. **看向目标**: `Quaternion.Slerp(当前旋转, LookRotation(目标-相机), damping * deltaTime)`
5. **LookAt 偏移**: 角色位置 + `Vector3.up * Height * 0.5`

---

## 4. 角色控制器

### 4.1 CharacterController（物理/移动）

纯 C# 类，驱动 CharacterData 更新。

#### 参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| MoveSpeed | 5.0f | 移动速度 |
| SprintSpeed | 8.0f | 冲刺速度 |
| RotationSpeed | 10.0f | 旋转平滑系数 |
| Gravity | -30f | 重力加速度 |
| JumpForce | 12f | 跳跃初速度 |

#### Update() 每帧流程

```
1. 应用水平移动 (MoveCommand.MoveDir × Speed)
2. 检测地面 (GroundDetector → CharacterController.isGrounded)
3. 走下悬崖检测 (wasGrounded && !isGrounded → velocity.y = 0)
4. JumpPhase.Start → Air (同帧转换)
5. 空中物理 (velocity.y += Gravity * dt)
6. 着地检测 (isGrounded && velocity.y ≤ 0 → JumpPhase.End, _stateLocked=true)
6.5 跳跃安全超时 (_stateLocked > 3秒 → 强制 FinishJump())
7. 非锁定状态 → 更新移动/静止状态
8. 同步数据到 CharacterData
```

#### 关键状态控制

| 方法 | 作用 |
|------|------|
| RequestJump() | 跳跃: JumpPhase→Start, velocity.y=JumpForce |
| FinishJump() | 跳跃结束: JumpPhase→None, _stateLocked=false |
| AbortJump() | 中断跳跃: 重置为 Idle |
| ApplyDeath() | 死亡: State→Death, 清除跳跃状态 |

### 4.2 GroundDetector

- 直接使用 `CharacterController.isGrounded`
- 依赖 Unity 的 `Move()` 调用后更新

### 4.3 CharacterData（数据结构）

```csharp
struct CharacterData {
    Vector3 Position;
    Quaternion Rotation;
    Vector3 Velocity;
    CharacterState State;        // enum: Idle=0 ~ Death=7
    bool IsGrounded;
    float VerticalVelocity;
    JumpPhase JumpPhase;         // enum: None=0, Start=1, Air=2, End=3
    bool ComboWindowActive;
    bool IsSprint;
}
```

### 4.4 CharacterAnimationDriver（动画驱动）

纯 C# 类，响应式读取 CharacterData，驱动 Animator。

#### 驱动策略

| 场景 | 驱动方式 |
|------|----------|
| 跳跃阶段变化 | `Animator.Play("JumpX", 0, 0f)` 直接进入 |
| 普通状态变化 | `SetInteger(State, value)` 通过转换 |
| 攻击/技能 | `Animator.Play("AttackX", 1, 0f)` 直接进入 |
| ForceSync 安全网 | 每帧检查 Animator 状态，不匹配时用 Play 强制修正 |

#### Update() 流程

```
1. 死亡检测 (State==Death 且非上帧) → 最高优先级
2. JumpPhase 变化 → OnJumpPhaseChanged() → Animator.Play()
3. 非跳跃 + State 变化 → OnStateChanged() → SetInteger()
4. ForceSync() — 每帧强制同步 Animator 参数
```

---

## 5. 输入系统

### 5.1 架构

```
IInputAdapter (接口)
  ├── KeyboardInputAdapter  (WASD + 鼠标)
  └── JoystickInputAdapter  (手柄，文件存在但未使用)

InputManager
  ├── 持有 IInputAdapter 实例
  ├── GetMoveCommand(cameraForward) → MoveCommand
  ├── GetCameraRotationInput() → Vector2
  └── 单帧事件: IsJumpPressed / IsAttackPressed / IsSkill2Pressed / IsSkill3Pressed
```

### 5.2 MoveCommand

```csharp
struct MoveCommand {
    Vector3 MoveDir;      // 世界空间方向（已转换）
    float Speed;
    Quaternion Rotation;  // 目标朝向
    long Timestamp;       // 网络预测用
    uint Sequence;        // 序列号
    bool IsSprint;
}
```

### 5.3 输入转换

```
WASD → GetAxisRaw(Horizontal/Vertical) → 标准化 Vector3
     → ConvertToWorldDirection(input, cameraForward)
     → 以相机水平朝向为基准旋转输入方向
     → 输出世界空间 MoveDir + 目标 Rotation
```

---

## 6. 模块关系图

```
Sys3CEntry (MonoBehaviour, 绑定在角色 GameObject)
  │
  ├── InputManager ──→ MoveCommand + 事件检测
  │     └── KeyboardInputAdapter
  │
  ├── CharacterController ──→ CharacterData (物理/状态)
  │     └── GroundDetector
  │
  ├── CharacterAnimationDriver ──→ Animator (读 Data, 驱动动画)
  │
  ├── ThirdPersonCameraController ──→ Camera Transform
  │
  └── NetworkBridge / NetworkPrediction / PositionInterpolator (预留)
```

### 数据流

```
每帧:
  InputManager.Update()
    ↓
  相机旋转输入 → CameraController.HandleRotationInput()
    ↓
  MoveCommand → CharacterController.Update(command) → CharacterData 更新
    ↓
  CharacterData → CharacterAnimationDriver.Update(data) → Animator 更新
    ↓
  CameraController.Update() → 相机位置/旋转更新
```

### 事件流

```
CharacterStateBehaviour.OnJumpEndCompletedEvent
  → Sys3CEntry.OnJumpEndCompletedHandler()
  → CharacterController.FinishJump()

AttackStateBehaviour.OnAttackCompletedEvent
  → Sys3CEntry.OnAttackCompletedHandler()
  → CharacterAnimationDriver.TryComboNext()

CharacterController.OnLanded
  → Sys3CEntry.OnCharacterLanded() (预留特效/音效)
```

---

## 7. 文件清单

| 文件 | 类型 | 职责 |
|------|------|------|
| Sys3CEntry.cs | MonoBehaviour | 入口，绑定所有模块 |
| CharacterController.cs | 纯 C# | 物理/移动/状态控制 |
| CharacterAnimationDriver.cs | 纯 C# | 响应式动画驱动 |
| CharacterData.cs | 数据定义 | CharacterState/JumpPhase/CharacterData/MoveCommand |
| CharacterStateBehaviour.cs | StateMachineBehaviour | JumpEnd 完成检测 |
| AttackStateBehaviour.cs | StateMachineBehaviour | 攻击完成检测 |
| GroundDetector.cs | 纯 C# | 地面检测 |
| InputManager.cs | 纯 C# | 输入管理/命令转换 |
| KeyboardInputAdapter.cs | 纯 C# | 键盘输入适配器 |
| ThirdPersonCameraController.cs | 纯 C# | 第三人称相机 |
| Character3C.controller | Animator Controller | FSM 定义 |
| AnimLayer.mask | Avatar Mask | 攻击层上半身遮罩 |
