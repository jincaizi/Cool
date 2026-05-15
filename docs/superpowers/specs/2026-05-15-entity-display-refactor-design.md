# EntityDisplayManager 职责拆分 + 配置化设计

> 对 `EntityDisplayManager` 进行单一职责拆分，并将铭牌和飘字的视觉参数全部抽取为 ScriptableObject 配置资产，支持运行时 Inspector 调试。

## 一、问题诊断

当前 `EntityDisplayManager.cs`（~460行）承担 7 项不同职责：

| 职责 | 位置 |
|------|------|
| Singleton 生命周期 + Canvas 创建 | Awake/OnDestroy/CreateCanvas |
| 铭牌池管理（租/还/模板创建） | RentNameplateRoot/ReturnNameplate/CreateNameplateTemplate |
| 铭牌每帧更新（WorldToScreen + 距离剔除 + Alpha淡出） | LateUpdate |
| 飘字池管理（租/还/模板创建） | RentFloatTMP/ReturnFloatText/CreateFloatTMP |
| 飘字动画（DOTween序列 x 4种类型） | SpawnFloatText |
| 伤害合并追踪（MergeTracker） | ShowDamageText + LateUpdate 清理 |
| 事件处理（Damage/MonsterDamage/SkillActivated） | OnEnable/OnDisable + 3个 Handler |
| 屏幕震动 | ShowDamageText 内 Crit 分支 |

硬编码的视觉参数散落各处：
- 铭牌字号 `18`、描边 `0.15`、剔除距离 `50f` 写在 `CreateNameplateTemplate`
- 飘字各类预设的 `FontSize`/`Duration`/`MoveUpDistance` 写在 `FloatTextPresets`
- 字体/材质引用只有 manager 级别的 `_fontAsset`/`_fontMaterial`，无法按类型区分

## 二、目标架构

```
EntityDisplayManager (MonoBehaviour, DontDestroyOnLoad, ~60行)
│  Inspector: 拖入 1个 NameplateSettings + 7个 FloatTextSettings
│  持有 Canvas、Camera 引用
│
├── NameplateRenderer (~120行)
│   ├── Register / Unregister / UpdateName / SetVisible
│   ├── Tick(camera): WorldToScreenPos + 距离剔除 + alpha 淡出
│   └── 池: Stack<GameObject> (模板: HorizontalLayoutGroup + Image + TMP_Text)
│
├── FloatTextRenderer (~150行)
│   ├── ShowFloatingText(worldPos, settings, value)
│   ├── ShowDamageText(entityId, worldPos, settings, value)
│   ├── Spawn + DOTween 动画序列
│   ├── MergeTracker (200ms 合并窗口)
│   └── 池: Stack<TextMeshProUGUI>
│
├── DisplayEventBridge (~40行)
│   ├── 订阅: DamageEvent / MonsterTakeDamageEvent / SkillActivatedEvent
│   └── 映射到 FloatTextRenderer + DamageScreenEffect 调用
│
└── DamageScreenEffect (~30行)
    └── 全屏红色 RawImage, DOTween: DoFade(0.15 → 持续3s → DoFade(0))
        仅在首次受到非NPC实体伤害时触发
```

### 数据流

```
EventBus → DisplayEventBridge → FloatTextRenderer (飘字)
                              → DamageScreenEffect (泛红)

MonsterEntity.Init()      → NameplateRenderer.Register(id, transform, config)
MonsterEntity.OnDestroy() → NameplateRenderer.Unregister(id)
```

## 三、配置资产体系

### 3.1 资产列表

所有配置以 ScriptableObject 形式存放在 `Assets/Settings/Display/`：

| 资产 | 文件名 | 用途 |
|------|--------|------|
| NameplateSettings | `NameplateSettings.asset` | 铭牌通用视觉参数 |
| FloatTextSettings (Damage) | `FloatText_Damage.asset` | 普通伤害飘字 |
| FloatTextSettings (CritDamage) | `FloatText_CritDamage.asset` | 暴击伤害飘字 |
| FloatTextSettings (Heal) | `FloatText_Heal.asset` | 治疗飘字 |
| FloatTextSettings (Dodge) | `FloatText_Dodge.asset` | 闪避飘字 |
| FloatTextSettings (Block) | `FloatText_Block.asset` | 格挡飘字 |
| FloatTextSettings (DOT) | `FloatText_DOT.asset` | 持续伤害飘字 |
| FloatTextSettings (SkillName) | `FloatText_SkillName.asset` | 技能名飘字 |

### 3.2 NameplateSettings 字段

```csharp
[CreateAssetMenu(menuName = "Display/NameplateSettings")]
public class NameplateSettings : ScriptableObject
{
    public TMP_FontAsset Font;
    public Material FontMaterial;
    public float FontSize = 18f;
    public Color DefaultColor = Color.white;
    public float OutlineWidth = 0.15f;
    public Color OutlineColor = Color.black;
    public float VerticalOffset = 2.5f;
    public float CullDistance = 50f;
    public float FadeStartDistance = 30f;
    public Vector2 IconSize = new(20, 20);
}
```

### 3.3 FloatTextSettings 字段

```csharp
[CreateAssetMenu(menuName = "Display/FloatTextSettings")]
public class FloatTextSettings : ScriptableObject
{
    public FloatTextType Type;
    public TMP_FontAsset Font;
    public Material FontMaterial;
    public float FontSize = 36f;
    public Color Color = Color.white;
    public float Duration = 1f;
    public float MoveUpDistance = 50f;
    [Range(0f, 1f)] public float FadeStartRatio = 0.5f;
    public float StartScale = 1f;
}
```

## 四、类设计

### 4.1 EntityDisplayManager（协调器）

