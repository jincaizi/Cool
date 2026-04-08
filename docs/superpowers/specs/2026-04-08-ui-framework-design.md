# UI Framework Design Specification

> **For agentic workers:** Use superpowers:writing-plans to create implementation plan after spec approval.

**Goal:** Build an independent, lightweight MVVM UI framework for Unity UGUI with hot-reload support.

**Architecture:** Layered architecture with UI Framework independent from business logic. Each UI layer has its own Canvas with Sort Order separation.

**Tech Stack:** Unity UGUI, DOTween, HybridCLR Hotfix

---

## 1. Layer Architecture

### Canvas Layers

Each layer is an independent Canvas:

| Layer Name | Sort Order Range | Purpose |
|------------|------------------|---------|
| Base | 0-999 | Background HUD, health bars |
| Main | 1000-1999 | Main gameplay UI, backpack |
| Popup | 2000-2999 | Dialogs, confirm boxes |
| Guide | 3000-3999 | Tutorial masks |
| Toast | 4000-4999 | Toast notifications, tips |

### Directory Structure

```
Assets/Scripts/Hotfix/GameSystems/
├── UI/
│   ├── Framework/
│   │   ├── Core/
│   │   │   ├── UIManager.cs       - Panel management, layer, Show/Hide
│   │   │   ├── UIPanel.cs         - Panel base class
│   │   │   │   ├── Lifecycle       - OnShow, OnHide, OnDestroy
│   │   │   ├── UIPool.cs          - Object pool with reference counting
│   │   │   └── UIConst.cs         - Constants (layers, paths)
│   │   ├── Binding/
│   │   │   ├── UIDataBinding.cs   - Indexer-based data binding
│   │   │   └── ViewModelBase.cs   - ViewModel base class
│   │   ├── Message/
│   │   │   └── UIMessage.cs       - Independent UI message system
│   │   └── Animation/
│   │       └── UIAnimation.cs     - DOTween extensions
│   ├── Components/
│   │   ├── Toast.cs               - Toast notification
│   │   ├── Loading.cs             - Loading mask
│   │   ├── Confirm.cs             - Confirm dialog
│   │   └── Tips.cs                - Floating tips
│   ├── Panel/ (Business implementation)
│   │   ├── HUD/
│   │   └── Backpack/
│   └── UIEntry.cs                 - UI system entry point
└── Sys3C/
```

---

## 2. UIManager

### Responsibilities
- Manage panel lifecycle (Show/Hide/Open/Close)
- Handle layer assignment and Sort Order
- Manage panel stack for back navigation
- Handle back button (Android/ESC)

### Key Features

```csharp
public class UIManager : MonoBehaviour
{
    // Open a panel (type-based)
    public void Open<T>(params object[] args) where T : UIPanel;

    // Close a panel
    public void Close<T>() where T : UIPanel;

    // Close topmost panel
    public void CloseTop();

    // Back button handler
    public void OnBackPressed();

    // Set default back action
    public void SetDefaultBackAction(Action callback);
}
```

### Panel Configuration

Each panel can configure:
- `CanMultiOpen` - Allow multiple instances
- `CloseOnClickOutside` - Close when clicking background
- `BlockBack` - Whether this panel captures back button

---

## 3. UIPanel Base Class

### Lifecycle

```csharp
public abstract class UIPanel : MonoBehaviour
{
    // Lifecycle
    public virtual void OnPreShow(params object[] args) { }
    public virtual void OnShow(params object[] args) { }
    public virtual void OnHide() { }
    public virtual void OnDestroy() { }

    // Panel configuration (set by business)
    public bool CanMultiOpen { get; set; }
    public bool CloseOnClickOutside { get; set; }
    public bool BlockBack { get; set; }

    // Animation hooks
    protected void PlayOpenAnim(Action onComplete);
    protected void PlayCloseAnim(Action onComplete);
    protected virtual void OnOpenAnimComplete() { }
    protected virtual void OnCloseAnimComplete() { }

    // Binding
    protected void Bind(ViewModelBase vm);
    protected void Unbind();

    // Data binding
    protected object this[string key] { get; set; }
}
```

### Abstract Methods

Subclasses must implement:
```csharp
protected abstract string PrefabPath { get; }  // e.g., "Assets/Prefabs/UI/HUD.prefab"
protected abstract int Layer { get; }          // Which canvas layer
```

---

## 4. UIDataBinding (Indexer Binding)

### Usage Pattern

```csharp
// In View
public class MyPanel : UIPanel
{
    private MyViewModel _vm;

    protected override void OnShow(params object[] args)
    {
        Bind(_vm);
        this["Health"] = () => _vm.Health;
        this["Name"] = () => _vm.Name;
    }
}

// In ViewModel
public class MyViewModel : ViewModelBase
{
    private int _health;
    public int Health
    {
        get => _health;
        set { _health = value; NotifyChanged("Health"); }
    }
}
```

### Binding Core

