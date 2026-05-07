# Attack Detection Optimization & Shape Extension Design

**Date:** 2026-05-07
**Status:** Approved
**Scope:** PhysicsRegistry performance, new Sector/Rect shapes, IEntityRegistry interface upgrade, debug Gizmos

## Problem

1. **Performance**: `PhysicsRegistry.FindNearby()` does O(n) linear scan over all entities. ConeShape uses `Vector3.Angle()` (acos) and `List.Contains()` (O(m²)). Allocating `new List<IDamageable>` and `new Collider[32]` per call.
2. **Missing shapes**: Rect and Sector shapes declared in enum but not implemented. Can't represent horizontal slash (Attack1), diagonal slash (Attack2), or thrust (SkillQ) accurately.
3. **No debug visualization**: No Gizmos, no visual feedback for shape tuning.
4. **Interface inefficiency**: `FindNearby` returns `Transform`, requiring `GetComponent<IDamageable>()` on every call — reflection/per-call overhead.

## Design

### 1. Interface Upgrades

**IEntityRegistry — Store IDamageable directly:**

`FindNearby` returns `IReadOnlyList<IDamageable>` instead of `IReadOnlyList<Transform>`. Eliminates `GetComponentInParent<IDamageable>()` per entity per query.

**IAttackShape — Add non-allocating overload:**

```csharp
// Existing (simple API, backward compatible)
IReadOnlyList<IDamageable> Resolve(Vector3 origin, Vector3 forward, LayerMask targetMask);

// New (high-perf: clears then fills caller-owned list, no allocation)
void ResolveNonAlloc(Vector3 origin, Vector3 forward, LayerMask targetMask, List<IDamageable> results);
```

### 2. PhysicsRegistry — Physics-First Query

Physics.OverlapSphereNonAlloc (PhysX spatial acceleration) is the primary path. The registered set serves as a supplement for entities without colliders.

```csharp
FindNearby(center, radius, type):
  1. Physics.OverlapSphereNonAlloc → add matching IDamageable from colliders
  2. Registered set → supplement non-collider entities, checked by sqrMagnitude
  3. Dedup via HashSet
```

### 3. Performance Optimizations (All Shapes)

| Optimization | Before | After |
|-------------|--------|-------|
| Angle check | Vector3.Angle() with acos | Vector3.Dot() with precomputed cos threshold |
| Distance check | Vector3.Distance() with sqrt | sqrMagnitude comparison |
| Dedup | List.Contains() O(n²) | HashSet<IDamageable> O(1) |
| Collider buffer | new Collider[32] per call | static Collider[64] shared |
| Precompute | halfAngle per call | cached in constructor |

### 4. New Shapes

**SectorShape** — Asymmetric cone with user-defined start/end angles:

```
Forward=0°, positive=right, negative=left
  Attack1 (horizontal slash):  start=0°,   end=90°   rightward
  Attack2 (diagonal slash):    start=-30°, end=0°    upper-right to front
  Generic uses: 180° half-circle, 270° sweep, etc.
```

Angle check: `localAngle = atan2(cross.y, dot) * Rad2Deg`, check if within [AngleStart, AngleEnd]. Note: ConeShape uses dot-product (faster, only needs upper bound); SectorShape needs atan2 (slower but necessary for asymmetric angle range).

**RectShape** — Forward rectangle for thrust/poke:

```
         range
    ┌──────────────┐
    │     width    │
    │    ┌───┐     │
    │    │ ● │     │  player at origin
    │    └───┘     │
    └──────────────┘
```

Check: project target onto forward axis (0 to range), check lateral offset (≤ width/2).

### 5. StopAtFirst Implementation

If `config.StopAtFirst == true`, shapes return only the first valid target (closest to origin along forward direction). Used by thrust attacks (SkillQ).

### 6. Debug Gizmos

File: `AttackShapeGizmos.cs` — static utility class:

```csharp
public static class AttackShapeGizmos
{
    public static bool Enabled = true;
    public static Color HitColor = Color.red;
    public static Color MissColor = Color.green;

    public static void DrawCone(Vector3 origin, Vector3 forward, float range, float angle);
    public static void DrawSector(Vector3 origin, Vector3 forward, float range, float start, float end);
    public static void DrawCircle(Vector3 origin, float radius);
    public static void DrawRect(Vector3 origin, Vector3 forward, float range, float width);
}
```

Shapes call these from their Resolve methods when `Enabled == true`. No MonoBehaviour required — invoked inline during gameplay.

### 7. Skill Shape Configurations

| Skill | Shape | Key Params |
|-------|-------|------------|
| Attack1 | Sector | range=2, start=0°, end=90° |
| Attack2 | Sector | range=2, start=-30°, end=0° |
| SkillQ | Rect | range=3, width=0.5, StopAtFirst=true |
| SkillR | Circle | radius=2 |

## Scope

### Modified Files
| File | Changes |
|------|---------|
| `Core/Combat/IEntityRegistry.cs` | Interface: IDamageable storage, updated FindNearby signature |
| `Core/Combat/IAttackShape.cs` | Add ResolveNonAlloc |
| `Core/Combat/PhysicsRegistry.cs` | Physics-first, IDamageable storage, shared buffer |
| `Core/Combat/AttackShapeConfig.cs` | Add Sector to enum, AngleStart/AngleEnd fields |
| `Core/Combat/AttackShapeFactory.cs` | SectorShape + RectShape creation branches |
| `Core/Combat/ConeShape.cs` | HashSet dedup, Dot angle check, sqrMagnitude, shared buffer |
| `Core/Combat/CircleShape.cs` | HashSet dedup, sqrMagnitude |
| `MeleeWeapon.cs` | Adapt to IEntityRegistry changes |
| `Sys3CEntry.cs` | Adapt IDamageable registration |
| `Monster/MonsterAI.cs` (if it registers entities) | Adapt registration |

### New Files
| File | Purpose |
|------|---------|
| `Core/Combat/SectorShape.cs` | Asymmetric sector shape |
| `Core/Combat/RectShape.cs` | Rectangle/box shape with StopAtFirst |
| `Core/Combat/AttackShapeGizmos.cs` | Static Gizmo drawing utilities |
