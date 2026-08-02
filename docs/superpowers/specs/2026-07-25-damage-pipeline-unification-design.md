# Damage Pipeline Unification Design

**Date:** 2026-07-25
**Status:** Ready for implementation

## 1. Problem Statement

Damage calculation and death determination are handled through two separate, asymmetric code paths for players and monsters. The player path (Sys3CEntry) manually calls a single DefendModifier inline with ~80 lines of procedural code; the monster path (MonsterEntity) uses a proper DamagePipeline with a modifier chain. Additionally, several numerical bugs in DamageBlock.CalculateFinalDamage prevent character stats from affecting critical hit calculations, and player death is never triggered when HP reaches zero.

## 2. Current State (TODO)

### Damage Formula (shared, in DamageBlock.CalculateFinalDamage)

```
BaseDamage + (ScalingAttribute × AttackRatio)
  → Critical: if 5% + CriticalRateBonus: × (1.5 + CriticalDamageBonus)
  → Charge bonus: × (1 + chargeProgress × 0.5)
  → DOT: × tickInterval
```

### Known numerical bugs (out of scope, tracked separately)

| Bug | File:Line | Detail |
|-----|-----------|--------|
| Crit damage multiplier | DamageBlock.cs:98 | `(1f + 1.5f + bonus)` should be `(1.5f + bonus)` |
| Crit rate ignores character stats | DamageBlock.cs:94 | Hardcodes 0.05, never reads attackerStats.CriticalRate |
| Crit damage ignores character stats | DamageBlock.cs:98 | Hardcodes 1.5, never reads attackerStats.CriticalDamage |
| DOT ticks multiply by interval | DamageBlock.cs:110 | `damage * tickInterval` is backwards |
| Armor/DamageType unused | DamageBlock.cs | _armorPenetration, _isTrueDamage, _damageType never read |
| RawDamage zero-value fallback | DamageContext.cs:23 | `OverrideDamage > 0` treats 0 as unset |

### Defensive paths

**Monster**: DamagePipeline [IFrameModifier(Pri=0) → DefendModifier(Pri=100)] → MonsterStats.ApplyDamage(HP subtraction) → OnDeath event → HandleDeath → DeathState

**Player**: Manual DefendModifier.Modify() → inline durability check → _currentHP subtraction → HandleDamage (no death trigger when HP=0)

### Death handling

| Aspect | Monster | Player |
|--------|---------|--------|
| Death trigger | HP<=0 in ApplyDamage → OnDeath event | ❌ None (HP=0 silently) |
| Death FSM transition | HandleDeath → ForceTransition(DeathState) | ❌ Not called |
| Death animation | DeathState.OnEnter | ❌ Never plays |

## 3. Target State (TO-BE)

### Unified flow

```
SkillExecutor.ApplyDamage
  │
  ├─ CalculateFinalDamage(attackerStats)
  │
  └─ IDamageable.TakeDamage(data, hitDir)
       │
       └─ [MonsterEntity | Sys3CEntry]
             1. Build DamageContext (OverrideDamage, Flags, HitDirection)
             2. damagePipeline.Process(ref ctx)
                  │
                  ├─ IFrameModifier     (Pri=0)   Full block when active
                  ├─ DefendModifier     (Pri=100) Full block when defending + frontal
                  ├─ [Future] ArmorModifier (Pri=200)
                  ├─ [Future] ThornsModifier  (Pri=300)
                  │
                  ├─ if WasBlocked → skip HP subtraction
                  ├─ else: receiver.ApplyDamage(finalDamage)
                  ├─ if receiver.IsDead → ReactLevel = Death
                  └─ receiver.OnDamageProcessed(result, ctx)
                       ├─ Player: durability + shield break + HitFSM routing + death
                       └─ Monster: no-op (death handled by OnDeath event)
             3. MonsterEntity only: brain.OnDamageReceived + EmitDamageEvents + KnockbackEvent
```

### Modifier behaviors

| Modifier | Condition | Output |
|----------|-----------|--------|
| IFrameModifier | Active=true | WasBlocked=true, ReactLevel=None |
| DefendModifier | Defending + frontal angle | WasBlocked=true, ReactLevel=None |
| DefendModifier | Defending + back attack | FinalDamage=RawDamage, ReactLevel=Flinch |

DefendModifier now grants **full immunity** (WasBlocked) instead of percentage reduction (WasReduced). DefendConfig.DamageReduction field is deleted.

### Death determination

```csharp
// In DamagePipeline.Process(), after modifier chain:
if (!merged.WasBlocked)
{
    receiver.ApplyDamage(merged.FinalDamage);

    if (receiver.IsDead)
    {
        merged.ReactLevel = HitReactLevel.Death;
        merged.ShouldKnockback = false;
    }
}

receiver.OnDamageProcessed(merged, ref ctx);
return merged;
```

Death is no longer triggered by magic number 9999 in HitFSM.EnterHit — it is a top-level outcome of the pipeline, carried by HitReactLevel.Death (= 5, new enum value).

### Merge logic fix

Each modifier writes its result into `ctx.CurrentDamage`. The pipeline uses `ctx.CurrentDamage` as `merged.FinalDamage` at the end, eliminating the "last WasReduced wins" race condition.

## 4. New / Changed Interfaces

### IDamageReceiver (new)

```csharp
// Location: Sys3C/Core/Combat/IDamageReceiver.cs
namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface IDamageReceiver
    {
        bool IsDead { get; }
        void ApplyDamage(float damage);
        void OnDamageProcessed(DamageResult result, ref DamageContext ctx);
    }
}
```

### DamagePipeline constructor change