```csharp
public class UIDataBinding
{
    // Register binding
    public void Register(string key, Func<object> getter);

    // Notify change
    public void NotifyChanged(string key);

    // Get current value
    public object GetValue(string key);
}
```

---

## 5. ViewModelBase

```csharp
public abstract class ViewModelBase
{
    // Indexer access
    public object this[string key] { get; set; }

    // Property notification
    protected void SetProperty(string key, object value);
    protected object GetProperty(string key);

    // Refresh (called when panel shows)
    public virtual void Refresh() { }

    // Cleanup
    public virtual void Dispose() { }
}
```

---

## 6. UIPool (Object Pool)

### Features
- Reference counting for shared panels
- Pre-warm pool on system init
- Auto-return to pool on Close (reference count reaches 0)

### Usage

```csharp
public class UIPool
{
    // Register a prefab to pool
    public void Register<T>(string prefabPath, int preLoadCount = 0) where T : UIPanel;

    // Get from pool
    public T Get<T>() where T : UIPanel;

    // Return to pool
    public void Release(UIPanel panel);
}
```

---

## 7. UIMessage (Independent Message System)

Separate from KCP networking messages. Internal UI communication.

```csharp
// Message types (business defines their own)
public struct UIMessage
{
    public string Type;
    public object Body;
    public long Timestamp;
}

// Subscribe/Unsubscribe
public class UIMessage
{
    public static void Subscribe(string messageType, Action<object> callback);
    public static void Unsubscribe(string messageType, Action<object> callback);
    public static void UnsubscribeAll();

    // Send
    public static void Send(string messageType, object body = null);
}
```

---

## 8. UIAnimation (DOTween Extensions)

```csharp
public static class UIAnimation
{
    // Scale tween
    public static Tweener ScaleIn(this RectTransform rect, float duration, Action onComplete = null);
    public static Tweener ScaleOut(this RectTransform rect, float duration, Action onComplete = null);

    // Fade tween
    public static Tweener FadeIn(this CanvasGroup group, float duration, Action onComplete = null);
    public static Tweener FadeOut(this CanvasGroup group, float duration, Action onComplete = null);

    // Slide tween
    public static Tweener SlideIn(this RectTransform rect, Vector2 startPos, float duration, Action onComplete = null);
    public static Tweener SlideOut(this RectTransform rect, Vector2 endPos, float duration, Action onComplete = null);
}
```

---

## 9. UIEntry (System Entry)

```csharp
public class UIEntry : MonoBehaviour
{
    public static UIEntry Instance { get; }

    public UIManager Manager { get; }
    public UIPool Pool { get; }

    // Initialize pools
    public void Preload();

    // Lifecycle
    public void Shutdown();
}
```

---

## 10. Common Components

### Toast

```csharp
// Show toast message
Toast.Show(string message, float duration = 2f);

// Show with icon
Toast.Show(string message, Sprite icon, float duration = 2f);
```

### Loading

```csharp
// Show loading
Loading.Show(string tips = null);

// Hide loading
Loading.Hide();
```

### Confirm

```csharp
// Show confirm dialog
Confirm.Show(
    string title,
    string message,
    Action onConfirm,
    Action onCancel = null,
    string confirmText = "Confirm",
    string cancelText = "Cancel"
);
```

---

## 11. Business Panel Implementation

Example: HUD Panel

```csharp
public class HUDPanel : UIPanel
{
    protected override string PrefabPath => "Assets/Prefabs/UI/HUD.prefab";
    protected override int Layer => UIConst.Layer_Base;

    public override void OnShow(params object[] args)
    {
        base.OnShow(args);
        // Bind to ViewModel
    }
}
```

---

## 12. Implementation Order

### Phase 1: Core Framework
1. UIConst.cs - Constants
2. UIPool.cs - Object pool
3. ViewModelBase.cs - ViewModel base
4. UIDataBinding.cs - Binding core
5. UIMessage.cs - Message system
6. UIAnimation.cs - DOTween extensions
7. UIPanel.cs - Panel base class
8. UIManager.cs - Manager

### Phase 2: Common Components
1. Toast.cs
2. Loading.cs
3. Confirm.cs
4. Tips.cs

### Phase 3: Integration & Example
1. UIEntry.cs - Entry point
2. Example HUD panel
3. Integration with existing systems

---

## Appendix: Constants

```csharp
public static class UIConst
{
    // Layer definitions
    public const int Layer_Base = 0;
    public const int Layer_Main = 1000;
    public const int Layer_Popup = 2000;
    public const int Layer_Guide = 3000;
    public const int Layer_Toast = 4000;

    // Layer canvas names
    public const string Canvas_Base = "Canvas_Base";
    public const string Canvas_Main = "Canvas_Main";
    public const string Canvas_Popup = "Canvas_Popup";
    public const string Canvas_Guide = "Canvas_Guide";
    public const string Canvas_Toast = "Canvas_Toast";
}
```
