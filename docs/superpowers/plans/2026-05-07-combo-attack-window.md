# Combo Attack Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace frame-based combo unlock with time-based window (20%-85%) + input buffering in AttackFSM, fix a chaining bug where OnStateExit resets FSM mid-transition, and clean up duplicate combo tracking in AttackStateBehaviour.

**Architecture:** Add `GetAttackLayerClipLength()` to AnimationDriver for duration queries. Rewrite AttackFSM combo fields and logic to use normalizedTime phases (lockout/window/wind-down). Change `OnAnimationCompleted` to return bool so FSMManager can skip trigger-reset during chains. Remove dead `_comboUnlocked` / `_framesInState` from AttackStateBehaviour.

**Tech Stack:** Unity 2022.3 LTS, C# (.NET Standard 2.1)

**Bug found during analysis:** When Attack1 chains to Attack2, `AttackStateBehaviour.OnStateExit` fires `OnAnimationCompleted("Attack1")` → `ReturnToIdle()` unconditionally resets `_currentState` to Idle and sets `AttackState=0` on the Animator, which triggers Attack2→AttackIdle mid-transition. This is why combos break.

**Animator Controller check:** Inspected `Character3C.controller` — no `Any State → Attack1` transition exists, so no Animator-side fix is needed. The self-restart problem is entirely caused by the C# bug above.

---

