# SkillQ 突刺位移设计

## 概述

为 SkillQ（右手持剑突刺）添加前向突刺位移效果：角色在播放突刺动画时向正前方突进约 3 米。

## 参数

| 参数 | 值 |
|------|-----|
| 突进距离 | 3 米 |
| 突进时长 | 0.3 秒 |
| 碰撞响应 | 碰到障碍物立即停止位移 |

## 实现方案

### 1. 状态新增

在 `AttackState` 枚举中，SkillQ 保持单一状态，通过计时器区分阶段：
- `SkillQ_Startup` — 起手阶段（前 0.05 秒，不可移动）
- `SkillQ_Dash` — 突进阶段（0.05~0.25 秒，执行位移）
- `SkillQ_Recovery` — 收尾阶段（0.25~0.3 秒，动画尾声）

### 2. 核心组件

**`SkillDashComponent`**（新增组件）:
- 挂载在角色上，复用已有的 `CharacterController`
- `StartDash(Vector3 direction, float distance, float duration)` — 开始突进
- `StopDash()` — 立即停止
- `bool IsDashing { get; }` — 是否正在突进

### 3. 数据流

```
AttackFSM.RequestSkillQ()
  → 设置状态为 SkillQ
  → 启动 SkillDashComponent.StartDash()
    → 每帧 ApplyDashMovement()（CharacterController.Move）
    → 检测碰撞（SimpleMove 碰到障碍物 → StopDash()）
  → 动画结束时 OnAnimationCompleted()
    → FSMManager 重置 triggers，状态回 Idle
```

### 4. 碰撞检测

使用 `CharacterController.Move(direction * distance)` 移动时，如果 `movePosition` 与 `characterController.radius` 范围内检测到碰撞（通过 `Physics.SphereCast` 前方探测），立即调用 `StopDash()`。

### 5. 文件改动

| 文件 | 改动 |
|------|------|
| `SkillDashComponent.cs`（新增） | 突进位移逻辑组件 |
| `AttackFSM.cs` | SkillQ 状态计时器 + 触发突进 |
| `CharacterController.cs` | 暴露 `LockMovement` 供 SkillDashComponent 使用 |
| `FSMManager.cs` | 初始化 SkillDashComponent |

## 时序图

```
[SkillQ 按下]
    ↓
[AttackFSM.RequestSkillQ()]
    ↓
[启动突进: 3m/0.3s]
    ├─ 前0.05s: 起手（不移动）
    ├─ 0.05~0.25s: 突进（Move前向）
    └─ 0.25~0.3s: 收尾（停止移动）
    ↓
[动画完成回调]
    ↓
[状态重置为 Idle]
```
