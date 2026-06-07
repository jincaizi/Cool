# Defend Skill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add held-shield defend mechanic to player character — hold button to raise shield, slow-walk, block frontal hits with reduced damage, shield breaks after absorbing enough damage.

**Architecture:** Defend pose in BaseFSM (LockState pattern), DefendHit reaction in HitFSM (new state), shield durability in CharacterController, damage routing via DefendModifier→FSMManager. No new FSM class, no new Animator layer, no new LayerType.

**Tech Stack:** Unity 2022 LTS, C#, Hotfix layer

**Spec:** `docs/superpowers/specs/2026-06-08-defend-skill-design.md`

---

## File Structure

| File | Change | Responsibility |
|------|--------|----------------|
| `CharacterData.cs` | Add field | `IsDefending` struct field |
| `AnimHashes.cs` | Add hash | `IsDefending` animator param |
| `BaseFSM.cs` | Modify | Accept Defend LockState, handle Unlock targets |
| `StateTransitionTable.cs` | Modify | Defend→Idle/Move/Death transition rules |
| `FSMConfig.cs` | Add field | `DefendHitDuration` default 0.4s |
| `HitFSM.cs` | Modify | Add DefendHit state, EnterDefendHit method |
| `HitStateBehaviour.cs` | Modify | Add DefendHit hash mapping |
| `CharacterController.cs` | Modify | Defend enter/exit, shield durability, speed limiting |
| `DefendModifier.cs` | Modify | Remove MonsterConfig dependency, use generic config |
| `FSMManager.cs` | Modify | Damage routing for defend, EnterDefend/ExitDefend/HandleShieldBreak |
| `StateCoordinator.cs` | Modify | Add CanDefend property |
| `InputManager.cs` | Modify | Add IsDefendHeld() |
| `Sys3CEntry.cs` | Modify | Wire input + DefendModifier to damage pipeline |

---

### Task 1: Data Foundation — CharacterData + AnimHashes

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs:24-40`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimHashes.cs:5-21`

- [ ] **Step 1: Add `IsDefending` to CharacterData struct**

```csharp
// CharacterData.cs — add after HasLeftGround (line 38)
public bool IsDefending;     // 是否处于防御姿态
```

- [ ] **Step 2: Add `IsDefending` hash to AnimHashes**

```csharp
// AnimHashes.cs — add after Blend (line 16)
public static readonly int IsDefending = Animator.StringToHash("IsDefending");
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/AnimHashes.cs
git commit -m "feat: add IsDefending field and animator hash for defend skill"
```

---

### Task 2: Base Layer Defend — BaseFSM + StateTransitionTable

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs:9-19` (BaseState enum)
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/BaseFSM.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/StateTransitionTable.cs`

- [ ] **Step 1: Add `Defend` to BaseState enum**

```csharp
// CharacterData.cs — BaseState enum, add before closing brace
public enum BaseState
{
    Idle = 0,
    Move = 1,
    Sprint = 2,
    Locomotion = 7,
    JumpStart = 3,
    JumpAir = 4,
    JumpEnd = 5,
    Death = 6,
    Defend = 8   // 持盾防御姿态
}
```

- [ ] **Step 2: Add Defend transition rules to StateTransitionTable.Initialize()**

```csharp
// StateTransitionTable.cs — add after Death transition rules (before _transitions closing)
// ========== Defend 状态 ==========
_transitions[BaseState.Defend] = new List<StateTransition>
{
    new StateTransition(BaseState.Death, d => d.IsDead, 100),
    new StateTransition(BaseState.Move, d => d.MoveDir.sqrMagnitude > 0.01f, 2),
    new StateTransition(BaseState.Idle, d => true, 1)
};
```

- [ ] **Step 3: Add `Defend` case to CanEnter**

```csharp
// StateTransitionTable.cs — add to CanEnter switch, before default
case BaseState.Defend:
    return data.IsGrounded && !data.IsDead;
```

- [ ] **Step 4: Handle locked Defend state in BaseFSM.Update()**

BaseFSM.Update() already skips transition evaluation when `_lockedState.HasValue`. No code change needed — LockState(Defend) already works via the existing LockState mechanism. Verify:

