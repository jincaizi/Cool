# Hit Feedback Unify & Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify normal-attack and skill damage paths through `MonsterEntity.TakeDamage` so all hit feedback (animation, flash, particles, knockback, float text) fires consistently.

**Architecture:** Both `HitZone.OnTriggerStay` (normal attacks) and `SkillExecutor.ApplyDamage` (skills) route through `IDamageable.TakeDamage(DamageBlock, hitDir)`. MonsterEntity.TakeDamage becomes the single point that triggers AI hit state, emits all VFX events, and emits knockback.

**Tech Stack:** Unity 2022.3 LTS, C# Hotfix layer, EventBus, DOTween

**Note on PhysicsSystemAdapter:** The spec's Task 5 (implement ApplyKnockback) is dead code — player knockback already works through HitFSM → CharacterController.ApplyHitDisplacement, and nobody calls PhysicsSystemAdapter.ApplyKnockback. Skipped.

---

### Task 1: Add SkillId and ComboIndex to DamageBlock

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs:53-56`

- [ ] **Step 1: Add runtime-only properties**

Add after the KnockbackForce block (line 56). These are runtime-only (not serialized) so SkillData assets don't need regeneration:

```csharp
        // Runtime skill context (not serialized — set by SkillExecutor before calling TakeDamage)
        [System.NonSerialized] public int SkillId;
        [System.NonSerialized] public int ComboIndex = 1;
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Data/DamageBlock.cs
git commit -m "feat(damage): add SkillId/ComboIndex runtime fields to DamageBlock"
```

---

### Task 2: Unify SkillExecutor to call IDamageable.TakeDamage

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs:191-248`

- [ ] **Step 1: Rewrite ApplyDamage to use IDamageable.TakeDamage**

Replace `ApplyDamage` (lines 371-383) with:

```csharp
        private void ApplyDamage(IEffectTarget target, int frameIndex)
        {
            var damageBlock = _skillData.Damage;
            if (damageBlock == null) return;

            float damage = damageBlock.CalculateFinalDamage(_owner.Stats);
            if (CurrentSubState == SkillSubState.Charging || CurrentSubState == SkillSubState.Execution)
                damage *= 1f + GetChargeProgress() * 0.5f;

            _lastDamageBlock = damageBlock;

            // Set runtime skill context so MonsterEntity.TakeDamage can emit
            // MonsterTakeDamageEvent with correct SkillId/ComboIndex
            damageBlock.SkillId = _skillData.SkillId;
            damageBlock.ComboIndex = frameIndex + 1;

            // Set knockback force from EffectBlock if present (overrides DamageBlock default)
            var effect = GetEffect();
            if (effect != null && effect.KnockbackForce > 0)
                damageBlock.KnockbackForce = effect.KnockbackForce;

            // Route through IDamageable for unified feedback path
            if (target is Sys3C.Core.Combat.IDamageable damageable)
            {
                Vector3 hitDir = (_owner.transform.position - target.transform.position).normalized;
                damageable.TakeDamage(damageBlock, hitDir);
            }
            else
            {
                target.Heal(-damage);
            }
        }
```

- [ ] **Step 2: Remove duplicate KnockbackEvent emission from OnHitboxTriggered**

In `OnHitboxTriggered` (lines 191-249), remove lines 204-213 (the knockback block) — this is now handled by MonsterEntity.TakeDamage:

```csharp
        private void OnHitboxTriggered(int frameIndex)
        {
            var targets = DetectTargets();

            foreach (var target in targets)
            {
                ApplyDamage(target, frameIndex);
                ApplyEffects(target);
                OnTargetHit?.Invoke(target);
            }

            OnHitboxFrame?.Invoke(frameIndex);

            if (targets.Count > 0)
            {
                var hitPos = targets[0].transform.position;
                foreach (var t in targets)
                {
                    EventBus.Emit(new SkillHitTargetEvent
                    {
                        SkillId = _skillData.SkillId,
                        CasterId = _owner.transform.GetInstanceID(),
                        HitPosition = hitPos,
                        IsFullCharge = _wasFullCharge
                    });
                }
            }
        }
```

Key change: removed the `MonsterTakeDamageEvent` emit block (was lines 236-246) since MonsterEntity.TakeDamage now emits it. Removed the `KnockbackEvent` emit block (was lines 204-213). Kept `SkillHitTargetEvent` — it's skill-specific, not damage feedback.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Skills/Runtime/SkillExecutor.cs
git commit -m "refactor(skill): route damage through IDamageable.TakeDamage for unified feedback"
```

---

### Task 3: Read SkillId/ComboIndex in MonsterEntity.TakeDamage

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs:161-189`

- [ ] **Step 1: Use DamageBlock fields for event emission**

