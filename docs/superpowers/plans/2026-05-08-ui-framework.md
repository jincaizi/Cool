# UI Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a complete UGUI-based UI framework with 5-layer Canvas architecture, lifecycle-managed panels, DOTween animation system, and screen adaptation.

**Architecture:** UIManager singleton manages 5 Canvas layers. UIPanel is the abstract base all panels inherit from, with lifecycle hooks and visibility mode declarations. UIAnimation + SequenceBuilder provide DOTween-based animation. ScreenAdapter handles CanvasScaler and SafeArea.

**Tech Stack:** Unity 2022.3 LTS, UGUI, DOTween (Plugins), UniTask, Core (AOT assembly for Res)

**Prerequisites:** UniTask (Task 1 from Resource plan), Core.asmdef with Res system (Tasks 1-6 from Resource plan), DOTween (already installed in Plugins)

---

### Task 1: Create Assembly Definition and UIConst

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/UI.asmdef`
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Const/UIConst.cs`

Foundation: assembly definition and shared enums.

- [ ] **Step 1: Create UI.asmdef**

```json
{
    "name": "Hotfix.GameSystems.UI",
    "rootNamespace": "Hotfix.GameSystems.UI",
    "references": [
        "Core",
        "UniTask"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Create UIConst.cs**

```csharp
namespace Hotfix.GameSystems.UI
{
    public enum LayerType
    {
        Base   = 0,   // HUD, joystick, status bar — always visible
        Main   = 1,   // Bag, skills, character, settings — stack managed
        Popup  = 2,   // Confirm, tips, detail popup — stack managed
        Top    = 3,   // Loading, toast, reconnect — overlay
        Guide  = 4    // Newbie guide mask — overlay
    }

    public enum VisibilityMode
    {
        ToggleActive,   // SetActive(true/false) — full GC release, low-frequency panels
        CanvasSwitch,   // Canvas.enabled — keeps GO tree, medium-frequency
        CanvasGroup     // alpha + blocksRaycasts — fastest, high-frequency
    }

