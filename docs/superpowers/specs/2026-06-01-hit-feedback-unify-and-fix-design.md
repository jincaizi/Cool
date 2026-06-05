# Hit Feedback: Unify Damage Path & Fix Missing Effects

## Overview

Unify normal attack and skill damage paths through `IDamageable.TakeDamage`, fix broken
HitFlashVFX / HitParticleController on monster prefabs, and implement player knockback.

## Current State — Two Divergent Paths

| Path | Trigger | AI NotifyHit | Hit Anim | Damage Events | Knockback |
|------|---------|:---:|:---:|:---:|:---:|
| Normal attack | HitZone.OnTriggerStay | Yes | Yes | Yes | Yes |
| Skill | SkillExecutor.OnHitboxTriggered | **No** | **No** | Yes | Yes (events) |

Skill hits skip `MonsterAI.NotifyHit` — no hit animation, no AI state transition.

## Target Architecture — Single Entry Point

```
AttackHitbox → HitZone.OnTriggerStay ──┐
SkillExecutor.ApplyDamage ────────────┘
                      │
                      ▼
        IDamageable.TakeDamage(DamageBlock, hitDir)
                      │
              MonsterEntity.TakeDamage
                      │
        ┌─────────────┼─────────────┐
        ▼             ▼             ▼
   _stats.TakeDamage  _ai.NotifyHit  Emit events
   (HP change)       (Hit state,    (VFX, float text,
                      hit anim)      knockback)
```

All feedback components subscribe to `MonsterTakeDamageEvent` and `KnockbackEvent`
independently. No central orchestrator.

## Changes

### 1. Unify damage entry (SkillExecutor.cs)

`ApplyDamage` currently calls `target.Heal(-damage)` then manually emits events in
`OnHitboxTriggered`. Instead:

- Detect `IDamageable` on target, call `TakeDamage(DamageBlock, hitDir)` directly
- Remove duplicate `MonsterTakeDamageEvent` and `KnockbackEvent` emission from
  `OnHitboxTriggered` — `MonsterEntity.TakeDamage` already emits both
- Keep `SkillHitTargetEvent` emission (it's skill-specific, not damage feedback)

Knockback force source: `DamageBlock.KnockbackForce` (SkillExecutor currently reads from
`EffectBlock.KnockbackForce`; after unification, the DamageBlock path through
MonsterEntity handles it). For skill-specific knockback overrides, set the value on
DamageBlock before calling TakeDamage.

### 2. Add SkillId / ComboIndex to DamageBlock (DamageBlock.cs / MonsterEntity.cs)

`MonsterEntity.TakeDamage` emits `MonsterTakeDamageEvent` with `skillId=0, comboIndex=1`
(hardcoded for normal attacks). Add two fields to `DamageBlock`:
- `SkillId` (default 0)
- `ComboIndex` (default 1)

SkillExecutor sets these before calling TakeDamage. MonsterEntity reads them when
building the event.

### 3. Fix HitFlashVFX (prefab)

- Flash color currently serialized as white `(1,1,1,1)` on SlimePBR prefab
- Reset to default or set per-monster. `_flashColor` is already `[SerializeField]` —
  configure red for green slime, orange for dark monsters, etc.

### 4. Enable HitParticleController (prefab)

SlimePBR has HitParticleController attached but **disabled** and with wrong references:

| Field | Current | Fix |
|-------|---------|-----|
| enabled | false | true |
| `_normalHitParticles` | HitSparkParticle (wrong) | BloodSplatterNormal.prefab |
| `_criticalHitParticles` | HitSparkCritical (wrong) | BloodSplatterCritical.prefab |
| `_profile` | null | HitFeedbackProfile.asset |

Same fixes apply to TurtleShellPBR and SwordShield prefabs.

### 5. Implement PhysicsSystemAdapter.ApplyKnockback (CharacterAdapters.cs)

Current stub:
```csharp
// TODO: 实现击退效果
```

Implement velocity-based knockback displacement on the player's CharacterController,
following the same decay pattern as `MonsterMovement.ApplyKnockback`.

### 6. Propagate KnockbackForce in PlayerHitZone (PlayerHitZone.cs)

Currently hardcoded `knockbackForce: 1f`. Change to read from
`AttackHitboxData.KnockbackForce` — same as HitZone does for monsters.

## Light vs Heavy Hit Differentiation

| Hit type | KnockbackForce | Result |
|----------|:---:|--------|
| Normal combo (light) | 0 or very small | Hit animation only, body flash, blood particles |
| Skill (heavy) | medium-large | Animation + displacement knockback + blood particles |

Controlled entirely by `DamageBlock.KnockbackForce` in skill/weapon data assets.

## Files Changed

| File | Change |
|------|--------|
| `SkillExecutor.cs` | Call IDamageable.TakeDamage, remove duplicate event emission |
| `DamageBlock.cs` | Add SkillId, ComboIndex fields |
| `MonsterEntity.cs` | Read SkillId/ComboIndex from DamageBlock when emitting event |
| `CharacterAdapters.cs` | Implement PhysicsSystemAdapter.ApplyKnockback |
| `PlayerHitZone.cs` | Read KnockbackForce from AttackHitboxData |
| `SlimePBR.prefab` | Fix HitFlashVFX color, enable HitParticleController, fix references |
| `TurtleShellPBR.prefab` | Same prefab fixes |
| `SwordShield prefabs` | Same prefab fixes |

## Edge Cases

- **Zero knockback**: still plays hit animation + flash + particles, no displacement
- **Rapid hits**: knockback velocity overwrites (not stacks), AI state timer resets
- **Hit during death**: skipped (IsDead check in TakeDamage and OnKnockback)
- **No particle prefab assigned**: log warning once, skip spawn, don't block
- **Flash color per monster**: configured on each prefab via serialized field

## Dependencies

- Existing: EventBus, DOTween, ComponentPool, MonsterAI/MonsterMovement
- No new packages