Replace `TakeDamage` (lines 161-189) with version that reads SkillId/ComboIndex from DamageBlock:

```csharp
        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
        {
            if (_stats.IsDead) return;
            _stats.TakeDamage(data);
            _ai.NotifyHit(data, hitDirection);

            var damageEvent = new MonsterTakeDamageEvent(
                GetInstanceID(),
                transform.position + Vector3.up * 1.2f,
                hitDirection,
                Mathf.CeilToInt(data.BaseDamage),
                data.WasCritical,
                data.SkillId,
                data.ComboIndex
            );
            EventBus.Emit(damageEvent);
            EventBus.TargetedEmit(GetInstanceID(), damageEvent);

            if (data.KnockbackForce > 0)
            {
                EventBus.TargetedEmit(GetInstanceID(), new KnockbackEvent(
                    GetInstanceID(),
                    hitDirection,
                    data.KnockbackForce
                ));
            }
        }
```

The only changes from the original: `0` → `data.SkillId` and `1` → `data.ComboIndex` in the MonsterTakeDamageEvent constructor.

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "feat(monster): forward DamageBlock SkillId/ComboIndex to MonsterTakeDamageEvent"
```

---

### Task 4: Fix PlayerHitZone — read KnockbackForce from AttackHitboxData

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Combat/PlayerHitZone.cs:22-38`

- [ ] **Step 1: Replace hardcoded knockbackForce**

Replace the hardcoded `knockbackForce: 1f` with value from hitbox data:

```csharp
        private void OnTriggerStay(Collider other)
        {
            var hitbox = other.GetComponent<IAttackHitbox>();
            if (hitbox == null || !hitbox.IsActive || _hitSources.Contains(hitbox)) return;

            _hitSources.Add(hitbox);

            if (_fsmManager == null) return;

            var hitboxData = hitbox.CurrentData;
            float damage = hitboxData != null && hitboxData.DamageData != null
                ? hitboxData.DamageData.CalculateFinalDamage(null)
                : 10f;

            float knockbackForce = hitboxData != null ? hitboxData.KnockbackForce : 0f;

            Vector3 hitDir = (transform.position - hitbox.GetBounds().center).normalized;
            _fsmManager.HandleDamage(
                sourceId: -1,
                damage: damage,
                hitDirection: hitDir,
                knockbackForce: knockbackForce,
                launchForce: 0,
                stunDuration: 0,
                isCritical: false
            );
        }
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Combat/PlayerHitZone.cs
git commit -m "fix(combat): read KnockbackForce from AttackHitboxData instead of hardcoded 1f"
```

---

### Task 5: Fix HitFlashVFX color on SlimePBR prefab

**Files:**
- Modify: `Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab` (HitFlashVFX._flashColor)

- [ ] **Step 1: Open SlimePBR prefab and fix flash color**

Use MCP tools (must have Unity Editor running with MCP bridge):

```
gameobject-component-modify → find SlimePBR's HitFlashVFX component
→ set _flashColor to red (1, 0, 0, 1)
```

The TurtleShellPBR already has correct red flash `(1, 0.1, 0.1, 1)` — no change needed.

- [ ] **Step 2: Commit**

```bash
git add Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab
git commit -m "fix(prefab): fix SlimePBR HitFlashVFX color from white to red"
```

---

### Task 6: Enable and fix HitParticleController on monster prefabs

**Files:**
- Modify: `Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab` (HitParticleController)
- Modify: `Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/TurtleShellPBR.prefab` (HitParticleController)

- [ ] **Step 1: Fix SlimePBR HitParticleController**

Using MCP tools:
1. Enable the HitParticleController component
2. Set `_normalHitParticles` → `Assets/Prefabs/VFX/BloodSplatterNormal.prefab`
3. Set `_criticalHitParticles` → `Assets/Prefabs/VFX/BloodSplatterCritical.prefab`
4. Set `_profile` → `Assets/Data/HitFeedbackProfile.asset`

- [ ] **Step 2: Fix TurtleShellPBR HitParticleController**

Same as Step 1 for TurtleShellPBR.

- [ ] **Step 3: Commit**

```bash
git add Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/SlimePBR.prefab
git add Assets/Monstor/DuoPolyart/Prefabs/PBRDefault/TurtleShellPBR.prefab
git commit -m "fix(prefab): enable HitParticleController and assign blood splatter prefabs"
```

---

## Verification

After all tasks, verify in Unity Editor Play mode:

1. **Normal attack monster** → hit animation plays, red flash, blood particles at hit point, damage float text
2. **Skill attack monster** → same feedback + knockback displacement (if KnockbackForce > 0)
3. **Monster attacks player** → player takes damage + knockback displacement (if KnockbackForce set on monster config)
4. **Kill a monster** → death sequence plays correctly