```csharp
public class EntityDisplayManager : MonoBehaviour
{
    [SerializeField] private NameplateSettings _nameplateSettings;
    [SerializeField] private FloatTextSettings _damageSettings;
    [SerializeField] private FloatTextSettings _critDamageSettings;
    [SerializeField] private FloatTextSettings _healSettings;
    [SerializeField] private FloatTextSettings _dodgeSettings;
    [SerializeField] private FloatTextSettings _blockSettings;
    [SerializeField] private FloatTextSettings _dotSettings;
    [SerializeField] private FloatTextSettings _skillNameSettings;

    private Canvas _canvas;
    private Camera _camera;
    private NameplateRenderer _nameplate;
    private FloatTextRenderer _floatText;
    private DisplayEventBridge _eventBridge;
    private DamageScreenEffect _damageScreenEffect;

    public static EntityDisplayManager Instance { get; private set; }

    void Awake()     { /* 单例检查 + DontDestroyOnLoad + 创建 Canvas + new 各子系统 */ }
    void LateUpdate(){ _nameplate?.Tick(_camera); }
    void OnEnable()  { _eventBridge?.Enable(); }
    void OnDisable() { _eventBridge?.Disable(); }
    void OnDestroy() { /* 清理子系统 + 池 */ }

    // 转发 API（仅暴露一个方法给 DamageScreenEffect）
    public void TriggerDamageFlash() => _damageScreenEffect?.Flash();
}
```

### 4.2 NameplateRenderer

- 持有 `NameplateSettings`，从 settings 读取所有视觉参数
- `Tick(camera)` 替代原来的 LateUpdate 逻辑
- 池模板创建时从 settings 取值而非硬编码

### 4.3 FloatTextRenderer

- `ShowFloatingText(worldPos, FloatTextSettings, value)` — 使用给定 settings 的视觉参数
- `ShowDamageText(entityId, worldPos, FloatTextSettings, value)` — 额外做合并判断
- `FloatTextSettings` 直接作为参数传入，替代原来的 `FloatTextConfig`（不再承担视觉参数）

### 4.4 DisplayEventBridge

- 内部类，负责 EventBus 订阅/解订阅
- `Enable()` / `Disable()` 由 Manager 的 OnEnable/OnDisable 驱动
- 持有对 FloatTextRenderer、DamageScreenEffect 及各个 Settings 的引用
- `OnPlayerDamaged` → 首次非NPC伤害触发 `DamageScreenEffect.Flash()`
- `OnMonsterDamaged` → 根据 IsCritical 选择 `_critDamageSettings` 或 `_damageSettings`
- `OnSkillActivated` → 使用 `_skillNameSettings`

### 4.5 DamageScreenEffect

- 在 Canvas 下创建一个全屏 `RawImage`（红色，初始 alpha=0）
- `Flash()` — 如果 3 秒冷却已过：`DoFade(0.15f, 0.1f)` → `Wait(3f)` → `DoFade(0, 0.5f)`

## 五、现有代码改动

### 5.1 NameplateConfig 精简

移除 `VerticalOffset`、`CullDistance`（归入 `NameplateSettings`），仅保留业务数据：

```csharp
public struct NameplateConfig
{
    public string DisplayName;
    public Color NameColor;      // 由职业/实体类型决定，覆盖 settings.DefaultColor
    public Sprite ClassIcon;     // null = 不显示图标
}
```

### 5.2 FloatTextConfig 精简

移除视觉参数，改为持有 Settings 引用：

```csharp
public class FloatTextConfig
{
    public FloatTextSettings Settings;
    public string TextOverride;  // 仅用于覆盖文字内容（如"闪避"、"格挡"）
    public bool ShowName;
}
```

### 5.3 删除 FloatTextPresets

所有预设值迁移到 `FloatTextSettings` 资产中。`DisplayEventBridge` 内部根据事件类型选择对应的 Settings。

### 5.4 删除 ColorPalette

颜色归入各 `FloatTextSettings.Color` 或由调用方通过 `NameplateConfig.NameColor` 传入。

## 六、性能影响

| 项目 | 变化 |
|------|------|
| 每帧额外分配 | 无（子系统已是堆上对象，无额外 GC） |
| LateUpdate | 相同循环逻辑，移至 `NameplateRenderer.Tick()` |
| 资产加载 | 8 个小型 ScriptableObject，启动时序列化引用，无额外运行时加载 |
| Canvas 数量 | 仍为 1 |

无性能退化。

## 七、文件改动清单

### 新建

| 文件 | 说明 |
|------|------|
| `Assets/Settings/Display/` 目录 + 8 个 `.asset` | 配置资产 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateSettings.cs` | ScriptableObject |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextSettings.cs` | ScriptableObject |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/NameplateRenderer.cs` | 铭牌子系统 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs` | 飘字子系统 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs` | 事件桥接 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/DamageScreenEffect.cs` | 伤害泛红效果 |

### 重写

| 文件 | 变化 |
|------|------|
| `EntityDisplayManager.cs` | ~460行 → ~60行，仅做协调和转发 |

### 修改

| 文件 | 变化 |
|------|------|
| `NameplateConfig.cs` | 移除 VerticalOffset、CullDistance |
| `FloatTextConfig.cs` | 移除视觉参数，加入 FloatTextSettings 引用 |

### 删除

| 文件 | 原因 |
|------|------|
| `FloatTextPresets`（FloatTextConfig.cs 内） | 迁移到 ScriptableObject 资产 |
| `ColorPalette.cs` | 颜色归入各 Settings |

## 八、不在此设计范围内的内容

- 血条（HP Bar）— 后续独立设计
- 称号/头衔系统
- 任何 UI 框架层的改动
