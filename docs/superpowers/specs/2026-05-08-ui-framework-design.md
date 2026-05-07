# UI Framework Design

**Date:** 2026-05-08
**Engine:** Unity 2022.3.25f1
**Layer:** Hotfix (`Assets/Scripts/Hotfix/GameSystems/UI/`)
**UI System:** UGUI
**Animation:** DOTween
**Async Model:** UniTask

## 1. Motivation

- 项目尚无 UI 框架，仅有 Bag 系统的独立 MonoBehaviour（BagPanel、ItemCell、ItemTooltip）
- 旧的 MVVM 设计文档（2026-04-08）从未实现，现有 UI 目录为空
- DOTween 已安装但代码中从未使用
- 需要：横屏适配、首包最小化、显隐性能可控、动画系统

## 2. Architecture Overview

```
┌────────────────────────────────────────────────────────┐
│                    UIManager (singleton)                │
│  ┌──────────┐  ┌──────────┐  ┌────────────────────┐   │
│  │  Stack   │  │ Overlay  │  │  Always (Base)     │   │
│  │Main/Popup│  │Top/Guide │  │  HUD permanently    │   │
│  └──────────┘  └──────────┘  └────────────────────┘   │
├────────────────────────────────────────────────────────┤
│                    UIPanel (base class)                │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐ │
│  │VisibilityMode│  │  Lifecycle   │  │  UIAnimation │ │
│  │ToggleActive/ │  │  OnPreShow   │  │  Preset      │ │
│  │CanvasSwitch/ │  │  ShowAnim    │  │  Builder     │ │
│  │CanvasGroup   │  │  OnShow/OnHide│  │  Extensions  │ │
│  └──────────────┘  └──────────────┘  └──────────────┘ │
├────────────────────────────────────────────────────────┤
│               ScreenAdapter (per Canvas)               │
│         CanvasScaler + SafeArea + Auto Refresh         │
└────────────────────────────────────────────────────────┘
```

**设计原则：**
- 简化 MVVM：保留 ViewModel 概念但不强制，数据直接赋值 >> 反射绑定
- 性能优先：每种面板声明显隐策略，UIManager 自动选择最优方式
- YAGNI：不做 UI 模板系统、不做数据绑定引擎、不做过度抽象

## 3. Canvas Layer Architecture

### 3.1 Five Layers

| Layer | Sort Order | Type | Behavior | Examples |
|-------|-----------|------|----------|---------|
| Base | 1000 | Always | 常驻，不受栈控制 | HUD, 摇杆, 状态栏 |
| Main | 2000 | Stack | 栈管理，Push/Pop | 背包, 技能, 角色, 设置 |
| Popup | 3000 | Stack | 栈管理，Push/Pop | 确认框, Tips, 详情弹窗 |
| Top | 4000 | Overlay | 独立 Show/Hide | Loading, Toast, 重连提示 |
| Guide | 5000 | Overlay | 独立 Show/Hide | 新手引导蒙版/高亮 |

### 3.2 Layer Behavior

**Stack Layers (Main / Popup):**
- Push 新面板时：底层面板调用 OnHide 暂停事件，新面板执行完整入场流程
- Pop 时：顶层面板 OnHide → 出场动画 → 关闭显隐，底层面板 OnShow 恢复事件
- PopTo(panelId)：连续 Pop 直到指定面板回到栈顶

**Overlay Layers (Top / Guide):**
- 不受栈控制，多个 Overlay 可同时显示
- Open/Close 独立管理，不影响 Stack 状态
- 层级由 Canvas sort order 决定

**Always Layer (Base):**
- 始终显示，Show/Hide 独立控制
- HUD 等常驻元素

## 4. ScreenAdapter

### 4.1 CanvasScaler Configuration

```csharp
// Auto-configured by ScreenAdapter on each Canvas root
uiScaleMode = ScaleWithScreenSize
referenceResolution = 1920 x 1080
screenMatchMode = MatchWidthOrHeight
matchWidthOrHeight = 0.5
```

### 4.2 SafeArea

- ScreenAdapter 读取 `Screen.safeArea`，将偏移量应用到 Canvas RectTransform
- 可选：根面板撑满全屏，SafeArea 仅对 HUD/TopBar 生效
- `AutoRefreshOnResize = true` 时监听分辨率变化自动重算

### 4.3 Component

```csharp
public class ScreenAdapter : MonoBehaviour
{
    public bool ApplySafeArea = true;
    public bool AutoRefreshOnResize = true;

    void Awake() { ConfigureCanvasScaler(); ApplySafeAreaIfNeeded(); }
    void OnRectTransformDimensionsChange() { /* re-apply safe area */ }
}
```

