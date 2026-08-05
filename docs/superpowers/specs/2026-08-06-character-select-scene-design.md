# 独立选人场景重构设计

- 日期: 2026-08-06
- 状态: 已批准（方案 A：独立选人场景 + 单体预览切换）
- 前置: `2026-08-05-character-selection-design.md`（双 prefab 选人系统，已交付）
- 触发背景: 交付后用户反馈两个问题 —— ① 选完角色后 DemoDay 场景残留 2 个预览模型；② 选人应单体展示对应角色并支持切换，选完再进 DemoDay

## 1. 背景与结论

现有实现把两个剥离预览体烘焙在 DemoDay 场景中，选人面板是覆盖层，确认后在同一场景生成玩家。问题：预览体残留在游戏世界；选人展示是"两体并排"而非"展示所选角色"。

**结论：新建独立选人场景 `CharacterSelect`（营地背景），预览改为单体切换；确认后关闭面板并 `SceneManager.LoadScene("DemoDay")`，DemoDay 的 `PlayerSpawner` 改为 `Start` 时从保存的选择生成玩家。** 预览体只存在于选人场景（随场景卸载消失，问题①根除）；点击卡片即时切换展示模型（问题②）。

## 2. 架构

```
CharacterSelect.unity（场景 0，启动场景）        DemoDay.unity（场景 1，游戏场景）
├─ 营地背景（SimpleLowPolyNature 资产）          ├─ 无预览体（已清理）
├─ 灯光（Directional + 篝火点光）                ├─ PlayerSpawner（Start 兜底生成）
├─ Main Camera（普通相机，静态取景）              ├─ PlayerSpawnPoint
├─ UIManager（复制自 DemoDay，DontDestroyOnLoad） ├─ UIManager（保留，自毁兜底）
├─ Preview_Male / Preview_Female（同一位置，      ├─ Main Camera（复位为游戏视角）
│  默认只显示 Load() 性别，PreviewRotator 各一）   └─ 其余（怪物/环境/管理器不变）
└─ SelectStageController（场景接线）
```

**UIManager 双实例折衷（已记录）**：CharacterSelect 场景放一个（复制自 DemoDay），DemoDay 保留。DontDestroyOnLoad 单例自毁逻辑兜底（后加载的自毁），正式流程只会存在一个实例。保留双份是为 dev 便利路径（直接 Play DemoDay 时面板/ HUD 可用）。维护成本：两处场景对象，内容同步。

## 3. 场景结构（CharacterSelect.unity）

- 路径：`Assets/Scenes/CharacterSelect.unity`
- 营地背景：SimpleLowPolyNature 草地地面 + 帐篷 + 篝火 + 少量树/植物（复用 DemoDay 同款 prefab 实例）
- 灯光：Directional Light（active）+ 篝火点光（Point Light）
- Main Camera：**普通相机**（不挂 ThirdPersonCameraController），静态取景面向舞台中央
- 预览位：`Preview_Male` / `Preview_Female`（**同一位置**，共用骨架视觉上无缝切换），默认仅显示 `CharacterSelectionService.Load()` 的性别；各挂 `PreviewRotator`
- `SelectStageController`：序列化引用 `_malePreview` / `_femalePreview`
- `UIManager`：从 DemoDay 复制（含 5 个 Canvas 层子对象）

## 4. 代码变更

### 4.1 `CharacterSelectPanel`（修改，+2 事件 + 重入保护）

```csharp
public event Action<CharacterGender> OnSelectionChanged;  // Select() 时触发
public event Action OnClosed;                              // CloseSelf 完成后触发

private bool _closing;                                     // 重入保护

private void Select(CharacterGender gender)
{
    _selected = gender;
    RefreshHighlight();
    OnSelectionChanged?.Invoke(gender);
}

private void Confirm()
{
    if (_closing) return;      // 双击/重复确认保护
    _closing = true;
    CharacterSelectionService.Save(_selected);
    EventBus.Emit(new CharacterSelectedEvent(_selected));
    CloseSelf();
}

private async void CloseSelf()
{
    if (UIManager.Instance != null)
        await UIManager.Instance.CloseAsync(PanelId);
    OnClosed?.Invoke();
}
```

（Confirm 其余逻辑不变；事件用 C# event 而非 EventBus —— 面板→场景组件的同层直连，不跨系统。）

### 4.2 `SelectStageController`（新增，`Hotfix.GameSystems.UI`）

