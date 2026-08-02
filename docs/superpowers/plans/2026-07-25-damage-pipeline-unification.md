# Damage Pipeline Unification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify player and monster damage handling through a shared DamagePipeline, add proper death determination, and fix defend to grant full immunity.

**Architecture:** DamagePipeline moves from Monster namespace to Sys3C.Core.Combat as shared infrastructure. Both MonsterStats and Sys3CEntry implement new IDamageReceiver interface. Pipeline handles damage processing + death detection; OnDamageProcessed callback handles entity-specific post-processing (shield durability, HitFSM routing).

**Tech Stack:** Unity 2022.3, C# Hotfix layer, no new external dependencies.

---

## File Structure

```
Sys3C/Core/Combat/
├── DamagePipeline.cs        (moved from Monster/Damage/, refactored)
├── DamageContext.cs         (moved from Monster/Damage/)
├── DamageResult.cs          (moved from Monster/Damage/, add Death enum)
├── IDamageModifier.cs       (moved from Monster/Damage/)
├── IDamageReceiver.cs       (NEW)
└── Modifiers/
    ├── DefendModifier.cs    (moved, modified)
    └── IFrameModifier.cs    (moved from Monster/Damage/)

Monster/
├── MonsterStats.cs          (implement IDamageReceiver, delete old TakeDamage)
├── MonsterEntity.cs         (adapt constructor)
└── AI/HitState.cs           (unchanged)

Sys3C/
├── Sys3CEntry.cs            (implement IDamageReceiver, replace TakeDamage)
└── FSM/HitFSM.cs            (delete 9999 death logic)
```

---

### Task 1: Add `Death = 5` to `HitReactLevel` enum

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamageResult.cs:18`

- [ ] **Step 1: Add Death value**

Replace line 18 `Launch = 4,` with:

```csharp
        Launch = 4,     // Airborne (reserved for future launcher skills)
        Death = 5,      // Fatal damage — triggers death state
```

- [ ] **Step 2: Refresh and verify no compilation errors**

Use `mcp__ai-game-developer__assets-refresh` with `ForceSynchronousImport`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamageResult.cs
git commit -m "feat: add Death=5 to HitReactLevel enum"
```

---

### Task 2: Create `IDamageReceiver` interface

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageReceiver.cs`

- [ ] **Step 1: Write the interface file**

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// Entity-side damage handling contract.
    /// DamagePipeline calls ApplyDamage for HP subtraction,
    /// then OnDamageProcessed for entity-specific post-processing.
    /// </summary>
    public interface IDamageReceiver
    {
        bool IsDead { get; }
        void ApplyDamage(float damage);
        void OnDamageProcessed(DamageResult result, ref DamageContext ctx);
    }
}
```

- [ ] **Step 2: Refresh to compile the new file**

Use `mcp__ai-game-developer__assets-refresh` with `ForceSynchronousImport`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageReceiver.cs
git commit -m "feat: add IDamageReceiver interface for unified damage handling"
```

---

### Task 3: Move 6 damage system files to shared location

**Files:**
- Move: `Monster/Damage/DamagePipeline.cs` → `Sys3C/Core/Combat/DamagePipeline.cs`
- Move: `Monster/Damage/DamageContext.cs` → `Sys3C/Core/Combat/DamageContext.cs`
- Move: `Monster/Damage/DamageResult.cs` → `Sys3C/Core/Combat/DamageResult.cs`
- Move: `Monster/Damage/IDamageModifier.cs` → `Sys3C/Core/Combat/IDamageModifier.cs`
- Move: `Monster/Damage/Modifiers/DefendModifier.cs` → `Sys3C/Core/Combat/Modifiers/DefendModifier.cs`
- Move: `Monster/Damage/Modifiers/IFrameModifier.cs` → `Sys3C/Core/Combat/Modifiers/IFrameModifier.cs`

For each file: write the new location with namespace changed, then delete the old.

- [ ] **Step 1: Move DamageResult.cs**

Read the current file, change namespace from `Hotfix.GameSystems.Monster` to `Hotfix.GameSystems.Sys3C.Core.Combat`, write to new path:

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Combat
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
        Death = 5,      // Fatal damage — triggers death state
    }
}
```

