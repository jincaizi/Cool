# Hit Feedback Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix knockback, event data completeness, and HitZone propagation so hit feedback effects (knockback, VFX, camera shake) actually work in-game.

**Architecture:** Event-driven knockback via new `KnockbackEvent`. `MonsterEntity` subscribes and forwards to `MonsterMovement`. HitZone propagates `AttackHitboxData.KnockbackForce` to `DamageBlock` before calling `TakeDamage`.

**Tech Stack:** Unity 2022 LTS, C#, EventBus (existing)

---

### Task 1: Add KnockbackEvent to DamageEvents.cs

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs:76`

- [ ] **Step 1: Add KnockbackEvent struct**

Add before the closing `}` of the namespace (after line 75):

```csharp
    /// <summary>
    /// 击退事件
    /// </summary>
    public struct KnockbackEvent : IEvent
    {
        public int EntityId;
        public Vector3 Direction;
        public float Force;

        public KnockbackEvent(int entityId, Vector3 direction, float force)
        {
            EntityId = entityId;
            Direction = direction;
            Force = force;
        }
    }
```

- [ ] **Step 2: Verify compilation**

Run: Unity Editor compilation or check for errors in the file. No errors expected since `IEvent` is already imported via `using Hotfix.GameSystems.Skills.Events;` and `Vector3` via `using UnityEngine;`.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Events/DamageEvents.cs
git commit -m "feat(combat): add KnockbackEvent struct to DamageEvents"
```

---

### Task 2: Fix MonsterEntity — Emit KnockbackEvent + Fix Event Params

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs:145-158`

- [ ] **Step 1: Update TakeDamage to emit KnockbackEvent and fix MonsterTakeDamageEvent params**

Replace the `TakeDamage` method (lines 145-158) with:

```csharp
        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
        {
            if (_stats.IsDead) return;
            _stats.TakeDamage(data);
            _ai.NotifyHit(data, hitDirection);

            // Emit monster damage event for floating text + VFX
            EventBus.Emit(new MonsterTakeDamageEvent(
                GetInstanceID(),
                transform.position + Vector3.up * 2f,
                hitDirection,
                Mathf.CeilToInt(data.BaseDamage),
                data.WasCritical,
                0,  // skillId = 0 for normal attacks
                1   // comboIndex = 1
            ));

            // Emit knockback event
            if (data.KnockbackForce > 0)
            {
                EventBus.Emit(new KnockbackEvent(
                    GetInstanceID(),
                    hitDirection,
                    data.KnockbackForce
                ));
            }
        }
```

- [ ] **Step 2: Verify compilation**

Check for errors. `KnockbackEvent` should resolve from the same namespace (`Hotfix.GameSystems.Sys3C.Core.Events`) already imported.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "fix(combat): emit KnockbackEvent from TakeDamage, fix MonsterTakeDamageEvent params"
```

---

### Task 3: MonsterEntity Subscribe to KnockbackEvent

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 1: Add OnEnable/OnDisable for KnockbackEvent subscription**

The file currently has no `OnEnable`/`OnDisable`. Add them after the `Awake()` method (after line 42):

```csharp
        private void OnEnable()
        {
            EventBus.Subscribe<KnockbackEvent>(OnKnockback);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<KnockbackEvent>(OnKnockback);
        }

        private void OnKnockback(KnockbackEvent e)
        {
            if (_stats == null || _stats.IsDead) return;
            if (e.EntityId != GetInstanceID()) return;
            _movement.ApplyKnockback(e.Direction, e.Force);
        }
```

- [ ] **Step 2: Verify compilation**

Check for errors. `_movement` is already a field on `MonsterEntity` (line 27).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "feat(monster): subscribe to KnockbackEvent in MonsterEntity"
```

---

### Task 4: Remove Direct Knockback from MonsterAI.NotifyHit

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs:170`

- [ ] **Step 1: Remove _movement.ApplyKnockback from NotifyHit**

In `NotifyHit` method, remove line 170:
```csharp
            _movement.ApplyKnockback(hitDirection, _lastKnockbackForce);
```

The method should now be:
```csharp
        public void NotifyHit(DamageBlock damageData, Vector3 hitDirection)
        {
            if (_state == MonsterAIState.Death) return;

            _lastHitDirection = hitDirection;
            _lastKnockbackForce = damageData?.KnockbackForce ?? 0f;

            // Defend: front absorbs, behind interrupts with knockback
            if (_state == MonsterAIState.Defend && _defend != null)
            {
                float angle = Vector3.Angle(_self.forward, -hitDirection);
                if (angle < _config.DefendAngle * 0.5f)
                {
                    var ctx = BuildContext();
                    ctx.DefendBlockCount++;
                    _animator.SetTrigger(HASH_Hit);
                    return;
                }
            }

            _preHitState = _state == MonsterAIState.Hit ? _preHitState : _state;

            _movement.Stop();
            _stateTimer = 0.3f;
            TransitionTo(MonsterAIState.Hit);
            _animator.SetTrigger(HASH_Hit);
        }
```

