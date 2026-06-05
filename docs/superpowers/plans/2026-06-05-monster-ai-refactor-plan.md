# Monster AI Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor MonsterAI from a 464-line monolithic class into AIBrain + AIStateMachine + per-state classes + DamagePipeline, fixing 15 identified bugs and preparing for boss system.

**Architecture:** AIBrain owns the update loop and delegates to AIStateMachine (FSM with 9 state classes) and DamagePipeline (PreCheck→Gate→Apply→PostNotify with pluggable IDamageModifier). AIContext is a mutable class shared across components. State behaviours (Defend, Taunt) are merged into their respective state classes.

**Tech Stack:** Unity 2022.3.25f1, HybridCLR (Hotfix layer), C# 9.0, NavMeshAgent, Animator

---

## File Structure

### Create (19 new files)

```
Assets/Scripts/Hotfix/GameSystems/Monster/
├── AI/
│   ├── AIBrain.cs
│   ├── AIStateMachine.cs
│   ├── AIContext.cs
│   ├── MonsterAnimHashes.cs
│   └── States/
│       ├── AIStateBase.cs
│       ├── IdleState.cs
│       ├── PatrolState.cs
│       ├── ChaseState.cs
│       ├── AttackState.cs
│       ├── HitState.cs
│       ├── DeathState.cs
│       ├── DefendState.cs
│       └── TauntState.cs
├── Damage/
│   ├── IDamageModifier.cs
│   ├── DamagePipeline.cs
│   ├── DamageContext.cs
│   ├── DamageResult.cs
│   └── Modifiers/
│       ├── DefendModifier.cs
│       └── IFrameModifier.cs
```

### Modify (4 existing files)

```
Assets/Scripts/Hotfix/GameSystems/Monster/
├── MonsterEntity.cs       — Rewire to AIBrain + DamagePipeline
├── MonsterConfig.cs       — Add Attack timing, HitReact, IFrame fields
├── MonsterMovement.cs     — Fix knockback sync + Resume ResetPath
├── MonsterStats.cs        — Simplify: HP + attributes only
```

### Remove (8 files)

```
Assets/Scripts/Hotfix/GameSystems/Monster/
├── MonsterAI.cs           — Replaced by AIBrain + AIStateMachine + States
├── MonsterAIContext.cs    — Replaced by AIContext class
├── IAIBehaviour.cs        — Merged into state classes
├── DefendBehaviour.cs     — Merged into DefendState
├── TauntBehaviour.cs      — Merged into TauntState
├── AlertBehaviour.cs      — Dead code, removed
```

---

### Task 1: Damage Types — DamageContext, DamageResult, DamageFlags, HitReactLevel

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamageContext.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamageResult.cs`

- [ ] **Step 1: Create DamageContext.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // struct: stack-allocated, zero GC pressure.
    // Passed by ref through the pipeline so modifiers can mutate CurrentDamage.
    public struct DamageContext
    {
        // ── Input (set by caller, read-only for modifiers) ──
        public Skills.Data.DamageBlock RawData;
        public Vector3 HitDirection;
        public int AttackerId;
        public DamageFlags Flags;

        // ── Mutated by modifiers ──
        public float CurrentDamage;
        public int BlockCount;

        public float RawDamage => RawData?.BaseDamage ?? 0f;
    }

    // Flags allow modifiers to branch behavior without type-checking each damage source.
    // Add new flags for future damage types (e.g., TrueDamage, Heal).
    [System.Flags]
    public enum DamageFlags
    {
        None = 0,
        IsDoT = 1 << 0,         // Damage-over-time tick — no hit reaction, minimal VFX
        IsCritical = 1 << 1,    // Critical hit — special VFX/float text
        IgnoresDefense = 1 << 2,// Bypasses DefendModifier and armor
        IsReflected = 1 << 3,   // Reflected/thorns damage — prevents infinite loops
    }
}
```

- [ ] **Step 2: Create DamageResult.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    // Output of IDamageModifier.Modify() and the DamagePipeline.
    // Each modifier returns a result; the pipeline merges them (last non-default value wins).
    public struct DamageResult
    {
        public float FinalDamage;
        public bool WasBlocked;       // Zero damage — skip VFX entirely
        public bool WasReduced;       // Partial damage — play "glancing hit" VFX
        public bool PreventDeath;     // "Survive with 1 HP" buff support (future)
        public bool ShouldKnockback;  // Boss super armor sets this to false
        public HitReactLevel ReactLevel;
    }

    // Determines which hit state the monster enters, and for how long.
    // Index into MonsterConfig.HitReactDurations[] for timing.
    // Add new levels for future knockup/launch/pull mechanics.
    public enum HitReactLevel
    {
        None = 0,       // Boss super armor — no hit state transition
        Flinch = 1,     // Brief interrupt for light attacks
        Stagger = 2,    // Medium stun for heavy attacks
        Knockback = 3,  // Push back with displacement
        Launch = 4,     // Airborne (reserved for future launcher skills)
    }
}
```

- [ ] **Step 3: Verify compilation**

After Unity auto-refreshes, check Console for compilation errors. The files have no dependencies beyond `Skills.Data.DamageBlock` (existing type).

---

### Task 2: IDamageModifier Interface

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/IDamageModifier.cs`

