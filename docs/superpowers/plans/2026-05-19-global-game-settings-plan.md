# Global Game Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a centralized `GameSettings` ScriptableObject with static accessor, plus `GameConsts` placeholder, and migrate three hardcoded references.

**Architecture:** Single ScriptableObject (`GameSettings.asset`) under `Resources/Setting/`, loaded via `Resources.Load` lazy singleton. Class definition and `GameConsts` live in `Assets/Scripts/AOT/DataDefinition/` using namespace `DataDefinition`. Hotfix consumers reference AOT class directly — no bridge needed.

**Tech Stack:** Unity 2022 LTS, C#, HybridCLR

---

### Task 1: Create GameSettings.cs

**Files:**
- Create: `Assets/Scripts/AOT/DataDefinition/GameSettings.cs`

- [ ] **Step 1: Write GameSettings.cs**

```csharp
using UnityEngine;

namespace DataDefinition
{
    [CreateAssetMenu(fileName = "GameSettings", menuName = "Game/GameSettings")]
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

        [Header("Display")]
        [Tooltip("设计分辨率")]
        public Vector2 ReferenceResolution = new(1920, 1080);

        [Tooltip("目标帧率")]
        public int TargetFrameRate = 60;

        [Header("VFX")]
        [Tooltip("受击闪屏贴图")]
        public Sprite HitFlashSprite;

        [Tooltip("受击闪屏颜色")]
        public Color HitFlashColor = Color.white;

        [Tooltip("受击闪屏时长(秒)")]
        public float HitFlashDuration = 0.15f;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/AOT/DataDefinition/GameSettings.cs Assets/Scripts/AOT/DataDefinition/GameSettings.cs.meta
git commit -m "feat: add GameSettings ScriptableObject with singleton accessor"
```

---

### Task 2: Create GameConsts.cs

**Files:**
- Create: `Assets/Scripts/AOT/DataDefinition/GameConsts.cs`

- [ ] **Step 1: Write GameConsts.cs**

```csharp
namespace DataDefinition
{
    public static class GameConsts
    {
        // Populate as needed — Addressables keys, layer names, shader property IDs, etc.
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/AOT/DataDefinition/GameConsts.cs Assets/Scripts/AOT/DataDefinition/GameConsts.cs.meta
git commit -m "feat: add GameConsts placeholder static class"
```

---

### Task 3: Create GameSettings.asset

**Files:**
- Create: `Assets/Resources/Setting/GameSettings.asset`

**Note:** This step uses MCP to create the SO asset in Unity. Ensure Unity Editor is open with the project loaded.

- [ ] **Step 1: Create the Resources/Setting folder if needed**

Use `assets-create-folder` MCP tool to create `Assets/Resources/Setting`.

- [ ] **Step 2: Create GameSettings.asset**

Use `assets-modify` MCP tool or Unity context menu `Game > GameSettings` to create the asset at path `Assets/Resources/Setting/GameSettings.asset`.

Alternative: Right-click in Project window → Create → Game → GameSettings, move to `Assets/Resources/Setting/`.

- [ ] **Step 3: Verify asset exists at correct path**

Use `assets-get-data` MCP tool with asset path `Assets/Resources/Setting/GameSettings.asset` to confirm creation.

- [ ] **Step 4: Commit**

```bash
git add Assets/Resources/Setting/GameSettings.asset Assets/Resources/Setting/GameSettings.asset.meta
git commit -m "feat: add GameSettings.asset instance with default values"
```

---

### Task 4: Migrate ScreenAdapter.cs hardcoded resolution

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs:38`

- [ ] **Step 1: Replace hardcoded resolution**

Change line 38 from:
```csharp
_scaler.referenceResolution = new Vector2(1920, 1080);
```
to:
```csharp
_scaler.referenceResolution = DataDefinition.GameSettings.Instance.ReferenceResolution;
```

- [ ] **Step 2: Add using directive if needed**

If the file doesn't already have `using DataDefinition;`, add it at the top of the file after the existing `using UnityEngine.UI;` line.

- [ ] **Step 3: Verify compilation**

Wait for Unity to compile. Check for errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs
git commit -m "refactor: read design resolution from GameSettings in ScreenAdapter"
```