Note: `_lastKnockbackForce` is still stored because `HandleDeath()` in `MonsterEntity` uses `_ai.LastKnockbackForce` for death knockback. The `KnockbackEvent` subscriber in `MonsterEntity.OnKnockback()` handles the actual physics knockback now.

- [ ] **Step 2: Verify compilation**

Check for errors. No new imports needed.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs
git commit -m "refactor(monster): remove direct ApplyKnockback from NotifyHit, now event-driven"
```

---

### Task 5: Fix HitZone KnockbackForce Propagation

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Combat/HitZone.cs:30-43`

- [ ] **Step 1: Update OnTriggerStay to propagate KnockbackForce**

Replace `OnTriggerStay` method (lines 30-43):

```csharp
        private void OnTriggerStay(Collider other)
        {
            var hitbox = other.GetComponent<IAttackHitbox>();
            if (hitbox == null || !hitbox.IsActive) return;
            if (!_hitInstanceIds.Add(hitbox.GetInstanceID())) return;

            Vector3 hitDir = (transform.position - hitbox.GetBounds().center).normalized;

            var data = hitbox.CurrentData;
            if (data != null && data.DamageData != null)
            {
                data.DamageData.KnockbackForce = data.KnockbackForce;
                _owner?.TakeDamage(data.DamageData, hitDir);
            }
        }
```

The key change is adding `data.DamageData.KnockbackForce = data.KnockbackForce;` before `TakeDamage`.

- [ ] **Step 2: Verify compilation**

Check for errors. `DamageBlock.KnockbackForce` has a public setter (line 56 of DamageBlock.cs).

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Combat/HitZone.cs
git commit -m "fix(combat): propagate AttackHitboxData.KnockbackForce to DamageBlock in HitZone"
```

---

### Task 6: Editor — Set Weapon KnockbackForce + Assign VFX Prefabs

**Files:**
- Modify (via Unity Editor): `Assets/Data/` weapon config assets
- Modify (via Unity Editor): Scene — HitParticleController component

This task cannot be done via code. It requires Unity Editor.

- [ ] **Step 1: Set KnockbackForce on weapon DamageBlock**

In Unity Editor:
1. Find the weapon config asset (e.g., `SwordShieldConfig.asset`) in `Assets/Data/`
2. Select it, in Inspector find `Damage` → `Knockback Force`
3. Set value to `5`

- [ ] **Step 2: Assign VFX prefabs on HitParticleController**

In Unity Editor:
1. Find the GameObject with `HitParticleController` in the scene
2. In Inspector, assign:
   - `_normalHitParticles` → `Assets/Prefabs/VFX/BloodSplatterNormal.prefab`
   - `_criticalHitParticles` → `Assets/Prefabs/VFX/BloodSplatterCritical.prefab`
   - `_hitShockwavePrefab` → `Assets/Prefabs/VFX/HitShockwave.prefab`
   - `_hitSparkBurstPrefab` → `Assets/Prefabs/VFX/HitSparkBurst.prefab`
   - `_slashBloodTrailPrefab` → `Assets/Prefabs/VFX/SlashBloodTrail.prefab`
   - `_profile` → `Assets/Data/HitFeedbackProfile.asset`

- [ ] **Step 3: Verify HitStopManager and CameraShakeManager are in scene**

Ensure these GameObjects exist in the scene with:
- `HitStopManager` — `_profile` assigned, `_playerAnimator` assigned to player's Animator
- `CameraShakeManager` — `_profile` assigned, `_camera` left empty (auto-finds Camera.main)

- [ ] **Step 4: Save scene**

Save the scene after making changes.

---

### Task 7: Verify — Play Test

- [ ] **Step 1: Enter Play mode in Unity Editor**

- [ ] **Step 2: Test normal attack knockback**

Walk up to a monster, left-click attack. Verify:
- Monster plays hit animation
- Monster is knocked back (moves away from player)
- Blood particles spawn at hit position
- Camera shakes briefly
- Hit flash (outline) appears on monster

- [ ] **Step 3: Test skill attack knockback**

Use skill Q or R. Verify:
- Same as above but with larger particles (skill scale)
- Shockwave ring appears on crit/skill hits
- HitStop is slightly longer

- [ ] **Step 4: Test crit**

Land a critical hit. Verify:
- Larger particle scale
- Camera shake is stronger
- Brief time slow effect
- Slash blood trail appears