```csharp
// BaseFSM.cs Update() — this existing code handles it:
// if (_lockedState.HasValue)
// {
//     if (_currentState != _lockedState.Value)
//         ForceState(_lockedState.Value);
//     return;
// }
// No change needed.
```

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterData.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/BaseFSM.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/StateTransitionTable.cs
git commit -m "feat: add Defend state to BaseFSM and StateTransitionTable"
```

---

### Task 3: Hit Layer DefendHit — FSMConfig + HitFSM + HitStateBehaviour

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/FSMConfig.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs`

- [ ] **Step 1: Add DefendHitDuration to FSMConfig**

```csharp
// FSMConfig.cs — add after GetUpDuration (line 35)
[Header("防御受击")]
[Tooltip("防御受击动画时长")]
public float DefendHitDuration = 0.4f;
```

- [ ] **Step 2: Add `DefendHit` to HitState enum**

```csharp
// HitFSM.cs — HitState enum, add before closing brace
public enum HitState
{
    None = 0,
    Hit = 1,
    Knockback = 2,
    Launched = 3,
    Dizzy = 4,
    Down = 5,
    GetUp = 6,
    Death = 7,
    DefendHit = 8   // 防御受击（举盾时正面受击）
}
```

- [ ] **Step 3: Add DefendHit to Priority property**

```csharp
// HitFSM.cs — Priority property switch, add case
public int Priority => _currentState switch
{
    HitState.Death => 100,
    HitState.Down => 90,
    HitState.Launched => 80,
    HitState.Knockback => 70,
    HitState.Dizzy => 60,
    HitState.Hit => 50,
    HitState.DefendHit => 50,   // Same priority as Hit
    HitState.GetUp => 40,
    HitState.None => 0,
    _ => 0
};
```

- [ ] **Step 4: Add EnterDefendHit method to HitFSM**

```csharp
// HitFSM.cs — add after EnterDown method (after line 252)
/// <summary>
/// 进入防御受击状态 — 举盾时正面受击触发
/// </summary>
public void EnterDefendHit(HitData hitData)
{
    // 只在 None 或 DefendHit 状态时接受（DefendHit 期间不重复触发）
    if (_currentState == HitState.Death || _currentState == HitState.Down ||
        _currentState == HitState.Launched || _currentState == HitState.Dizzy ||
        _currentState == HitState.Knockback || _currentState == HitState.GetUp)
        return;

    _hitData = hitData;
    TransitionTo(HitState.DefendHit);
}
```

- [ ] **Step 5: Add `case HitState.DefendHit` to TransitionTo()**

```csharp
// HitFSM.cs — in TransitionTo switch, add case before HitState.Dizzy
case HitState.DefendHit:
    _animator.SetInteger(AnimHashes.HitState, (int)HitState.DefendHit);
    _animator.SetLayerWeight(AnimHashes.HitLayerIndex, 1f);
    break;
```

- [ ] **Step 6: Add GetStateDuration for DefendHit**

```csharp
// HitFSM.cs — in GetStateDuration switch, add case before default
case HitState.DefendHit:
    return _config.DefendHitDuration;
```

- [ ] **Step 7: Add DefendHit to OnStateTimerEnd**

```csharp
// HitFSM.cs — in OnStateTimerEnd switch, add DefendHit case
case HitState.DefendHit:
    Recover();
    break;
```

- [ ] **Step 8: Add "DefendHit" case to OnAnimationEnd**

```csharp
// HitFSM.cs — in OnAnimationEnd switch, add before default
case "DefendHit":
    if (_currentState == HitState.DefendHit)
        Recover();
    break;
```

- [ ] **Step 9: Add DefendHit to TransitionTo None case for cleanup**

```csharp
// HitFSM.cs — in TransitionTo, HitState.None case already handles cleanup correctly.
// No additional change needed — TransitionTo(None) resets layer weight and HitState.
```

- [ ] **Step 10: Add DefendHit hash to HitStateBehaviour**

```csharp
// HitStateBehaviour.cs — add hash field
private static readonly int HASH_DefendHit = Animator.StringToHash("DefendHit");

// In OnStateEnter, add HASH_DefendHit to the check:
if (stateHash == HASH_Hit || stateHash == HASH_Knockback ||
    stateHash == HASH_Launched || stateHash == HASH_Dizzy ||
    stateHash == HASH_Down || stateHash == HASH_GetUp ||
    stateHash == HASH_Death || stateHash == HASH_DefendHit)

// In IsHitState, add DefendHit check:
private static bool IsHitState(int hash)
{
    return hash == HASH_Hit || hash == HASH_Knockback ||
           hash == HASH_Launched || hash == HASH_Dizzy ||
           hash == HASH_Down || hash == HASH_GetUp ||
           hash == HASH_DefendHit;
}

// In GetStateName, add mapping:
if (hash == HASH_DefendHit) return "DefendHit";
```

