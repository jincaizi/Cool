# Global Game Settings — Design

**Date:** 2026-05-19
**Status:** Approved

## Purpose

Centralize global game constants and resource references — design resolution, VFX sprites, common paths — into a single ScriptableObject with a static accessor. Eliminate hardcoded magic numbers and scattered `[SerializeField]` values that should be game-wide defaults.

## File Layout

```
Assets/Scripts/AOT/DataDefinition/
├── GameSettings.cs       # ScriptableObject class definition + static singleton
└── GameConsts.cs         # Pure compile-time constants (enums, keys — populated as needed)

Assets/Resources/Setting/
└── GameSettings.asset    # Serialized instance (designer-editable data)
```

Both `.cs` files live in the AOT layer (`DataDefinition/`) so they are accessible from both AOT and Hotfix code. The `.asset` lives under `Resources/` to enable `Resources.Load`.

## GameSettings — Fields

| Header | Field | Type | Default | Notes |
|--------|-------|------|---------|-------|
| Display | `ReferenceResolution` | Vector2 | (1920, 1080) | Replaces hardcoded values in `ScreenAdapter.cs` and `EntityDisplayManager.cs` |
| Display | `TargetFrameRate` | int | 60 | Applied at startup |
| VFX | `HitFlashSprite` | Sprite | null | Screen flash image on damage taken |
| VFX | `HitFlashColor` | Color | white | Tween target color |
| VFX | `HitFlashDuration` | float | 0.15 | Flash tween duration in seconds |
| Paths | *(direct asset references)* | — | — | No string paths; drag asset references directly |

New fields are added under `[Header]` groupings. If a category grows large, it can be extracted into a sub-ScriptableObject referenced from `GameSettings` without breaking the public API.

## Access Pattern

```csharp
public class GameSettings : ScriptableObject
{
    private static GameSettings _instance;

    public static GameSettings Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<GameSettings>("Setting/GameSettings");
            return _instance;
        }
    }

    // ... fields
}
```

- Lazy-loaded on first access via `Resources.Load`.
- Synchronous — no async init dependency.
- Pattern already used in the project (`ResourceManager.cs`).
- Callers write: `GameSettings.Instance.ReferenceResolution`.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| SO vs static class | ScriptableObject | Inspector-editable, supports asset references, HybridCLR-hot-reloadable |
| Single file vs multi-file | Single `GameSettings` | Current scope is small; extract sub-SOs later if needed |
| AOT vs Hotfix layer | AOT (`DataDefinition/`) | AOT code also needs config access; AOT is reachable from everywhere |
| Resources vs Addressables | Resources | Small asset, synchronous, no async chain dependency at startup |
| Manual injection vs auto-load | `Resources.Load` in getter | Zero setup; any system can access without init order concerns |
| HitFlashVFX params: override vs default | Global override | `HitFlashVFX` reads from `GameSettings.Instance` instead of its own `[SerializeField]` |

## Migration Plan

1. Create `GameSettings.cs` with fields above and singleton accessor.
2. Create `GameSettings.asset` in `Assets/Resources/Setting/`.
3. Remove hardcoded 1920x1080 from `ScreenAdapter.cs` → read from `GameSettings.Instance`.
4. Remove hardcoded 1920x1080 from `EntityDisplayManager.cs` → read from `GameSettings.Instance`.
5. Update `HitFlashVFX.cs` to read `_hitFlashSprite`/`_hitFlashColor`/`_hitFlashDuration` from `GameSettings.Instance`, remove local `[SerializeField]` fields.
6. Create `GameConsts.cs` as a placeholder static class for future compile-time constants.

## GameConsts (placeholder)

```csharp
namespace AOT.DataDefinition
{
    public static class GameConsts
    {
        // Populate as needed — Addressables keys, layer names, shader property IDs, etc.
    }
}
```

No fields yet. Added now to establish the pattern — when a constant belongs at compile-time rather than in a designer-editable SO, it goes here.