Delete old file: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamageResult.cs`

- [ ] **Step 2: Move DamageContext.cs**

Read the current file, change namespace to `Hotfix.GameSystems.Sys3C.Core.Combat`, write to `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/DamageContext.cs`.

Delete old file: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamageContext.cs`

- [ ] **Step 3: Move IDamageModifier.cs**

Read the current file, change namespace to `Hotfix.GameSystems.Sys3C.Core.Combat`, write to `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IDamageModifier.cs`.

Delete old file: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/IDamageModifier.cs`

- [ ] **Step 4: Move IFrameModifier.cs**

Read the current file, change namespace to `Hotfix.GameSystems.Sys3C.Core.Combat`, write to `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/Modifiers/IFrameModifier.cs`.

Delete old file: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/Modifiers/IFrameModifier.cs`

- [ ] **Step 5: Move DamagePipeline.cs** (raw move, refactoring in Task 4)

Read the current file, change namespace to `Hotfix.GameSystems.Sys3C.Core.Combat`, add `using Hotfix.GameSystems.Skills.Data;` if not present, write to `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/DamagePipeline.cs`.

Delete old file: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/DamagePipeline.cs`

- [ ] **Step 6: Move DefendModifier.cs** (raw move, refactoring in Task 5)

Read the current file, change namespace to `Hotfix.GameSystems.Sys3C.Core.Combat`, write to `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/Modifiers/DefendModifier.cs`.

Delete old file: `Assets/Scripts/Hotfix/GameSystems/Monster/Damage/Modifiers/DefendModifier.cs`

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/
git add Assets/Scripts/Hotfix/GameSystems/Monster/Damage/
git commit -m "refactor: move DamagePipeline types to Sys3C.Core.Combat shared layer"
```

> **Note:** Project will have compilation errors after this task due to namespace changes. Tasks 4-11 resolve them.

---

### Task 4: Refactor DamagePipeline

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/DamagePipeline.cs`

- [ ] **Step 1: Rewrite DamagePipeline with new constructor, merge fix, and death detection**

Overwrite the file with:

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    // Central damage processing pipeline.
    //
    // Flow: Modifier Chain → Gate Check → ApplyDamage → Death Check → OnDamageProcessed
    //
    // Modifiers are sorted by priority (lowest first) and each gets a chance to modify
    // ctx.CurrentDamage. WasBlocked stops the chain immediately.
    //
    // After modifiers: if not blocked, ApplyDamage is called, then IsDead is checked.
    // OnDamageProcessed gives the receiver a hook for entity-specific post-processing.
    //
    // To register additional modifiers: call AddModifier() during initialization.
    public class DamagePipeline
    {
        private readonly IDamageReceiver _receiver;
        private readonly List<IDamageModifier> _modifiers = new List<IDamageModifier>();
        private readonly IFrameModifier _iFrameModifier;
        private bool _sorted = true;

        public DamagePipeline(IDamageReceiver receiver)
        {
            _receiver = receiver;
            _iFrameModifier = new IFrameModifier();
            _modifiers.Add(_iFrameModifier);
        }

        // Add a modifier. Called during initialization to register defense types.
        // Modifiers are sorted lazily — only when the list changes, not on every Process call.
        public void AddModifier(IDamageModifier modifier)
        {
            _modifiers.Add(modifier);
            _sorted = false;
        }

        // Called by HitState.OnEnter and HitState.OnExit (monster) or EnableIFrame (player).
        public void SetIFrameActive(bool active)
        {
            _iFrameModifier.Active = active;
        }

        // Entry point called from IDamageable.TakeDamage.
        // Returns the merged result — caller uses HitReactLevel and ShouldKnockback
        // to drive AI state transitions or knockback events.
        public DamageResult Process(ref DamageContext ctx)
        {
            ctx.CurrentDamage = ctx.RawDamage;

            if (!_sorted)
            {
                _modifiers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
                _sorted = true;
            }

            var merged = new DamageResult
            {
                FinalDamage = ctx.CurrentDamage,
                ShouldKnockback = true,
                ReactLevel = HitReactLevel.Flinch
            };

            foreach (var modifier in _modifiers)
            {
                var result = modifier.Modify(ref ctx);
                ctx.CurrentDamage = result.FinalDamage;

                merged.ShouldKnockback = result.ShouldKnockback;
                merged.ReactLevel = result.ReactLevel;
                merged.WasReduced = result.WasReduced;

                if (result.WasBlocked)
                {
                    merged.WasBlocked = true;
                    merged.FinalDamage = 0;
                    break;
                }
            }

            merged.FinalDamage = ctx.CurrentDamage;

            // Apply damage if not blocked
            if (!merged.WasBlocked && !_receiver.IsDead)
            {
                _receiver.ApplyDamage(merged.FinalDamage);

                // Unified death detection
                if (_receiver.IsDead)
                {
                    merged.ReactLevel = HitReactLevel.Death;
                    merged.ShouldKnockback = false;
                }
            }

            _receiver.OnDamageProcessed(merged, ref ctx);

            return merged;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/DamagePipeline.cs
git commit -m "refactor: DamagePipeline — IDamageReceiver, merge fix, unified death detection"
```