- [ ] **Step 11: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/FSMConfig.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs
git commit -m "feat: add DefendHit state to HitFSM with EnterDefendHit method"
```

---

### Task 4: Shield Durability & Defend Logic — CharacterController

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs`

- [ ] **Step 1: Add defend fields to CharacterController**

```csharp
// CharacterController.cs — add after LockMovement property (line 38)
private bool _isDefending;
public bool IsDefending => _isDefending;

// 盾耐久
private float _shieldDurability;
private const float MaxShieldDurability = 50f;
private const float DefendSpeedMultiplier = 0.4f;

public float ShieldDurabilityPercent =>
    _shieldDurability / MaxShieldDurability;

public bool IsShieldBroken => _shieldDurability <= 0f;
```

- [ ] **Step 2: Add TryEnterDefend method**

```csharp
// CharacterController.cs — add after Heal method (end of class, before closing brace)
/// <summary>
/// 尝试进入防御姿态。条件：着地、未死亡、未受击、未已在防御。
/// </summary>
public bool TryEnterDefend()
{
    if (!_data.IsGrounded || _data.IsDead || _isDefending)
        return false;

    _isDefending = true;
    _data.IsDefending = true;
    _shieldDurability = MaxShieldDurability;
    return true;
}
```

- [ ] **Step 3: Add TryExitDefend method**

```csharp
/// <summary>
/// 退出防御姿态。
/// </summary>
public void TryExitDefend()
{
    if (!_isDefending) return;

    _isDefending = false;
    _data.IsDefending = false;
}
```

- [ ] **Step 4: Add AbsorbDamage method**

```csharp
/// <summary>
/// 盾吸收伤害，返回是否盾已破。
/// </summary>
public bool AbsorbDamage(float absorbedAmount)
{
    _shieldDurability -= absorbedAmount;
    return _shieldDurability <= 0f;
}
```

- [ ] **Step 5: Add OnShieldBreak method**

```csharp
/// <summary>
/// 盾被打破，强制退出防御。
/// </summary>
public void OnShieldBreak()
{
    _isDefending = false;
    _data.IsDefending = false;
    _shieldDurability = 0f;
}
```

- [ ] **Step 6: Apply defend speed limit and jump block in Update()**

Add after the death check at the top of Update (after line 180):

```csharp
// CharacterController.cs — in Update(), after the death state check (after SyncPositionAndRotation return)
// 防御姿态：限制移动速度 + 禁用跳跃
if (_isDefending)
{
    _data.RequestJump = false;
    _jumpRequested = false;
}
```

- [ ] **Step 7: Modify ApplyHorizontalMovement to slow defend movement**

```csharp
// CharacterController.cs — in ApplyHorizontalMovement, modify speed calculation
private void ApplyHorizontalMovement(MoveCommand command)
{
    if (LockMovement) return;

    float currentSpeed = command.IsSprint ? SprintSpeed : MoveSpeed;

    // 防御中减速
    if (_isDefending)
        currentSpeed *= DefendSpeedMultiplier;

    Vector3 moveVelocity = command.MoveDir * currentSpeed;
    moveVelocity.y = _velocity.y;
    _controller.Move(moveVelocity * Time.deltaTime);
}
```

- [ ] **Step 8: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Character/CharacterController.cs
git commit -m "feat: add shield durability, defend enter/exit, speed limiting to CharacterController"
```

---

### Task 5: Generic DefendModifier

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/Modifiers/DefendModifier.cs`

The current DefendModifier takes `MonsterConfig` which is monster-specific. Make it take a standalone config with just the defend parameters.

- [ ] **Step 1: Modify DefendModifier to accept generic config**

