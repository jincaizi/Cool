# Blood Slash VFX Design

## Overview

Replace the current spark-style hit particles with blood-themed effects and add a slash blood trail on critical hits. The goal is to make sword hits on monsters feel more visceral — blood splatter on every hit, with an additional slash mark trail on critical strikes.

## Current State

- `HitParticleController` manages hit particle spawning via `MonsterTakeDamageEvent`
- Two prefab slots: `_normalHitParticles` (all hits) and `_criticalHitParticles` (critical hits)
- Uses `ComponentPool<ParticleSystem>` for object pooling
- `HitFlashVFX` provides outline flash on the monster mesh (unchanged by this design)
- `HitSparkParticle.prefab` is the current spark-style hit particle

## Design Decisions

- **Replace** existing spark particles with blood splatter (not additive)
- **Extend** `HitParticleController` for the new slash trail (not a separate component)
- **TrailRenderer** for the slash blood trail (not Decal or particle-based)
- **Pool** the slash trail prefab like existing particles

## Changes

### 1. Blood Splatter Particles (All Hits)

Replace the prefabs assigned to the existing slots in `HitParticleController`:

| Slot | Current | New |
|------|---------|-----|
| `_normalHitParticles` | Spark particle | `BloodSplatterNormal.prefab` — red blood droplets spraying outward, gravity-affected, ~0.4s lifetime |
| `_criticalHitParticles` | Spark particle | `BloodSplatterCritical.prefab` — larger, denser blood burst, more droplets, ~0.5s lifetime |

No code changes needed for this part — just prefab asset replacement.

### 2. Slash Blood Trail (Critical Hits Only)

**New script: `SlashBloodTrail.cs`**

Location: `Assets/Scripts/Hotfix/GameSystems/VFX/SlashBloodTrail.cs`

```csharp
public class SlashBloodTrail : MonoBehaviour
{
    // Inspector-configured
    TrailRenderer _trailRenderer;
    float _moveSpeed = 3f;       // units per second along slash direction
    float _moveDistance = 0.5f;  // total distance to travel
    float _fadeDelay = 0.3f;     // wait after move completes before returning to pool

    ComponentPool<SlashBloodTrail> _pool;

    public void SetPool(ComponentPool<SlashBloodTrail> pool);
    public void Activate(Vector3 startPos, Vector3 direction);
    // Coroutine: move along direction, wait for trail fade, return to pool
}
```

Behavior on `Activate`:
1. Position at `startPos`
2. Enable TrailRenderer
3. Move along `direction` at `_moveSpeed` for `_moveDistance`
4. Wait `_fadeDelay` seconds for trail to fade
5. Disable TrailRenderer and return to pool

**New prefab: `SlashBloodTrail.prefab`**

Structure:
```
SlashBloodTrail (GameObject)
├── TrailRenderer
│   - Width curve: 0.08 → 0
│   - Color gradient: opaque red → transparent red
│   - Time: 0.4s (trail lifetime)
│   - Min vertex distance: 0.01
│   - Material: Particles/Standard Unlit (additive or alpha blend)
└── SlashBloodTrail.cs
```

**`HitParticleController` modifications:**

```csharp
// New field
[SerializeField] private GameObject _slashBloodTrailPrefab;
private static ComponentPool<SlashBloodTrail> _trailPool;

// In OnMonsterDamaged, after existing particle logic:
if (e.IsCritical && _slashBloodTrailPrefab != null)
{
    var pool = GetOrCreateTrailPool(_slashBloodTrailPrefab);
    var trail = pool.Get();
    trail.transform.position = e.HitPosition;
    trail.SetPool(pool);
    trail.Activate(e.HitPosition, e.HitDirection);
}
```

### 3. File Summary

| Action | File | Purpose |
|--------|------|---------|
| Modify | `HitParticleController.cs` | Add slash trail slot, pool, and critical-hit trigger |
| Create | `SlashBloodTrail.cs` | TrailRenderer lifecycle + movement + pooling |
| Create | `SlashBloodTrail.prefab` | Blood trail prefab (TrailRenderer + script) |
| Create | `BloodSplatterNormal.prefab` | Normal hit blood splatter particle |
| Create | `BloodSplatterCritical.prefab` | Critical hit blood splatter particle |

### 4. Not Changed

- `HitFlashVFX` — outline flash stays as-is
- `MonsterTakeDamageEvent` — data structure already sufficient
- `WeaponVFXController`, `SlashTrailVFX`, etc. — unaffected
- `HitZone`, `AttackHitbox` — hit detection unchanged

## Integration

The system plugs into the existing event-driven architecture:

```
AttackHitbox collides with HitZone
  → MonsterEntity.TakeDamage()
    → EventBus.Emit(MonsterTakeDamageEvent)
      → HitParticleController.OnMonsterDamaged()
        → Spawn blood splatter (all hits)
        → Spawn slash blood trail (critical only)
      → HitFlashVFX.OnMonsterDamaged() (outline flash, unchanged)
      → DisplayEventBridge (floating text, unchanged)
```

## Tuning Guide

| Parameter | Location | Default | Effect |
|-----------|----------|---------|--------|
| Trail move speed | `SlashBloodTrail._moveSpeed` | 3.0 | How fast the slash mark draws |
| Trail move distance | `SlashBloodTrail._moveDistance` | 0.5 | Length of the slash mark |
| Trail fade delay | `SlashBloodTrail._fadeDelay` | 0.3 | How long trail persists after drawing |
| Trail width | Prefab TrailRenderer | 0.08 → 0 | Thickness of the blood trail |
| Trail color | Prefab TrailRenderer gradient | Red → transparent | Blood color |
| Splatter lifetime | Prefab ParticleSystem | 0.4s / 0.5s | How long blood droplets linger |
