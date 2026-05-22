# Player HUD & Target Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build PlayerHudPanel (top-left) and enhance TargetPanel (top-center) with Image.fillAmount bars, overlaid numbers, and object-pooled buff icons.

**Architecture:** Both panels extend UIPanel at LayerType.Base with CanvasGroup visibility. PlayerHudPanel binds to IPlayerStatsProvider and stays always visible. TargetPanel binds to ITargetable + optional ITargetStatsProvider for MP/buffs. BuffIcon component handles per-icon countdown overlay.

**Tech Stack:** Unity 2022 LTS, UGUI (Image.fillAmount, CanvasGroup), TMPro, DOTween

---

### Task 1: Create BuffInfo struct

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/BuffInfo.cs`

- [ ] **Step 1: Write BuffInfo.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public struct BuffInfo
    {
        public string Id;
        public Sprite Icon;
        public float RemainingTime;
        public float Duration;
        public bool IsDebuff;
    }
}
```

- [ ] **Step 2: Wait for Unity compilation**

Run MCP `assets-refresh` to trigger recompile, then check `console-get-logs` for errors.

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/BuffInfo.cs
git commit -m "feat: add BuffInfo struct"
```

---

### Task 2: Create IPlayerStatsProvider interface

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IPlayerStatsProvider.cs`

- [ ] **Step 1: Write IPlayerStatsProvider.cs**

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
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
}
```

- [ ] **Step 2: Wait for Unity compilation**

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IPlayerStatsProvider.cs
git commit -m "feat: add IPlayerStatsProvider interface"
```

---

### Task 3: Create ITargetStatsProvider interface

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ITargetStatsProvider.cs`

- [ ] **Step 1: Write ITargetStatsProvider.cs**

```csharp
using System;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface ITargetStatsProvider
    {
        float MPPercent { get; }
        int CurrentMP { get; }
        int MaxMP { get; }
        BuffInfo[] ActiveBuffs { get; }
        event Action<float, int, int> OnMPChanged;
        event Action<BuffInfo[]> OnBuffsChanged;
    }
}
```

- [ ] **Step 2: Wait for Unity compilation**

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ITargetStatsProvider.cs
git commit -m "feat: add ITargetStatsProvider interface"
```

---

### Task 4: Create BuffIcon component

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/BuffIcon.cs`

- [ ] **Step 1: Write BuffIcon.cs**

```csharp
using Hotfix.GameSystems.Sys3C.Core.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class BuffIcon : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private Image _overlay;
        [SerializeField] private Image _border;

        private float _expireTime;
        private float _duration;
        private bool _isPermanent;

        public void SetBuff(BuffInfo buff)
        {
            _isPermanent = buff.RemainingTime < 0;
            if (_icon != null && buff.Icon != null)
                _icon.sprite = buff.Icon;

            if (_border != null)
                _border.color = buff.IsDebuff
                    ? new Color(0.8f, 0.2f, 0.2f, 1f)
                    : new Color(0.2f, 0.8f, 0.2f, 1f);

            _duration = buff.Duration;
            _expireTime = _isPermanent ? float.MaxValue : Time.time + buff.RemainingTime;

            if (_overlay != null)
            {
                _overlay.gameObject.SetActive(!_isPermanent);
                _overlay.fillAmount = 0f;
            }
        }

        private void Update()
        {
            if (_isPermanent || _overlay == null || _duration <= 0) return;
            float remaining = Mathf.Max(0f, _expireTime - Time.time);
            _overlay.fillAmount = 1f - remaining / _duration;
        }
    }
}
```

- [ ] **Step 2: Wait for Unity compilation**

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/BuffIcon.cs
git commit -m "feat: add BuffIcon component with countdown overlay"
```

---

