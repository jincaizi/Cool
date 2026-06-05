# Monster AI Refactor Design

**Date:** 2026-06-05
**Status:** Approved
**Context:** Refactor MonsterAI to fix 15 identified bugs, prepare for boss system, and establish a clean extensible architecture.

---

## Problem Summary

The current `MonsterAI.cs` (464 lines, single class) has three root-cause design flaws and 15 specific defects:

### Root Causes

1. **Struct context discarded** — `MonsterAIContext` is a struct. All mutations (DefendBlockCount, CooldownTimer) are lost when the method returns.
2. **Damage order** — `TakeDamage → ApplyDamage → NotifyHit(defense check)`. Defense runs after HP is already subtracted.
3. **Instant attack resolution** — `ResolveAttack()` called during `TransitionTo(Attack)`, before the animation plays. Zero reaction window.

### Specific Defects (see analysis in conversation for full details)

| # | Severity | Description |
|---|----------|-------------|
| 1 | Critical | DefendDamageReduction never applied |
| 2 | Critical | DefendBlockCount always 0 (struct mutation lost) |
| 3 | Critical | DefendBehaviour.Update() never called |
| 4 | Critical | Attack damage resolved instantly on state entry |
| 5 | Critical | Alert state is unreachable dead code |
| 6 | Major | Knockback writes `_self.position` while NavMeshAgent active |
| 7 | Major | Hit recovery → Attack bypasses cooldown check |
| 8 | Major | HitState duration hardcoded to 0.3s |
| 9-15 | Minor | Various (see conversation) |

---

## Architecture

### File Structure

```
Monster/
├── AI/
│   ├── AIBrain.cs              ← Orchestrator: owns AIContext + AIStateMachine, runs main loop
│   ├── AIStateMachine.cs       ← Global transitions + state switching
│   ├── AIContext.cs            ← Shared mutable state (class, with field ownership docs)
│   └── States/
│       ├── AIStateBase.cs      ← OnEnter / OnUpdate / EvaluateTransitions / OnExit
│       ├── IdleState.cs        ← Timer → Patrol or Chase
│       ├── PatrolState.cs      ← Navigate waypoints → Idle or Chase
│       ├── ChaseState.cs       ← Follow target → Attack or Idle (target lost)
│       ├── AttackState.cs      ← Windup → Active(resolve damage) → Recovery → exit
│       ├── HitState.cs         ← Duration from HitReactLevel table → recover
│       ├── DeathState.cs       ← Terminal, triggers death animation
│       ├── DefendState.cs      ← Block count, cooldown, counter-attack (was DefendBehaviour)
│       └── TauntState.cs       ← Random trigger on attack miss (was TauntBehaviour)
│
├── Damage/
│   ├── IDamageModifier.cs      ← Pluggable pre-damage hook interface
│   ├── DamagePipeline.cs       ← PreCheck → Gate → Apply → PostNotify
│   ├── DamageContext.cs        ← Struct, passed by ref, zero GC alloc
│   ├── DamageResult.cs         ← Struct: FinalDamage, HitReactLevel, flags
│   └── Modifiers/
│       ├── DefendModifier.cs   ← Front-facing damage reduction
│       └── IFrameModifier.cs   ← Post-hit invincibility window
│
├── MonsterEntity.cs            ← Thin MonoBehaviour: wires pipeline + AI + events
├── MonsterConfig.cs            ← ScriptableObject: +Attack timing, +HitReact, +IFrame fields
├── MonsterMovement.cs          ← +NavMeshAgent sync in ResetKnockback, +ResetPath in Resume
├── MonsterStats.cs             ← HP + attributes (death logic moved to pipeline)
├── MonsterSpawner.cs           ← Unchanged
├── MonsterEvents.cs            ← Unchanged
└── MonsterAnimHashes.cs        ← Static animator parameter hash constants
```

### Removed Files

- `MonsterAI.cs` (464 lines) — split into AIBrain + AIStateMachine + States
- `IAIBehaviour.cs` — behaviour logic merged into state classes
- `DefendBehaviour.cs` — merged into DefendState
- `TauntBehaviour.cs` — merged into TauntState
- `AlertBehaviour.cs` — dead code, removed
- `MonsterAIContext.cs` (struct) — replaced by AIContext class

