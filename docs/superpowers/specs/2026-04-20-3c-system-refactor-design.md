# 3C 系统重构设计文档

## 问题概述

1. **浮空问题**：角色站在地面时视觉上脚悬空，且物理上存在漂浮感
2. **跳跃高度不变**：跳跃物理高度不符合预期

## 根因分析

### 浮空问题

**问题A：地面检测时序错误**

当前代码在 `Move()` 之前检测地面：
```csharp
_data.IsGrounded = _groundDetector.IsGrounded();  // Move 之前
_controller.Move(moveVelocity * Time.deltaTime);  // Move 之后
```
这导致 `IsGrounded` 基于上一帧位置判断，而移动发生在判断之后。

**问题B：走下悬崖时速度未重置**

当角色从地面走向悬崖边缘，`_velocity.y = 0`（地面状态）。当 `Move()` 使角色超出边缘后，`IsGrounded = false`，但 `isInJump == false`（因为 `JumpPhase == None`），导致**不应用重力**，角色保持当前 Y 位置漂浮。

**问题C：地面检测参数偏小**

- `capsuleRadius = _controller.radius * 0.8f`（0.4）太小
- `checkDistance = capsuleRadius + 0.3f`（0.7）可能检测不到薄边缘

### 跳跃高度问题

当前物理模型：
```csharp
_velocity.y += Gravity * Time.deltaTime;  // Gravity = -30
JumpForce = 12f
```

理论跳高 = v²/(2g) = 12²/(2×30) = 2.4 单位

问题在于落地检测依赖帧率，且缺乏跳起速度的独立管理。

## 修改设计

### 1. 调整 Update 顺序（CharacterController.cs）

将地面检测从 Move() 之前移到 Move() 之后：

```csharp
public void Update(MoveCommand command)
{
    _currentCommand = command;
    _data.Position = _transform.position;
    _data.Rotation = _transform.rotation;
    _data.IsSprint = command.IsSprint;

    // 1. 先应用移动（让角色移动到新位置）
    bool wasGrounded = _data.IsGrounded;

    // 计算本帧移动向量
    Vector3 moveVelocity = command.MoveDir * command.Speed;
    moveVelocity.y = _velocity.y;
    _controller.Move(moveVelocity * Time.deltaTime);

    // 2. 移动后再检测地面（基于新位置）
    _data.IsGrounded = _groundDetector.IsGrounded();

    // 3. 检测走下悬崖
    if (wasGrounded && !_data.IsGrounded && !_jumpRequested && _data.JumpPhase == JumpPhase.None)
    {
        _velocity.y = 0f;
    }

    // 4. 处理跳跃请求
    bool isInJump = _data.JumpPhase == JumpPhase.Start || _data.JumpPhase == JumpPhase.Air;
    if (_jumpRequested && _data.IsGrounded)
    {
        _velocity.y = JumpForce;
        _jumpRequested = false;
        _data.JumpPhase = JumpPhase.Start;
        _data.State = CharacterState.JumpStart;
    }

    // 5. 应用重力
    if (isInJump || !_data.IsGrounded)
    {
        _velocity.y += Gravity * Time.deltaTime;
        _velocity.y = Mathf.Max(_velocity.y, -50f);
    }
    else if (_data.IsGrounded)
    {
        _velocity.y = 0f;
    }

    // 6. 处理地面/空中状态...
}
```

### 2. 修正地面检测参数（GroundDetector.cs）

```csharp
float capsuleRadius = _controller.radius * 0.9f;  // 90% 半径（原 80%）
float checkDistance = capsuleRadius + 0.5f;        // 增加检测距离（原 0.3f）
```

### 3. 独立垂直速度管理（可选重构）

将垂直速度逻辑独立为方法，参考 Unity-CharacterController 的结构：

```csharp
private void UpdateVerticalSpeed(float deltaTime)
{
    bool isInJump = _data.JumpPhase == JumpPhase.Start || _data.JumpPhase == JumpPhase.Air;

    if (_data.IsGrounded)
    {
        _velocity.y = -2f;  // 轻微吸附力
        if (_jumpRequested)
        {
            _velocity.y = JumpForce;
            _jumpRequested = false;
            _data.JumpPhase = JumpPhase.Start;
            _data.State = CharacterState.JumpStart;
        }
    }
    else
    {
        _velocity.y += Gravity * deltaTime;
        _velocity.y = Mathf.Max(_velocity.y, -50f);
    }
}
```

## 保留的设计决策

- **45度地面角度过滤**：保留 MAX_GROUND_ANGLE = 45
- **固定跳跃高度**：不实现 JumpAbortSpeed，JumpForce 固定
- **CharacterState 状态机**：保留现有架构
- **Rigidbody 配置**：保持 isKinematic = true，自定义重力

## 涉及文件

- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs`
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs`

## 验证方法

1. 走下悬崖：角色应立即开始下落，不应漂浮
2. 跳跃高度：用 JumpForce=12, Gravity=-30 验证跳高约 2.4 单位
