# SkillData Refactor: Lean Base + Release-Type Subclasses

## Motivation

Current `SkillData` (~45 fields) is a god object. Many fields apply to only one release type (e.g. `channelDuration` for Channeled, `minChargeTime` for Charged). The Inspector is cluttered, and the file is hard to understand.

Goals:
- **Planner UX (primary)**: Inspector shows only relevant fields per skill type
- **Code maintainability (secondary)**: Each config class is small and has a single responsibility
- Performance is not a concern at this stage (~15 skills)

## Design

### SkillData (abstract base, ~15 fields)

Only fields that EVERY skill needs. Dash stays because it's a cross-cutting mechanic.

```
skillId, skillName, description, icon, skillType, quality     // identity
manaCost, staminaCost, cooldown                                 // cost
canBeInterruptedByDamage, canBeInterruptedByMovement, interruptionPriority  // cross-cutting
canCancelIntoBasicAttack, canCancelIntoOtherSkill               // cross-cutting
dashDistance, dashDuration                                      // cross-cutting
animatorTrigger, castClip, releaseClip                          // animation
damage: DamageBlock?                                            // nullable, absent for pure buff skills
```

### Shared Config Blocks (4 [Serializable] classes)

Reused across all subclasses with identical structure. Each block is a folded section in the Inspector.

**DamageBlock**: baseDamage, attackRatio, scalingAttribute, damageType, criticalRateBonus, criticalDamageBonus, isTrueDamage, armorPenetration

**ShapeBlock**: targetType (Single/AOE_Circle/AOE_Cone/AOE_Sector/Self), range, angle, angleStart, angleEnd, areaRadius, targetMask, hitboxTimings[], stopAtFirst

**EffectBlock**: EffectData[] applyEffects (reuses existing EffectData hierarchy)

**PresentationBlock**: castVFX, releaseVFX, castSFX, showCastingBar, castingBarColor, hitStopDuration

### Subclasses (by release mechanism)

Each subclass embeds the shared blocks it needs, plus its own unique fields.

| Subclass | Own Fields | Blocks Used |
|----------|------------|-------------|
| **ComboSkillData** | comboIndex, comboWindow, comboResetTime, nextCombo, enableMovement, movementSpeed, hitType, impactForce, impactDirection, cancelableWindowStart/End, allowRecoveryCancel, overrideClip | ShapeBlock, DamageBlock, PresentationBlock |
| **InstantSkillData** | (no extra release fields) | ShapeBlock, DamageBlock, EffectBlock, PresentationBlock |
| **ChargedSkillData** | holdToCharge, releaseToFire, minChargeTime, maxChargeTime, chargeDamageCurve, chargeAreaCurve, canMoveWhileCharging, canRotateWhileCharging | ShapeBlock, DamageBlock, EffectBlock, PresentationBlock |
| **ChanneledSkillData** | castTime, channelDuration, tickInterval, tickDamagePercent, channelClip, channelFollowsTarget, breakOnTargetMove, canMoveWhileChanneling | ShapeBlock, DamageBlock, EffectBlock, PresentationBlock |
| **ProjectileSkillData** | projectilePrefab, projectileSpeed, projectilePierce, maxPierceTargets, homing | DamageBlock, EffectBlock, PresentationBlock |

New skill forms add new subclasses without touching the base.

### Design Rationale

- **Release type drives state machine branching** → natural subclass boundary. `SkillStateMachine` dispatches on concrete type.
- **Damage/Shape/Effect/VFX are shared concerns** → config blocks avoid field duplication across subclasses.
- **Dash/interruption/cancel are cross-cutting** → kept in base. Any subtype may need them.
- **C# single inheritance is the constraint** → orthogonal concerns (e.g. "AOE + ranged + dot") are handled by config blocks within a subclass, not by multiple base classes.

### Consumer Code Pattern

```csharp
// Base fields: same as before
if (skill.DashDistance > 0)
    Dash(skill.DashDistance, skill.DashDuration);

// Subclass fields: runtime type check
if (skill is ChargedSkillData charged)
    HandleCharge(charged.MinChargeTime, charged.MaxChargeTime);
```

### Files Affected

| File | Action |
|------|--------|
| `Skills/Data/SkillData.cs` | Rewrite: slim base + 4 config blocks |
| `Skills/Data/BasicAttackData.cs` | Rename to ComboSkillData.cs, refactor |
| `Skills/Data/SpecialSkillData.cs` | Remove, split into Instant/Charged/Channeled |
| `Skills/Effect/DamageData.cs` | Rename to DamageBlock, move to Data/ |
| `Sys3C/Core/Combat/AttackShapeConfig.cs` | Merge into ShapeBlock |
| `Sys3C/Core/Combat/AttackEffectConfig.cs` | Split into EffectBlock / PresentationBlock |
| `Skills/Runtime/SkillStateMachine.cs` | Dispatch on concrete subtype |
| `Skills/Runtime/SkillExecutor.cs` | Access via blocks |
| `Skills/Definition/` | `SkillType` enum updated to match new subclasses |
| All `.asset` files | Recreate in Inspector |

### What This Does NOT Do

- No component/ECS pattern. No `[SerializeReference]` or `ISkillComponent` interfaces.
- Backward compatibility is not preserved. Existing `.asset` files must be recreated.
- No custom Editor/PropertyDrawer scripts. Default Unity Inspector behavior is sufficient because config blocks naturally fold.