### Task 5: Create PlayerHudPanel

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/PlayerHudPanel.cs`

- [ ] **Step 1: Write PlayerHudPanel.cs**

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class PlayerHudPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Base;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "PlayerHudPanel";

        [SerializeField] private Image _portrait;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _levelText;
        [SerializeField] private Image _hpFill;
        [SerializeField] private TMP_Text _hpOverlay;
        [SerializeField] private Image _mpFill;
        [SerializeField] private TMP_Text _mpOverlay;
        [SerializeField] private Transform _buffContainer;
        [SerializeField] private GameObject _buffIconPrefab;

        private IPlayerStatsProvider _provider;
        private readonly List<GameObject> _activeBuffIcons = new();

        public void Bind(IPlayerStatsProvider provider)
        {
            Unbind();
            _provider = provider;

            if (_nameText != null) _nameText.text = provider.Name;
            if (_levelText != null) _levelText.text = $"Lv.{provider.Level}";
            if (_portrait != null && provider.Portrait != null) _portrait.sprite = provider.Portrait;

            UpdateHP(provider.HPPercent, provider.CurrentHP, provider.MaxHP);
            UpdateMP(provider.MPPercent, provider.CurrentMP, provider.MaxMP);
            UpdateBuffs(provider.ActiveBuffs);

            provider.OnHPChanged += UpdateHP;
            provider.OnMPChanged += UpdateMP;
            provider.OnBuffsChanged += UpdateBuffs;
        }

        public void Unbind()
        {
            if (_provider != null)
            {
                _provider.OnHPChanged -= UpdateHP;
                _provider.OnMPChanged -= UpdateMP;
                _provider.OnBuffsChanged -= UpdateBuffs;
                _provider = null;
            }
            ClearBuffIcons();
        }

        private void UpdateHP(float percent, int current, int max)
        {
            if (_hpFill != null) _hpFill.fillAmount = percent;
            if (_hpOverlay != null) _hpOverlay.text = $"{current}/{max}";
        }

        private void UpdateMP(float percent, int current, int max)
        {
            if (_mpFill != null) _mpFill.fillAmount = percent;
            if (_mpOverlay != null) _mpOverlay.text = $"{current}/{max}";
        }

        private void UpdateBuffs(BuffInfo[] buffs)
        {
            ClearBuffIcons();
            if (buffs == null || buffs.Length == 0 || _buffContainer == null || _buffIconPrefab == null) return;

            foreach (var buff in buffs)
            {
                var go = GetOrCreateBuffIcon();
                go.SetActive(true);
                var icon = go.GetComponent<BuffIcon>();
                if (icon == null) icon = go.AddComponent<BuffIcon>();
                icon.SetBuff(buff);
            }
        }

        private GameObject GetOrCreateBuffIcon()
        {
            foreach (var go in _activeBuffIcons)
            {
                if (!go.activeSelf) return go;
            }
            var newGo = Instantiate(_buffIconPrefab, _buffContainer);
            _activeBuffIcons.Add(newGo);
            return newGo;
        }

        private void ClearBuffIcons()
        {
            foreach (var go in _activeBuffIcons)
            {
                if (go != null) go.SetActive(false);
            }
        }

        private new void OnDestroy()
        {
            Unbind();
        }
    }
}
```

- [ ] **Step 2: Wait for Unity compilation**

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/PlayerHudPanel.cs
git commit -m "feat: add PlayerHudPanel for top-left character HUD"
```

---

### Task 6: Modify TargetPanel

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/TargetPanel.cs`

- [ ] **Step 1: Rewrite TargetPanel.cs**

Replace the entire file with:

```csharp
using System.Collections.Generic;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class TargetPanel : UIPanel
    {
        public override LayerType Layer => LayerType.Base;
        public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        public override string PanelId => "TargetPanel";

        [SerializeField] private Image _portrait;
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _levelText;
        [SerializeField] private Image _hpFill;
        [SerializeField] private TMP_Text _hpOverlay;
        [SerializeField] private Image _mpFill;
        [SerializeField] private TMP_Text _mpOverlay;
        [SerializeField] private Transform _buffContainer;
        [SerializeField] private GameObject _buffIconPrefab;
        [SerializeField] private GameObject _contentRoot;

        private ITargetable _currentTarget;
        private ITargetStatsProvider _targetStats;
        private readonly List<GameObject> _activeBuffIcons = new();

        public void Bind(ITargetable target)
        {
            Clear();
            _currentTarget = target;
            _targetStats = target as ITargetStatsProvider;

            if (_nameText != null) _nameText.text = target.DisplayName;
            if (_levelText != null) _levelText.text = $"Lv.{target.Level}";
            if (_portrait != null && target.Portrait != null) _portrait.sprite = target.Portrait;

            UpdateHP(target.HPPercent, target.CurrentHP, target.MaxHP);
            target.OnHPChanged += UpdateHP;
            target.OnDeath += OnTargetDeath;

            if (_targetStats != null)
            {
                UpdateMP(_targetStats.MPPercent, _targetStats.CurrentMP, _targetStats.MaxMP);
                UpdateBuffs(_targetStats.ActiveBuffs);
                _targetStats.OnMPChanged += UpdateMP;
                _targetStats.OnBuffsChanged += UpdateBuffs;
            }

            if (_contentRoot != null) _contentRoot.SetActive(true);
        }

        public void Clear()
        {
            if (_currentTarget != null)
            {
                _currentTarget.OnHPChanged -= UpdateHP;
                _currentTarget.OnDeath -= OnTargetDeath;
                _currentTarget = null;
            }
            if (_targetStats != null)
            {
                _targetStats.OnMPChanged -= UpdateMP;
                _targetStats.OnBuffsChanged -= UpdateBuffs;
                _targetStats = null;
            }
            ClearBuffIcons();
            if (_contentRoot != null) _contentRoot.SetActive(false);
        }

        private void UpdateHP(float percent, int current, int max)
        {
            if (_hpFill != null) _hpFill.fillAmount = percent;
            if (_hpOverlay != null) _hpOverlay.text = $"{current}/{max}";
        }

        private void UpdateMP(float percent, int current, int max)
        {
            if (_mpFill != null) _mpFill.fillAmount = percent;
            if (_mpOverlay != null) _mpOverlay.text = $"{current}/{max}";
        }

        private void UpdateBuffs(BuffInfo[] buffs)
        {
            ClearBuffIcons();
            if (buffs == null || buffs.Length == 0 || _buffContainer == null || _buffIconPrefab == null) return;

            foreach (var buff in buffs)
            {
                var go = GetOrCreateBuffIcon();
                go.SetActive(true);
                var icon = go.GetComponent<BuffIcon>();
                if (icon == null) icon = go.AddComponent<BuffIcon>();
                icon.SetBuff(buff);
            }
        }

        private GameObject GetOrCreateBuffIcon()
        {
            foreach (var go in _activeBuffIcons)
            {
                if (!go.activeSelf) return go;
            }
            var newGo = Instantiate(_buffIconPrefab, _buffContainer);
            _activeBuffIcons.Add(newGo);
            return newGo;
        }

        private void ClearBuffIcons()
        {
            foreach (var go in _activeBuffIcons)
            {
                if (go != null) go.SetActive(false);
            }
        }

        private void OnTargetDeath()
        {
            Clear();
            UIManager.Instance?.HideAlwaysAsync(PanelId);
        }

        private new void OnDestroy()
        {
            Clear();
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

The older serialized fields `_hpSlider` and `_hpText` are removed. The prefab will be updated in Task 8.

- [ ] **Step 3: Commit**

```
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/TargetPanel.cs
git commit -m "refactor: replace TargetPanel Slider with Image.fillAmount, add MP bar and buff icons"
```

---

### Task 7: Create BuffIcon prefab via MCP

**Files:**
- Create: `Assets/Resources/Prefabs/UI/BuffIcon.prefab`

Requires Unity Editor open with MCP server running.

- [ ] **Step 1: Create BuffIcon prefab GameObject**

Use MCP `gameobject-create` in scene:
```
name: "BuffIcon"
```

- [ ] **Step 2: Add icon child Image**

Use MCP `gameobject-create` as child of BuffIcon:
```
name: "Icon"
parent: BuffIcon
```
Add Image component via `gameobject-component-add` with type `UnityEngine.UI.Image`.

- [ ] **Step 3: Add overlay child Image**

Use MCP `gameobject-create` as child of BuffIcon:
```
name: "Overlay"
parent: BuffIcon
```
Add Image component. Set color to rgba(0, 0, 0, 0.6), Image.fillMethod = Vertical (3), fillOrigin = Bottom (0).

- [ ] **Step 4: Add border child Image**

Use MCP `gameobject-create` as child of BuffIcon:
```
name: "Border"
parent: BuffIcon
```

- [ ] **Step 5: Add BuffIcon script**

Use MCP `gameobject-component-add` on BuffIcon root:
```
component: "Hotfix.GameSystems.UI.BuffIcon"
```

- [ ] **Step 6: Wire serialized fields**

Use MCP `gameobject-component-modify` on BuffIcon component to assign `_icon`, `_overlay`, `_border` to the corresponding child GameObjects' Image components.

- [ ] **Step 7: Save as prefab**

Use MCP `assets-prefab-create`:
```
prefabAssetPath: "Assets/Resources/Prefabs/UI/BuffIcon.prefab"
gameObjectRef: BuffIcon
```

- [ ] **Step 8: Delete scene GameObject (cleanup)**

Use MCP `gameobject-destroy` on the scene BuffIcon.

- [ ] **Step 9: Commit**

```
git add Assets/Resources/Prefabs/UI/BuffIcon.prefab
git commit -m "feat: add BuffIcon prefab"
```

---

### Task 8: Create PlayerHudPanel prefab via MCP

**Files:**
- Create: `Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab`

- [ ] **Step 1: Create root GameObject**

Use MCP `gameobject-create`:
```
name: "PlayerHudPanel"
```

Add components: `UnityEngine.UI.CanvasScaler`, `UnityEngine.UI.GraphicRaycaster`, `Hotfix.GameSystems.UI.PlayerHudPanel`.

- [ ] **Step 2: Create Portrait (round avatar)**

```
child: "Portrait" under PlayerHudPanel
component: UnityEngine.UI.Image
width: 40, height: 40, anchor: top-left
```

- [ ] **Step 3: Create NameLevelRow**

```
child: "NameLevelRow" under PlayerHudPanel (horizontal layout)
├── "NameText" — TMP_Text, font-size 13, color white
└── "LevelText" — TMP_Text, font-size 12, color yellow
```

- [ ] **Step 4: Create HP bar**

```
child: "HPBar" under PlayerHudPanel
├── "Background" — Image, color dark gray (0.2,0.2,0.2,1), width 140 height 10
├── "Fill" — Image (child of Background), color red, fillAmount, stretch
└── "Overlay" — TMP_Text, centered, font-size 9, color white
```

- [ ] **Step 5: Create MP bar**

Same structure as HP bar:
```
child: "MPBar" under PlayerHudPanel
├── "Background" — Image, dark gray, width 140 height 10
├── "Fill" — Image, color blue
└── "Overlay" — TMP_Text, centered, font-size 9
```

- [ ] **Step 6: Create BuffContainer**

```
child: "BuffContainer" under PlayerHudPanel
HorizontalLayoutGroup, spacing 2
```

- [ ] **Step 7: Wire serialized fields**

Use `gameobject-component-modify` on PlayerHudPanel component to assign all `[SerializeField]` fields.

- [ ] **Step 8: Save as prefab**

```
prefabAssetPath: "Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab"
```

- [ ] **Step 9: Cleanup scene and commit**

```
git add Assets/Resources/Prefabs/UI/PlayerHudPanel.prefab
git commit -m "feat: add PlayerHudPanel prefab"
```

---

### Task 9: Update TargetPanel prefab via MCP

**Files:**
- Modify: TargetPanel prefab (existing path, update via MCP)

Find existing prefab path with `assets-find "TargetPanel t:Prefab"`.

- [ ] **Step 1: Open TargetPanel prefab**

Use MCP `assets-prefab-open` on the TargetPanel prefab.

- [ ] **Step 2: Remove old Slider-based HP**

Destroy `_hpSlider` and `_hpText` GameObjects. Replace with HPBar (Background + Fill Image + Overlay TMP_Text) matching PlayerHudPanel structure.

- [ ] **Step 3: Add MP bar**

Create MPBar child with same structure as HP bar.

- [ ] **Step 4: Add BuffContainer**

Create empty GameObject with HorizontalLayoutGroup.

- [ ] **Step 5: Wire serialized fields**

Assign all new `[SerializeField]` fields on the TargetPanel component.

- [ ] **Step 6: Save and close prefab**

Use MCP `assets-prefab-save`, then `assets-prefab-close`.

- [ ] **Step 7: Commit**

```
git add Assets/Resources/Prefabs/UI/TargetPanel.prefab  # or actual path
git commit -m "feat: update TargetPanel prefab with MP bar and buff icons"
```

---

### Task 10: Register PlayerHudPanel in UIManager

**Files:**
- Modify: Game startup/initialization code that registers panels with UIManager

- [ ] **Step 1: Locate panel registration code**

Search for existing `UIManager.Instance.Register` calls to find where panels are instantiated and registered.

- [ ] **Step 2: Add PlayerHudPanel registration**

Instantiate PlayerHudPanel prefab and call `UIManager.Instance.Register(playerHudPanel)` followed by `UIManager.Instance.ShowAlwaysAsync("PlayerHudPanel")` to make it always visible.

- [ ] **Step 3: Commit**

```
git add <registration file>
git commit -m "feat: register PlayerHudPanel in UIManager"
```
