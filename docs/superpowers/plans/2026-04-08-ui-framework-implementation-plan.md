# UI Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an independent, lightweight MVVM UI framework for Unity UGUI with hot-reload support.

**Architecture:** Layered architecture with UI Framework independent from business logic. Each UI layer has its own Canvas with Sort Order separation. Framework code lives in `Assets/Scripts/Hotfix/GameSystems/UI/Framework/`.

**Tech Stack:** Unity UGUI, DOTween, HybridCLR Hotfix

---

## File Structure

```
Assets/Scripts/Hotfix/GameSystems/UI/
├── Framework/
│   ├── Core/
│   │   ├── UIConst.cs
│   │   ├── UIPool.cs
│   │   ├── UIPanel.cs
│   │   └── UIManager.cs
│   ├── Binding/
│   │   ├── UIDataBinding.cs
│   │   └── ViewModelBase.cs
│   ├── Message/
│   │   └── UIMessage.cs
│   └── Animation/
│       └── UIAnimation.cs
├── Components/
│   ├── Toast.cs
│   ├── Loading.cs
│   ├── Confirm.cs
│   └── Tips.cs
├── Panel/
│   └── HUD/
│       ├── HUDPanel.cs
│       └── HUDViewModel.cs
└── UIEntry.cs
```

---

## Phase 1: Core Framework

### Task 1: UIConst.cs - Constants

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Framework/Core/UIConst.cs`

- [ ] **Step 1: Create UIConst.cs with layer constants**

```csharp
namespace Hotfix.GameSystems.UI.Framework.Core
{
    /// <summary>
    /// UI layer constants for sort order separation.
    /// Each layer is an independent Canvas.
    /// </summary>
    public static class UIConst
    {
        // Layer sort order ranges
        public const int Layer_Base = 0;
        public const int Layer_Main = 1000;
        public const int Layer_Popup = 2000;
        public const int Layer_Guide = 3000;
        public const int Layer_Toast = 4000;

        // Layer canvas names (for hierarchy organization)
        public const string Canvas_Base = "Canvas_Base";
        public const string Canvas_Main = "Canvas_Main";
        public const string Canvas_Popup = "Canvas_Popup";
        public const string Canvas_Guide = "Canvas_Guide";
        public const string Canvas_Toast = "Canvas_Toast";

        // Default animation durations
        public const float DefaultAnimDuration = 0.3f;