### Task 1: Add GetAttackLayerClipLength() to AnimationDriver

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimationDriver.cs:230-232`

- [ ] **Step 1: Add method**

Add after line 231 (end of class, before closing braces):

```csharp
/// <summary>
/// Get the length (seconds) of the current animation clip on the Attack layer.
/// Returns 0 if no clip is playing.
/// </summary>
public float GetAttackLayerClipLength()
{
    var stateInfo = _animator.GetCurrentAnimatorStateInfo(ATTACK_LAYER_INDEX);
    return stateInfo.length;
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimationDriver.cs
git commit -m "feat: add GetAttackLayerClipLength() to AnimationDriver"
```

---

### Task 2: Rewrite AttackFSM — fields, RequestNormalAttack, Update, OnAnimationCompleted

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs`

This is the core change. Replace frame-based `_comboUnlocked` with time-based window + input buffer.

- [ ] **Step 1: Replace old combo fields with new window fields**

Replace lines 14-22 (fields `_comboCount` through `_comboUnlocked` and the SKILLQ_DASH constants):

```csharp
// Combo window timing
private float _attackAnimStartTime;
private float _attackAnimDuration;
private bool _inputBuffered;
private bool _comboWindowOpen;

private const float COMBO_LOCK_RATIO = 0.2f;
private const float COMBO_WINDOW_END_RATIO = 0.85f;

// SkillQ 突进参数
private const float SKILLQ_DASH_DISTANCE = 3f;
private const float SKILLQ_DASH_DURATION = 0.3f;
```

- [ ] **Step 2: Replace Update's idle-reset and combo-unlock logic**

Replace lines 96-113 (the `if (_currentState == AttackState.Idle)` block and its `else`):

```csharp
if (_currentState == AttackState.Idle)
{
    _attackAnimStartTime = 0f;
    _attackAnimDuration = 0f;
    _inputBuffered = false;
    _comboWindowOpen = false;
    _skillStateTimer = 0f;
    _skillRDuration = 0f;
    _isSkillRActive = false;
}
else if (_currentState == AttackState.Attack1)
{
    // Lazy-init duration on first frame after transition
    if (_attackAnimDuration <= 0f)
    {
        _attackAnimDuration = _driver.GetAttackLayerClipLength();
    }

    if (_attackAnimDuration > 0f)
    {
        float elapsed = Time.time - _attackAnimStartTime;
        float normalizedTime = elapsed / _attackAnimDuration;

        if (normalizedTime < COMBO_LOCK_RATIO)
        {
            _comboWindowOpen = false;
        }
        else if (normalizedTime < COMBO_WINDOW_END_RATIO)
        {
            _comboWindowOpen = true;
            if (_inputBuffered)
            {
                EnterAttack(AttackState.Attack2);
            }
        }
        else
        {
            _comboWindowOpen = false;
        }
    }
}
```

- [ ] **Step 3: Replace RequestNormalAttack with switch-based logic**

Replace lines 116-133:

```csharp
public void RequestNormalAttack()
{
    switch (_currentState)
    {
        case AttackState.Idle:
            EnterAttack(AttackState.Attack1);
            break;
        case AttackState.Attack1:
            if (_comboWindowOpen)
            {
                EnterAttack(AttackState.Attack2);
            }
            else if (!_comboWindowOpen && !_inputBuffered)
            {
                _inputBuffered = true;
            }
            break;
        case AttackState.Attack2:
            // End of combo, input ignored
            break;
    }
}

private void EnterAttack(AttackState state)
{
    _currentState = state;
    _attackAnimStartTime = Time.time;
    _attackAnimDuration = 0f;
    _inputBuffered = false;
    _comboWindowOpen = false;
    _driver.SetAttackState(_currentState);
    _driver.SetAttackLayerWeight(1f);
    _driver.TriggerAttack();
}
```

- [ ] **Step 4: Fix OnAnimationCompleted — guard with state check, return bool**

Replace lines 212-235:

```csharp
public bool OnAnimationCompleted(string stateName)
{
    switch (stateName)
    {
        case "Attack1":
            // Guard: only reset if still in Attack1 (not chained to Attack2)
            if (_currentState == AttackState.Attack1)
            {
                ReturnToIdle();
                OnAttackCompleted?.Invoke();
                OnSkillOrAttackEnded?.Invoke();
                return true;
            }
            return false;
        case "Attack2":
            ReturnToIdle();
            OnAttackCompleted?.Invoke();
            OnSkillOrAttackEnded?.Invoke();
            return true;
        case "AttackQ":
            ReturnToIdle();
            OnSkillCompleted?.Invoke();
            OnSkillOrAttackEnded?.Invoke();
            return true;
        case "SkillR_Start":
            OnSkillRStartCompleted();
            return true;
        case "SkillR_Loop":
            return false;
    }
    return false;
}
```

- [ ] **Step 5: Update RequestSkillQ and RequestSkillR — remove references to deleted fields**

Replace `_comboCount = 0; _framesInState = 0; _comboUnlocked = false;` in `RequestSkillQ` (line 140-143) and `RequestSkillR` (line 158-161) with:

```csharp
            _inputBuffered = false;
            _comboWindowOpen = false;
```

The exact replacement for RequestSkillQ (lines 138-148) becomes:

```csharp
public void RequestSkillQ()
{
    if (_currentState == AttackState.Idle || _currentState == AttackState.Attack1 || _currentState == AttackState.Attack2)
    {
        _currentState = AttackState.SkillQ;
        _inputBuffered = false;
        _comboWindowOpen = false;
        _driver.SetAttackState(_currentState);
        _driver.SetAttackLayerWeight(1f);
        _driver.TriggerSkillQ();
    }
}
```

And RequestSkillR (lines 150-166) becomes:

```csharp
public void RequestSkillR(bool isGrounded)
{
    if (!isGrounded) return;

    if (_currentState == AttackState.Idle || _currentState == AttackState.Attack1 || _currentState == AttackState.Attack2)
    {
        _currentState = AttackState.SkillR_Start;
        _inputBuffered = false;
        _comboWindowOpen = false;
        _isSkillRActive = true;
        _skillRDuration = 0f;
        _driver.SetAttackState(_currentState);
        _driver.SetAttackLayerWeight(1f);
        _driver.TriggerSkillR();
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/AttackFSM.cs
git commit -m "feat: replace frame-based combo with time-based window + input buffer; fix OnAnimationCompleted chain reset bug"
```

---

### Task 3: Fix FSMManager.HandleAnimationCompleted — check bool return

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs:275-304`

- [ ] **Step 1: Guard trigger reset and movement unlock on OnAnimationCompleted result**

Replace lines 282-297 (the `case "Attack1":` through the end of that case block):

```csharp
case "Attack1":
case "Attack2":
case "AttackQ":
    bool handled = _attackFSM.OnAnimationCompleted(stateName);
    if (handled)
    {
        _driver.ResetAttackTrigger();
        _driver.ResetSkillQTrigger();
        _driver.ResetSkillRTrigger();
        _characterController.LockMovement = false;
        _characterController.LockRotation = false;
    }
    break;
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs
git commit -m "fix: skip trigger reset when OnAnimationCompleted was ignored (mid-chain)"
```

---

### Task 4: Clean up AttackStateBehaviour dead code

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs`

The `_framesInState`, `_comboUnlocked`, and `COMBO_FRAME_LOCK` in AttackStateBehaviour are no longer used. The combo window is now managed entirely in AttackFSM.

- [ ] **Step 1: Remove dead fields and constants**

Replace lines 14-19 (delete `COMBO_FRAME_LOCK`, `COMBO_WINDOW_START`, `COMBO_WINDOW_END`, `_framesInState`, `_comboUnlocked`):

```csharp
private static AnimationDriver _driver;
private static Action<string> _onAnimationCompleted;
```

- [ ] **Step 2: Simplify OnStateEnter**

Replace lines 49-55:

```csharp
override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
{
}
```

- [ ] **Step 3: Simplify OnStateUpdate — remove combo tracking, keep completion check**

Replace lines 58-82 (the entire OnStateUpdate body):

```csharp
override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
{
    // 检查动画是否接近完成（normalizedTime >= 0.95），防止循环播放问题
    // SkillR_Loop是循环动画，不触发完成回调
    if (IsAttackState(stateInfo) && stateInfo.normalizedTime >= 0.95f && stateInfo.normalizedTime < 1.1f)
    {
        if (stateInfo.shortNameHash == HASH_SkillR_Loop) return;

        if (_onAnimationCompleted != null)
        {
            _onAnimationCompleted.Invoke(GetStateName(stateInfo));
        }
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/AttackStateBehaviour.cs
git commit -m "cleanup: remove dead combo tracking from AttackStateBehaviour (now in AttackFSM)"
```

---

### Task 5: Verification

- [ ] **Step 1: Refresh Unity assets**

```bash
Use MCP assets-refresh to recompile scripts
```

- [ ] **Step 2: In Unity Editor — Play Mode test**

1. Open DemoDay scene
2. Enter Play Mode
3. Test Attack1 → Attack2 chain: press attack during window (0.2–0.85 of animation), verify Attack2 plays
4. Test input buffer: press attack during lockout (first 0.2), verify Attack2 auto-triggers at window start
5. Test no-chain: let Attack1 play completely without pressing, verify it returns to Idle
6. Test Attack2 → Idle: verify Attack2 plays fully and returns to Idle, pressing during Attack2 is ignored
7. Test spam: rapidly press attack, verify Attack1 → Attack2 → Idle → Attack1 cycle works cleanly
8. Verify no console errors via MCP console-get-logs

- [ ] **Step 3: Commit verification notes**

```bash
git add production/session-logs/session-log.md
git commit -m "verify: combo window + input buffer play test passed"
```