---

### Task 5: Update DefendModifier to full block

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/Modifiers/DefendModifier.cs`

- [ ] **Step 1: Rewrite DefendModifier — WasReduced→WasBlocked, delete DamageReduction**

Overwrite the file with:

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    // Configuration values for defend behavior.
    public struct DefendConfig
    {
        public float DefendAngle;      // full-angle in degrees for frontal block check

        public static DefendConfig Default => new DefendConfig
        {
            DefendAngle = 160f
        };
    }

    // Full block of frontal attacks during defend state.
    // Priority=100: runs after invincibility checks.
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

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/Modifiers/DefendModifier.cs
git commit -m "refactor: DefendModifier — full block (WasBlocked), remove DamageReduction"
```

---

### Task 6: MonsterStats implement IDamageReceiver

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterStats.cs`

- [ ] **Step 1: Update MonsterStats**

Add `using Hotfix.GameSystems.Sys3C.Core.Combat;`, implement `IDamageReceiver`, delete old `TakeDamage(DamageBlock)`.

Read the current file, then apply these changes:

1. Add using at top after existing usings:
```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
```

2. Change class declaration:
```csharp
    public class MonsterStats : IDamageReceiver
```

3. Change `ApplyDamage` to match interface (keep existing body):
```csharp
        public void ApplyDamage(float damage)
```

4. Add interface members after `ApplyDamage`:
```csharp
        bool IDamageReceiver.IsDead => IsDead;

        void IDamageReceiver.OnDamageProcessed(DamageResult result, ref DamageContext ctx)
        {
            // Monster death is handled by the OnDeath event fired in ApplyDamage.
            // No additional post-processing needed at the monster level.
        }
```

5. Delete the old `TakeDamage(DamageBlock damageData)` method (lines 51-66 in the original file).

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterStats.cs
git commit -m "refactor: MonsterStats implement IDamageReceiver, remove legacy TakeDamage"
```

---

### Task 7: MonsterEntity adapt to new DamagePipeline constructor

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 1: Update Init method**

In `Init()`, line 88, change:
```csharp
            _damagePipeline = new DamagePipeline(config, _stats);
```
To:
```csharp
            _damagePipeline = new DamagePipeline(_stats);
```

And update usings at top — replace `using Hotfix.GameSystems.Monster;` (if present for damage types) with:
```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
```

The DefendConfig construction (lines 89-94) stays the same since it now only has `DefendAngle`. Remove the `DamageReduction` line if present:
```csharp
            var defendCfg = new DefendConfig
            {
                DefendAngle = config.DefendAngle
            };
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "refactor: MonsterEntity adapt to new DamagePipeline(IDamageReceiver)"
```

---

### Task 8: Sys3CEntry — pipeline initialization + IDamageReceiver + new TakeDamage

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`

This task replaces the 80-line inline TakeDamage with pipeline-driven code. Changes are in three areas: fields/init, TakeDamage replacement, and new methods.

- [ ] **Step 1: Add field for DamagePipeline**

Replace the `_defendModifier` and `_defendConfig` fields (lines 45-46) with:
```csharp
        private DefendConfig _defendConfig = DefendConfig.Default;
        private DamagePipeline _damagePipeline;