```csharp
// DefendModifier.cs — replace entire file
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // Configuration values for defend behavior, extracted from monster/player configs.
    public struct DefendConfig
    {
        public float DamageReduction;  // 0..1, fraction of damage blocked (e.g. 0.8 = 80% reduction)
        public float DefendAngle;      // full-angle in degrees for frontal block check

        public static DefendConfig Default => new DefendConfig
        {
            DamageReduction = 0.8f,
            DefendAngle = 160f
        };
    }

    // Reduces damage from frontal attacks during defend state.
    // Priority=100: runs after invincibility checks, before shields.
    public class DefendModifier : IDamageModifier
    {
        private readonly DefendConfig _config;
        private readonly Transform _self;
        private readonly System.Func<bool> _isDefending;

        public int Priority => 100;

        public DefendModifier(DefendConfig config, Transform self, System.Func<bool> isDefending)
        {
            _config = config;
            _self = self;
            _isDefending = isDefending;
        }

        public DamageResult Modify(ref DamageContext ctx)
        {
            if (!_isDefending())
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            if ((ctx.Flags & DamageFlags.IgnoresDefense) != 0)
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            float angle = Vector3.Angle(_self.forward, -ctx.HitDirection);
            if (angle >= _config.DefendAngle * 0.5f)
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            ctx.BlockCount++;
            float reducedDmg = ctx.CurrentDamage * (1f - _config.DamageReduction);

            var result = new DamageResult
            {
                FinalDamage = reducedDmg <= 0 ? 0 : reducedDmg,
                WasReduced = true,
                WasBlocked = reducedDmg <= 0,
                ShouldKnockback = false,
                ReactLevel = HitReactLevel.None,
            };

            return result;
        }
    }
}
```

- [ ] **Step 2: Update MonsterEntity.cs to use new DefendConfig**

Check how MonsterEntity creates DefendModifier and update to use `DefendConfig`:

```csharp
// MonsterEntity.cs — find the line creating DefendModifier and update:
// OLD: new DefendModifier(config, _self, isDefending)
// NEW: new DefendModifier(DefendConfig.Default, _self, isDefending)
```

If MonsterEntity wants to read from MonsterConfig, construct DefendConfig from it:

```csharp
var defendCfg = new DefendConfig
{
    DamageReduction = config.DefendDamageReduction,
    DefendAngle = config.DefendAngle
};
new DefendModifier(defendCfg, _self, isDefending)
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/Damage/Modifiers/DefendModifier.cs Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "refactor: make DefendModifier generic with DefendConfig struct"
```

---

### Task 6: Damage Routing — FSMManager + StateCoordinator

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs`

- [ ] **Step 1: Add CanDefend to StateCoordinator**

```csharp
// StateCoordinator.cs — add property near CanAttack
public bool CanDefend => _activeLayer != LayerType.Hit && _activeLayer != LayerType.Attack;
```

- [ ] **Step 2: Add EnterDefend/ExitDefend/HandleShieldBreak to FSMManager**

```csharp
// FSMManager.cs — add after RequestResurrect method (line 111)
public void EnterDefend()
{
    _baseFSM.LockState(BaseState.Defend);
    _animator.SetBool(AnimHashes.IsDefending, true);
}

public void ExitDefend()
{
    _baseFSM.Unlock(BaseState.Idle);
    _animator.SetBool(AnimHashes.IsDefending, false);
}

public void HandleShieldBreak(HitData hitData)
{
    _baseFSM.Unlock(BaseState.Idle);
    _animator.SetBool(AnimHashes.IsDefending, false);
    _hitFSM.EnterHit(new HitData
    {
        Damage = hitData.Damage,
        HitDirection = hitData.HitDirection,
        StunDuration = 1.5f  // 盾破眩晕时长
    });
}
```

- [ ] **Step 3: Add CanDefend to StateCoordinator (and expose via FSMManager)**

```csharp
// FSMManager.cs — add property
public bool CanDefend => CanMove && _stateCoordinator.ActiveLayer != LayerType.Hit;
```

Wait — StateCoordinator already has `CanMove` which checks `_activeLayer != LayerType.Hit`. Defend should also not activate during Attack. Let me use the StateCoordinator approach:

```csharp
// StateCoordinator.cs — add property
public bool CanDefend => _activeLayer == LayerType.Base;
```

Then in FSMManager:

```csharp
// FSMManager.cs — add property
public bool CanDefend => _stateCoordinator.CanDefend;
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/StateCoordinator.cs
git commit -m "feat: add EnterDefend/ExitDefend/HandleShieldBreak to FSMManager"
```

---

### Task 7: Defend Input — InputManager

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`

- [ ] **Step 1: Add IsDefendHeld method to InputManager**

```csharp
// InputManager.cs — add after IsSprintHeld method (line 167)
/// <summary>
/// 防御键按住（持续状态）— 鼠标右键
/// </summary>
public bool IsDefendHeld()
{
    return UnityInput.GetMouseButton(1);  // Right mouse button
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs
git commit -m "feat: add IsDefendHeld() to InputManager for right-click defend"
```

