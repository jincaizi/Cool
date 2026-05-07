# Nameplate & Floating Text System Design

**Date:** 2026-05-08
**Engine:** Unity 2022.3.25f1
**Layer:** Hotfix (`Assets/Scripts/Hotfix/GameSystems/Nameplate/`)
**UI:** TMP (3D Mode) + Screen Space Canvas
**Animation:** DOTween

## 1. Motivation

- 项目无名牌/伤害数字/技能名显示
- 怪物最多 100 个同屏，角色 5-10 个，需保证性能
- 当前怪物受伤不发任何事件（MonsterStats.OnHPChanged 无订阅者），需补事件钩子
- 需要世界空间昵称 + 目标框血条 + 浮字特效三套系统

## 2. Architecture Overview

```
┌────────────────────────────────────────────────────────┐
│                NameplateManager (singleton)             │
│  ┌─────────────────────┐  ┌───────────────────────────┐│
│  │  TMP 3D Nameplates   │  │  FloatingTextPool        ││
│  │  (常驻，3D World)    │  │  (临时，Screen Space)     ││
│  │  - 每单位1个TMP       │  │  - 预建10个，不够+10     ││
│  │  - Billboard相机朝向  │  │  - WorldToScreenPoint    ││
│  │  - 同字体/材质合批    │  │  - DOTween动画后回池     ││
│  └─────────────────────┘  └───────────────────────────┘│
└────────────────────────────────────────────────────────┘
           │
    ┌──────┴──────┐
    │ EventBus     │
    │ DamageEvent  │  (player takes damage)
    │ MonsterTD…   │  (monster takes damage — NEW)
    │ SkillActi…   │  (skill fired — NEW)
    └─────────────┘
```

## 3. TMP 3D Nameplate System

### 3.1 Rendering

- TextMeshPro 3D 模式（MeshRenderer），非 Canvas
- 所有昵称用同一个 TMP Font Asset + 同一个 Material 实例
- 同字体 + 同材质 + 同 Shader → Unity Dynamic Batching 自动合批
- 放在独立 Layer（`Nameplate`），主相机额外渲染该层

### 3.2 Billboard

每帧（LateUpdate）每个 TMP GameObject 的 transform 朝向相机：

```csharp
transform.rotation = Camera.main.transform.rotation;
```

### 3.3 NameplateTag

挂载在每个需要昵称的单位头顶：

```csharp
public class NameplateTag : MonoBehaviour
{
    public Vector3 Offset = new Vector3(0, 2.5f, 0);  // 头顶偏移
    public string DisplayName;                          // Editor 设置或代码赋值
    public Color NameColor = Color.white;
}
```

### 3.4 NameplateManager

```csharp
public class NameplateManager : MonoBehaviour
{
    private Dictionary<Transform, TextMeshPro> _nameplates;
    private TMP_FontAsset _font;
    private Material _material;

    public void Register(Transform owner, string name, Color color);
    public void Unregister(Transform owner);
    public void UpdateName(Transform owner, string newName);
    public void SetVisible(Transform owner, bool visible);

    void LateUpdate() { /* billboard + follow position for all registered */ }
}
```

### 3.5 LOD / Culling

- 距离超过阈值（如 50m）不更新不渲染
- 视野外的 TMP Object 通过 `renderer.enabled = false` 关闭

## 4. FloatingTextPool

### 4.1 Pool Design

```csharp
public class FloatingTextPool
{
    private Stack<TextMeshProUGUI> _free;     // idle items
    private HashSet<TextMeshProUGUI> _active;  // currently animating
    private const int GrowSize = 10;

    public void PreWarm(int count = 10);       // init pool
    public void Spawn(Vector3 worldPos, string text, FloatingTextConfig cfg);
    private void Grow();                       // instantiate +10 items
    private void Enqueue(TextMeshProUGUI item); // return to pool
}
```

- 初始化时 PreWarm(10) 创建 10 个 TMP GameObject
- Spawn 时如果 _free 为空，Grow() 创建额外 10 个
- 动画完成（DOTween Sequence OnComplete）后自动 Enqueue 回池
- 回到池时重置：alpha=1, scale=1, text="", obj.SetActive(false)

### 4.2 Canvas Setup

- 一个独立 Screen Space Canvas，sortOrder = 4500（高于 Top=4000，低于 Guide=5000）
- 所有池中的 TMP 都是此 Canvas 的子节点
- 不需要额外 Canvas，合批最优

### 4.3 FloatingTextConfig

```csharp
[Serializable]
public class FloatingTextConfig
{
    public Color Color;
    public float FontSize;
    public float Duration;
    public float MoveUpDistance;
    public float StartScale;
    public bool PunchScale;
    public Ease Ease;
}

public static class FloatingTextPresets
{
    public static FloatingTextConfig Damage = new() {
        Color = new Color(1f, 0.27f, 0.27f),     // #FF4444
        FontSize = 36f, Duration = 1f,
        MoveUpDistance = 50f, StartScale = 1f, Ease = Ease.OutCubic
    };
    public static FloatingTextConfig CritDamage = new() {
        Color = new Color(1f, 0.53f, 0f),         // #FF8800
        FontSize = 42f, Duration = 1.2f,
        MoveUpDistance = 70f, PunchScale = true, Ease = Ease.OutBack
    };
    public static FloatingTextConfig Heal = new() {
        Color = new Color(0.27f, 1f, 0.27f),      // #44FF44
        FontSize = 32f, Duration = 1f,
        MoveUpDistance = 40f, Ease = Ease.OutCubic
    };
    public static FloatingTextConfig SkillName = new() {
        Color = new Color(1f, 0.84f, 0f),           // #FFD700
        FontSize = 28f, Duration = 1.5f,
        MoveUpDistance = 20f, StartScale = 0.8f, Ease = Ease.OutCubic
    };
}
```