```

- [ ] **Step 2: Initialize pipeline in Start()**

After the existing `_defendModifier = new DefendModifier(...)` line (current line 67), add:
```csharp
            _damagePipeline = new DamagePipeline(this);
            _damagePipeline.AddModifier(new DefendModifier(_defendConfig, transform, () => _cc.IsDefending));
```

And delete the old `_defendModifier = new DefendModifier(...)` line — the DefendModifier is now registered through the pipeline.

The block at current lines 64-69 becomes:
```csharp
            _cc = new Hotfix.GameSystems.Sys3C.Character.CharacterController(
                transform, CharacterController, GroundLayer);

            _damagePipeline = new DamagePipeline(this);
            _damagePipeline.AddModifier(new DefendModifier(_defendConfig, transform, () => _cc.IsDefending));

            _fsmManager = new FSMManager(_cc, Animator);
```

- [ ] **Step 3: Replace IDamageable.TakeDamage implementation**

Replace lines 325-404 (the entire `void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)` method) with:

```csharp
        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
        {
            if (_currentHP <= 0) return;

            var ctx = new DamageContext
            {
                RawData = data,
                HitDirection = hitDirection,
                OverrideDamage = data?.CalculatedDamage ?? 0f,
                Flags = (data != null && data.WasCritical) ? DamageFlags.IsCritical : DamageFlags.None,
            };

            _damagePipeline.Process(ref ctx);
        }
```

- [ ] **Step 4: Add IDamageReceiver implementation**

Add after the TakeDamage method (the closing `}` at line 404 in original):

```csharp
        bool IDamageReceiver.IsDead => _currentHP <= 0;

        void IDamageReceiver.ApplyDamage(float damage)
        {
            _currentHP -= damage;
        }

        void IDamageReceiver.OnDamageProcessed(DamageResult result, ref DamageContext ctx)
        {
            // 1 — Defense penetration: exit defend on back attack
            if (_cc.IsDefending && !result.WasBlocked)
            {
                _cc.TryExitDefend();
                _fsmManager.ExitDefend();
            }

            // 2 — Successful block: consume shield durability
            if (_cc.IsDefending && result.WasBlocked)
            {
                bool broken = _cc.AbsorbDamage(ctx.RawDamage);
                if (broken)
                {
                    _cc.OnShieldBreak();
                    _fsmManager.HandleShieldBreak(new HitData
                    {
                        Damage = result.FinalDamage,
                        HitDirection = ctx.HitDirection
                    });
                    return;
                }

                _fsmManager.HitFSM.EnterDefendHit(new HitData
                {
                    Damage = result.FinalDamage,
                    HitDirection = ctx.HitDirection
                });
                _fsmManager.Coordinator.SetActiveLayer(LayerType.Hit);
                return;
            }

            // 3 — Death
            if (result.ReactLevel == HitReactLevel.Death)
            {
                _fsmManager.HandleDeath();
                _fsmManager.HitFSM.EnterDeath();
                return;
            }

            // 4 — Normal hit routing
            if (!result.WasBlocked)
            {
                _fsmManager.HandleDamage(
                    sourceId: -1,
                    damage: result.FinalDamage,
                    hitDirection: ctx.HitDirection,
                    knockbackForce: ctx.RawData?.KnockbackForce ?? 0
                );
            }
        }
```

- [ ] **Step 5: Add EnableIFrame method**

Add after OnDamageProcessed:

```csharp
        /// <summary>
        /// Enable temporary invincibility frames. Called by shield skill or buff system.
        /// </summary>
        public void EnableIFrame(float duration)
        {
            _damagePipeline.SetIFrameActive(true);
            StartCoroutine(Co_DisableIFrame(duration));
        }

        private System.Collections.IEnumerator Co_DisableIFrame(float duration)
        {
            yield return new UnityEngine.WaitForSeconds(duration);
            _damagePipeline.SetIFrameActive(false);
        }