---

### Task 8: Wiring — Sys3CEntry

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

- [ ] **Step 1: Add defend member fields to Sys3CEntry**

```csharp
// Sys3CEntry.cs — add after _canFireHeavy field (line 42)
private DefendModifier _defendModifier;
private DefendConfig _defendConfig = DefendConfig.Default;
```

- [ ] **Step 2: Create DefendModifier in Start()**

```csharp
// Sys3CEntry.cs — in Start(), add after PhysicsRegistry.Register line (line 47)
// Create defend modifier linked to CharacterController.IsDefending predicate
_defendModifier = new DefendModifier(_defendConfig, transform, () => _cc.IsDefending);
```

- [ ] **Step 3: Add defend input handling to HandleInput()**

```csharp
// Sys3CEntry.cs — in HandleInput(), add at top of method (after method opening brace)
// 防御处理（按住右键）
if (_inputManager.IsDefendHeld())
{
    if (FSMManager.CanDefend && _cc.TryEnterDefend())
    {
        _fsmManager.EnterDefend();
    }
}
else
{
    if (_cc.IsDefending)
    {
        _cc.TryExitDefend();
        _fsmManager.ExitDefend();
    }
}
```

Wait, FSMManager is a private field `_fsmManager` not `FSMManager`. Let me use the right name:

```csharp
// Sys3CEntry.cs — in HandleInput(), add at top
// 防御处理（按住右键举盾，松开放下）
if (_inputManager.IsDefendHeld())
{
    if (_fsmManager.CanDefend && _cc.TryEnterDefend())
    {
        _fsmManager.EnterDefend();
    }
}
else
{
    if (_cc.IsDefending)
    {
        _cc.TryExitDefend();
        _fsmManager.ExitDefend();
    }
}
```

- [ ] **Step 4: Modify HandleDamage to route through DefendModifier**

```csharp
// Sys3CEntry.cs — modify IDamageable.TakeDamage (line 297)
void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
{
    if (_currentHP <= 0) return;

    float baseDamage = data != null ? data.BaseDamage : 10f;

    // Run DefendModifier if defending
    var ctx = new DamageContext
    {
        RawData = data,
        HitDirection = hitDirection,
        CurrentDamage = baseDamage
    };

    var result = _defendModifier?.Modify(ref ctx) ?? new DamageResult
    {
        FinalDamage = baseDamage,
        ShouldKnockback = true,
        ReactLevel = HitReactLevel.Flinch
    };

    float finalDamage = result.FinalDamage;

    // Defend was bypassed (back hit or not defending) → exit defend + full damage
    if (_cc.IsDefending && result.ReactLevel != HitReactLevel.None)
    {
        _cc.TryExitDefend();
        _fsmManager.ExitDefend();
    }

    // Shield absorbed damage
    if (_cc.IsDefending && result.WasReduced)
    {
        float absorbed = baseDamage - finalDamage;
        bool broken = _cc.AbsorbDamage(absorbed);
        if (broken)
        {
            _cc.OnShieldBreak();
            _fsmManager.HandleShieldBreak(new HitData
            {
                Damage = finalDamage,
                HitDirection = hitDirection
            });
            return;
        }

        // Enter DefendHit animation
        _fsmManager.HitFSM.EnterDefendHit(new HitData
        {
            Damage = finalDamage,
            HitDirection = hitDirection
        });
    }

    _currentHP -= finalDamage;
    if (_currentHP <= 0)
    {
        _currentHP = 0;
        ApplyDeath();
    }
}
```

Wait, actually looking at the original TakeDamage more carefully:

```csharp
void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
{
    if (_currentHP <= 0) return;

    float damage = data != null ? data.BaseDamage : 10f;
    _currentHP -= damage;

    _fsmManager.HandleDamage(sourceId: -1, damage: damage, hitDirection: hitDirection,
        knockbackForce: data?.KnockbackForce ?? 0);

    if (_currentHP <= 0)
    {
        _currentHP = 0;
    }
}
```

The original calls `_fsmManager.HandleDamage()` which routes to `_stateCoordinator.HandleDamage()` which emits DamageEvent. This is separate from the HP deduction. Let me keep the structure consistent:

```csharp
// Modified TakeDamage with defend routing
void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
{
    if (_currentHP <= 0) return;

    float baseDamage = data?.BaseDamage ?? 10f;

    // 构建伤害上下文
    var ctx = new DamageContext
    {
        RawData = data,
        HitDirection = hitDirection,
        CurrentDamage = baseDamage
    };

    // 防御修正器介入
    var result = _defendModifier?.Modify(ref ctx) ?? new DamageResult
    {
        FinalDamage = baseDamage,
        ShouldKnockback = true,
        ReactLevel = HitReactLevel.Flinch
    };

    float finalDamage = result.FinalDamage;
    bool wasBlocked = result.ReactLevel == HitReactLevel.None;

    // 防御未被触发（背面受击或不在防御状态）→ 退出防御
    if (_cc.IsDefending && !wasBlocked)
    {
        _cc.TryExitDefend();
        _fsmManager.ExitDefend();
    }

    // 格挡成功 → 扣耐久 + 播 DefendHit
    if (_cc.IsDefending && wasBlocked)
    {
        float absorbed = baseDamage - finalDamage;
        bool broken = _cc.AbsorbDamage(absorbed);
        if (broken)
        {
            _cc.OnShieldBreak();
            _fsmManager.HandleShieldBreak(new HitData
            {
                Damage = finalDamage,
                HitDirection = hitDirection
            });
            return;
        }

        _fsmManager.HitFSM.EnterDefendHit(new HitData
        {
            Damage = finalDamage,
            HitDirection = hitDirection
        });
    }

    // 扣血
    _currentHP -= finalDamage;

    // 正常受击路由（非格挡时走 HitFSM）
    if (!wasBlocked)
    {
        _fsmManager.HandleDamage(sourceId: -1, damage: finalDamage, hitDirection: hitDirection,
            knockbackForce: data?.KnockbackForce ?? 0);
    }

    if (_currentHP <= 0)
    {
        _currentHP = 0;
    }
}
```

Hmm, actually `DamageContext` requires `using Hotfix.GameSystems.Monster;` and `DamageResult` / `HitReactLevel` are in Monster namespace too. Need to add the using.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "feat: wire defend input and DefendModifier into Sys3CEntry damage pipeline"
```

---

### Task 9: Animator Controller Setup (Manual in Unity Editor)

**This task requires Unity Editor.** Open the Animator Controller for the player character and:

- [ ] **Step 1: Add `IsDefending` Bool parameter** to the Animator Controller parameters list.

- [ ] **Step 2: In Base Layer, add Defend state**
  - Add a new state pointing to `Defend_SwordAndShield.fbx` motion
  - Add transition: `Any State → Defend` with condition `IsDefending == true`
  - Add transition: `Defend → Locomotion` with condition `IsDefending == false`

- [ ] **Step 3: In Hit Layer, add DefendHit state**
  - Add a new state pointing to `DefendHit_SwordAndShield.fbx` motion
  - Add transition: `Any State → DefendHit` with condition `HitState == 8`
  - Add transition: `DefendHit → Empty` with `Has Exit Time` and exit time = 0.95

- [ ] **Step 4: In Hit Layer, ensure Dizzy state** points to `Dizzy_SwordAndShield.fbx` (the existing Dizzy state may need reconfiguring).

---

## Self-Review

### 1. Spec coverage check
- [x] Defend state in BaseFSM → Task 2
- [x] DefendHit state in HitFSM → Task 3
- [x] Shield durability → Task 4
- [x] Damage routing → Task 6 + 8
- [x] DefendModifier wiring → Task 5 + 8
- [x] IsDefending animator param → Task 1 + 9
- [x] Input handling → Task 7 + 8
- [x] Speed limiting → Task 4
- [x] Jump disabled during defend → Task 4
- [x] No new FSM/LayerType → Confirmed across all tasks

### 2. Placeholder scan
No TBD, TODO, or vague instructions found.

### 3. Type consistency
- `CharacterData.IsDefending` (bool) — Task 1
- `CharacterController.IsDefending` (bool property) — Task 4
- `_cc.IsDefending` used in predicate in Task 8
- `HitState.DefendHit = 8` — consistent between Task 3 and Task 9
- `BaseState.Defend = 8` — consistent between Task 2
- `DefendConfig` struct defined in Task 5, used in Task 8
- `FSMManager.CanDefend` — Task 6, used in Task 8
- `FSMManager.EnterDefend/ExitDefend/HandleShieldBreak` — Task 6, used in Task 8