---

## Core Systems

### 1. Damage Pipeline

Design intent: Separate damage processing into discrete, overridable stages. Defense mechanisms are pluggable `IDamageModifier` instances rather than hardcoded checks.

```
TakeDamage(DamageContext ctx)
  │
  ├─ 1. PreDamageCheck — Run IDamageModifier chain by priority (0→N)
  │     Each modifier reads ctx, returns DamageResult
  │     ctx.CurrentDamage is progressively reduced
  │
  ├─ 2. Gate Check
  │     If result.WasBlocked → skip to PostDamageNotify
  │     If IsDead → return (no posthumous processing)
  │
  ├─ 3. ApplyDamage
  │     stats.HP -= result.FinalDamage (clamped to 0)
  │     If HP ≤ 0 && !result.PreventDeath → flag pending death
  │
  ├─ 4. PostDamageNotify (always fires, even if blocked)
  │     HitReactLevel → AI state machine
  │     ShouldKnockback → movement system
  │     VFX, floating text, on-hit procs
  │
  └─ 5. Death Check — if pending → EnterDeath()
```

### 2. AIBrain + AIStateMachine

#### AIBrain — Single Entry Point

AIBrain owns the update loop. It knows nothing about specific states or transitions.

```
Update(deltaTime):
  1. If IsDead → return
  2. Decrement cooldowns (_ctx.AttackCooldown)
  3. UpdateKnockback(deltaTime) — applies knockback displacement
  4. TryFindTarget() — finds nearest player if no current target
  5. EvaluateTransitions — checks global then state transitions
  6. TransitionTo — if new state returned, OnExit→swap→OnEnter
  7. ExecuteState — runs current state OnUpdate exactly once
```

Three-phase execution (Evaluate → Transition → Execute) ensures OnUpdate never runs twice per frame for a newly entered state.

#### AIStateMachine — Global Transitions First

```csharp
public AIStateBase EvaluateTransitions(AIContext ctx)
{
    return CheckGlobalTransitions(ctx)      // Death, future: boss phase change
        ?? _currentState.EvaluateTransitions(ctx);
}
```

Death always interrupts any state. Future boss phase transitions use the same mechanism.

#### AIStateBase — State Contract

```csharp
// IMPORTANT: States receive AIContext as a method parameter.
// Do NOT store AIContext as a field — it contains Unity Object references
// (Animator, Transform) that become invalid on GameObject destruction.
// All state access happens through the method parameter,
// which is guaranteed valid for the duration of the call.

public abstract class AIStateBase
{
    // Called once when entering. Set animator params, start timers.
    public virtual void OnEnter(AIContext ctx) { }

    // Called every frame for the CURRENT state. Movement, look-at, etc.
    public virtual void OnUpdate(AIContext ctx) { }

    // Return the next state to transition to, or null to stay.
    // Override for per-state transition logic.
    public virtual AIStateBase EvaluateTransitions(AIContext ctx) { return null; }

    // Called once when leaving. Clean up animator params, timers.
    public virtual void OnExit(AIContext ctx) { }

    public abstract MonsterAIState StateType { get; }
}
```

States are created once in AIStateMachine constructor and reused (zero allocation during gameplay). Each OnEnter must fully reset state (clear timers, reset flags). This is a documented convention.

### 3. Attack Sub-State Timing

Timer-based (not animation callback), configurable via MonsterConfig:

```
AttackState lifecycle:
  OnEnter: Stop movement, LookAt target, play Attack anim, windup timer starts
  OnUpdate:
    If timer >= AttackWindupTime && !damageDealt → ResolveDamage()
    If timer >= AttackWindupTime + AttackRecoveryTime → signal exit
  EvaluateTransitions:
    If exit signaled → next state (Chase/Idle based on target)
  OnExit: Deactivate hitbox, reset state
```

Config fields: `AttackWindupTime`, `AttackRecoveryTime`. Comment notes that animation-event-driven timing can replace this for frame-accurate hits in the future.

### 4. Movement + Knockback

