# Player HUD & Target Panel Design

## Overview

Player HUD at top-left corner and enhanced Target Panel at top-center. Both use Image.fillAmount for bars with overlaid numbers, event-driven data binding, and object-pooled buff icons.

## Panels

### PlayerHudPanel — Top-left

| Property | Value |
|----------|-------|
| Layer | Base |
| Visibility | CanvasGroup, always visible |

Layout (horizontal):
- Round avatar (40×40) on the left
- Right column: name + level → HP bar (with overlaid numbers) → MP bar (with overlaid numbers) → buff icon row

Serialized fields:
- `Image _portrait` — round avatar
- `TMP_Text _nameText`, `_levelText`
- `Image _hpFill`, `TMP_Text _hpOverlay`
- `Image _mpFill`, `TMP_Text _mpOverlay`
- `Transform _buffContainer`, `GameObject _buffIconPrefab`

### TargetPanel — Top-center (enhance existing)

| Property | Value |
|----------|-------|
| Layer | Base |
| Visibility | CanvasGroup, hidden when no target |

Changes from current:
- Replace Slider-based HP with Image.fillAmount + overlaid text
- Add MP bar (Image.fillAmount + overlaid text)
- Add Buff icon row (object-pooled)

Serialized fields (new):
- `Image _hpFill`, `TMP_Text _hpOverlay` (replace `_hpSlider`, `_hpText`)
- `Image _mpFill`, `TMP_Text _mpOverlay`
- `Transform _buffContainer`, `GameObject _buffIconPrefab`

## Data Interfaces

### IPlayerStatsProvider (new)

```csharp
public interface IPlayerStatsProvider
{
    string Name { get; }
    int Level { get; }
    Sprite Portrait { get; }
    float HPPercent { get; }
    int CurrentHP { get; }
    int MaxHP { get; }
    float MPPercent { get; }
    int CurrentMP { get; }
    int MaxMP { get; }
    BuffInfo[] ActiveBuffs { get; }
    event Action<float, int, int> OnHPChanged;
    event Action<float, int, int> OnMPChanged;
    event Action<BuffInfo[]> OnBuffsChanged;
}
```

### ITargetStatsProvider (new)

```csharp
public interface ITargetStatsProvider
{
    float MPPercent { get; }
    int CurrentMP { get; }
    int MaxMP { get; }
    BuffInfo[] ActiveBuffs { get; }
    event Action<float, int, int> OnMPChanged;
    event Action<BuffInfo[]> OnBuffsChanged;
}
```

### BuffInfo (new struct)

```csharp
public struct BuffInfo
{
    public string Id;
    public Sprite Icon;
    public float RemainingTime; // -1 = permanent
    public float Duration;
    public bool IsDebuff;
}
```

## Data Flow

```
PlayerHudPanel                    TargetPanel
     │                                │
     │ Bind(IPlayerStatsProvider)      │ Bind(ITargetable)
     │   → Subscribe OnHPChanged       │   → Subscribe OnHPChanged
     │   → Subscribe OnMPChanged       │   → TryCast ITargetStatsProvider
     │   → Subscribe OnBuffsChanged    │     → Subscribe OnMPChanged (if available)
     │                                │     → Subscribe OnBuffsChanged (if available)
     │ OnDestroy → Unbind()           │ OnDestroy / Clear → Unbind()
```

- PlayerHudPanel is always visible, never calls Hide/Close
- TargetPanel shows on Bind, hides when target dies or deselected

## Buff Icon Display

Each buff icon shows the buff sprite with a semi-transparent dark overlay that fills from bottom to top as time remaining decreases. Debuffs get a red border, buffs get a green border. Icons are object-pooled in `_buffContainer`.

## Files

| File | Action |
|------|--------|
| `UI/Panel/HUD/PlayerHudPanel.cs` | Create |
| `UI/Panel/HUD/TargetPanel.cs` | Modify |
| `Sys3C/Core/Combat/IPlayerStatsProvider.cs` | Create |
| `Sys3C/Core/Combat/ITargetStatsProvider.cs` | Create |
| `Sys3C/Core/Combat/BuffInfo.cs` | Create |
| `UI/Panel/HUD/PlayerHudPanel.prefab` | Create (MCP) |
| `UI/Panel/HUD/TargetPanel.prefab` | Modify (MCP) |