- [ ] **Step 1: Create IDamageModifier.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    // Design: Each defense mechanism is a pluggable IDamageModifier.
    // Modifiers are checked in priority order (lowest first).
    //
    // To add a new defense type (e.g., ShieldModifier, MagicArmorModifier):
    // 1. Create a class implementing this interface
    // 2. Assign a priority (Defend=100, Shield=200, Invincible=0)
    // 3. Register it in DamagePipeline's modifier list
    // No other code changes needed.
    //
    // Priority conventions:
    //   0   = Invincibility (must run first to block all damage)
    //   100 = Defense (armor/damage reduction)
    //   200 = Shield (absorbs damage after reduction)
    //   300 = Thorns (reflects remaining damage)
    public interface IDamageModifier
    {
        // Lower values execute first. Modifiers at the same priority execute in registration order.
        int Priority { get; }

        // Mutate ctx.CurrentDamage and return the modified result.
        // ctx is passed by ref so modifiers can read/write shared state (BlockCount, etc.).
        DamageResult Modify(ref DamageContext ctx);
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 3: MonsterAnimHashes Constants

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/MonsterAnimHashes.cs`

- [ ] **Step 1: Create MonsterAnimHashes.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // Single source of truth for all animator parameter hashes.
    // Each state class references these rather than duplicating hash strings.
    // Using static readonly int avoids repeated Animator.StringToHash calls.
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
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 4: Add Config Fields to MonsterConfig

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs`

- [ ] **Step 1: Add new serialized fields**

Add the following inside the `MonsterConfig` class, after the existing `[Header("Attack Shape")]` section and before `[Header("Loot & Death")]`. Use Edit to insert after line 112 (after the `EffectBlock AttackEffect` field).

```csharp
        [Header("Attack Timing")]
        // Total attack duration = WindupTime + RecoveryTime.
        // Damage is dealt at WindupTime seconds after attack starts.
        // For future: replace timer with animation-event-driven callback for frame-accurate hits.
        [Tooltip("前摇时间（秒），在此时间后结算伤害。Windup duration before damage is dealt (seconds).")]
        public float AttackWindupTime = 0.2f;

        [Tooltip("后摇时间（秒），伤害结算后到状态可退出的时间。Recovery duration after damage before state can exit (seconds).")]
        public float AttackRecoveryTime = 0.3f;

        [Header("Hit Reaction")]
        // Index = (int)HitReactLevel. Duration of 0 = no hit state transition for that level.
        // In-editor: set array size to 5. Indices: [0]=None, [1]=Flinch, [2]=Stagger, [3]=Knockback, [4]=Launch.
        [Tooltip("每种HitReactLevel对应的受击硬直时长（秒）。Hit reaction duration per HitReactLevel (seconds). Indices: [0]=None, [1]=Flinch, [2]=Stagger, [3]=Knockback, [4]=Launch.")]
        public float[] HitReactDurations = { 0f, 0.3f, 0.6f, 0.2f, 1.0f };

        [Tooltip("受击后的短暂无敌时间（秒），防止被连续硬直锁死。Brief invincibility duration after being hit (seconds).")]
        public float HitIFrameDuration = 0.15f;
```

- [ ] **Step 2: Add runtime-readonly invariant comment at top of class**

Add after the `[CreateAssetMenu]` attribute, before the first field:

```csharp
    // INVARIANT: MonsterConfig is a shared ScriptableObject asset.
    // All fields are read-only at runtime. States and modifiers read config
    // values but NEVER write to them. State-specific mutable data lives in
    // AIContext, not in config.
```

- [ ] **Step 3: Verify compilation**

---

### Task 5: DefendModifier

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/Modifiers/DefendModifier.cs`

- [ ] **Step 1: Create DefendModifier.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // Reduces damage from frontal attacks during Defend state.
    // Priority=100: runs after invincibility checks, before shields.
    // Read MonsterConfig.DefendAngle and DefendDamageReduction at runtime.
    public class DefendModifier : IDamageModifier
    {
        private readonly MonsterConfig _config;
        private readonly Transform _self;

        public int Priority => 100;

        public DefendModifier(MonsterConfig config, Transform self)
        {
            _config = config;
            _self = self;
        }

        public DamageResult Modify(ref DamageContext ctx)
        {
            var result = new DamageResult
            {
                FinalDamage = ctx.CurrentDamage,
                ShouldKnockback = true,
                ReactLevel = HitReactLevel.Flinch,
            };

            // Only active during Defend state — caller checks state before invoking pipeline.
            // If the hit comes from behind, defense is bypassed entirely.
            float angle = Vector3.Angle(_self.forward, -ctx.HitDirection);
            if (angle >= _config.DefendAngle * 0.5f)
                return result;

            // Frontal hit: reduce damage and suppress knockback
            ctx.BlockCount++;
            result.FinalDamage = ctx.CurrentDamage * (1f - _config.DefendDamageReduction);
            result.WasReduced = true;
            result.ShouldKnockback = false;
            result.ReactLevel = HitReactLevel.None;

            if (result.FinalDamage <= 0)
            {
                result.FinalDamage = 0;
                result.WasBlocked = true;
            }

            return result;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 6: IFrameModifier

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/Modifiers/IFrameModifier.cs`

- [ ] **Step 1: Create IFrameModifier.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    // Brief invincibility window after being hit.
    // Priority=0: runs first to block all damage during i-frame window.
    // Timer is managed externally — AIBrain or HitState sets Active=true/false.
    public class IFrameModifier : IDamageModifier
    {
        public bool Active { get; set; }

        public int Priority => 0;

        public DamageResult Modify(ref DamageContext ctx)
        {
            if (!Active)
                return new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            return new DamageResult
            {
                FinalDamage = 0,
                WasBlocked = true,
                ShouldKnockback = false,
                ReactLevel = HitReactLevel.None,
            };
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 7: DamagePipeline

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamagePipeline.cs`

- [ ] **Step 1: Create DamagePipeline.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // Central damage processing pipeline.
    //
    // Flow: PreDamageCheck → Gate Check → ApplyDamage → PostDamageNotify → Death Check
    //
    // Modifiers are sorted by priority (lowest first) and each gets a chance to reduce
    // or block damage before it reaches HP. The pipeline is synchronous and atomic per call.
    //
    // To register additional modifiers: call AddModifier() in MonsterEntity.Init().
    public class DamagePipeline
    {
        private readonly MonsterConfig _config;
        private readonly MonsterStats _stats;
        private readonly List<IDamageModifier> _modifiers = new List<IDamageModifier>();
        private readonly IFrameModifier _iFrameModifier;

        public DamagePipeline(MonsterConfig config, MonsterStats stats)
        {
            _config = config;
            _stats = stats;
            _iFrameModifier = new IFrameModifier();
            _modifiers.Add(_iFrameModifier);
        }

        // Add a modifier. Called during initialization to register defense types.
        // Order doesn't matter — modifiers are sorted by Priority before each Process call.
        public void AddModifier(IDamageModifier modifier)
        {
            _modifiers.Add(modifier);
        }

        // Enable/disable brief invincibility after being hit.
        // Called by HitState.OnEnter and HitState.OnExit.
        public void SetIFrameActive(bool active)
        {
            _iFrameModifier.Active = active;
        }

        // Entry point called from MonsterEntity.TakeDamage.
        // Returns the merged result — caller uses HitReactLevel and ShouldKnockback
        // to drive AI state transitions.
        public DamageResult Process(ref DamageContext ctx)
        {
            ctx.CurrentDamage = ctx.RawDamage;

            // Phase 1: Run all modifiers in priority order
            _modifiers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            var merged = new DamageResult { FinalDamage = ctx.CurrentDamage, ShouldKnockback = true, ReactLevel = HitReactLevel.Flinch };

            foreach (var modifier in _modifiers)
            {
                var result = modifier.Modify(ref ctx);
                if (result.WasBlocked)
                {
                    merged.WasBlocked = true;
                    merged.FinalDamage = 0;
                    merged.ShouldKnockback = false;
                    merged.ReactLevel = HitReactLevel.None;
                    break;
                }
                if (result.WasReduced)
                {
                    merged.WasReduced = true;
                    merged.FinalDamage = result.FinalDamage;
                    merged.ShouldKnockback = result.ShouldKnockback;
                    merged.ReactLevel = result.ReactLevel;
                }
            }

            // Phase 2: Gate check — if blocked, skip damage but still notify
            if (merged.WasBlocked)
                return merged;

            // Phase 3: Apply damage
            if (_stats.IsDead)
                return merged;

            _stats.ApplyDamage(merged.FinalDamage);

            return merged;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

Note: `MonsterStats.ApplyDamage()` is a renamed `TakeDamage()` — see Task 20.

---

### Task 8: AIContext Class

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIContext.cs`

- [ ] **Step 1: Create AIContext.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // AIContext is the single source of truth for all AI-related mutable state.
    // It is a class (not struct) so mutations persist across method calls.
    //
    // Field ownership conventions (enforced by convention, not compiler):
    //
    // ── Owned by AIBrain ──
    //   Target, DeltaTime, AttackCooldown
    //
    // ── Owned by AIStateMachine ──
    //   CurrentState, StateTimer, PreviousStateType
    //
    // ── Owned by specific State ──
    //   BlockCount (DefendState), CurrentAttackIndex (AttackState)
    //
    // ── Owned by DamagePipeline ──
    //   LastHitResult, LastHitDirection, LastKnockbackForce
    //
    // Rule: Read any field. Write only your own fields.
    // If you find yourself writing another component's field, add a method
    // to that component instead.
    public class AIContext
    {
        // ── References (set once at construction, never null after Init) ──
        public Transform Self;
        public Animator Animator;
        public MonsterStats Stats;
        public MonsterMovement Movement;
        public MonsterConfig Config;

        // ── AIBrain-owned ──
        public Transform Target;
        public float DeltaTime;
        public float AttackCooldown;

        // ── AIStateMachine-owned ──
        public MonsterAIState CurrentState;
        public float StateTimer;
        public MonsterAIState PreviousStateType;

        // ── DefendState-owned ──
        public int BlockCount;

        // ── AttackState-owned ──
        public int CurrentAttackIndex;

        // ── DamagePipeline-owned ──
        public DamageResult LastHitResult;
        public Vector3 LastHitDirection;
        public float LastKnockbackForce;

        public bool IsDead => Stats.IsDead;
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 9: AIStateBase Abstract Class

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/AIStateBase.cs`

- [ ] **Step 1: Create AIStateBase.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    // Base class for all AI states.
    //
    // Lifecycle (called by AIStateMachine):
    //   1. OnExit(old state) → 2. swap current → 3. OnEnter(new state) → 4. OnUpdate(new state, next frame)
    //
    // IMPORTANT: States receive AIContext as a method parameter.
    // Do NOT store AIContext as a field — it contains Unity Object references
    // (Animator, Transform) that become invalid on GameObject destruction.
    // All state access must go through the method parameter, which is
    // guaranteed valid for the duration of the call.
    //
    // States are created once in AIStateMachine constructor and reused.
    // Each OnEnter must fully reset state (clear timers, reset flags).
    //
    // To add a new state:
    // 1. Create a subclass implementing these methods
    // 2. Register in AIStateMachine's state dictionary
    // 3. Done — no changes to AIBrain or other states needed
    public abstract class AIStateBase
    {
        // Called once when entering this state. Use for:
        // - Setting Animator parameters (triggers, bools, ints)
        // - Starting timers
        // - Initial movement commands (Stop, Resume, Chase)
        public virtual void OnEnter(AIContext ctx) { }

        // Called every frame while this state is active. Use for:
        // - Movement updates (LookAt, Chase destination)
        // - Mid-state logic (windup countdown, block tracking)
        // Do NOT transition out here — use EvaluateTransitions instead.
        public virtual void OnUpdate(AIContext ctx) { }

        // Called every frame. Return the next state to transition to,
        // or null to stay in this state. AIStateMachine evaluates
        // global transitions (Death) before calling this.
        public virtual AIStateBase EvaluateTransitions(AIContext ctx) { return null; }

        // Called once when leaving this state. Use for:
        // - Cleaning up Animator parameters
        // - Stopping timers
        // - Resetting movement flags
        public virtual void OnExit(AIContext ctx) { }

        // Unique identifier for this state, used for Animator hash lookup.
        public abstract MonsterAIState StateType { get; }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 10: AIStateMachine

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIStateMachine.cs`

- [ ] **Step 1: Create AIStateMachine.cs**

```csharp
using System.Collections.Generic;

namespace Hotfix.GameSystems.Monster
{
    // Core FSM that manages AI state lifecycle.
    //
    // States are created once and reused (zero allocation during gameplay).
    // Global transitions (Death) are checked before state-specific ones.
    // Three-phase execution prevents double-OnUpdate bugs on transition frames.
    //
    // To add a state: create the class, then register it in BuildStates().
    public class AIStateMachine
    {
        private readonly Dictionary<MonsterAIState, AIStateBase> _states;
        private AIStateBase _currentState;

        public MonsterAIState CurrentStateType => _currentState.StateType;

        // All state instances are passed in — AIStateMachine doesn't create them.
        // This allows states to have constructor dependencies (config, movement, etc.).
        public AIStateMachine(Dictionary<MonsterAIState, AIStateBase> states, MonsterAIState initialState)
        {
            _states = states;
            _currentState = _states[initialState];
        }

        // Phase 1+2: Evaluate global transitions, then state-specific.
        // Returns the next state if a transition should happen, null to stay.
        public AIStateBase EvaluateTransitions(AIContext ctx)
        {
            return CheckGlobalTransitions(ctx)
                ?? _currentState.EvaluateTransitions(ctx);
        }

        // Phase 3: Execute the transition. OnExit(old) → swap → OnEnter(new).
        public void TransitionTo(AIStateBase nextState, AIContext ctx)
        {
            if (_currentState == nextState) return;

            _currentState.OnExit(ctx);
            ctx.PreviousStateType = _currentState.StateType;
            _currentState = nextState;
            ctx.CurrentState = nextState.StateType;
            nextState.OnEnter(ctx);
        }

        // Phase 4: Execute current state's per-frame logic.
        public void ExecuteState(AIContext ctx)
        {
            _currentState.OnUpdate(ctx);
        }

        public void ForceState(MonsterAIState stateType, AIContext ctx)
        {
            if (_states.TryGetValue(stateType, out var state))
                TransitionTo(state, ctx);
        }

        // Global transitions that can interrupt any state.
        // Death always takes priority. Future: boss phase transitions also go here.
        private AIStateBase CheckGlobalTransitions(AIContext ctx)
        {
            if (ctx.IsDead && _currentState.StateType != MonsterAIState.Death)
                return _states[MonsterAIState.Death];
            return null;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 11: IdleState + PatrolState

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/IdleState.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/PatrolState.cs`

- [ ] **Step 1: Create IdleState.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    public class IdleState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Idle;

        public override void OnEnter(AIContext ctx)
        {
            ctx.Animator.SetFloat(MonsterAnimHashes.Speed, 0f);
            ctx.StateTimer = ctx.Config.IdleDuration
                + UnityEngine.Random.Range(-ctx.Config.IdleDurationVariance, ctx.Config.IdleDurationVariance);
        }

        public override AIStateBase EvaluateTransitions(AIContext ctx)
        {
            if (ctx.Target != null)
            {
                float dist = UnityEngine.Vector3.Distance(ctx.Self.position, ctx.Target.position);
                if (dist < ctx.Config.DetectRange)
                    return null; // Handled by ChaseState registration — caller checks this
            }
            // Delegate to AIBrain/StateMachine for target detection → Chase transition.
            // This state's only auto-transition is to Patrol when timer expires.
            return null;
        }
    }
}
```

Wait — the IdleState needs access to other state instances to return them from EvaluateTransitions. Best approach: pass all states to each state's constructor, or have states request transitions by MonsterAIState enum and let AIStateMachine resolve the instance.

Let me revise: EvaluateTransitions returns `MonsterAIState?` (the enum), and AIStateMachine resolves it to an AIStateBase instance.

- [ ] **Step 1 (revised): Create IdleState.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    public class IdleState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Idle;

        public override void OnEnter(AIContext ctx)
        {
            ctx.Movement.Stop();
            ctx.Animator.SetFloat(MonsterAnimHashes.Speed, 0f);
            ctx.StateTimer = RandomRange(ctx.Config.IdleDuration, ctx.Config.IdleDurationVariance);
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            float distToTarget = ctx.Target != null
                ? UnityEngine.Vector3.Distance(ctx.Self.position, ctx.Target.position)
                : float.MaxValue;

            if (distToTarget < ctx.Config.DetectRange)
                return MonsterAIState.Chase;

            if (ctx.StateTimer <= 0)
                return MonsterAIState.Patrol;

            return null;
        }

        private static float RandomRange(float baseValue, float variance)
        {
            if (variance <= 0) return baseValue;
            return baseValue + UnityEngine.Random.Range(-variance, variance);
        }
    }
}
```

- [ ] **Step 2: Create PatrolState.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    public class PatrolState : AIStateBase
    {
        private readonly List<Vector3> _patrolPoints = new List<Vector3>();
        private int _patrolIndex;

        public override MonsterAIState StateType => MonsterAIState.Patrol;

        // Generate patrol points. Called externally after spawn point is known.
        public void GeneratePatrolPoints(Vector3 spawnPoint, float patrolRadius)
        {
            _patrolPoints.Clear();
            if (patrolRadius <= 0) return;
            for (int i = 0; i < 3; i++)
            {
                float angle = (360f / 3) * i * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * patrolRadius;
                _patrolPoints.Add(spawnPoint + offset);
            }
        }

        public override void OnEnter(AIContext ctx)
        {
            ctx.Animator.SetFloat(MonsterAnimHashes.Speed, 1f);
            if (_patrolPoints.Count > 0)
            {
                ctx.Movement.PatrolTo(_patrolPoints[_patrolIndex]);
                _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
            }
            else
            {
                // No patrol points configured — go back to idle immediately
                ctx.StateTimer = 0;
            }
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            float distToTarget = ctx.Target != null
                ? UnityEngine.Vector3.Distance(ctx.Self.position, ctx.Target.position)
                : float.MaxValue;

            if (distToTarget < ctx.Config.DetectRange)
                return MonsterAIState.Chase;

            if (ctx.Movement.HasReachedDestination)
                return MonsterAIState.Idle;

            return null;
        }
    }
}
```

- [ ] **Step 3: Update AIStateBase.EvaluateTransitions return type**

Change `EvaluateTransitions` to return `MonsterAIState?` instead of `AIStateBase`:

```csharp
// In AIStateBase.cs, change the method signature:
public virtual MonsterAIState? EvaluateTransitions(AIContext ctx) { return null; }
```

- [ ] **Step 4: Update AIStateMachine to resolve enum → state instance**

```csharp
// In AIStateMachine.cs, update EvaluateTransitions:
public void EvaluateAndTransition(AIContext ctx)
{
    var nextStateType = CheckGlobalTransitions(ctx)
        ?? _currentState.EvaluateTransitions(ctx);

    if (nextStateType.HasValue && _states.TryGetValue(nextStateType.Value, out var nextState))
        TransitionTo(nextState, ctx);
}
```

- [ ] **Step 5: Verify compilation**

---

### Task 12: ChaseState

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/ChaseState.cs`

- [ ] **Step 1: Create ChaseState.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    public class ChaseState : AIStateBase
    {
        // Tracks how long the monster has been chasing.
        // Used by DefendState to trigger defense after prolonged chase.
        // Reset to 0 on entering Chase; incremented in OnUpdate.
        public float ChaseTimer { get; set; }

        public override MonsterAIState StateType => MonsterAIState.Chase;

        public override void OnEnter(AIContext ctx)
        {
            ChaseTimer = 0f;
            ctx.Movement.Resume();
            ctx.Animator.SetFloat(MonsterAnimHashes.Speed, ctx.Config.ChaseAnimIsRun ? 2f : 1f);
        }

        public override void OnUpdate(AIContext ctx)
        {
            ChaseTimer += ctx.DeltaTime;

            if (ctx.Target != null)
            {
                ctx.Movement.Chase(ctx.Target);
                ctx.Movement.LookAt(ctx.Target.position);
            }
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            // Target lost — return to spawn
            if (ctx.Target == null)
                return MonsterAIState.Idle;

            float dist = Vector3.Distance(ctx.Self.position, ctx.Target.position);

            // Out of range — give up chase
            if (dist > ctx.Config.LeaveRange)
                return MonsterAIState.Idle;

            // In attack range and cooldown ready — attack
            if (dist < ctx.Config.AttackRange && ctx.AttackCooldown <= 0)
                return MonsterAIState.Attack;

            // Check defend trigger conditions (HP low OR chasing too long)
            if (ctx.Config.EnableDefend)
            {
                float hpRatio = ctx.Stats.HP / ctx.Stats.MaxHP;
                if (hpRatio < ctx.Config.DefendHPThreshold
                    || ChaseTimer > ctx.Config.DefendChaseTimeThreshold)
                    return MonsterAIState.Defend;
            }

            return null;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 13: AttackState

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/AttackState.cs`

- [ ] **Step 1: Create AttackState.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    // AttackState lifecycle:
    //   Windup (timer < WindupTime) → Active (resolve damage) → Recovery (timer < total) → Exit
    //
    // Damage is resolved ONCE at the Windup→Active boundary.
    // Timer-based (not animation callback) for simplicity.
    // For frame-accurate timing, replace timer check with animation event callback in the future.
    public class AttackState : AIStateBase
    {
        private readonly List<IDamageable> _hitBuffer = new List<IDamageable>(8);
        private bool _damageDealt;
        private float _totalDuration;
        private int _attackIndex;

        public override MonsterAIState StateType => MonsterAIState.Attack;

        public override void OnEnter(AIContext ctx)
        {
            _damageDealt = false;
            _totalDuration = ctx.Config.AttackWindupTime + ctx.Config.AttackRecoveryTime;
            ctx.StateTimer = 0f;
            ctx.Movement.Stop();

            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);

            // Pick attack animation index
            _attackIndex = PickAttackIndex(ctx.Config);
            ctx.CurrentAttackIndex = _attackIndex;

            ctx.Animator.SetInteger(MonsterAnimHashes.AttackIndex, _attackIndex);
            ctx.Animator.SetTrigger(MonsterAnimHashes.Attack);
            ctx.AttackCooldown = RandomRange(ctx.Config.AttackCooldown, ctx.Config.AttackCooldownVariance);
        }

        public override void OnUpdate(AIContext ctx)
        {
            ctx.StateTimer += ctx.DeltaTime;

            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);

            // Resolve damage at the windup→active boundary
            if (!_damageDealt && ctx.StateTimer >= ctx.Config.AttackWindupTime)
            {
                ResolveDamage(ctx);
                _damageDealt = true;
            }
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            if (ctx.StateTimer < _totalDuration)
                return null;

            // Attack finished. Check if taunt should trigger (missed attack).
            if (ctx.Config.EnableTaunt && UnityEngine.Random.value < ctx.Config.TauntChance)
                return MonsterAIState.Taunt;

            // Back to chase (will re-engage or idle if target lost)
            return MonsterAIState.Chase;
        }

        private void ResolveDamage(AIContext ctx)
        {
            var damage = ctx.Config.AttackDamage ?? DamageBlock.CreateDefault(ctx.Config.AttackPower);
            var effect = ctx.Config.AttackEffect;

            int mask = LayerMask.GetMask("Character");
            var shape = AttackShapeFactory.Create(ctx.Config.AttackShape, PhysicsRegistry.Instance, EntityType.Player);
            _hitBuffer.Clear();
            shape.ResolveNonAlloc(ctx.Self.position, ctx.Self.forward, mask, _hitBuffer);

            foreach (var t in _hitBuffer)
            {
                Vector3 dir = (t.Transform.position - ctx.Self.position).normalized;
                t.TakeDamage(damage, dir);
            }
        }

        private static int PickAttackIndex(MonsterConfig config)
        {
            if (config.AttackAnimCount <= 1) return 0;
            if (config.AttackWeights == null || config.AttackWeights.Length == 0) return 0;
            float roll = Random.value;
            float cumulative = 0;
            for (int i = 0; i < config.AttackWeights.Length && i < config.AttackAnimCount; i++)
            {
                cumulative += config.AttackWeights[i];
                if (roll <= cumulative) return i;
            }
            return 0;
        }

        private static float RandomRange(float baseValue, float variance)
        {
            if (variance <= 0) return baseValue;
            return baseValue + Random.Range(-variance, variance);
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 14: HitState

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/HitState.cs`

- [ ] **Step 1: Create HitState.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // HitState duration is determined by HitReactLevel, looked up from config table.
    // During HitState: movement stopped, i-frame modifier active for configurable window.
    public class HitState : AIStateBase
    {
        private readonly DamagePipeline _pipeline;
        private float _hitDuration;

        public HitState(DamagePipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public override MonsterAIState StateType => MonsterAIState.Hit;

        public override void OnEnter(AIContext ctx)
        {
            // Determine duration from react level
            int level = (int)ctx.LastHitResult.ReactLevel;
            var durations = ctx.Config.HitReactDurations;
            _hitDuration = (durations != null && level < durations.Length)
                ? durations[level]
                : 0.3f;

            ctx.StateTimer = _hitDuration;
            ctx.Movement.Stop();
            ctx.Animator.SetTrigger(MonsterAnimHashes.Hit);

            // Enable brief i-frames to prevent stun-lock
            _pipeline.SetIFrameActive(true);
        }

        public override void OnUpdate(AIContext ctx)
        {
            // i-frame duration managed by timer — deactivate after configured window
            if (_hitDuration - ctx.StateTimer >= ctx.Config.HitIFrameDuration)
                _pipeline.SetIFrameActive(false);
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            if (ctx.IsDead)
                return null; // Death is a global transition, handled by AIStateMachine

            if (ctx.StateTimer <= 0)
                return RecoverFromHit(ctx);

            return null;
        }

        public override void OnExit(AIContext ctx)
        {
            _pipeline.SetIFrameActive(false);
            ctx.Movement.ResetKnockback();
        }

        private MonsterAIState? RecoverFromHit(AIContext ctx)
        {
            if (ctx.Target != null)
            {
                float dist = Vector3.Distance(ctx.Self.position, ctx.Target.position);
                if (dist < ctx.Config.AttackRange)
                    return MonsterAIState.Attack;
                return MonsterAIState.Chase;
            }

            // No target — return to previous state or idle
            return ctx.PreviousStateType == MonsterAIState.Hit
                || ctx.PreviousStateType == MonsterAIState.Death
                ? MonsterAIState.Idle
                : ctx.PreviousStateType;
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 15: DeathState

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/DeathState.cs`

- [ ] **Step 1: Create DeathState.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    // Terminal state. Once entered, never transitions out.
    // Death animation and knockback are played once. No OnUpdate logic needed.
    public class DeathState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Death;

        public override void OnEnter(AIContext ctx)
        {
            ctx.Animator.SetTrigger(MonsterAnimHashes.Death);
            ctx.Movement.Stop();
        }

        // Death is terminal — EvaluateTransitions always returns null.
        // The death sequence (loot, destroy) is handled by MonsterEntity coroutine.

        // Called by MonsterEntity when death animation completes.
        // External systems can subscribe to this via AIBrain.OnDeathComplete event.
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 16: DefendState

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/DefendState.cs`

- [ ] **Step 1: Create DefendState.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // DefendState merges the old DefendBehaviour logic directly into the state.
    // Block counting is tracked in ctx.BlockCount (incremented by DefendModifier).
    // When block count reaches threshold, counter-attack is triggered.
    public class DefendState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Defend;

        public override void OnEnter(AIContext ctx)
        {
            ctx.BlockCount = 0;
            ctx.StateTimer = ctx.Config.DefendDuration;
            ctx.Movement.Stop();
            ctx.Animator.SetBool(MonsterAnimHashes.IsDefending, true);
        }

        public override void OnUpdate(AIContext ctx)
        {
            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            if (ctx.StateTimer > 0)
                return null;

            // Defend finished. Counter-attack if enough blocks, otherwise chase.
            if (ctx.BlockCount >= ctx.Config.DefendBlockCountToCounter)
                return MonsterAIState.Attack;

            float distToTarget = ctx.Target != null
                ? Vector3.Distance(ctx.Self.position, ctx.Target.position)
                : float.MaxValue;

            if (distToTarget < ctx.Config.AttackRange && ctx.AttackCooldown <= 0)
                return MonsterAIState.Attack;

            return MonsterAIState.Chase;
        }

        public override void OnExit(AIContext ctx)
        {
            ctx.Animator.SetBool(MonsterAnimHashes.IsDefending, false);
            ctx.Movement.Resume();
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 17: TauntState

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/TauntState.cs`

- [ ] **Step 1: Create TauntState.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    // TauntState merges the old TauntBehaviour logic.
    // Plays a taunt animation after a missed attack, then re-engages.
    public class TauntState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Taunt;

        public override void OnEnter(AIContext ctx)
        {
            ctx.StateTimer = ctx.Config.TauntDuration;
            ctx.Movement.Stop();
            ctx.Animator.SetTrigger(MonsterAnimHashes.Taunt);
        }

        public override MonsterAIState? EvaluateTransitions(AIContext ctx)
        {
            if (ctx.StateTimer > 0)
                return null;

            float distToTarget = ctx.Target != null
                ? Vector3.Distance(ctx.Self.position, ctx.Target.position)
                : float.MaxValue;

            if (distToTarget < ctx.Config.AttackRange)
                return MonsterAIState.Attack;
            if (ctx.Target != null)
                return MonsterAIState.Chase;
            return MonsterAIState.Idle;
        }

        public override void OnExit(AIContext ctx)
        {
            ctx.Movement.Resume();
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 18: AIBrain (Orchestrator)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs`

- [ ] **Step 1: Create AIBrain.cs**

```csharp
using System;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Monster
{
    // AIBrain is the single entry point for monster AI.
    // It owns the AIContext, AIStateMachine, and references to all states.
    // MonsterEntity calls Update() each frame — everything else is internal.
    //
    // Design principle: AIBrain knows nothing about specific state behavior.
    // Adding a new state requires zero changes to this class.
    public class AIBrain
    {
        private readonly AIContext _ctx;
        private readonly AIStateMachine _fsm;
        private readonly MonsterConfig _config;
        private readonly Vector3 _spawnPoint;

        public event Action OnDeathComplete;

        public MonsterAIState CurrentState => _ctx.CurrentState;
        public Vector3 LastHitDirection => _ctx.LastHitDirection;
        public float LastKnockbackForce => _ctx.LastKnockbackForce;

        public AIBrain(
            AIContext ctx,
            AIStateMachine fsm,
            MonsterConfig config,
            Vector3 spawnPoint)
        {
            _ctx = ctx;
            _fsm = fsm;
            _config = config;
            _spawnPoint = spawnPoint;
        }

        public void Update(float deltaTime)
        {
            if (_ctx.IsDead) return;

            _ctx.DeltaTime = deltaTime;
            _ctx.AttackCooldown -= deltaTime;
            _ctx.Movement.UpdateKnockback(deltaTime);

            TryFindTarget();

            // Phase 1+2: Evaluate and execute transitions
            _fsm.EvaluateAndTransition(_ctx);

            // Phase 3: Execute current state (always runs once)
            _fsm.ExecuteState(_ctx);
        }

        private void TryFindTarget()
        {
            if (_ctx.Target != null) return;

            var players = PhysicsRegistry.Instance.FindNearby(
                _ctx.Self.position, _config.DetectRange, EntityType.Player);
            if (players.Count > 0)
                _ctx.Target = players[0].Transform;
        }

        // Called by MonsterEntity when damage is received.
        // Checks defend behavior (front absorb), then transitions to Hit state if needed.
        public void OnDamageReceived(DamageResult result, Vector3 hitDirection)
        {
            if (_ctx.IsDead) return;

            _ctx.LastHitResult = result;
            _ctx.LastHitDirection = hitDirection;
            _ctx.LastKnockbackForce = result.ShouldKnockback ? result.FinalDamage * 0.5f : 0f;

            // Defend: front-facing hits play a block animation instead of hit reaction
            if (_ctx.CurrentState == MonsterAIState.Defend && result.WasReduced)
            {
                _ctx.Animator.SetTrigger(MonsterAnimHashes.Hit); // Block spark VFX
                return;
            }

            // Hit reaction level None = ignore (boss super armor)
            if (result.ReactLevel == HitReactLevel.None)
                return;

            _fsm.ForceState(MonsterAIState.Hit, _ctx);
        }

        // Called by MonsterEntity when HP reaches 0.
        public void EnterDeath()
        {
            _fsm.ForceState(MonsterAIState.Death, _ctx);
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 19: Update MonsterMovement (NavMeshAgent Fixes)

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterMovement.cs`

- [ ] **Step 1: Add ResetPath() to Resume()**

Replace the `Resume()` method:

```csharp
public void Resume()
{
    // ResetPath clears any stale path from before the agent was stopped.
    // Without this, the agent may move 1 frame towards the old destination
    // before the new SetDestination overrides it.
    _agent.ResetPath();
    _agent.isStopped = false;
}
```

- [ ] **Step 2: Add nextPosition sync in ResetKnockback()**

Replace the `ResetKnockback()` method:

```csharp
public void ResetKnockback()
{
    _knockbackVelocity = Vector3.zero;
    _knockbackTimer = 0;
    // Sync NavMeshAgent to current position after knockback displacement.
    // Without this, the agent resumes from its last calculated position
    // which may be far from where knockback pushed the transform.
    // Only sync when enabled — disabled agents reject nextPosition (e.g., after death).
    if (_agent.enabled)
        _agent.nextPosition = _self.position;
}
```

- [ ] **Step 3: Add performance note comment on Chase()**

Add above the `Chase()` method:

```csharp
// Performance note: SetDestination is called every frame.
// If this becomes a bottleneck with many active monsters (20+),
// throttle to every 0.25s: cache last destination + only update
// if target moved > 0.5m or timer elapsed.
```

- [ ] **Step 4: Verify compilation**

---

### Task 20: Update MonsterStats (Simplify)

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterStats.cs`

- [ ] **Step 1: Rename TakeDamage to ApplyDamage, remove death logic**

Replace the `TakeDamage` method with `ApplyDamage`. Death handling moves to the pipeline caller (MonsterEntity).

```csharp
// ApplyDamage is called by DamagePipeline after all modifiers have run.
// Unlike the old TakeDamage, this is a pure HP subtraction — no death check,
// no event emission. Death is handled by MonsterEntity after the pipeline completes.
public void ApplyDamage(float damage)
{
    if (IsDead || damage <= 0) return;

    _attributes[AttributeType.Health] -= damage;
    if (_attributes[AttributeType.Health] < 0)
        _attributes[AttributeType.Health] = 0;

    OnHPChanged?.Invoke(HP, MaxHP);
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 21: Update MonsterEntity (Wire Everything)

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

This is the largest integration step. Let me show the full updated file.

- [ ] **Step 1: Rewrite MonsterEntity.cs**

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Nameplate;

namespace Hotfix.GameSystems.Monster
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(NavMeshAgent))]
    public class MonsterEntity : MonoBehaviour, IDamageable, ITargetable, IEffectTarget
    {
        [Header("Components")]
        public Animator Animator;
        public NavMeshAgent NavAgent;
        public HitZone HitZone;
        public AttackHitbox AttackHitbox;

        private MonsterConfig _config;
        public MonsterConfig Config => _config;
        private MonsterStats _stats;
        private MonsterMovement _movement;
        private DamagePipeline _damagePipeline;
        private AIBrain _brain;
        private AIContext _ctx;
        private Vector3 _spawnPoint;

        bool IDamageable.IsAlive => !_stats.IsDead;
        Transform IDamageable.Transform => transform;

        public event Action OnDeathComplete;
        public event Action<LootResult[]> OnLootDrop;

        private void Awake()
        {
            if (Animator == null) Animator = GetComponent<Animator>();
            if (NavAgent == null) NavAgent = GetComponent<NavMeshAgent>();
            if (HitZone == null) HitZone = GetComponent<HitZone>();
            if (AttackHitbox == null) AttackHitbox = GetComponentInChildren<AttackHitbox>();
        }

        private void OnEnable()
        {
            EventBus.SubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
        }

        private void OnDisable()
        {
            EventBus.UnsubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
        }

        private void OnKnockback(KnockbackEvent e)
        {
            if (_stats == null || _stats.IsDead) return;
            _movement.ApplyKnockback(e.Direction, e.Force);
        }

        public void Init(MonsterConfig config, Vector3 spawnPoint)
        {
            _config = config;
            _spawnPoint = spawnPoint;

            PhysicsRegistry.Instance.Register(this, EntityType.Monster);
            EnsurePhysicsCollider();

            // Build systems bottom-up
            _stats = new MonsterStats(config);
            _movement = new MonsterMovement(NavAgent, transform, config);

            // Damage pipeline
            _damagePipeline = new DamagePipeline(config, _stats);
            _damagePipeline.AddModifier(new DefendModifier(config, transform));

            // Shared AI context (class — mutations persist across calls)
            _ctx = new AIContext
            {
                Self = transform,
                Animator = Animator,
                Stats = _stats,
                Movement = _movement,
                Config = config,
            };

            // Build states
            var patrolState = new PatrolState();
            var patrolRadius = RandomRange(config.PatrolRadius, config.PatrolRadiusVariance);
            patrolState.GeneratePatrolPoints(spawnPoint, patrolRadius);

            var hitState = new HitState(_damagePipeline);

            var states = new Dictionary<MonsterAIState, AIStateBase>
            {
                { MonsterAIState.Idle, new IdleState() },
                { MonsterAIState.Patrol, patrolState },
                { MonsterAIState.Chase, new ChaseState() },
                { MonsterAIState.Attack, new AttackState() },
                { MonsterAIState.Hit, hitState },
                { MonsterAIState.Death, new DeathState() },
                { MonsterAIState.Defend, new DefendState() },
                { MonsterAIState.Taunt, new TauntState() },
            };

            var fsm = new AIStateMachine(states, MonsterAIState.Idle);
            _brain = new AIBrain(_ctx, fsm, config, spawnPoint);

            // Wire hit zone
            if (HitZone != null) HitZone.Init(this);

            // Wire stats events → external systems
            _stats.OnHPChanged += (cur, max) =>
            {
                _onHPChanged?.Invoke(
                    max > 0 ? cur / max : 0f,
                    Mathf.CeilToInt(cur),
                    Mathf.CeilToInt(max));
            };
            _stats.OnDeath += () =>
            {
                _onDeath?.Invoke();
                HandleDeath();
            };

            // Wire AI events
            // (AttackHitbox activation handled by animation callbacks if needed)

            // Register nameplate
            var displayMgr = EntityDisplayManager.Instance;
            if (displayMgr != null && !string.IsNullOrEmpty(_config.DisplayName))
            {
                var cfg = _config.NameplateData != null
                    ? NameplateConfig.FromData(_config.NameplateData, _config.DisplayName)
                    : new NameplateConfig(_config.DisplayName);
                displayMgr.Register(GetInstanceID(), transform, cfg);
            }
        }

        private void Update()
        {
            if (_stats == null || _stats.IsDead || _brain == null) return;
            _brain.Update(Time.deltaTime);
        }

        // Safety net: unsubscribe even if OnDisable was skipped (e.g., DestroyImmediate in editor)
        private void OnDestroy()
        {
            EventBus.UnsubscribeTargeted<KnockbackEvent>(GetInstanceID(), OnKnockback);
            EntityDisplayManager.Instance?.Unregister(GetInstanceID());
            PhysicsRegistry.Instance?.Unregister(this);
        }

        // ── Damage Pipeline ──

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
        {
            if (_stats.IsDead) return;

            // Build damage context (struct — zero alloc)
            var ctx = new DamageContext
            {
                RawData = data,
                HitDirection = hitDirection,
                AttackerId = 0,
                Flags = data.WasCritical ? DamageFlags.IsCritical : DamageFlags.None,
            };

            // Run through pipeline (PreCheck → Gate → Apply)
            var result = _damagePipeline.Process(ref ctx);

            // Notify AI (may transition to Hit state)
            _brain.OnDamageReceived(result, hitDirection);

            // Post-damage events (VFX, floating text)
            EmitDamageEvents(data, hitDirection, result);

            // Knockback
            if (result.ShouldKnockback && data.KnockbackForce > 0)
            {
                EventBus.TargetedEmit(GetInstanceID(), new KnockbackEvent(
                    GetInstanceID(),
                    hitDirection,
                    data.KnockbackForce
                ));
            }
        }

        private void EmitDamageEvents(DamageBlock data, Vector3 hitDirection, DamageResult result)
        {
            var damageEvent = new MonsterTakeDamageEvent(
                GetInstanceID(),
                transform.position + Vector3.up * 1.2f,
                hitDirection,
                Mathf.CeilToInt(result.WasBlocked ? 0 : result.FinalDamage),
                data.WasCritical,
                data.SkillId,
                data.ComboIndex
            );
            EventBus.Emit(damageEvent);
            EventBus.TargetedEmit(GetInstanceID(), damageEvent);
        }

        private void HandleDeath()
        {
            if (_movement != null)
            {
                // Apply death knockback
                var dir = _brain.LastHitDirection;
                var force = _brain.LastKnockbackForce * _config.DeathKnockbackMultiplier;
                _movement.ApplyKnockback(dir, force);
                _movement.Stop();
            }

            _brain.EnterDeath();
            if (NavAgent != null) NavAgent.enabled = false;

            StartCoroutine(DeathSequence());
        }

        private IEnumerator DeathSequence()
        {
            // Wait for death animation to play
            yield return new WaitForSeconds(0.5f);

            var loot = _config.LootTable?.Roll();
            if (loot != null && loot.Count > 0)
            {
                var lootArr = loot.ToArray();
                OnLootDrop?.Invoke(lootArr);
                EventBus.Emit(new MonsterDeathEvent(_config.MonsterId, transform.position, lootArr));
            }

            yield return new WaitForSeconds(_config.DeathDestroyDelay);
            OnDeathComplete?.Invoke();
            Destroy(gameObject);
        }

        // ── Physics Collider ──

        private void EnsurePhysicsCollider()
        {
            var triggerCol = GetComponent<Collider>();
            if (triggerCol == null) return;

            var allColliders = GetComponents<Collider>();
            foreach (var c in allColliders)
            {
                if (!c.isTrigger) return;
            }

            if (triggerCol is CapsuleCollider capsule)
            {
                var physicsCol = gameObject.AddComponent<CapsuleCollider>();
                physicsCol.center = capsule.center;
                physicsCol.radius = capsule.radius;
                physicsCol.height = capsule.height;
                physicsCol.isTrigger = false;
            }
            else if (triggerCol is SphereCollider sphere)
            {
                var physicsCol = gameObject.AddComponent<SphereCollider>();
                physicsCol.center = sphere.center;
                physicsCol.radius = sphere.radius;
                physicsCol.isTrigger = false;
            }
            else if (triggerCol is BoxCollider box)
            {
                var physicsCol = gameObject.AddComponent<BoxCollider>();
                physicsCol.center = box.center;
                physicsCol.size = box.size;
                physicsCol.isTrigger = false;
            }
        }

        // ── Utility ──

        private static float RandomRange(float baseValue, float variance)
        {
            if (variance <= 0) return baseValue;
            return baseValue + UnityEngine.Random.Range(-variance, variance);
        }

        // ── ITargetable ──

        private event Action<float, int, int> _onHPChanged;
        private event Action _onDeath;

        event Action<float, int, int> ITargetable.OnHPChanged
        {
            add { _onHPChanged += value; }
            remove { _onHPChanged -= value; }
        }
        event Action ITargetable.OnDeath
        {
            add { _onDeath += value; }
            remove { _onDeath -= value; }
        }

        string ITargetable.DisplayName => _config != null ? _config.DisplayName : name;
        int ITargetable.Level => 1;
        Sprite ITargetable.Portrait => null;
        float ITargetable.HPPercent => _stats != null ? _stats.HP / _stats.MaxHP : 0f;
        int ITargetable.CurrentHP => _stats != null ? Mathf.CeilToInt(_stats.HP) : 0;
        int ITargetable.MaxHP => _stats != null ? Mathf.CeilToInt(_stats.MaxHP) : 0;
        Vector3 ITargetable.WorldPosition => transform.position;
        float ITargetable.SelectionRingYOffset => _config?.RingYOffset ?? 0f;

        // ── IEffectTarget ──

        IEffectStats IEffectTarget.Stats => null;
        IShieldSystem IEffectTarget.ShieldSystem => null;
        IPhysicsSystem IEffectTarget.PhysicsSystem => null;
        IStatusController IEffectTarget.StatusController => null;

        void IEffectTarget.Heal(float amount)
        {
            // Negative amount = damage. Route through pipeline for consistency.
            if (amount >= 0 || _stats == null || _stats.IsDead) return;

            float damage = -amount;
            var damageBlock = DamageBlock.CreateDefault(damage);
            ((IDamageable)this).TakeDamage(damageBlock, Vector3.zero);
        }
    }
}
```

- [ ] **Step 2: Verify compilation**

---

### Task 22: Remove Old Files + Update asmdef

**Files:**
- Delete: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAIContext.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Monster/IAIBehaviour.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Monster/DefendBehaviour.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Monster/TauntBehaviour.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Monster/AlertBehaviour.cs`
- Check: `Assets/Scripts/Hotfix/GameSystems/Monster/Monster.asmdef` (if it exists)

- [ ] **Step 1: Delete old source files**

```bash
rm "Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs.meta"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAIContext.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAIContext.cs.meta"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/IAIBehaviour.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/IAIBehaviour.cs.meta"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/DefendBehaviour.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/DefendBehaviour.cs.meta"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/TauntBehaviour.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/TauntBehaviour.cs.meta"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/AlertBehaviour.cs"
rm "Assets/Scripts/Hotfix/GameSystems/Monster/AlertBehaviour.cs.meta"
```

- [ ] **Step 2: Check if .asmdef exists and update if needed**

The git status shows that `Monster.asmdef` was deleted (D status). The new files are added to the existing Hotfix assembly. No `.asmdef` changes needed unless the old `.asmdef` was referencing removed types.

- [ ] **Step 3: Refresh Unity AssetDatabase**

Run `assets-refresh` skill to force Unity to recompile and detect new files.

---

### Task 23: Update MonsterConfig Assets

**Files:**
- Modify (via MCP): `Assets/Monstor/TurtleShellConfig.asset` — EnableDefend should be true
- Modify (via MCP): `Assets/Monstor/SlimeConfig.asset` — EnableTaunt should be true
- Modify (via MCP): `Assets/Monstor/DuoMonsterConfig.asset` — Verify fields
- Modify (via MCP): `Assets/Monstor/SwordShieldConfig.asset` — Verify fields

- [ ] **Step 1: Verify each MonsterConfig asset has new fields with default values**

The new fields (`AttackWindupTime`, `AttackRecoveryTime`, `HitReactDurations`, `HitIFrameDuration`) are serialized by Unity automatically with their C# defaults. No manual asset changes needed unless we want non-default values.

- [ ] **Step 2: Verify existing config EnableDefend/EnableTaunt flags**

Already set in the .asset files per the last commit history. No changes needed.

---

### Task 24: Build Verification

- [ ] **Step 1: Open Launch scene, check for compilation errors in Unity Console**

```bash
# Use console-get-logs to check for errors after Unity processes all new files
```

- [ ] **Step 2: Enter Play Mode and verify:**

1. Monster spawns correctly (no NullRef on Init)
2. Monster patrols between waypoints
3. Player enters DetectRange → monster transitions to Chase
4. Player in AttackRange → monster attacks (with windup delay)
5. Damage resolves after windup, not instantly
6. Player hits monster → Hit state with proper duration
7. TurtleShell monster enters Defend state when HP low
8. Defend blocks frontal damage (reduced damage taken)
9. Monster death → death animation → destroy after delay

---

## Dependency Order

```
Phase 1: Foundation
  T1 (DamageContext, DamageResult)
  T2 (IDamageModifier)
  T3 (MonsterAnimHashes)
  T4 (MonsterConfig fields)
    ↓
Phase 2: Damage Pipeline
  T5 (DefendModifier) → depends on T1, T2
  T6 (IFrameModifier) → depends on T1, T2
  T7 (DamagePipeline) → depends on T1, T2, T5, T6
    ↓
Phase 3: AI Core
  T8 (AIContext) → depends on T1
  T9 (AIStateBase) → depends on T8
  T10 (AIStateMachine) → depends on T9
    ↓
Phase 4: AI States (all depend on T9, can run in parallel)
  T11 (IdleState, PatrolState)
  T12 (ChaseState)
  T13 (AttackState)
  T14 (HitState) → depends on T7
  T15 (DeathState)
  T16 (DefendState)
  T17 (TauntState)
    ↓
Phase 5: Orchestrator
  T18 (AIBrain) → depends on T8, T10
    ↓
Phase 6: Integration
  T19 (MonsterMovement fixes)
  T20 (MonsterStats simplify)
  T21 (MonsterEntity rewire) → depends on ALL above
    ↓
Phase 7: Cleanup & Verify
  T22 (Delete old files)
  T23 (Config verification)
  T24 (Play Mode test)
```

---

## Self-Review Results

**Spec coverage check:**
- Damage pipeline (Section 1) → T1, T2, T5, T6, T7
- AIBrain + AIStateMachine (Section 2) → T8, T9, T10, T18
- Attack sub-state timing (Section 3) → T13
- Movement + Knockback (Section 4) → T19
- Target loss handling (Section 5) → each State's EvaluateTransitions
- AIContext design → T8
- IDamageModifier, DamageContext, DamageResult → T1, T2
- MonsterConfig additions → T4
- MonoBehaviour safety ([RequireComponent], OnDestroy) → T21
- Animator hash constants → T3
- Extensibility points → documented in code comments
- All 15 defects → each tracked to a specific task above

**Placeholder scan:** None found. All code is fully specified.

**Type consistency check:**
- `MonsterAIState` enum preserved (existing type)
- `EvaluateTransitions` returns `MonsterAIState?` consistently across all states
- `DamagePipeline.Process()` takes `ref DamageContext`, returns `DamageResult` — consistent
- `AIContext` field names match across all state files
- `MonsterAnimHashes` constants used everywhere, no raw string hashes