- During knockback: `MonsterMovement.UpdateKnockback()` is called every frame regardless of AI state. NavMeshAgent is stopped (`isStopped = true`) during Hit state, so direct transform writes don't conflict.
- On knockback recovery: `ResetKnockback()` syncs `_agent.nextPosition = _self.position` to prevent warp-back. Only syncs when `_agent.enabled` is true (agent is disabled after death).
- On movement resume: `Resume()` calls `_agent.ResetPath()` before `isStopped = false` to clear any stale path from before knockback.

### 5. Target Loss Handling

Each state handles target=null gracefully rather than the global `ReturnToSpawn()`:

| State | Target Loss Behavior |
|-------|---------------------|
| Idle/Patrol | No change needed |
| Chase | Transition to Idle, return to spawn immediately |
| Attack | Finish current swing animation, then return to spawn |
| Defend | Finish defend duration, then return to spawn |
| Taunt | Finish taunt animation, then return to spawn |

---

## AIContext Design

### Why a class (not struct)

AIContext is shared mutable state that survives across multiple method calls. Modifications made by one component (e.g., DefendState incrementing BlockCount) must be visible to other components (e.g., AIStateMachine checking counter-ready condition). A struct would require passing by ref everywhere and is error-prone.

### Field Ownership (documented by convention)

```
── Owned by AIBrain ──
  Target, DeltaTime, AttackCooldown

── Owned by AIStateMachine ──
  CurrentState, StateTimer, PreviousStateType

── Owned by specific State ──
  BlockCount (DefendState), CurrentAttackIndex (AttackState)

── Owned by DamagePipeline ──
  LastHitResult, LastHitDirection, LastKnockbackForce

Rule: Read any field. Write only your own fields.
```

---

## Key Interfaces & Types

### IDamageModifier

```csharp
// Design: Each defense mechanism is a pluggable IDamageModifier.
// Modifiers are checked in priority order (lowest first).
// To add a new defense type (e.g., ShieldModifier, MagicArmorModifier):
// 1. Create a class implementing this interface
// 2. Assign a priority (Defend=100, Shield=200, Invincible=0)
// 3. Register it in DamagePipeline's modifier list
// No other code changes needed.

public interface IDamageModifier
{
    int Priority { get; }
    DamageResult Modify(ref DamageContext ctx);
}
```

### DamageContext (struct, ref-passed)

```csharp
// struct: stack-allocated, zero GC pressure.
// Passed by ref through the pipeline so modifiers can mutate CurrentDamage.

public struct DamageContext
{
    // ── Input (set by caller, read-only for modifiers) ──
    public DamageBlock RawData;
    public Vector3 HitDirection;
    public int AttackerId;
    public DamageFlags Flags;

    // ── Mutated by modifiers ──
    public float CurrentDamage;
    public int BlockCount;

    public float RawDamage => RawData?.BaseDamage ?? 0f;
}

[Flags]
public enum DamageFlags
{
    None = 0,
    IsDoT = 1 << 0,         // No hit reaction, minimal VFX
    IsCritical = 1 << 1,    // Critical hit VFX
    IgnoresDefense = 1 << 2,// Bypass DefendModifier
    IsReflected = 1 << 3,   // Reflected damage (no infinite loops)
}
```

### DamageResult (struct)

```csharp
public struct DamageResult
{
    public float FinalDamage;
    public bool WasBlocked;       // Zero damage, skip VFX
    public bool WasReduced;       // Partial damage, play "glancing" VFX
    public bool PreventDeath;     // "Survive with 1 HP" buff
    public bool ShouldKnockback;  // Boss super armor sets this to false
    public HitReactLevel ReactLevel;
}

public enum HitReactLevel
{
    None = 0,       // Boss super armor — no hit state
    Flinch = 1,     // Brief interrupt, light attacks
    Stagger = 2,    // Medium stun, heavy attacks
    Knockback = 3,  // Push back
    Launch = 4,     // Airborne (reserved for future)
}
```

---

## MonsterConfig Additions

All new config fields include Chinese + English Tooltip attributes. Config is read-only at runtime (documented invariant).