### 4.4 Spawn Lifecycle

```
1. Pop TMP from _free (Grow if empty)
2. Set text, color, fontSize from config
3. obj.SetActive(true)
4. Position: Camera.main.WorldToScreenPoint(worldPos + offsetY)
5. DOTween Sequence:
   - Move anchoredPosition.y += MoveUpDistance
   - DOFade(0f, Duration) or tmp.DOColor with 0 alpha
   - if PunchScale: DOPunchScale
   - if StartScale != 1: scale from StartScale to 1
6. OnComplete → reset → Enqueue back to _free
```

## 5. Target Panel (目标框)

### 5.1 UIPanel

TargetPanel 是标准 UIPanel，放在 Base 层（sort=1000），CanvasGroup 模式：

```csharp
public class TargetPanel : UIPanel
{
    public override LayerType Layer => LayerType.Base;
    public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
    public override string PanelId => "TargetPanel";

    [SerializeField] private Image _portrait;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _levelText;
    [SerializeField] private Slider _hpSlider;
    [SerializeField] private TMP_Text _hpText;

    private ITargetable _currentTarget;

    public void Bind(ITargetable target);
    public void Clear();
    void OnDestroy() { if (_currentTarget != null) Clear(); }
}
```

### 5.2 ITargetable Interface

```csharp
public interface ITargetable
{
    string DisplayName { get; }
    int Level { get; }
    Sprite Portrait { get; }
    float HPPercent { get; }
    int CurrentHP { get; }
    int MaxHP { get; }
    Vector3 WorldPosition { get; }
    event Action<float, int, int> OnHPChanged;  // percent, current, max
    event Action OnDeath;
}
```

MonsterEntity 和玩家角色类实现此接口。TargetPanel 只依赖 ITargetable，不绑具体类型。

### 5.3 Flow

```
Select target → UIManager.ShowAlwaysAsync("TargetPanel") → Bind(target)
              → subscribe OnHPChanged + OnDeath

Deselect / target dies → Clear() → unsubscribe → UIManager.HideAlwaysAsync("TargetPanel")
```

## 6. Event Hooks

### 6.1 New Events

```csharp
// DamageEvents.cs — add:
public struct MonsterTakeDamageEvent
{
    public MonsterEntity Monster;
    public int Damage;
    public bool IsCritical;
    public Vector3 HitPosition;
}

// Skills or Sys3C events — add:
public struct SkillActivatedEvent
{
    public string CasterId;
    public string SkillName;
    public Vector3 CasterPosition;
}
```

### 6.2 Emit Changes

- `MonsterEntity.TakeDamage()` → after `_stats.TakeDamage(data)`, emit `MonsterTakeDamageEvent`
- `SkillExecutor.OnHitboxTriggered()` or `SkillCoordinator` → on skill activation, emit `SkillActivatedEvent`
- Player damage: existing `DamageEvent` in `StateCoordinator.HandleDamage()`

### 6.3 FloatingTextPool Subscriptions

```
EventBus.Subscribe<DamageEvent> → Spawn(floatingPos, $"-{e.Damage}", Damage/CritDamage)
EventBus.Subscribe<MonsterTakeDamageEvent> → Spawn(hitPos, $"-{e.Damage}", Damage/CritDamage)
EventBus.Subscribe<SkillActivatedEvent> → Spawn(casterPos, e.SkillName, SkillName)
```

## 7. File Structure

```
Assets/Scripts/Hotfix/GameSystems/
├── Nameplate/
│   ├── Nameplate.asmdef               # refs: Core, UniTask, DOTween.Modules
│   ├── NameplateManager.cs            # 单例
│   ├── NameplateTag.cs                # 头顶挂载标记
│   ├── FloatingTextPool.cs           # 对象池 + 动画
│   └── FloatingTextConfig.cs          # 预设 + 配置SO
│
├── UI/Panel/HUD/
│   └── TargetPanel.cs                 # 目标框 UIPanel
│
├── Sys3C/Core/Combat/
│   └── ITargetable.cs                 # 目标接口
│
├── Sys3C/Core/Events/
│   └── DamageEvents.cs                # + MonsterTakeDamageEvent, SkillActivatedEvent
│
├── Monster/
│   └── MonsterEntity.cs               # + ITargetable 实现, + 发射 MonsterTakeDamageEvent
│
└── Skills/Runtime/
    └── SkillExecutor.cs               # + 发射 SkillActivatedEvent
```

## 8. Prerequisites

- 安装 `com.unity.textmeshpro` 包（Unity 内置，需手动添加）
- 创建 TMP Font Asset（从项目中文字体生成 SDF）
- 配置 Layer `Nameplate`，主相机 Culling Mask 包含该层

## 9. Out of Scope

- 怪物头顶小血条（3D 空间进度条）
- Buff/Debuff 图标显示
- PvP 名字颜色区分
- 多目标同时显示
- 浮字数字的弹射/曲线路径（当前仅支持直线上飘）