```csharp
// Before:
public DamagePipeline(MonsterConfig config, MonsterStats stats)

// After:
public DamagePipeline(IDamageReceiver receiver)
```

MonsterConfig field removed (never read by pipeline). Initial IFrameModifier still registered in constructor.

## 5. File Changes

### 5.1 Move to shared location (namespace change: Monster → Sys3C.Core.Combat)

| Source | Destination |
|--------|-------------|
| `Monster/Damage/DamagePipeline.cs` | `Sys3C/Core/Combat/DamagePipeline.cs` |
| `Monster/Damage/DamageContext.cs` | `Sys3C/Core/Combat/DamageContext.cs` |
| `Monster/Damage/DamageResult.cs` | `Sys3C/Core/Combat/DamageResult.cs` |
| `Monster/Damage/IDamageModifier.cs` | `Sys3C/Core/Combat/IDamageModifier.cs` |
| `Monster/Damage/Modifiers/DefendModifier.cs` | `Sys3C/Core/Combat/Modifiers/DefendModifier.cs` |
| `Monster/Damage/Modifiers/IFrameModifier.cs` | `Sys3C/Core/Combat/Modifiers/IFrameModifier.cs` |

### 5.2 New file

| File | Content |
|------|---------|
| `Sys3C/Core/Combat/IDamageReceiver.cs` | IDamageReceiver interface |

### 5.3 Modify: DamageResult.cs

Add `Death = 5` to `HitReactLevel` enum.

### 5.4 Modify: DamagePipeline.cs

- Constructor: `(MonsterConfig, MonsterStats)` → `(IDamageReceiver)`
- `Process()`: Fix merge logic (use ctx.CurrentDamage), add death detection, call OnDamageProcessed
- Delete `_config` field

### 5.5 Modify: DefendModifier.cs

- Delete `DefendConfig.DamageReduction`
- Modify(): `WasReduced=true` → `WasBlocked=true`, reduce damage to 0 instead of percentage

### 5.6 Modify: MonsterStats.cs

- Implement `IDamageReceiver`
- `IsDead` → `HP <= 0`
- `ApplyDamage` keeps current logic (HP subtraction + OnDeath event)
- `OnDamageProcessed` → no-op
- Delete old `TakeDamage(DamageBlock)` method

### 5.7 Modify: MonsterEntity.cs

- `Init()`: `new DamagePipeline(config, stats)` → `new DamagePipeline(stats)`
- `TakeDamage()`: unchanged post-processing (brain.OnDamageReceived, EmitDamageEvents, KnockbackEvent)

### 5.8 Modify: Sys3CEntry.cs

- Implement `IDamageReceiver`
- Add `_damagePipeline` field and initialization
- Replace ~80 lines of inline TakeDamage with:
  ```csharp
  void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
  {
      if (_currentHP <= 0) return;
      var ctx = new DamageContext { ... };
      _damagePipeline.Process(ref ctx);
  }
  ```
- `ApplyDamage`: `_currentHP -= damage`
- `IsDead`: `_currentHP <= 0`
- `OnDamageProcessed`: defense exit, shield durability (using ctx.RawDamage as absorbed amount), shield break, DefendHit routing, death handling, normal hit routing
- Add `EnableIFrame(float duration)` with Coroutine timer

### 5.9 Modify: HitFSM.cs

- Delete magic number `9999` death check in `EnterHit()` and `GetPriorityForHitData()`

### 5.10 Update using directives

All consumers updating `using Hotfix.GameSystems.Monster;` → `using Hotfix.GameSystems.Sys3C.Core.Combat;`:
- MonsterEntity.cs
- AIBrain.cs
- AIContext.cs
- HitState.cs (Monster AI)
- Sys3CEntry.cs (already has the using)

## 6. Sys3CEntry.OnDamageProcessed Detail

```csharp
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
        _fsmManager.HandleDeath();           // Layer management: lock Base, activate Hit
        _fsmManager.HitFSM.EnterDeath();     // HitFSM: TransitionTo(Death) + DeathEvent
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

## 7. Implementation Checklist

| # | Task | Files |
|---|------|-------|
| 1 | Add `Death = 5` to `HitReactLevel` enum | DamageResult.cs |
| 2 | Create `IDamageReceiver` interface | new file |
| 3 | Move 6 files, change namespace | DamagePipeline, DamageContext, DamageResult, IDamageModifier, DefendModifier, IFrameModifier |
| 4 | DamagePipeline: constructor change, merge fix, death detection | DamagePipeline.cs |
| 5 | DefendModifier: WasReduced→WasBlocked, delete DamageReduction | DefendModifier.cs |
| 6 | MonsterStats: implement IDamageReceiver, delete old TakeDamage | MonsterStats.cs |
| 7 | MonsterEntity: adapt constructor | MonsterEntity.cs |
| 8 | Sys3CEntry: implement IDamageReceiver, add pipeline, EnableIFrame | Sys3CEntry.cs |
| 9 | HitFSM: delete magic number 9999 logic | HitFSM.cs |
| 10 | Update using directives in all consumers | 5+ files |
| 11 | Update MonsterConfig DefendDamageReduction refs if any | MonsterConfig.cs |

## 8. Out of Scope

- Numerical formula bugs in `DamageBlock.CalculateFinalDamage` (tracked separately)
- Armor/resistance reduction modifier (future, architecture supports it)
- DOT damage defense application (no DOT system implemented yet)
- Shield skill integration (calling EnableIFrame from skill system — requires skill system design)
- RawDamage zero-value fallback fix (edge case, zero-damage skills blocked at SkillExecutor level)