```csharp
// INVARIANT: MonsterConfig is a shared ScriptableObject asset.
// All fields are read-only at runtime. States and modifiers read config
// values but NEVER write to them. State-specific mutable data lives in
// AIContext, not in config.

[Header("Attack Timing")]
[Tooltip("前摇时间（秒），在此时间后结算伤害。"
       + "Windup duration before damage is dealt (seconds). "
       + "For future: replace with animation-event-driven timing for frame-accurate hits.")]
public float AttackWindupTime = 0.2f;

[Tooltip("后摇时间（秒），伤害结算后到状态可退出的时间。"
       + "Recovery duration after damage before state can exit (seconds). "
       + "Total attack duration = WindupTime + RecoveryTime.")]
public float AttackRecoveryTime = 0.3f;

[Header("Hit Reaction")]
[Tooltip("每种HitReactLevel对应的受击硬直时长（秒）。"
       + "Hit reaction duration per HitReactLevel (seconds). "
       + "Indices: [0]=None, [1]=Flinch, [2]=Stagger, [3]=Knockback, [4]=Launch. "
       + "Duration of 0 means that level causes no hit state transition.")]
public float[] HitReactDurations = { 0f, 0.3f, 0.6f, 0.2f, 1.0f };

[Tooltip("受击后的短暂无敌时间（秒），防止被连续硬直锁死。"
       + "Brief invincibility duration after being hit (seconds). "
       + "Prevents rapid consecutive hits from stun-locking the monster.")]
public float HitIFrameDuration = 0.15f;
```

---

## MonoBehaviour Safety

```csharp
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(NavMeshAgent))]
public class MonsterEntity : MonoBehaviour, IDamageable, ITargetable, IEffectTarget
{
    // OnDestroy includes safety unsubscribe for EventBus (in case OnDisable is skipped)
}
```

---

## Animator Hash Constants

```csharp
// Single source of truth for all animator parameter hashes.
// Each state class references these rather than duplicating hash strings.
public static class MonsterAnimHashes
{
    public static readonly int AIState     = Animator.StringToHash("AIState");
    public static readonly int Attack      = Animator.StringToHash("Attack");
    public static readonly int AttackIndex = Animator.StringToHash("AttackIndex");
    public static readonly int Hit         = Animator.StringToHash("Hit");
    public static readonly int Death       = Animator.StringToHash("Death");
    public static readonly int Speed       = Animator.StringToHash("Speed");
    public static readonly int IsDefending = Animator.StringToHash("IsDefending");
    public static readonly int Taunt       = Animator.StringToHash("Taunt");
}
```

---

## Performance Notes

- **DamageContext is a struct**: passed by ref, zero GC allocation per hit.
- **States are singletons**: created once in AIStateMachine constructor, reused.
- **Chase SetDestination**: called every frame. Comment notes that if performance becomes an issue with many active monsters, throttle to every 0.25s.
- **Object pooling**: not in scope for this refactor, but noted as future improvement for MonsterSpawner.

---

## Extensibility Points

| What | How to Add |
|------|-----------|
| New AI state | Subclass `AIStateBase`, register in `AIStateMachine` |
| New defense type | Implement `IDamageModifier`, assign priority, register in `DamagePipeline` |
| New DamageFlag | Add to `DamageFlags` enum |
| New HitReactLevel | Add to `HitReactLevel` enum, add duration in `HitReactDurations` array |
| Boss phase | Implement `IPhaseCondition` (future), check in `CheckGlobalTransitions` |
| Animation-driven attack timing | Replace timer in `AttackState.OnUpdate` with callback from `StateMachineBehaviour` |

---

## Defects Addressed

All 15 original defects are fixed structurally by this design:

- #1: Damage pipeline PreCheck runs before ApplyDamage
- #2: AIContext is a class, mutations persist
- #3: State OnUpdate always called by FSM (no separate Behaviour scheduler)
- #4: AttackState uses Windup timer before ResolveDamage
- #5: Alert state removed
- #6: NavMeshAgent stopped during knockback; agent synced on recovery
- #7: AttackCooldown checked in ChaseState.EvaluateTransitions before Attack transition
- #8: HitState reads duration from HitReactDurations[HitReactLevel]
- #9-15: Addressed in state-specific implementations