        // Pool defaults
        public const int DefaultPreLoadCount = 3;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Framework/Core/UIConst.cs
git commit -m "feat(ui): add UIConst with layer constants"
```

---

### Task 2: UIPool.cs - Object Pool

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Framework/Core/UIPool.cs`

- [ ] **Step 1: Create UIPool.cs with reference counting**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.UI.Framework.Core
{
    /// <summary>
    /// Object pool for UI panels with reference counting.
    /// Supports pre-warm and auto-release on reference count reaching 0.
    /// </summary>
    public class UIPool
    {
        private readonly Dictionary<string, PoolData> _pools = new();
        private readonly Dictionary<UIPanel, string> _panelToPool = new();

        /// <summary>
        /// Register a prefab path for pooling.
        /// </summary>
        public void Register(string prefabPath, int preLoadCount = 0)
        {
            if (_pools.ContainsKey(prefabPath))
            {
                Debug.LogWarning($"Pool already registered for: {prefabPath}");
                return;
            }

            var poolData = new PoolData(prefabPath);
            _pools[prefabPath] = poolData;

            // Pre-warm
            for (int i = 0; i < preLoadCount; i++)
            {
                var panel = poolData.Instantiate();
                panel.gameObject.SetActive(false);
                poolData.Return(panel);
            }

            Debug.Log($"UIPool registered: {prefabPath}, preloaded: {preLoadCount}");
        }

        /// <summary>
        /// Get a panel from pool (increments reference count).
        /// </summary>
        public T Get<T>(string prefabPath) where T : UIPanel
        {
            if (!_pools.TryGetValue(prefabPath, out var poolData))
            {
                Debug.LogError($"Pool not registered: {prefabPath}");
                return null;
            }

            var panel = poolData.Get();
            if (panel == null)
            {
                panel = poolData.Instantiate();
            }

            panel.gameObject.SetActive(true);
            _panelToPool[panel] = prefabPath;
            poolData.IncrementRef();
            return panel as T;
        }

        /// <summary>
        /// Return a panel to pool (decrements reference count).
        /// </summary>
        public void Release(UIPanel panel)
        {
            if (panel == null) return;

            if (!_panelToPool.TryGetValue(panel, out var prefabPath))
            {
                Debug.LogWarning($"Panel not from pool: {panel.name}");
                return;
            }

            if (!_pools.TryGetValue(prefabPath, out var poolData))
            {
                Debug.LogWarning($"Pool not found: {prefabPath}");
                return;
            }

            panel.gameObject.SetActive(false);
            poolData.Return(panel);
            poolData.DecrementRef();
            _panelToPool.Remove(panel);
        }

        /// <summary>
        /// Get current reference count for a prefab path.
        /// </summary>
        public int GetRefCount(string prefabPath)
        {
            if (_pools.TryGetValue(prefabPath, out var poolData))
            {
                return poolData.RefCount;
            }
            return 0;
        }

        private class PoolData
        {
            private readonly string _prefabPath;
            private readonly Queue<UIPanel> _available = new();
            private int _refCount;
            private readonly Transform _parent;

            public int RefCount => _refCount;

            public PoolData(string prefabPath)
            {
                _prefabPath = prefabPath;

                // Create invisible parent for pooled objects
                var go = new GameObject($"Pool_{System.IO.Path.GetFileNameWithoutExtension(prefabPath)}");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.SetActive(false);
                _parent = go.transform;
            }

            public UIPanel Instantiate()
            {
                var prefab = Resources.Load<GameObject>(_prefabPath);
                if (prefab == null)
                {
                    // Try direct path without extension
                    prefab = Resources.Load<GameObject>(_prefabPath.Replace(".prefab", ""));
                }
                if (prefab == null)
                {
                    Debug.LogError($"Prefab not found: {_prefabPath}");
                    return null;
                }

                var go = UnityEngine.Object.Instantiate(prefab, _parent);
                var panel = go.GetComponent<UIPanel>();
                if (panel == null)
                {
                    Debug.LogError($"Prefab missing UIPanel component: {_prefabPath}");
                    UnityEngine.Object.Destroy(go);
                    return null;
                }
                return panel;
            }

            public UIPanel Get()
            {
                if (_available.Count > 0)
                {
                    return _available.Dequeue();
                }
                return null;
            }

            public void Return(UIPanel panel)
            {
                panel.transform.SetParent(_parent, false);
                _available.Enqueue(panel);
            }

            public void IncrementRef() => _refCount++;
            public void DecrementRef() => _refCount = Math.Max(0, _refCount - 1);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Framework/Core/UIPool.cs
git commit -m "feat(ui): add UIPool with reference counting"
```

---

### Task 3: ViewModelBase.cs - ViewModel Base

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Framework/Binding/ViewModelBase.cs`

- [ ] **Step 1: Create ViewModelBase.cs**

```csharp
using System;
using System.Collections.Generic;

namespace Hotfix.GameSystems.UI.Framework.Binding
{
    /// <summary>
    /// Base class for ViewModels in MVVM pattern.
    /// Provides indexer access and property change notification.
    /// </summary>
    public abstract class ViewModelBase
    {
        private readonly Dictionary<string, object> _properties = new();
        private readonly Dictionary<string, List<Action>> _changeHandlers = new();

        /// <summary>
        /// Indexer for binding access.
        /// </summary>
        public object this[string key]
        {
            get => GetProperty(key);
            set => SetProperty(key, value);
        }

        /// <summary>
        /// Set a property value and notify observers.
        /// </summary>
        protected void SetProperty(string key, object value)
        {
            _properties[key] = value;
            NotifyChanged(key);
        }

        /// <summary>
        /// Get a property value.
        /// </summary>
        protected object GetProperty(string key)
        {
            return _properties.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// Notify that a property changed.
        /// </summary>
        protected void NotifyChanged(string key)
        {
            if (_changeHandlers.TryGetValue(key, out var handlers))
            {
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler?.Invoke();
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError($"NotifyChanged handler error: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Subscribe to property changes.
        /// </summary>
        public void Subscribe(string key, Action handler)
        {
            if (!_changeHandlers.ContainsKey(key))
            {
                _changeHandlers[key] = new List<Action>();
            }
            _changeHandlers[key].Add(handler);
        }

        /// <summary>
        /// Unsubscribe from property changes.
        /// </summary>
        public void Unsubscribe(string key, Action handler)
        {
            if (_changeHandlers.TryGetValue(key, out var handlers))
            {
                handlers.Remove(handler);
            }
        }

        /// <summary>
        /// Refresh data (called when panel shows).
        /// Override in subclass to reload data.
        /// </summary>
        public virtual void Refresh() { }

        /// <summary>
        /// Cleanup resources.
        /// </summary>
        public virtual void Dispose()
        {
            _properties.Clear();
            _changeHandlers.Clear();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Framework/Binding/ViewModelBase.cs
git commit -m "feat(ui): add ViewModelBase with property notification"
```

---

### Task 4: UIDataBinding.cs - Data Binding Core

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Framework/Binding/UIDataBinding.cs`

- [ ] **Step 1: Create UIDataBinding.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.UI.Framework.Binding
{
    /// <summary>
    /// Data binding for UI panels using indexer pattern.
    /// Connects View to ViewModel without reflection overhead.
    /// </summary>
    public class UIDataBinding
    {
        private readonly ViewModelBase _viewModel;
        private readonly Dictionary<string, Func<object>> _getters = new();
        private readonly Dictionary<string, List<Action>> _updateHandlers = new();

        public UIDataBinding(ViewModelBase viewModel)
        {
            _viewModel = viewModel;
        }

        /// <summary>
        /// Register a binding: key -> ViewModel property.
        /// </summary>
        public void Register(string key, Func<object> getter)
        {
            _getters[key] = getter;

            // Subscribe to ViewModel changes
            _viewModel.Subscribe(key, () => OnViewModelChanged(key));
        }

        /// <summary>
        /// Register an update handler (called when bound data changes).
        /// </summary>
        public void RegisterUpdater(string key, Action<object> updater)
        {
            if (!_updateHandlers.ContainsKey(key))
            {
                _updateHandlers[key] = new List<Action<object>>();
            }
            _updateHandlers[key].Add(updater);
        }

        /// <summary>
        /// Notify that a key changed (called from View via panel[key] = value).
        /// </summary>
        public void NotifyChanged(string key)
        {
            if (_updateHandlers.TryGetValue(key, out var handlers))
            {
                var value = _getters.TryGetValue(key, out var getter) ? getter?.Invoke() : _viewModel[key];
                foreach (var handler in handlers)
                {
                    try
                    {
                        handler?.Invoke(value);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"UpdateHandler error for {key}: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Get current value for a key.
        /// </summary>
        public object GetValue(string key)
        {
            if (_getters.TryGetValue(key, out var getter))
            {
                return getter?.Invoke();
            }
            return _viewModel[key];
        }

        /// <summary>
        /// Get all values (for initial binding).
        /// </summary>
        public void RefreshAll()
        {
            foreach (var kvp in _getters)
            {
                NotifyChanged(kvp.Key);
            }
        }

        private void OnViewModelChanged(string key)
        {
            NotifyChanged(key);
        }

        /// <summary>
        /// Cleanup bindings.
        /// </summary>
        public void Unbind()
        {
            _getters.Clear();
            _updateHandlers.Clear();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Framework/Binding/UIDataBinding.cs
git commit -m "feat(ui): add UIDataBinding with indexer pattern"
```

---

### Task 5: UIMessage.cs - Independent Message System

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Framework/Message/UIMessage.cs`

- [ ] **Step 1: Create UIMessage.cs**

```csharp
using System;
using System.Collections.Generic;

namespace Hotfix.GameSystems.UI.Framework.Message
{
    /// <summary>
    /// Independent UI message system.
    /// Separate from KCP networking messages.
    /// Used for internal UI communication.
    /// </summary>
    public struct UIMessage
    {
        public string Type { get; set; }
        public object Body { get; set; }
        public long Timestamp { get; set; }

        public UIMessage(string type, object body = null)
        {
            Type = type;
            Body = body;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    /// <summary>
    /// Message subscription entry for tracking.
    /// </summary>
    public class MessageCallback
    {
        public string Type { get; set; }
        public Action<object> Callback { get; set; }
    }

    /// <summary>
    /// Static message broker for pub/sub pattern.
    /// </summary>
    public static class UIMessage
    {
        private static readonly Dictionary<string, List<Action<object>>> _subscriptions = new();
        private static readonly List<MessageCallback> _pendingRemovals = new();

        /// <summary>
        /// Subscribe to a message type.
        /// </summary>
        public static void Subscribe(string messageType, Action<object> callback)
        {
            if (callback == null) return;

            if (!_subscriptions.ContainsKey(messageType))
            {
                _subscriptions[messageType] = new List<Action<object>>();
            }

            // Avoid duplicate
            if (!_subscriptions[messageType].Contains(callback))
            {
                _subscriptions[messageType].Add(callback);
            }
        }

        /// <summary>
        /// Unsubscribe from a message type.
        /// </summary>
        public static void Unsubscribe(string messageType, Action<object> callback)
        {
            if (callback == null) return;

            if (_subscriptions.TryGetValue(messageType, out var handlers))
            {
                handlers.Remove(callback);
            }
        }

        /// <summary>
        /// Unsubscribe all handlers (call on scene change).
        /// </summary>
        public static void UnsubscribeAll()
        {
            _subscriptions.Clear();
        }

        /// <summary>
        /// Send a message to all subscribers.
        /// </summary>
        public static void Send(string messageType, object body = null)
        {
            var msg = new UIMessage(messageType, body);

            if (_subscriptions.TryGetValue(messageType, out var handlers))
            {
                // Copy list to avoid modification during iteration
                var handlersCopy = new List<Action<object>>(handlers);
                foreach (var handler in handlersCopy)
                {
                    try
                    {
                        handler?.Invoke(body);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogError($"UIMessage handler error ({messageType}): {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Send a typed message (convenience overload).
        /// </summary>
        public static void Send<T>(string messageType, T body) where T : class
        {
            Send(messageType, (object)body);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Framework/Message/UIMessage.cs
git commit -m "feat(ui): add independent UIMessage system"
```

---

### Task 6: UIAnimation.cs - DOTween Extensions

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Framework/Animation/UIAnimation.cs`

- [ ] **Step 1: Create UIAnimation.cs**

```csharp
using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Framework.Animation
{
    /// <summary>
    /// DOTween animation extensions for UI panels.
    /// Provides convenient tween methods for common animations.
    /// </summary>
    public static class UIAnimation
    {
        // Default easing
        private const Ease DefaultEase = Ease.OutQuad;
        private const Ease DefaultCloseEase = Ease.InQuad;

        #region Scale Animations

        /// <summary>
        /// Scale in animation (from zero to original).
        /// </summary>
        public static Tweener ScaleIn(this RectTransform rect, float duration = 0.3f, Action onComplete = null, Vector3? fromScale = null)
        {
            rect.localScale = fromScale ?? Vector3.zero;
            return rect.DOScale(1f, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Scale out animation (from original to zero).
        /// </summary>
        public static Tweener ScaleOut(this RectTransform rect, float duration = 0.3f, Action onComplete = null, Vector3? toScale = null)
        {
            return rect.DOScale(toScale ?? Vector3.zero, duration)
                .SetEase(DefaultCloseEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Scale in from a specific anchor position.
        /// </summary>
        public static Tweener ScaleInFrom(this RectTransform rect, Vector3 startScale, float duration = 0.3f, Action onComplete = null)
        {
            rect.localScale = startScale;
            return rect.DOScale(1f, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        #endregion

        #region Fade Animations

        /// <summary>
        /// Fade in animation using CanvasGroup.
        /// </summary>
        public static Tweener FadeIn(this CanvasGroup canvasGroup, float duration = 0.3f, Action onComplete = null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            return canvasGroup.DOFade(1f, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Fade out animation using CanvasGroup.
        /// </summary>
        public static Tweener FadeOut(this CanvasGroup canvasGroup, float duration = 0.3f, Action onComplete = null, bool disableRaycasts = true)
        {
            return canvasGroup.DOFade(0f, duration)
                .SetEase(DefaultCloseEase)
                .OnComplete(() =>
                {
                    if (disableRaycasts)
                        canvasGroup.blocksRaycasts = false;
                    onComplete?.Invoke();
                });
        }

        /// <summary>
        /// Fade in using Image.color.
        /// </summary>
        public static Tweener FadeIn(this Image image, float duration = 0.3f, Action onComplete = null)
        {
            var color = image.color;
            color.a = 0f;
            image.color = color;
            return image.DOColor(Color.white, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Fade out using Image.color.
        /// </summary>
        public static Tweener FadeOut(this Image image, float duration = 0.3f, Action onComplete = null)
        {
            var color = image.color;
            color.a = 0f;
            return image.DOColor(color, duration)
                .SetEase(DefaultCloseEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        #endregion

        #region Slide Animations

        /// <summary>
        /// Slide in from a direction.
        /// </summary>
        public static Tweener SlideIn(this RectTransform rect, Vector2 startPos, float duration = 0.3f, Action onComplete = null)
        {
            var originalPos = rect.anchoredPosition;
            rect.anchoredPosition = startPos;

            return rect.DOAnchorPos(originalPos, duration)
                .SetEase(DefaultEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Slide out to a direction.
        /// </summary>
        public static Tweener SlideOut(this RectTransform rect, Vector2 endPos, float duration = 0.3f, Action onComplete = null)
        {
            return rect.DOAnchorPos(endPos, duration)
                .SetEase(DefaultCloseEase)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Slide in from left.
        /// </summary>
        public static Tweener SlideInFromLeft(this RectTransform rect, float offset = -100f, float duration = 0.3f, Action onComplete = null)
        {
            return rect.SlideIn(new Vector2(rect.anchoredPosition.x + offset, rect.anchoredPosition.y), duration, onComplete);
        }

        /// <summary>
        /// Slide out to left.
        /// </summary>
        public static Tweener SlideOutToLeft(this RectTransform rect, float offset = -100f, float duration = 0.3f, Action onComplete = null)
        {
            return rect.SlideOut(new Vector2(rect.anchoredPosition.x + offset, rect.anchoredPosition.y), duration, onComplete);
        }

        #endregion

        #region Pop Animations

        /// <summary>
        /// Pop in animation (scale overshoot).
        /// </summary>
        public static Tweener PopIn(this RectTransform rect, float duration = 0.4f, Action onComplete = null)
        {
            rect.localScale = Vector3.zero;
            return rect.DOScale(1f, duration)
                .SetEase(Ease.OutBack)
                .OnComplete(() => onComplete?.Invoke());
        }

        /// <summary>
        /// Pop out animation (scale shrink).
        /// </summary>
        public static Tweener PopOut(this RectTransform rect, float duration = 0.3f, Action onComplete = null)
        {
            return rect.DOScale(0f, duration)
                .SetEase(Ease.InBack)
                .OnComplete(() => onComplete?.Invoke());
        }

        #endregion

        #region Utility

        /// <summary>
        /// Kill all tweens on a RectTransform.
        /// </summary>
        public static void KillAllTweens(this RectTransform rect)
        {
            rect.DOKill();
        }

        /// <summary>
        /// Kill all tweens on a CanvasGroup.
        /// </summary>
        public static void KillAllTweens(this CanvasGroup canvasGroup)
        {
            canvasGroup.DOKill();
        }

        #endregion
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Framework/Animation/UIAnimation.cs
git commit -m "feat(ui): add UIAnimation DOTween extensions"
```

---

### Task 7: UIPanel.cs - Panel Base Class

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Framework/Core/UIPanel.cs`

- [ ] **Step 1: Create UIPanel.cs**

```csharp
using System;
using DG.Tweening;
using Hotfix.GameSystems.UI.Framework.Animation;
using Hotfix.GameSystems.UI.Framework.Binding;
using UnityEngine;

namespace Hotfix.GameSystems.UI.Framework.Core
{
    /// <summary>
    /// Base class for all UI panels.
    /// Provides lifecycle, animation, binding, and configuration.
    /// </summary>
    public abstract class UIPanel : MonoBehaviour
    {
        [Header("Panel Configuration")]
        [SerializeField] protected bool _canMultiOpen = true;
        [SerializeField] protected bool _closeOnClickOutside = false;
        [SerializeField] protected bool _blockBack = false;

        [Header("Animation Settings")]
        [SerializeField] protected bool _useOpenAnim = true;
        [SerializeField] protected bool _useCloseAnim = true;
        [SerializeField] protected float _openAnimDuration = 0.3f;
        [SerializeField] protected float _closeAnimDuration = 0.2f;

        protected UIDataBinding _binding;
        protected bool _isVisible;
        protected int _sortOrder;

        #region Properties

        /// <summary>
        /// Allow multiple instances of this panel.
        /// </summary>
        public bool CanMultiOpen
        {
            get => _canMultiOpen;
            set => _canMultiOpen = value;
        }

        /// <summary>
        /// Close panel when clicking background.
        /// </summary>
        public bool CloseOnClickOutside
        {
            get => _closeOnClickOutside;
            set => _closeOnClickOutside = value;
        }

        /// <summary>
        /// Block back button when visible.
        /// </summary>
        public bool BlockBack
        {
            get => _blockBack;
            set => _blockBack = value;
        }

        /// <summary>
        /// Current sort order in canvas.
        /// </summary>
        public int SortOrder
        {
            get => _sortOrder;
            set
            {
                _sortOrder = value;
                if (_canvasGroup != null)
                    _canvasGroup.sortingOrder = value;
            }
        }

        /// <summary>
        /// Is panel currently visible.
        /// </summary>
        public bool IsVisible => _isVisible;

        /// <summary>
        /// RectTransform cache.
        /// </summary>
        public RectTransform RectTransform => _rect;

        #endregion

        #region Abstract

        /// <summary>
        /// Prefab path for pool loading.
        /// </summary>
        protected abstract string PrefabPath { get; }

        /// <summary>
        /// Which canvas layer this panel belongs to.
        /// </summary>
        protected abstract int Layer { get; }

        #endregion

        #region Virtual Lifecycle

        /// <summary>
        /// Called before Show animation.
        /// Override for data preparation.
        /// </summary>
        public virtual void OnPreShow(params object[] args) { }

        /// <summary>
        /// Called when panel is shown.
        /// Override for binding and initialization.
        /// </summary>
        public virtual void OnShow(params object[] args) { }

        /// <summary>
        /// Called when panel is hidden.
        /// Override for cleanup.
        /// </summary>
        public virtual void OnHide() { }

        /// <summary>
        /// Called when panel is destroyed.
        /// </summary>
        public virtual void OnDestroy() { }

        #endregion

        #region Animation Hooks

        /// <summary>
        /// Override for custom open animation.
        /// </summary>
        protected virtual void OnOpenAnimComplete() { }

        /// <summary>
        /// Override for custom close animation.
        /// </summary>
        protected virtual void OnCloseAnimComplete() { }

        /// <summary>
        /// Play open animation (called automatically unless disabled).
        /// </summary>
        protected virtual void PlayOpenAnim(Action onComplete)
        {
            if (!_useOpenAnim)
            {
                onComplete?.Invoke();
                return;
            }

            var rect = RectTransform;
            rect.ScaleIn(_openAnimDuration, onComplete);
        }

        /// <summary>
        /// Play close animation (called automatically unless disabled).
        /// </summary>
        protected virtual void PlayCloseAnim(Action onComplete)
        {
            if (!_useCloseAnim)
            {
                onComplete?.Invoke();
                return;
            }

            var rect = RectTransform;
            rect.ScaleOut(_closeAnimDuration, onComplete);
        }

        #endregion

        #region Binding

        /// <summary>
        /// Indexer for data binding.
        /// Usage: this["Health"] = () => vm.Health;
        /// </summary>
        protected object this[string key]
        {
            get => _binding?.GetValue(key);
            set => _binding?.NotifyChanged(key);
        }

        /// <summary>
        /// Bind to a ViewModel.
        /// </summary>
        protected void Bind(ViewModelBase vm)
        {
            Unbind();
            _binding = new UIDataBinding(vm);
        }

        /// <summary>
        /// Unbind from current ViewModel.
        /// </summary>
        protected void Unbind()
        {
            _binding?.Unbind();
            _binding = null;
        }

        /// <summary>
        /// Register a binding (called by panel for each bound property).
        /// </summary>
        protected void RegisterBinding(string key, Func<object> getter, Action<object> updater)
        {
            _binding?.Register(key, getter);
            if (updater != null)
            {
                _binding?.RegisterUpdater(key, updater);
            }
        }

        /// <summary>
        /// Refresh all bindings.
        /// </summary>
        protected void RefreshBindings()
        {
            _binding?.RefreshAll();
        }

        #endregion

        #region Internal

        private CanvasGroup _canvasGroup;
        private RectTransform _rect;
        private Canvas _canvas;

        // Called by UIManager to show panel
        internal void Show(params object[] args)
        {
            _isVisible = true;
            gameObject.SetActive(true);
            OnPreShow(args);
            OnShow(args);

            PlayOpenAnim(() =>
            {
                OnOpenAnimComplete();
            });
        }

        // Called by UIManager to hide panel
        internal void Hide(Action onHideComplete)
        {
            PlayCloseAnim(() =>
            {
                OnCloseAnimComplete();
                OnHide();
                _isVisible = false;
                gameObject.SetActive(false);
                Unbind();
                onHideComplete?.Invoke();
            });
        }

        protected virtual void Awake()
        {
            _rect = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _canvas = GetComponentInParent<Canvas>();
        }

        protected virtual void Start()
        {
            // Set initial state
            gameObject.SetActive(false);
        }

        protected virtual void OnDestroy()
        {
            OnDestroy();
            Unbind();
        }

        #endregion
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Framework/Core/UIPanel.cs
git commit -m "feat(ui): add UIPanel base class with lifecycle and binding"
```

---

### Task 8: UIManager.cs - Panel Manager

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Framework/Core/UIManager.cs`

- [ ] **Step 1: Create UIManager.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Hotfix.GameSystems.UI.Framework.Core
{
    /// <summary>
    /// Manages UI panel lifecycle, layers, and back navigation.
    /// Singleton accessible via UIManager.Instance.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        #region Singleton

        public static UIManager Instance { get; private set; }

        #endregion

        #region Layer Canvases

        private readonly Dictionary<int, Canvas> _layerCanvases = new();
        private readonly Dictionary<int, Transform> _layerParents = new();

        #endregion

        #region Panel Tracking

        private readonly Dictionary<Type, List<UIPanel>> _openPanels = new();
        private readonly Stack<UIPanel> _panelStack = new();
        private Action _defaultBackAction;

        #endregion

        #region Initialization

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeLayers();
        }

        private void InitializeLayers()
        {
            // Create canvas for each layer
            var layers = new (int layer, string name)[]
            {
                (UIConst.Layer_Base, UIConst.Canvas_Base),
                (UIConst.Layer_Main, UIConst.Canvas_Main),
                (UIConst.Layer_Popup, UIConst.Canvas_Popup),
                (UIConst.Layer_Guide, UIConst.Canvas_Guide),
                (UIConst.Layer_Toast, UIConst.Canvas_Toast),
            };

            foreach (var (layer, name) in layers)
            {
                CreateLayerCanvas(layer, name);
            }

            Debug.Log("UIManager initialized with layers");
        }

        private void CreateLayerCanvas(int layer, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = layer;
            canvas.pixelPerfect = true;

            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;

            _layerCanvases[layer] = canvas;
            _layerParents[layer] = go.transform;

            Debug.Log($"Created layer canvas: {name} (order: {layer})");
        }

        private Transform GetLayerParent(int layer)
        {
            // Find appropriate parent in layer range
            foreach (var kvp in _layerParents)
            {
                if (layer >= kvp.Key && layer < kvp.Key + 1000)
                {
                    return kvp.Value;
                }
            }
            return _layerParents[UIConst.Layer_Main]; // Default
        }

        #endregion

        #region Panel Operations

        /// <summary>
        /// Open a panel (type-based).
        /// Creates instance if not exists, or shows existing.
        /// </summary>
        public void Open<T>(params object[] args) where T : UIPanel
        {
            var type = typeof(T);

            // Check if already open (for single-instance panels)
            if (!_openPanels.TryGetValue(type, out var panels))
            {
                panels = new List<UIPanel>();
                _openPanels[type] = panels;
            }

            if (!panels.FirstOrDefault()?.CanMultiOpen ?? true)
            {
                // Single instance - show existing
                var existing = panels.FirstOrDefault();
                if (existing != null)
                {
                    ShowPanel(existing, args);
                    return;
                }
            }

            // Create new instance
            var panel = CreatePanel<T>();
            ShowPanel(panel, args);
        }

        /// <summary>
        /// Close a panel (type-based).
        /// </summary>
        public void Close<T>() where T : UIPanel
        {
            var type = typeof(T);
            if (_openPanels.TryGetValue(type, out var panels))
            {
                var panel = panels.LastOrDefault();
                if (panel != null)
                {
                    ClosePanel(panel);
                }
            }
        }

        /// <summary>
        /// Close topmost panel.
        /// </summary>
        public void CloseTop()
        {
            if (_panelStack.Count > 0)
            {
                var panel = _panelStack.Pop();
                ClosePanel(panel);
            }
        }

        /// <summary>
        /// Close all panels.
        /// </summary>
        public void CloseAll()
        {
            foreach (var panels in _openPanels.Values)
            {
                foreach (var panel in panels.ToList())
                {
                    ClosePanel(panel, immediate: true);
                }
            }
            _openPanels.Clear();
            _panelStack.Clear();
        }

        private T CreatePanel<T>() where T : UIPanel
        {
            var prefabPath = GetPanelPrefabPath<T>();
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError($"Panel prefab path not found for {typeof(T).Name}");
                return null;
            }

            var prefab = Resources.Load<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Panel prefab not found: {prefabPath}");
                return null;
            }

            var layer = GetPanelLayer<T>();
            var parent = GetLayerParent(layer);

            var go = Instantiate(prefab, parent);
            var panel = go.GetComponent<T>();

            if (panel == null)
            {
                Debug.LogError($"Prefab missing UIPanel component: {prefabPath}");
                Destroy(go);
                return null;
            }

            return panel;
        }

        private void ShowPanel(UIPanel panel, params object[] args)
        {
            var type = panel.GetType();

            // Set sort order
            var layer = GetPanelLayer(type);
            panel.SortOrder = GetNextSortOrder(layer);

            // Add to tracking
            if (!_openPanels.ContainsKey(type))
            {
                _openPanels[type] = new List<UIPanel>();
            }
            if (!_openPanels[type].Contains(panel))
            {
                _openPanels[type].Add(panel);
            }

            // Push to stack if blocking
            if (panel.BlockBack)
            {
                _panelStack.Push(panel);
            }

            // Show
            panel.Show(args);
        }

        private void ClosePanel(UIPanel panel, bool immediate = false)
        {
            var type = panel.GetType();

            // Remove from stack
            if (_panelStack.Contains(panel))
            {
                var stackList = _panelStack.ToList();
                stackList.Remove(panel);
                _panelStack.Clear();
                foreach (var p in stackList)
                {
                    _panelStack.Push(p);
                }
            }

            // Remove from tracking
            if (_openPanels.TryGetValue(type, out var panels))
            {
                panels.Remove(panel);
            }

            // Hide
            if (immediate)
            {
                panel.OnHide();
                panel.gameObject.SetActive(false);
            }
            else
            {
                panel.Hide(() =>
                {
                    Destroy(panel.gameObject);
                });
            }
        }

        private int GetNextSortOrder(int layer)
        {
            int maxOrder = layer;
            foreach (var panels in _openPanels.Values)
            {
                foreach (var panel in panels)
                {
                    if (panel.SortOrder > maxOrder)
                    {
                        maxOrder = panel.SortOrder;
                    }
                }
            }
            return maxOrder + 1;
        }

        #endregion

        #region Back Navigation

        /// <summary>
        /// Handle back button (Android/ESC).
        /// </summary>
        public void OnBackPressed()
        {
            if (_panelStack.Count > 0)
            {
                var topPanel = _panelStack.Peek();
                if (topPanel.BlockBack)
                {
                    CloseTop();
                    return;
                }
            }

            // Default action
            _defaultBackAction?.Invoke();
        }

        /// <summary>
        /// Set default back action (e.g., show main menu, exit app).
        /// </summary>
        public void SetDefaultBackAction(Action callback)
        {
            _defaultBackAction = callback;
        }

        #endregion

        #region Panel Type Helpers

        private string GetPanelPrefabPath<T>() where T : UIPanel
        {
            // Create instance to get path (suboptimal but works)
            var panel = CreateInstance<T>();
            var path = panel.PrefabPath;
            Destroy(panel.gameObject);
            return path;
        }

        private int GetPanelLayer<T>() where T : UIPanel
        {
            var panel = CreateInstance<T>();
            var layer = panel.Layer;
            Destroy(panel.gameObject);
            return layer;
        }

        private int GetPanelLayer(Type type)
        {
            var panel = Activator.CreateInstance(type) as UIPanel;
            var layer = panel?.Layer ?? UIConst.Layer_Main;
            Destroy(panel.gameObject);
            return layer;
        }

        private T CreateInstance<T>() where T : UIPanel
        {
            var type = typeof(T);
            var go = new GameObject(type.Name);
            return go.AddComponent<T>();
        }

        #endregion
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Framework/Core/UIManager.cs
git commit -m "feat(ui): add UIManager with layer and panel lifecycle management"
```

---

## Phase 2: Common Components

### Task 9: Toast.cs - Toast Notification

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Components/Toast.cs`

- [ ] **Step 1: Create Toast.cs**

```csharp
using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Components
{
    /// <summary>
    /// Toast notification component.
    /// Shows brief messages that auto-dismiss.
    /// </summary>
    public class Toast : MonoBehaviour
    {
        [SerializeField] private Text _messageText;
        [SerializeField] private Image _iconImage;
        [SerializeField] private RectTransform _rect;
        [SerializeField] private CanvasGroup _canvasGroup;

        private static Toast _instance;
        private static Queue<ToastItem> _pendingToasts = new();
        private bool _isShowing;

        private class ToastItem
        {
            public string Message;
            public Sprite Icon;
            public float Duration;
        }

        public static Toast Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateInstance();
                }
                return _instance;
            }
        }

        private static Toast CreateInstance()
        {
            // Create from Resources or instantiate new
            var prefab = Resources.Load<GameObject>("Prefabs/UI/Toast");
            Toast toast;

            if (prefab != null)
            {
                toast = Instantiate(prefab).GetComponent<Toast>();
            }
            else
            {
                // Create programmatic toast
                var go = new GameObject("Toast");
                toast = go.AddComponent<Toast>();
                toast.CreateLayout();
            }

            DontDestroyOnLoad(toast.gameObject);
            return toast;
        }

        private void CreateLayout()
        {
            _rect = gameObject.AddComponent<RectTransform>();
            _rect.anchorMin = new Vector2(0.5f, 0f);
            _rect.anchorMax = new Vector2(0.5f, 0f);
            _rect.pivot = new Vector2(0.5f, 0f);
            _rect.anchoredPosition = new Vector2(0, 100f);
            _rect.sizeDelta = new Vector2(400, 60);

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            var bg = new GameObject("Background");
            bg.transform.SetParent(transform);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0, 0, 0, 0.8f);
            var bgRect = bg.AddComponent<RectTransform>();
            bgRect.StretchParent();

            _messageText = new GameObject("Message").AddComponent<Text>();
            _messageText.transform.SetParent(transform);
            _messageText.text = "";
            _messageText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _messageText.fontSize = 24;
            _messageText.color = Color.white;
            _messageText.alignment = TextAnchor.MiddleCenter;
            _messageText.raycastTarget = false;
            var textRect = _messageText.GetComponent<RectTransform>();
            textRect.StretchParent();
            textRect.sizeDelta = new Vector2(-20, 0);

            _iconImage = new GameObject("Icon").AddComponent<Image>();
            _iconImage.transform.SetParent(transform);
            _iconImage.raycastTarget = false;
            var iconRect = _iconImage.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0, 0.5f);
            iconRect.anchorMax = new Vector2(0, 0.5f);
            iconRect.pivot = new Vector2(0, 0.5f);
            iconRect.anchoredPosition = new Vector2(30, 0);
            iconRect.sizeDelta = new Vector2(40, 40);

            gameObject.SetActive(false);
        }

        public static void Show(string message, float duration = 2f)
        {
            Show(message, null, duration);
        }

        public static void Show(string message, Sprite icon, float duration = 2f)
        {
            _pendingToasts.Enqueue(new ToastItem
            {
                Message = message,
                Icon = icon,
                Duration = duration
            });

            Instance.ProcessQueue();
        }

        private void ProcessQueue()
        {
            if (_isShowing || _pendingToasts.Count == 0)
                return;

            var item = _pendingToasts.Dequeue();
            ShowToast(item);
        }

        private void ShowToast(ToastItem item)
        {
            _isShowing = true;
            gameObject.SetActive(true);

            _messageText.text = item.Message;

            if (item.Icon != null)
            {
                _iconImage.sprite = item.Icon;
                _iconImage.gameObject.SetActive(true);
                _messageText.alignment = TextAnchor.MiddleLeft;
                var textRect = _messageText.GetComponent<RectTransform>();
                textRect.anchoredPosition = new Vector2(60, 0);
            }
            else
            {
                _iconImage.gameObject.SetActive(false);
                _messageText.alignment = TextAnchor.MiddleCenter;
            }

            _rect.ScaleOut(0f, () => { });
            _rect.ScaleIn(0.3f, () =>
            {
                DOVirtual.DelayedCall(item.Duration, () =>
                {
                    _rect.ScaleOut(0.2f, () =>
                    {
                        gameObject.SetActive(false);
                        _isShowing = false;
                        ProcessQueue();
                    });
                });
            });
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Components/Toast.cs
git commit -m "feat(ui): add Toast component"
```

---

### Task 10: Loading.cs - Loading Mask

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Components/Loading.cs`

- [ ] **Step 1: Create Loading.cs**

```csharp
using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Components
{
    /// <summary>
    /// Loading mask component.
    /// Shows blocking overlay with optional tips text.
    /// </summary>
    public class Loading : MonoBehaviour
    {
        [SerializeField] private RectTransform _rect;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _background;
        [SerializeField] private Image _spinner;
        [SerializeField] private Text _tipsText;
        [SerializeField] private Transform _spinnerTransform;

        private static Loading _instance;
        private int _loadingCount;
        private Tween _spinTween;

        public static Loading Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateInstance();
                }
                return _instance;
            }
        }

        private static Loading CreateInstance()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/Loading");
            Loading loading;

            if (prefab != null)
            {
                loading = Instantiate(prefab).GetComponent<Loading>();
            }
            else
            {
                var go = new GameObject("Loading");
                loading = go.AddComponent<Loading>();
                loading.CreateLayout();
            }

            DontDestroyOnLoad(loading.gameObject);
            loading.gameObject.SetActive(false);
            return loading;
        }

        private void CreateLayout()
        {
            _rect = gameObject.AddComponent<RectTransform>();
            _rect.StretchParent();

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            _background = CreateImage("Background");
            _background.color = new Color(0, 0, 0, 0.5f);
            _background.raycastTarget = true;

            var content = new GameObject("Content");
            content.transform.SetParent(transform);

            _spinner = CreateImage("Spinner", content.transform);
            _spinner.color = Color.white;
            var spinnerRect = _spinner.GetComponent<RectTransform>();
            spinnerRect.anchoredPosition = Vector2.zero;
            spinnerRect.sizeDelta = new Vector2(60, 60);

            _tipsText = CreateText("Tips", content.transform);
            _tipsText.fontSize = 24;
            _tipsText.color = Color.white;
            _tipsText.alignment = TextAnchor.MiddleCenter;
            var tipsRect = _tipsText.GetComponent<RectTransform>();
            tipsRect.anchoredPosition = new Vector2(0, -60);
            tipsRect.sizeDelta = new Vector2(300, 40);

            _spinnerTransform = _spinner.transform;
        }

        private Image CreateImage(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var img = go.AddComponent<Image>();
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.StretchParent();
            return img;
        }

        private Text CreateText(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return text;
        }

        public static void Show(string tips = null)
        {
            Instance.ShowLoading(tips);
        }

        public static void Hide()
        {
            Instance.HideLoading();
        }

        private void ShowLoading(string tips)
        {
            _loadingCount++;

            if (_loadingCount == 1)
            {
                gameObject.SetActive(true);
                _canvasGroup.blocksRaycasts = true;

                if (!string.IsNullOrEmpty(tips))
                {
                    _tipsText.text = tips;
                    _tipsText.gameObject.SetActive(true);
                }
                else
                {
                    _tipsText.gameObject.SetActive(false);
                }

                _canvasGroup.FadeIn(0.2f);
                StartSpinAnimation();
            }
        }

        private void HideLoading()
        {
            _loadingCount = Math.Max(0, _loadingCount - 1);

            if (_loadingCount == 0)
            {
                StopSpinAnimation();
                _canvasGroup.FadeOut(0.2f, () =>
                {
                    if (_loadingCount == 0)
                    {
                        gameObject.SetActive(false);
                    }
                });
            }
        }

        private void StartSpinAnimation()
        {
            _spinTween = _spinnerTransform.DOLocalRotate(Vector3.forward * -360f, 1f, RotateMode.LocalAxisAdd)
                .SetEase(Ease.Linear)
                .SetLoops(-1);
        }

        private void StopSpinAnimation()
        {
            _spinTween?.Kill();
            _spinnerTransform.localRotation = Quaternion.identity;
        }

        private void OnDestroy()
        {
            _spinTween?.Kill();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Components/Loading.cs
git commit -m "feat(ui): add Loading component"
```

---

### Task 11: Confirm.cs - Confirm Dialog

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Components/Confirm.cs`

- [ ] **Step 1: Create Confirm.cs**

```csharp
using System;
using DG.Tweening;
using Hotfix.GameSystems.UI.Framework.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Components
{
    /// <summary>
    /// Confirm dialog component.
    /// Shows modal dialog with confirm/cancel actions.
    /// </summary>
    public class Confirm : UIPanel
    {
        [Header("Confirm UI References")]
        [SerializeField] private Text _titleText;
        [SerializeField] private Text _messageText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Text _confirmText;
        [SerializeField] private Text _cancelText;

        private static Confirm _instance;
        private Action _onConfirm;
        private Action _onCancel;

        protected override string PrefabPath => "Assets/Prefabs/UI/Confirm.prefab";
        protected override int Layer => UIConst.Layer_Popup;

        public static Confirm Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateInstance();
                }
                return _instance;
            }
        }

        private static Confirm CreateInstance()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/Confirm");
            Confirm confirm;

            if (prefab != null)
            {
                confirm = Instantiate(prefab).GetComponent<Confirm>();
            }
            else
            {
                var go = new GameObject("Confirm");
                confirm = go.AddComponent<Confirm>();
                confirm.CreateLayout();
            }

            confirm.BlockBack = true;
            confirm.CloseOnClickOutside = false;
            confirm.CanMultiOpen = false;
            confirm._useOpenAnim = true;
            confirm._useCloseAnim = true;

            return confirm;
        }

        private void CreateLayout()
        {
            var rt = GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(500, 300);

            var bg = CreateImage("Background");
            bg.color = new Color(0.2f, 0.2f, 0.2f, 0.9f);
            bg.rect.StretchParent();
            bg.rect.SetAsLastSibling();

            var content = new GameObject("Content");
            content.transform.SetParent(transform);

            _titleText = CreateText("Title", content.transform);
            _titleText.fontSize = 32;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.color = Color.white;
            var titleRect = _titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 0.7f);
            titleRect.anchorMax = new Vector2(1, 1f);
            titleRect.sizeDelta = new Vector2(-40, 0);
            titleRect.anchoredPosition = new Vector2(0, 25);

            _messageText = CreateText("Message", content.transform);
            _messageText.fontSize = 24;
            _messageText.color = Color.white;
            _messageText.supportRichText = true;
            var msgRect = _messageText.GetComponent<RectTransform>();
            msgRect.anchorMin = new Vector2(0, 0.3f);
            msgRect.anchorMax = new Vector2(1, 0.7f);
            msgRect.sizeDelta = new Vector2(-40, 0);
            msgRect.anchoredPosition = new Vector2(0, 0);

            var btnContainer = new GameObject("Buttons");
            btnContainer.transform.SetParent(content.transform);

            _cancelButton = CreateButton("CancelBtn", btnContainer.transform);
            _cancelButton.GetComponent<Image>().color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var cancelRect = _cancelButton.GetComponent<RectTransform>();
            cancelRect.anchorMin = new Vector2(0.1f, 0.05f);
            cancelRect.anchorMax = new Vector2(0.45f, 0.25f);
            cancelRect.sizeDelta = Vector2.zero;
            _cancelText = _cancelButton.GetComponentInChildren<Text>();
            _cancelText.text = "Cancel";
            _cancelText.color = Color.white;

            _confirmButton = CreateButton("ConfirmBtn", btnContainer.transform);
            var confirmRect = _confirmButton.GetComponent<RectTransform>();
            confirmRect.anchorMin = new Vector2(0.55f, 0.05f);
            confirmRect.anchorMax = new Vector2(0.9f, 0.25f);
            confirmRect.sizeDelta = Vector2.zero;
            _confirmText = _confirmButton.GetComponentInChildren<Text>();
            _confirmText.text = "Confirm";
            _confirmText.color = Color.white;

            _confirmButton.onClick.AddListener(OnConfirmClicked);
            _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        private Image CreateImage(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var img = go.AddComponent<Image>();
            img.raycastTarget = true;
            return img;
        }

        private Text CreateText(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent ?? transform);
            var btn = go.AddComponent<Button>();
            var img = go.AddComponent<Image>();
            img.color = Color.white;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(go.transform);
            var txt = textObj.AddComponent<Text>();
            txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.black;
            var txtRect = txtObj.GetComponent<RectTransform>();
            txtRect.StretchParent();

            return btn;
        }

        public static void Show(
            string title,
            string message,
            Action onConfirm,
            Action onCancel = null,
            string confirmText = "Confirm",
            string cancelText = "Cancel")
        {
            Instance.ShowDialog(title, message, onConfirm, onCancel, confirmText, cancelText);
        }

        private void ShowDialog(
            string title,
            string message,
            Action onConfirm,
            Action onCancel,
            string confirmText,
            string cancelText)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            _titleText.text = title;
            _messageText.text = message;
            _confirmText.text = confirmText;
            _cancelText.text = cancelText;

            UIManager.Instance.Open<Confirm>();
        }

        private void OnConfirmClicked()
        {
            _onConfirm?.Invoke();
            Close();
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            Close();
        }

        private void Close()
        {
            UIManager.Instance.Close<Confirm>();
            _onConfirm = null;
            _onCancel = null;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Components/Confirm.cs
git commit -m "feat(ui): add Confirm component"
```

---

### Task 12: Tips.cs - Floating Tips

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Components/Tips.cs`

- [ ] **Step 1: Create Tips.cs**

```csharp
using System;
using System.Collections.Generic;
using DG.Tweening;
using Hotfix.GameSystems.UI.Framework.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Components
{
    /// <summary>
    /// Floating tips component.
    /// Shows contextual tips near UI elements.
    /// </summary>
    public class Tips : MonoBehaviour
    {
        [SerializeField] private RectTransform _rect;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Text _contentText;
        [SerializeField] private Image _background;

        private static Tips _instance;
        private Queue<TipsItem> _pendingTips = new();
        private bool _isShowing;
        private Tween _hoverTween;

        private struct TipsItem
        {
            public string Content;
            public Vector2 Position;
            public float Duration;
        }

        public static Tips Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateInstance();
                }
                return _instance;
            }
        }

        private static Tips CreateInstance()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/Tips");
            Tips tips;

            if (prefab != null)
            {
                tips = Instantiate(prefab).GetComponent<Tips>();
            }
            else
            {
                var go = new GameObject("Tips");
                tips = go.AddComponent<Tips>();
                tips.CreateLayout();
            }

            tips.gameObject.SetActive(false);
            return tips;
        }

        private void CreateLayout()
        {
            _rect = gameObject.AddComponent<RectTransform>();
            _rect.sizeDelta = new Vector2(200, 60);

            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;

            _background = CreateImage("Background");
            _background.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            _contentText = CreateText("Content");
            _contentText.fontSize = 20;
            _contentText.color = Color.white;
            _contentText.supportRichText = true;
        }

        private Image CreateImage(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.StretchParent();
            return img;
        }

        private Text CreateText(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.StretchParent();
            rt.sizeDelta = new Vector2(-20, 0);
            return text;
        }

        /// <summary>
        /// Show tips at screen position.
        /// </summary>
        public static void ShowAt(string content, Vector2 screenPos, float duration = 3f)
        {
            Instance.ShowTips(content, screenPos, duration);
        }

        /// <summary>
        /// Show tips anchored to a RectTransform.
        /// </summary>
        public static void ShowAnchored(string content, RectTransform anchor, float duration = 3f)
        {
            if (anchor == null) return;

            Vector2 pos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                anchor.root as RectTransform,
                RectTransformUtility.WorldToScreenPoint(Camera.main, anchor.position),
                null,
                out pos);

            Instance.ShowTips(content, pos, duration);
        }

        /// <summary>
        /// Hide current tips.
        /// </summary>
        public static void Hide()
        {
            Instance.HideTips();
        }

        private void ShowTips(string content, Vector2 position, float duration)
        {
            _pendingTips.Enqueue(new TipsItem
            {
                Content = content,
                Position = position,
                Duration = duration
            });

            ProcessQueue();
        }

        private void ProcessQueue()
        {
            if (_isShowing || _pendingTips.Count == 0)
                return;

            var item = _pendingTips.Dequeue();
            ShowTipsImmediate(item);
        }

        private void ShowTipsImmediate(TipsItem item)
        {
            _isShowing = true;
            gameObject.SetActive(true);

            _rect.anchoredPosition = item.Position;
            _contentText.text = item.Content;

            // Auto-size based on content
            var preferredWidth = Mathf.Min(_contentText.preferredWidth + 40, 400);
            _rect.sizeDelta = new Vector2(preferredWidth, 60);

            _canvasGroup.DOFade(1f, 0.2f).OnComplete(() =>
            {
                DOVirtual.DelayedCall(item.Duration, () =>
                {
                    _canvasGroup.DOFade(0f, 0.2f).OnComplete(() =>
                    {
                        gameObject.SetActive(false);
                        _isShowing = false;
                        ProcessQueue();
                    });
                });
            });
        }

        private void HideTips()
        {
            _pendingTips.Clear();
            _canvasGroup.DOFade(0f, 0.1f).OnComplete(() =>
            {
                gameObject.SetActive(false);
                _isShowing = false;
            });
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Components/Tips.cs
git commit -m "feat(ui): add Tips component"
```

---

## Phase 3: Integration & Example

### Task 13: UIEntry.cs - System Entry Point

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/UIEntry.cs`

- [ ] **Step 1: Create UIEntry.cs**

```csharp
using System;
using System.Collections.Generic;
using Hotfix.GameSystems.UI.Framework.Core;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// UI system entry point.
    /// Initialize UIManager and pool on game start.
    /// </summary>
    public class UIEntry : MonoBehaviour
    {
        [Serializable]
        public class PoolConfig
        {
            public string PanelType;
            public string PrefabPath;
            public int PreLoadCount;
        }

        [Header("Pool Configuration")]
        [SerializeField] private List<PoolConfig> _poolConfigs = new();

        private static UIEntry _instance;
        public static UIEntry Instance => _instance;

        public UIManager Manager => UIManager.Instance;
        public UIPool Pool => _pool;

        private UIPool _pool;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Initialize()
        {
            // Ensure UIManager exists
            if (UIManager.Instance == null)
            {
                var go = new GameObject("UIManager");
                go.AddComponent<UIManager>();
            }

            // Initialize pool
            _pool = new UIPool();

            // Register panels to pool
            foreach (var config in _poolConfigs)
            {
                if (string.IsNullOrEmpty(config.PrefabPath))
                    continue;

                // Register by type
                // Note: In production, use reflection or code generation
                RegisterPool(config.PrefabPath, config.PreLoadCount);
            }

            Debug.Log("UIEntry initialized");
        }

        private void RegisterPool(string prefabPath, int preLoadCount)
        {
            _pool.Register(prefabPath, preLoadCount);
        }

        /// <summary>
        /// Preload pools (call before showing first UI).
        /// </summary>
        public void Preload()
        {
            // Pool preloading is handled in Initialize
            Debug.Log("UI pools preloaded");
        }

        /// <summary>
        /// Shutdown UI system.
        /// </summary>
        public void Shutdown()
        {
            UIManager.Instance?.CloseAll();
            _pool = null;
            Debug.Log("UIEntry shutdown");
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/UIEntry.cs
git commit -m "feat(ui): add UIEntry as system entry point"
```

---

### Task 14: Example HUD Panel

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/HUDViewModel.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/HUDPanel.cs`

- [ ] **Step 1: Create HUDViewModel.cs**

```csharp
using Hotfix.GameSystems.UI.Framework.Binding;

namespace Hotfix.GameSystems.UI.Panel.HUD
{
    /// <summary>
    /// ViewModel for HUD panel.
    /// </summary>
    public class HUDViewModel : ViewModelBase
    {
        private int _health;
        private int _maxHealth;
        private int _mana;
        private int _maxMana;
        private string _playerName;

        public int Health
        {
            get => _health;
            set
            {
                _health = value;
                SetProperty("Health", value);
            }
        }

        public int MaxHealth
        {
            get => _maxHealth;
            set
            {
                _maxHealth = value;
                SetProperty("MaxHealth", value);
            }
        }

        public int Mana
        {
            get => _mana;
            set
            {
                _mana = value;
                SetProperty("Mana", value);
            }
        }

        public int MaxMana
        {
            get => _maxMana;
            set
            {
                _maxMana = value;
                SetProperty("MaxMana", value);
            }
        }

        public string PlayerName
        {
            get => _playerName;
            set
            {
                _playerName = value;
                SetProperty("PlayerName", value);
            }
        }

        public float HealthPercent => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;
        public float ManaPercent => MaxMana > 0 ? (float)Mana / MaxMana : 0f;

        public override void Refresh()
        {
            // Simulate data refresh
            // In real game, fetch from character system
        }
    }
}
```

- [ ] **Step 2: Create HUDPanel.cs**

```csharp
using Hotfix.GameSystems.UI.Framework.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI.Panel.HUD
{
    /// <summary>
    /// HUD panel example.
    /// Shows player health, mana, and name.
    /// </summary>
    public class HUDPanel : UIPanel
    {
        [Header("HUD References")]
        [SerializeField] private Slider _healthSlider;
        [SerializeField] private Slider _manaSlider;
        [SerializeField] private Text _healthText;
        [SerializeField] private Text _manaText;
        [SerializeField] private Text _nameText;

        private HUDViewModel _viewModel;

        protected override string PrefabPath => "Assets/Prefabs/UI/HUD.prefab";
        protected override int Layer => UIConst.Layer_Base;

        protected override void Awake()
        {
            base.Awake();

            // Create ViewModel
            _viewModel = new HUDViewModel();
        }

        public override void OnShow(params object[] args)
        {
            base.OnShow(args);

            // Bind
            Bind(_viewModel);

            // Setup bindings
            RegisterBinding("Health", () => _viewModel.Health, val => _healthText.text = $"{_viewModel.Health}/{_viewModel.MaxHealth}");
            RegisterBinding("Mana", () => _viewModel.Mana, val => _manaText.text = $"{_viewModel.Mana}/{_viewModel.MaxMana}");
            RegisterBinding("PlayerName", () => _viewModel.PlayerName, val => _nameText.text = _viewModel.PlayerName?.ToString() ?? "");

            // Subscribe to percent changes
            _viewModel.Subscribe("Health", () =>
            {
                if (_healthSlider != null)
                    _healthSlider.value = _viewModel.HealthPercent;
            });

            _viewModel.Subscribe("Mana", () =>
            {
                if (_manaSlider != null)
                    _manaSlider.value = _viewModel.ManaPercent;
            });

            // Refresh
            RefreshBindings();

            // Demo data
            _viewModel.MaxHealth = 100;
            _viewModel.Health = 75;
            _viewModel.MaxMana = 100;
            _viewModel.Mana = 50;
            _viewModel.PlayerName = "Player1";
        }

        public override void OnHide()
        {
            base.OnHide();
            Unbind();
        }

        private void OnDestroy()
        {
            _viewModel?.Dispose();
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/HUDViewModel.cs
git add Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/HUDPanel.cs
git commit -m "feat(ui): add example HUD panel with ViewModel"
```

---

### Task 15: Assembly Definition

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/UI/UI.asmdef`

- [ ] **Step 1: Create UI.asmdef**

```json
{
    "name": "Hotfix.GameSystems.UI",
    "rootNamespace": "",
    "references": [
        "KcpNet"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/UI.asmdef
git commit -m "feat(ui): add UI assembly definition"
```

---

## Summary

**Files Created:**

| File | Purpose |
|------|---------|
| UIConst.cs | Layer constants |
| UIPool.cs | Object pool with ref counting |
| ViewModelBase.cs | ViewModel base class |
| UIDataBinding.cs | Indexer binding |
| UIMessage.cs | Independent message system |
| UIAnimation.cs | DOTween extensions |
| UIPanel.cs | Panel base class |
| UIManager.cs | Panel manager |
| Toast.cs | Toast notification |
| Loading.cs | Loading mask |
| Confirm.cs | Confirm dialog |
| Tips.cs | Floating tips |
| UIEntry.cs | System entry |
| HUDPanel.cs | Example panel |
| HUDViewModel.cs | Example ViewModel |
| UI.asmdef | Assembly definition |
