# 3C System Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix floating/airborne character issue and ensure consistent jump height by reordering ground detection after Move(), adding cliff-walk detection, and tuning ground detection parameters.

**Architecture:** Adjust Update() method execution order so ground detection runs after CharacterController.Move(). Add walk-off-cliff detection that resets vertical velocity. Tune GroundDetector sphere cast parameters for better edge detection.

**Tech Stack:** Unity 2022.3.25f1, CharacterController, Physics.SphereCast

---

## Task 1: Modify GroundDetector.cs — Tune Detection Parameters

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs:29-37`

- [ ] **Step 1: Read current GroundDetector.cs implementation**

Verify current values at lines 29-37:
- `capsuleRadius = _controller.radius * 0.8f`
- `checkDistance = capsuleRadius + 0.3f`

- [ ] **Step 2: Update detection parameters**

Replace lines 29-30:
```csharp
float capsuleRadius = _controller.radius * 0.9f;  // 90% 半径（原 80%）
float checkDistance = capsuleRadius + 0.5f;        // 检测距离（原 capsuleRadius + 0.3f）
```

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs
git commit -m "fix(3c): increase ground detection radius and distance"
```

---

## Task 2: Modify CharacterController.cs — Reorder Update Logic

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs:85-171`

- [ ] **Step 1: Read current Update() method**

Review lines 85-171 to confirm current structure before editing.

- [ ] **Step 2: Replace Update() method body**

Replace the entire `Update(MoveCommand command)` method body (lines 85-171) with:

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

    // 6. 地面/空中状态处理
    if (_data.IsGrounded && !isInJump)
    {
        // 地面移动
        if (command.MoveDir.sqrMagnitude > 0.01f)
        {
            _stateLocked = false;
            Quaternion targetRot = command.Rotation;
            _transform.rotation = Quaternion.Slerp(
                _transform.rotation,
                targetRot,
                RotationSpeed * Time.deltaTime
            );
            _data.State = command.IsSprint ? CharacterState.Run : CharacterState.Move;
        }
        else
        {
            _data.State = CharacterState.Idle;
        }
    }
    else
    {
        // 空中状态
        _data.State = CharacterState.JumpAir;
        if (_data.JumpPhase == JumpPhase.Start)
            _data.JumpPhase = JumpPhase.Air;

        // 着地检测
        if (_data.IsGrounded && _data.JumpPhase == JumpPhase.Air && _velocity.y <= 0)
        {
            UnityEngine.Debug.Log($"[Landing] detected! JumpPhase=Air->End, velocity.y={_velocity.y:F3}");
            _data.JumpPhase = JumpPhase.End;
            _data.State = CharacterState.JumpEnd;
            _stateLocked = true;
            OnLanded?.Invoke();
        }
    }

    // 更新数据
    _data.Velocity = _controller.velocity;
    _data.VerticalVelocity = _velocity.y;
}
```

- [ ] **Step 3: Commit**
```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs
git commit -m "fix(3c): reorder ground detection after Move() and add cliff-walk detection"
```

---

## Task 3: Verify Changes

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs`

- [ ] **Step 1: Review final code**

Verify the key changes are in place:
1. `GroundDetector.cs`: `capsuleRadius = _controller.radius * 0.9f`, `checkDistance = capsuleRadius + 0.5f`
2. `CharacterController.cs`: `_data.IsGrounded = _groundDetector.IsGrounded()` appears AFTER `_controller.Move()`
3. `CharacterController.cs`: Walk-off-cliff detection `if (wasGrounded && !_data.IsGrounded && ...)` is present

- [ ] **Step 2: Commit**
```bash
git add -A
git commit -m "fix(3c): complete 3C refactor - ground detection timing and cliff walk"
```

---

## Self-Review Checklist

- [ ] Spec coverage: Floating issue fixed by Task 2 (reorder + cliff detection), Jump height consistent via correct physics timing
- [ ] No placeholders: All steps show exact code changes
- [ ] Type consistency: `JumpPhase.None`, `CharacterState`, `MoveCommand` all match existing code
- [ ] File paths verified: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/GroundDetector.cs`, `CharacterController.cs`