ScreenAdapter 挂载在 UIManager 创建的每个 Canvas 根节点上。

## 5. Visibility Strategy (Show/Hide Performance)

### 5.1 Three Modes

| Mode | Mechanism | Show Cost | Hide Benefit | Use Case |
|------|-----------|-----------|-------------|----------|
| ToggleActive | SetActive(true/false) | ★☆☆☆ (GC) | ★★★★ (full release) | 低频面板：背包,技能,设置 |
| CanvasSwitch | Canvas.enabled | ★★★★ | ★★★☆ (keeps tree) | 中频面板：确认框,弹窗 |
| CanvasGroup | alpha=0 + blocksRaycasts=false | ★★★★★ | ★★☆☆ (still renders) | 高频切换：HUD元素,Tips |

### 5.2 Declaration

```csharp
public abstract class UIPanel
{
    public abstract VisibilityMode Mode { get; }
}

public enum VisibilityMode
{
    ToggleActive,   // SetActive — full lifecycle, GC overhead
    CanvasSwitch,   // Canvas.enabled — keeps GO tree, skips OnEnable/OnDisable
    CanvasGroup     // alpha + raycast — fastest toggle, still in render loop
}
```

UIManager 根据 Mode 自动选择对应的显隐操作，业务层无需关心。

## 6. UIPanel Base Class

### 6.1 Abstract Members

```csharp
public abstract class UIPanel : MonoBehaviour
{
    public abstract LayerType Layer { get; }       // which canvas layer
    public abstract VisibilityMode Mode { get; }   // show/hide strategy
    public abstract string PanelId { get; }        // unique ID for registry

    // Cached component refs (lazy-loaded)
    public Canvas Canvas { get; private set; }
    public CanvasGroup CanvasGroup { get; private set; }
    public UIAnimation Animation { get; private set; }

    public bool IsVisible { get; private set; }
}
```

### 6.2 Lifecycle Hooks

```csharp
protected virtual void OnPreShow() { }             // prepare data, bind model, refresh UI
protected virtual Sequence PlayShowAnimation() { return null; }  // entry animation
protected virtual void OnShow() { }                // register events, start logic
protected virtual void OnHide() { }                // unregister events, stop logic (BEFORE animation)
protected virtual Sequence PlayHideAnimation() { return null; }  // exit animation (purely visual)
```

### 6.3 Show Flow

```
1. Visibility ON (per panel.Mode)
2. OnPreShow() → refresh data
3. PlayShowAnimation() → await sequence
4. OnShow() → register events
5. IsVisible = true
```

### 6.4 Hide Flow

```
1. OnHide() → unregister events immediately (panel stops responding)
2. PlayHideAnimation() → await sequence (pure visual transition)
3. Visibility OFF (per panel.Mode)
4. IsVisible = false
```

**设计决策：OnHide 在动画之前执行。** 关闭瞬间面板即停止交互，避免播放出场动画期间用户仍可点击按钮等不合理行为。出场动画仅用于视觉过渡。

## 7. UIManager

### 7.1 Core API

```csharp
public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // Registration (on init)
    public void Register(UIPanel panel);

    // Stack (Main / Popup layers)
    public async UniTask PushAsync(string panelId);
    public async UniTask PopAsync();
    public void PopTo(string panelId);

    // Overlay (Top / Guide layers)
    public async UniTask OpenAsync(string panelId);
    public async UniTask CloseAsync(string panelId);

    // Always (Base layer)
    public async UniTask ShowAlwaysAsync(string panelId);
    public async UniTask HideAlwaysAsync(string panelId);

    // Lookup
    public T GetPanel<T>(string panelId) where T : UIPanel;
}
```

### 7.2 Internal State

```csharp
private Canvas[] _layers = new Canvas[5];          // 5 canvas roots
private Dictionary<string, UIPanel> _registry;     // all registered panels
private Stack<UIPanel> _stack;                     // Main/Popup stack
private HashSet<UIPanel> _activeOverlays;          // active overlays
```

### 7.3 Stack Behavior

Push 新面板时：
1. 如果栈非空，当前栈顶面板调用 OnHide() 暂停事件（面板仍在视觉上可见但被遮挡，停止交互）
2. 新面板执行完整入场：OnPreShow → PlayShowAnimation → OnShow → Push to stack

Pop 时：
1. 栈顶面板：OnHide() → PlayHideAnimation() → visibility OFF → Pop from stack
2. 如果栈非空，新栈顶面板调用 OnShow() 恢复事件