---

### Task 5: Migrate EntityDisplayManager.cs hardcoded resolution

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs:50`

- [ ] **Step 1: Replace hardcoded resolution**

Change line 50 from:
```csharp
scaler.referenceResolution = new Vector2(1920, 1080);
```
to:
```csharp
scaler.referenceResolution = DataDefinition.GameSettings.Instance.ReferenceResolution;
```

- [ ] **Step 2: Add using directive if needed**

If the file doesn't already have `using DataDefinition;`, add it at the top after existing `using` statements.

- [ ] **Step 3: Verify compilation**

Wait for Unity to compile. Check for errors.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs
git commit -m "refactor: read design resolution from GameSettings in EntityDisplayManager"
```

---

### Task 6: Migrate HitFlashVFX.cs to use GameSettings

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs`

- [ ] **Step 1: Rewrite HitFlashVFX to read from GameSettings**

Replace the file content:

```csharp
using DG.Tweening;
using DataDefinition;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class HitFlashVFX : MonoBehaviour
    {
        [SerializeField] private Renderer _targetRenderer;

        private MaterialPropertyBlock _propBlock;
        private Tween _flashTween;
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            if (_targetRenderer == null)
                _targetRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (_targetRenderer == null)
                _targetRenderer = GetComponentInChildren<MeshRenderer>();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<DamageEvent>(OnPlayerDamaged);
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
            _flashTween?.Kill();
        }

        private void OnPlayerDamaged(DamageEvent e)
        {
            TriggerFlash();
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            TriggerFlash();
        }

        private void TriggerFlash()
        {
            if (_targetRenderer == null) return;

            var settings = GameSettings.Instance;
            var flashWidth = 0.05f;
            var flashDuration = settings.HitFlashDuration;

            _flashTween?.Kill();

            _targetRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(OutlineColorId, settings.HitFlashColor);
            _propBlock.SetFloat(OutlineWidthId, flashWidth);
            _targetRenderer.SetPropertyBlock(_propBlock);

            var startColor = settings.HitFlashColor;
            _flashTween = DOTween.To(() => flashWidth, width =>
            {
                if (_targetRenderer == null) return;
                _targetRenderer.GetPropertyBlock(_propBlock);
                _propBlock.SetFloat(OutlineWidthId, width);
                float t = 1f - width / flashWidth;
                _propBlock.SetColor(OutlineColorId, Color.Lerp(startColor, Color.clear, t));
                _targetRenderer.SetPropertyBlock(_propBlock);
            }, 0f, flashDuration).SetTarget(_targetRenderer);
        }

        private void OnDestroy()
        {
            _flashTween?.Kill();
        }
    }
}
```

Changes from original:
- Removed `_flashWidth`, `_flashDuration`, `_flashStartColor`, `_flashEndColor` fields — `flashWidth` stays local constant (0.05f is an outline width, not config), while `flashDuration`/`flashColor` come from `GameSettings.Instance`
- Added `using DataDefinition;`
- `TriggerFlash()` reads `HitFlashColor` and `HitFlashDuration` from `GameSettings.Instance`
- Removed unused `_flashEndColor` — tween now fades to `Color.clear`
- Removed `using Hotfix.GameSystems.Skills;` (not needed after refactor)

- [ ] **Step 2: Verify compilation**

Wait for Unity to compile. Check for errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/VFX/HitFlashVFX.cs
git commit -m "refactor: HitFlashVFX reads flash params from GameSettings"
```

---

### Task 7: Final verification

- [ ] **Step 1: Verify all references compile**

Use `assets-refresh` MCP tool to force full recompilation. Check `console-get-logs` for any compilation errors.

- [ ] **Step 2: Enter play mode and verify**

Use `editor-application-set-state` to enter play mode. Verify no null-reference exceptions from `GameSettings.Instance`. Exit play mode.

- [ ] **Step 3: Run existing tests**

```bash
mcp__ai-game-developer__tests-run with testMode=EditMode
```
Verify all tests pass.

- [ ] **Step 4: Commit any remaining changes**

```bash
git status
git add <any remaining files>
git commit -m "chore: final verification of GameSettings integration"
```