```

- [ ] **Step 6: Update usings at top of file**

Replace `using Hotfix.GameSystems.Monster;` with `using Hotfix.GameSystems.Sys3C.Core.Combat;`

Add if not present:
```csharp
using Hotfix.GameSystems.Sys3C.Core;
```

- [ ] **Step 7: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs
git commit -m "refactor: Sys3CEntry — DamagePipeline, IDamageReceiver, EnableIFrame"
```

---

### Task 9: HitFSM — remove magic number 9999 death logic

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs`

- [ ] **Step 1: Remove 9999 death check from EnterHit**

In `EnterHit()`, change lines 212-214 from:
```csharp
            if (hitData.Damage >= 9999) // 致命伤害
            {
                targetState = HitState.Death;
            }
            else if (hitData.LaunchForce > 0)
```
To:
```csharp
            if (hitData.LaunchForce > 0)
```

- [ ] **Step 2: Remove 9999 priority from GetPriorityForHitData**

In `GetPriorityForHitData()`, change lines 332 from:
```csharp
            if (data.Damage >= 9999) return 100;
            if (data.LaunchForce > 0) return 80;
```
To:
```csharp
            if (data.LaunchForce > 0) return 80;
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/HitFSM.cs
git commit -m "refactor: remove magic number 9999 death check from HitFSM"
```

---

### Task 10: Update remaining using directives

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIContext.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/HitState.cs`

- [ ] **Step 1: Update AIBrain.cs**

Replace `using Hotfix.GameSystems.Monster;` (if present for DamageResult/HitReactLevel types) with:
```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
```

Check `OnDamageReceived` method references `DamageResult` and `HitReactLevel` — these are now in the new namespace.

- [ ] **Step 2: Update AIContext.cs**

Replace `using Hotfix.GameSystems.Monster;` with:
```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
```

The `LastHitResult` field type `DamageResult` resolves through the new namespace.

- [ ] **Step 3: Update HitState.cs** (Monster AI)

Replace `using Hotfix.GameSystems.Monster;` with:
```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
```

The `DamagePipeline` type resolves through the new namespace.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIBrain.cs
git add Assets/Scripts/Hotfix/GameSystems/Monster/AI/AIContext.cs
git add Assets/Scripts/Hotfix/GameSystems/Monster/AI/States/HitState.cs
git commit -m "refactor: update AI using directives to Sys3C.Core.Combat"
```

---

### Task 11: Full compile verification

- [ ] **Step 1: Refresh AssetDatabase**

Use `mcp__ai-game-developer__assets-refresh` with `ForceSynchronousImport`.

- [ ] **Step 2: Check console for compilation errors**

Use `mcp__ai-game-developer__console-get-logs` with `logTypeFilter: "Error"`.

Expected: zero errors. If any errors appear, they will be namespace resolution issues — check that the file referenced has the correct `using` directive.

- [ ] **Step 3: Run EditMode tests** (if any exist for combat/damage)

Use `mcp__ai-game-developer__tests-run` with `testMode: "EditMode"`.

- [ ] **Step 4: Commit if any cleanup was needed**

If no changes: skip. If small fixes were needed:
```bash
git add -u
git commit -m "fix: compilation fixes after damage pipeline unification"
```

---

## Verification Checklist

After all tasks complete, verify:

1. `[ ]` Monster takes damage via pipeline and dies correctly (HP→0 → OnDeath → DeathState)
2. `[ ]` Player takes full damage when not defending (HP reduction + HitFSM hit reaction)
3. `[ ]` Player defending + frontal hit → full block, durability consumed, DefendHit animation
4. `[ ]` Player defending + back hit → full damage, exit defend state, normal hit reaction
5. `[ ]` Player shield breaks → OnShieldBreak + HandleShieldBreak (stunned) + damage penetrates
6. `[ ]` Player HP→0 → HandleDeath + HitFSM.EnterDeath → death animation plays
7. `[ ]` Player iFrame active → all damage blocked, no durability consumed (unless also defending)
8. `[ ]` Monster iFrame active (HitState) → all damage blocked
9. `[ ]` Dead entities reject further damage (guard clauses)