### 7.4 Overlay Behavior

- Open/Close 不影响 Stack
- 多个 Overlay 可同时活跃
- 层级由 Canvas sort order 自然决定

## 8. UIAnimation

### 8.1 UIAnimation Component

挂载在 UIPanel GameObject 上，由 UIPanel 通过 `Animation` 属性访问。

```csharp
public class UIAnimation : MonoBehaviour
{
    public UIAnimPreset ShowPreset;   // Inspector-configurable entry animation
    public UIAnimPreset HidePreset;   // Inspector-configurable exit animation

    public Sequence PlayShow();       // build + play ShowPreset
    public Sequence PlayHide();       // build + play HidePreset
    public SequenceBuilder Build();   // fluent API for custom sequences
    public void KillAll();            // kill all tweens on this object

    // Direct tween shortcuts
    public Tweener FadeIn(float duration = 0.3f);
    public Tweener ScaleIn(float duration = 0.3f);
    public Tweener SlideIn(Direction dir, float duration = 0.3f);
}
```

### 8.2 UIAnimPreset

```csharp
[Serializable]
public class UIAnimPreset
{
    public float Duration = 0.3f;
    public Ease Ease = Ease.OutCubic;
    public float Delay;
    public bool Fade;       // alpha transition
    public bool Scale;      // scale from 0.9→1 (show) or 1→0.95 (hide)
    public bool Slide;      // slide from direction
    public Direction SlideDir;
}
```

### 8.3 SequenceBuilder (Fluent API)

```csharp
var seq = anim.Build()
    .FadeIn(0.2f)
    .Join(() => anim.ScaleIn(0.3f))
    .Then(() => icon.Punch())
    .Delay(0.1f)
    .Then(() => text.CountUp(100))
    .Play();  // → Sequence
```

- `Then(fn)` — 在上一个完成后顺序执行
- `Join(fn)` — 与上一个并行执行
- `Delay(s)` — 插入等待

### 8.4 Tween Extension Methods

| Extension | Target | Use Case |
|-----------|--------|----------|
| Punch() | Transform | Icon click bounce / hit feedback |
| Shake() | Transform | Error / invalid input feedback |
| CountUp() | Text (TMP) | Score / damage numbers |
| Flash() | Graphic | Highlight / on-hit white flash |

## 9. File Structure

```
Assets/Scripts/Hotfix/GameSystems/UI/
├── UI.asmdef                    # Assembly definition
├── Const/
│   └── UIConst.cs               # LayerType enum, VisibilityMode enum
├── Core/
│   ├── UIManager.cs             # Singleton, stack, overlay, layer management
│   ├── UIPanel.cs               # Abstract base class
│   └── ScreenAdapter.cs         # CanvasScaler + SafeArea
├── Animation/
│   ├── UIAnimation.cs           # Component + presets
│   ├── UIAnimPreset.cs          # Preset data container
│   ├── SequenceBuilder.cs       # Fluent API
│   └── UITweenExtensions.cs     # Punch, Shake, CountUp, Flash
└── Panel/
    ├── HUD/                     # HUD panels (Base layer)
    ├── Main/                    # Bag, Skills, Character, Settings (Main layer)
    ├── Popup/                   # Confirm, Tips, Detail (Popup layer)
    ├── Top/                     # Loading, Toast, Reconnect (Top layer)
    └── Guide/                   # Guide mask, highlight (Guide layer)
```

## 10. Assembly Definition

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
    "allowUnsafeCode": false
}
```

- 依赖 `Core`（AOT 层，使用 Res 加载 UI Prefab）
- 依赖 `UniTask`（异步动画协调）
- DOTween 通过 Plugins 目录直接可用，无需 asmdef 引用

## 11. Integration with Res System

- UIManager 加载 Panel Prefab 使用 `Res.LoadAsync<GameObject>(key)`（已在 AOT/Core/ 中建立）
- Prefab 实例化由 UIManager 处理（不属于 Res 系统职责）
- UIManager 注册时记录 Prefab 的 Addressable key，用于释放

## 12. Configuration (Unity Editor Manual Work)

- 创建 5 个 Canvas Prefab，每层对应 UIManager 的一个 Layer
- Canvas Prefab 挂载 ScreenAdapter 组件
- 所有 UI Panel Prefab 标记为 Addressable
- DOTween Settings 配置默认 Ease、动画更新模式

## 13. Out of Scope

- 数据绑定引擎（反射/索引器绑定）
- ViewModel 自动装配
- UI 序列化/持久化（窗口状态保存）
- 多语言/本地化 UI（需单独设计）
- 3D UI / World Space UI