    public static class UIConst
    {
        public static readonly int[] SortOrders =
        {
            1000,   // Base
            2000,   // Main
            3000,   // Popup
            4000,   // Top
            5000    // Guide
        };
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/UI.asmdef \
        Assets/Scripts/Hotfix/GameSystems/UI/Const/UIConst.cs
git commit -m "$(cat <<'EOF'
feat: add UI assembly definition and UIConst enums
EOF
)"
```

---

### Task 2: Create UIAnimPreset

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimPreset.cs`

Serializable data container for animation presets.

- [ ] **Step 1: Create UIAnimPreset.cs**

```csharp
using System;
using DG.Tweening;

namespace Hotfix.GameSystems.UI
{
    public enum Direction
    {
        Left,
        Right,
        Top,
        Bottom
    }

    [Serializable]
    public class UIAnimPreset
    {
        public float Duration = 0.3f;
        public Ease Ease = Ease.OutCubic;
        public float Delay;
        public bool Fade;
        public bool Scale;
        public bool Slide;
        public Direction SlideDir;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimPreset.cs
git commit -m "$(cat <<'EOF'
feat: add UIAnimPreset data container
EOF
)"
```

---

### Task 3: Create UITweenExtensions

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Animation/UITweenExtensions.cs`

Static DOTween extension methods for Punch, Shake, CountUp, Flash.

- [ ] **Step 1: Create UITweenExtensions.cs**

```csharp
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public static class UITweenExtensions
    {
        /// <summary>Punch scale — icon click bounce / hit feedback</summary>
        public static Tweener Punch(this Transform target, float strength = 0.2f, float duration = 0.15f)
        {
            return target.DOPunchScale(Vector3.one * strength, duration, 1, 0f);
        }

        /// <summary>Shake position — error / invalid input feedback</summary>
        public static Tweener Shake(this Transform target, float strength = 10f, float duration = 0.3f)
        {
            return target.DOShakeAnchorPos(duration, strength, 20, 90f, false, true);
        }

        /// <summary>Count up a numeric Text from 0 to target value</summary>
        public static Tweener CountUp(this Text text, int targetValue, float duration = 0.5f)
        {
            var current = 0;
            return DOTween.To(() => current, v =>
            {
                current = v;
                text.text = current.ToString();
            }, targetValue, duration);
        }

        /// <summary>Flash graphic color — highlight / on-hit white flash</summary>
        public static Tweener Flash(this Graphic graphic, Color flashColor, float duration = 0.1f)
        {
            var original = graphic.color;
            graphic.color = flashColor;
            return graphic.DOColor(original, duration);
        }

        /// <summary>Flash white (convenience overload)</summary>
        public static Tweener FlashWhite(this Graphic graphic, float duration = 0.1f)
        {
            return graphic.Flash(Color.white, duration);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Animation/UITweenExtensions.cs
git commit -m "$(cat <<'EOF'
feat: add UITweenExtensions — Punch, Shake, CountUp, Flash
EOF
)"
```

---

### Task 4: Create SequenceBuilder

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Animation/SequenceBuilder.cs`

Fluent API for building DOTween sequences.

- [ ] **Step 1: Create SequenceBuilder.cs**

```csharp
using System;
using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public class SequenceBuilder
    {
        private readonly Transform _target;
        private readonly CanvasGroup _canvasGroup;
        private readonly Sequence _sequence;

        public SequenceBuilder(Transform target, CanvasGroup canvasGroup)
        {
            _target = target;
            _canvasGroup = canvasGroup;
            _sequence = DOTween.Sequence();
        }

        public SequenceBuilder FadeIn(float duration = 0.3f)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
                _sequence.Join(_canvasGroup.DOFade(1f, duration));
            }
            return this;
        }

        public SequenceBuilder FadeOut(float duration = 0.3f)
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _sequence.Join(_canvasGroup.DOFade(0f, duration));
            }
            return this;
        }

        public SequenceBuilder ScaleIn(float duration = 0.3f, float fromScale = 0.9f)
        {
            _target.localScale = Vector3.one * fromScale;
            _sequence.Join(_target.DOScale(1f, duration));
            return this;
        }

        public SequenceBuilder ScaleOut(float duration = 0.3f, float toScale = 0.95f)
        {
            _target.localScale = Vector3.one;
            _sequence.Join(_target.DOScale(toScale, duration));
            return this;
        }

        public SequenceBuilder SlideIn(Direction dir, float duration = 0.3f, float distance = 100f)
        {
            var anchored = _target as RectTransform;
            if (anchored == null) return this;

            var startPos = anchored.anchoredPosition;
            var offset = dir switch
            {
                Direction.Left   => new Vector2(-distance, 0),
                Direction.Right  => new Vector2(distance, 0),
                Direction.Top    => new Vector2(0, distance),
                Direction.Bottom => new Vector2(0, -distance),
                _ => Vector2.zero
            };

            anchored.anchoredPosition = startPos + offset;
            _sequence.Join(anchored.DOAnchorPos(startPos, duration));
            return this;
        }

        public SequenceBuilder Join(Action action)
        {
            action();
            return this;
        }

        public SequenceBuilder Then(Action action)
        {
            _sequence.AppendCallback(() => action());
            return this;
        }

        public SequenceBuilder Delay(float seconds)
        {
            _sequence.AppendInterval(seconds);
            return this;
        }

        public Sequence Play()
        {
            return _sequence.Play();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Animation/SequenceBuilder.cs
git commit -m "$(cat <<'EOF'
feat: add SequenceBuilder fluent API for DOTween sequences
EOF
)"
```

---

### Task 5: Create UIAnimation Component

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimation.cs`

MonoBehaviour component that builds and plays animation presets.

- [ ] **Step 1: Create UIAnimation.cs**

```csharp
using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public class UIAnimation : MonoBehaviour
    {
        public UIAnimPreset ShowPreset;
        public UIAnimPreset HidePreset;

        private CanvasGroup _canvasGroup;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
        }

        public Sequence PlayShow()
        {
            KillAll();
            var seq = BuildSequence(ShowPreset, isShow: true);
            seq.Play();
            return seq;
        }

        public Sequence PlayHide()
        {
            KillAll();
            var seq = BuildSequence(HidePreset, isShow: false);
            seq.Play();
            return seq;
        }

        public SequenceBuilder Build()
        {
            KillAll();
            return new SequenceBuilder(transform, _canvasGroup);
        }

        public void KillAll()
        {
            DOTween.Kill(transform);
        }

        public Tweener FadeIn(float duration = 0.3f)
        {
            if (_canvasGroup == null) return null;
            _canvasGroup.alpha = 0f;
            return _canvasGroup.DOFade(1f, duration).SetTarget(transform);
        }

        public Tweener ScaleIn(float duration = 0.3f)
        {
            transform.localScale = Vector3.one * 0.9f;
            return transform.DOScale(1f, duration).SetTarget(transform);
        }

        public Tweener SlideIn(Direction dir, float duration = 0.3f)
        {
            var rt = transform as RectTransform;
            if (rt == null) return null;

            var distance = dir switch
            {
                Direction.Left or Direction.Right => rt.rect.width,
                Direction.Top or Direction.Bottom => rt.rect.height,
                _ => 100f
            };

            var offset = dir switch
            {
                Direction.Left   => new Vector2(-distance, 0),
                Direction.Right  => new Vector2(distance, 0),
                Direction.Top    => new Vector2(0, distance),
                Direction.Bottom => new Vector2(0, -distance),
                _ => Vector2.zero
            };

            var startPos = rt.anchoredPosition;
            rt.anchoredPosition = startPos + offset;
            return rt.DOAnchorPos(startPos, duration).SetTarget(transform);
        }

        private Sequence BuildSequence(UIAnimPreset preset, bool isShow)
        {
            if (preset == null) return DOTween.Sequence();

            var seq = DOTween.Sequence();

            if (preset.Delay > 0f)
                seq.AppendInterval(preset.Delay);

            if (isShow)
            {
                if (preset.Fade && _canvasGroup != null)
                {
                    _canvasGroup.alpha = 0f;
                    seq.Join(_canvasGroup.DOFade(1f, preset.Duration).SetEase(preset.Ease));
                }
                if (preset.Scale)
                {
                    transform.localScale = Vector3.one * 0.9f;
                    seq.Join(transform.DOScale(1f, preset.Duration).SetEase(preset.Ease));
                }
                if (preset.Slide)
                {
                    var rt = transform as RectTransform;
                    if (rt != null)
                    {
                        var dist = preset.SlideDir is Direction.Left or Direction.Right ? rt.rect.width : rt.rect.height;
                        var off = preset.SlideDir switch
                        {
                            Direction.Left   => new Vector2(-dist, 0),
                            Direction.Right  => new Vector2(dist, 0),
                            Direction.Top    => new Vector2(0, dist),
                            Direction.Bottom => new Vector2(0, -dist),
                            _ => Vector2.zero
                        };
                        var orig = rt.anchoredPosition;
                        rt.anchoredPosition = orig + off;
                        seq.Join(rt.DOAnchorPos(orig, preset.Duration).SetEase(preset.Ease));
                    }
                }
            }
            else
            {
                if (preset.Fade && _canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                    seq.Join(_canvasGroup.DOFade(0f, preset.Duration).SetEase(preset.Ease));
                }
                if (preset.Scale)
                {
                    transform.localScale = Vector3.one;
                    seq.Join(transform.DOScale(0.95f, preset.Duration).SetEase(preset.Ease));
                }
                if (preset.Slide)
                {
                    var rt = transform as RectTransform;
                    if (rt != null)
                    {
                        var dist = preset.SlideDir is Direction.Left or Direction.Right ? rt.rect.width : rt.rect.height;
                        var off = preset.SlideDir switch
                        {
                            Direction.Left   => new Vector2(-dist, 0),
                            Direction.Right  => new Vector2(dist, 0),
                            Direction.Top    => new Vector2(0, dist),
                            Direction.Bottom => new Vector2(0, -dist),
                            _ => Vector2.zero
                        };
                        rt.DOAnchorPos(rt.anchoredPosition + off, preset.Duration).SetEase(preset.Ease);
                    }
                }
            }

            seq.SetTarget(transform);
            return seq;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimation.cs
git commit -m "$(cat <<'EOF'
feat: add UIAnimation component with preset-driven DOTween animations
EOF
)"
```

---

### Task 6: Create ScreenAdapter

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs`

Configures CanvasScaler and handles SafeArea for each Canvas root.

- [ ] **Step 1: Create ScreenAdapter.cs**

```csharp
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    public class ScreenAdapter : MonoBehaviour
    {
        public bool ApplySafeArea = true;
        public bool AutoRefreshOnResize = true;

        private CanvasScaler _scaler;
        private RectTransform _rectTransform;

        private void Awake()
        {
            _scaler = GetComponent<CanvasScaler>();
            _rectTransform = GetComponent<RectTransform>();
            ConfigureCanvasScaler();
        }

        private void Start()
        {
            if (ApplySafeArea)
                ApplySafeAreaOffset();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (AutoRefreshOnResize && ApplySafeArea)
                ApplySafeAreaOffset();
        }

        private void ConfigureCanvasScaler()
        {
            if (_scaler == null) return;

            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;
        }

        private void ApplySafeAreaOffset()
        {
            var safeArea = Screen.safeArea;
            var screenSize = new Vector2(Screen.width, Screen.height);

            var anchorMin = safeArea.position / screenSize;
            var anchorMax = (safeArea.position + safeArea.size) / screenSize;

            _rectTransform.anchorMin = anchorMin;
            _rectTransform.anchorMax = anchorMax;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs
git commit -m "$(cat <<'EOF'
feat: add ScreenAdapter for CanvasScaler and SafeArea
EOF
)"
```

---

### Task 7: Create UIPanel Base Class

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs`

Abstract base class for all UI panels with lifecycle, visibility, and animation integration.

- [ ] **Step 1: Create UIPanel.cs**

```csharp
using DG.Tweening;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public abstract class UIPanel : MonoBehaviour
    {
        public abstract LayerType Layer { get; }
        public abstract VisibilityMode Mode { get; }
        public abstract string PanelId { get; }

        public Canvas Canvas { get; private set; }
        public CanvasGroup CanvasGroup { get; private set; }
        public UIAnimation Animation { get; private set; }
        public bool IsVisible { get; private set; }

        protected virtual void Awake()
        {
            Canvas = GetComponent<Canvas>();
            CanvasGroup = GetComponent<CanvasGroup>();
            Animation = GetComponent<UIAnimation>();
        }

        protected virtual void OnPreShow() { }
        protected virtual Sequence PlayShowAnimation()
        {
            return Animation != null ? Animation.PlayShow() : null;
        }
        protected virtual void OnShow() { }
        protected virtual void OnHide() { }
        protected virtual Sequence PlayHideAnimation()
        {
            return Animation != null ? Animation.PlayHide() : null;
        }

        internal void SetVisible(bool visible)
        {
            IsVisible = visible;
        }

        internal void ApplyVisibilityOff()
        {
            switch (Mode)
            {
                case VisibilityMode.ToggleActive:
                    gameObject.SetActive(false);
                    break;
                case VisibilityMode.CanvasSwitch:
                    if (Canvas != null) Canvas.enabled = false;
                    break;
                case VisibilityMode.CanvasGroup:
                    if (CanvasGroup != null)
                    {
                        CanvasGroup.alpha = 0f;
                        CanvasGroup.blocksRaycasts = false;
                    }
                    break;
            }
        }

        internal void ApplyVisibilityOn()
        {
            switch (Mode)
            {
                case VisibilityMode.ToggleActive:
                    gameObject.SetActive(true);
                    break;
                case VisibilityMode.CanvasSwitch:
                    if (Canvas != null) Canvas.enabled = true;
                    break;
                case VisibilityMode.CanvasGroup:
                    if (CanvasGroup != null)
                    {
                        CanvasGroup.alpha = 1f;
                        CanvasGroup.blocksRaycasts = true;
                    }
                    break;
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs
git commit -m "$(cat <<'EOF'
feat: add UIPanel abstract base class with lifecycle and visibility modes
EOF
)"
```

---

### Task 8: Create UIManager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`

Singleton that manages 5 Canvas layers, panel registration, stack, and overlay display.

- [ ] **Step 1: Create UIManager.cs**

```csharp
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        private readonly Canvas[] _layers = new Canvas[5];
        private readonly Dictionary<string, UIPanel> _registry = new Dictionary<string, UIPanel>();
        private readonly Stack<UIPanel> _stack = new Stack<UIPanel>();
        private readonly HashSet<UIPanel> _activeOverlays = new HashSet<UIPanel>();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            CreateCanvasLayers();
        }

        private void CreateCanvasLayers()
        {
            for (int i = 0; i < 5; i++)
            {
                var go = new GameObject($"Canvas_Layer_{(LayerType)i}");
                go.transform.SetParent(transform);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = UIConst.SortOrders[i];

                go.AddComponent<UnityEngine.UI.CanvasScaler>();
                go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                go.AddComponent<ScreenAdapter>();

                _layers[i] = canvas;
            }
        }

        public void Register(UIPanel panel)
        {
            if (panel == null || string.IsNullOrEmpty(panel.PanelId))
            {
                Debug.LogError("UIManager: Cannot register null panel or empty PanelId");
                return;
            }

            if (_registry.ContainsKey(panel.PanelId))
            {
                Debug.LogWarning($"UIManager: Panel '{panel.PanelId}' already registered, replacing.");
            }

            _registry[panel.PanelId] = panel;

            // Reparent to correct layer canvas
            var layerIndex = (int)panel.Layer;
            if (layerIndex >= 0 && layerIndex < _layers.Length && _layers[layerIndex] != null)
            {
                panel.transform.SetParent(_layers[layerIndex].transform, false);
            }

            // Start hidden
            panel.ApplyVisibilityOff();
        }

        // ===== Stack (Main / Popup) =====

        public async UniTask PushAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
            {
                Debug.LogError($"UIManager: Panel '{panelId}' not registered");
                return;
            }

            // Pause current top panel
            if (_stack.Count > 0)
            {
                var current = _stack.Peek();
                current.OnHide();
            }

            // Show new panel
            await ShowPanelAsync(panel);
            _stack.Push(panel);
        }

        public async UniTask PopAsync()
        {
            if (_stack.Count == 0) return;

            var panel = _stack.Pop();
            await HidePanelAsync(panel);

            // Resume underlying panel
            if (_stack.Count > 0)
            {
                var newTop = _stack.Peek();
                newTop.ApplyVisibilityOn();
                newTop.OnShow();
            }
        }

        public async void PopTo(string panelId)
        {
            while (_stack.Count > 0 && _stack.Peek().PanelId != panelId)
            {
                await PopAsync();
            }
        }

        // ===== Overlay (Top / Guide) =====

        public async UniTask OpenAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
            {
                Debug.LogError($"UIManager: Panel '{panelId}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
            _activeOverlays.Add(panel);
        }

        public async UniTask CloseAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
                return;

            await HidePanelAsync(panel);
            _activeOverlays.Remove(panel);
        }

        // ===== Always (Base) =====

        public async UniTask ShowAlwaysAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
            {
                Debug.LogError($"UIManager: Panel '{panelId}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
        }

        public async UniTask HideAlwaysAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
                return;

            await HidePanelAsync(panel);
        }

        // ===== Lookup =====

        public T GetPanel<T>(string panelId) where T : UIPanel
        {
            _registry.TryGetValue(panelId, out var panel);
            return panel as T;
        }

        // ===== Internal =====

        private async UniTask ShowPanelAsync(UIPanel panel)
        {
            panel.ApplyVisibilityOn();
            panel.OnPreShow();

            var seq = panel.PlayShowAnimation();
            if (seq != null)
                await WaitForSequence(seq);

            panel.OnShow();
            panel.SetVisible(true);
        }

        private async UniTask HidePanelAsync(UIPanel panel)
        {
            panel.OnHide();

            var seq = panel.PlayHideAnimation();
            if (seq != null)
                await WaitForSequence(seq);

            panel.ApplyVisibilityOff();
            panel.SetVisible(false);
        }

        private static async UniTask WaitForSequence(DG.Tweening.Sequence seq)
        {
            var tcs = new UniTaskCompletionSource();
            seq.OnComplete(() => tcs.TrySetResult());
            await tcs.Task;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs
git commit -m "$(cat <<'EOF'
feat: add UIManager singleton with 5-layer Canvas, stack, and overlay management
EOF
)"
```

---

### Task 9: Verify Compilation

**Files:** None (verification only)

- [ ] **Step 1: Check all files exist**

```bash
find Assets/Scripts/Hotfix/GameSystems/UI -name "*.cs" -type f
```

Expected: 8 C# files across Const/, Core/, Animation/

- [ ] **Step 2: Fix any compilation issues**

Open Unity Editor, check console. Common issues:
- DOTween assembly not found: verify `overrideReferences: false` in UI.asmdef
- UniTask reference: verify UniTask package resolved
- Core reference: verify Core.asmdef exists in AOT

- [ ] **Step 3: Commit any fixes**

```bash
git add -A
git commit -m "$(cat <<'EOF'
fix: resolve compilation issues for UI framework
EOF
)"
```

---

### Approval Gate

**What this system provides after Task 9:**
- 5-layer Canvas system with auto-created Canvas roots
- UIPanel abstract base with lifecycle (OnPreShow → PlayShowAnimation → OnShow / OnHide → PlayHideAnimation)
- 3 visibility modes: ToggleActive, CanvasSwitch, CanvasGroup
- Stack management for Main/Popup, Overlay management for Top/Guide, Always for Base
- DOTween animation: UIAnimPreset, UIAnimation component, SequenceBuilder, extension methods
- ScreenAdapter: CanvasScaler auto-config + SafeArea

**What this system does NOT cover (out of scope):**
- Concrete UI panels (HUD, Bag, Confirm, Toast, etc.) — these are built on top of this framework
- UI Prefab creation and Addressable marking (Unity Editor manual work)
- DOTween Settings configuration
- Canvas Prefab creation (UIManager creates them programmatically)
- Unit tests (requires Unity PlayMode + Addressables setup)