```csharp
/// <summary>
/// 选人舞台控制器：切换预览体展示所选性别；面板关闭后加载游戏场景
/// </summary>
public class SelectStageController : MonoBehaviour
{
    [SerializeField] private GameObject _malePreview;
    [SerializeField] private GameObject _femalePreview;

    private bool _loading;
    private CharacterSelectPanel _panel;

    private async void Start()
    {
        // UIManager.Awake 异步注册面板，轮询等待
        while (UIManager.Instance == null)
            await UniTask.Delay(50);
        while (UIManager.Instance.GetPanel<CharacterSelectPanel>("CharacterSelectPanel") == null)
            await UniTask.Delay(50);

        _panel = UIManager.Instance.GetPanel<CharacterSelectPanel>("CharacterSelectPanel");
        ApplyPreview(CharacterSelectionService.Load());
        _panel.OnSelectionChanged += ApplyPreview;
        _panel.OnClosed += LoadGameScene;
    }

    private void ApplyPreview(CharacterGender gender)
    {
        if (_malePreview != null) _malePreview.SetActive(gender == CharacterGender.Male);
        if (_femalePreview != null) _femalePreview.SetActive(gender == CharacterGender.Female);
    }

    private void LoadGameScene()
    {
        if (_loading) return;   // 双保险：防重复加载
        _loading = true;
        SceneManager.LoadScene("DemoDay");
    }

    private void OnDestroy()
    {
        if (_panel != null)
        {
            _panel.OnSelectionChanged -= ApplyPreview;
            _panel.OnClosed -= LoadGameScene;
        }
    }
}
```

零 GetComponent（全序列化引用）；OnDestroy 反订阅防泄漏。

### 4.3 `PlayerSpawner`（修改，+Start 兜底）

```csharp
private void Start()
{
    // 场景加载后从保存的选择生成（选人场景发的事件本场景收不到）
    if (_currentPlayer == null)
        OnCharacterSelected(new CharacterSelectedEvent(CharacterSelectionService.Load()));
}
```

复用现有生成逻辑（含 `PlayerSpawnedEvent` 广播 → HitStopManager 绑定不受影响）。EventBus 订阅保留（未来游戏内重选入口）。

## 5. DemoDay 清理

- 删除 2 个预览实例（Preview_Male/Preview_Female）及其 `PreviewRotator`
- Main Camera 复位为普通游戏视角（出生点后方，可被 `SnapToTarget` 覆盖）
- UIManager / PlayerSpawner / PlayerSpawnPoint / 怪物/环境/管理器全部保留

## 6. Build Settings

`CharacterSelect` = 场景 0（启动场景），`DemoDay` = 场景 1（`EditorBuildSettings.asset` 修改）

## 7. 数据流

```
启动(CharacterSelect) → UIManager 开面板 → StageController 显示 Load() 性别预览
→ 点卡片 → 高亮切换 + OnSelectionChanged → 预览体切换（单选展示）
→ 确认（_closing 置位）→ Save + Emit(CharacterSelectedEvent) + CloseSelf
→ OnClosed（仅一次，_loading 双保险）→ LoadScene("DemoDay")
→ PlayerSpawner.Start 按 Load() 生成玩家 → Sys3CEntry 自动绑相机/HUD
→ PlayerSpawnedEvent → HitStopManager 绑定 → 游戏开始
```

## 8. 错误处理与边界

| 场景 | 处理 |
|---|---|
| 双击/重复确认 | 面板 `_closing` 重入保护 + StageController `_loading` 双保险 |
| 面板注册未完成（UIManager.Awake 异步） | StageController 50ms 轮询等待 |
| 面板获取失败（资源缺失） | 轮询不退出 → 预览保持默认；确认路径仍可走（Save+关面板）；玩家由 Start 兜底生成，无死锁 |
| dev 直接 Play DemoDay | 无 StageController → 面板确认后不切场景；玩家已由 Start 生成；面板确认前为模态盖屏（可接受，记录为 dev 行为） |
| LoadScene 目标不在 Build Settings | Unity 报错（构建配置错误，开发期暴露） |
| 重复进入选人（未来） | 面板 OnShow 重读 Load()；StageController 按场景重建 |

## 9. 测试

### 9.1 单元测试

无新增纯逻辑（面板事件/场景接线）；既有 `CharacterSelectionService` 4 单测回归。

### 9.2 场景验证清单（手动 + 脚本模拟）

**正式流程（Play CharacterSelect）：**
1. 面板出现；默认预览 = 上次选择（无存档 = 男）；营地背景渲染正常
2. 点女卡片 → 高亮切换 + 预览切换为女（男模型隐藏）；点男卡片反向
3. 确认 → 面板关闭 → DemoDay 加载
4. 生成玩家性别 = 选择（**组件全集抽查**：11 根组件 + weapon_r 7 VFX + RingVisual + 4 技能）
5. 相机跟随、HUD、HitStopManager 绑定（`_playerAnimator` 非空）
6. **DemoDay 无预览残留**（场景无 Preview_* 对象）
7. 连点确认 → 只加载一次 DemoDay（无双重加载）
8. 持久化：重进 → 默认预览/高亮 = 上次选择

**dev 路径（直接 Play DemoDay）：**
9. 面板出现 → 确认 → 玩家生成（Load() 性别）、面板关闭、游戏正常、不切场景

## 10. 决策记录

- 2026-08-06：方案 A（独立选人场景 + 单体预览切换）确认。自审修正：① `Confirm` 加 `_closing` 重入保护（双击 → 双重 LoadScene 缺陷）；② dev 路径行为明确记录；③ DemoDay 相机复位为游戏视角；④ 双 UIManager 折衷显式记录。选人场景相机用普通相机（不挂 ThirdPersonCameraController）。
