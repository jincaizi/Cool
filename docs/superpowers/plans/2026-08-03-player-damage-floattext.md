# 玩家掉血飘字差异化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 玩家掉血飘字与怪物掉血飘字一眼可辨:玩家=亮灰 (0.85,0.85,0.85) + 黑描边 + 58px,怪物保持红字 52px 不变;扣除血量 ≤ 0 不显示飘字。

**Architecture:** 数据驱动方案——`FloatTextSettings` 增加描边字段(默认 0 无描边,现有资产零影响),`FloatTextRenderer.Spawn()` 统一应用描边,新建玩家专用 `FloatText_PlayerDamage.asset` 由 `DisplayEventBridge` 玩家回调使用。0 值过滤放在 `ShowDamageText` 入口单点。规格: `docs/superpowers/specs/2026-08-03-player-damage-floattext-design.md`

**Tech Stack:** Unity 2022.3.25f1, TextMeshPro, DOTween, NUnit (EditMode tests via Unity MCP `tests-run`), HybridCLR Hotfix 层 (`Assets/Scripts/Hotfix/GameSystems/Nameplate/`)

---

### Task 1: 0 伤害飘字过滤(TDD)

**Files:**
- Create: `Assets/Scripts/Tests/Editor/FloatTextRendererZeroDamageTests.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs:38-40`

- [ ] **Step 1: 写失败测试**

创建 `Assets/Scripts/Tests/Editor/FloatTextRendererZeroDamageTests.cs`:

```csharp
using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Nameplate;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class FloatTextRendererZeroDamageTests
    {
        private GameObject _host;
        private FloatTextRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("TestCanvas");
            _renderer = new FloatTextRenderer(_host.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _renderer.Cleanup();
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void test_float_text_renderer_zero_damage_no_spawn()
        {
            // Arrange
            var settings = ScriptableObject.CreateInstance<FloatTextSettings>();
            var activeField = typeof(FloatTextRenderer)
                .GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance);
            var mergeField = typeof(FloatTextRenderer)
                .GetField("_mergeTracker", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act
            _renderer.ShowDamageText(1, Vector3.zero, settings, 0);

            // Assert
            var activeCount = ((ICollection)activeField.GetValue(_renderer)).Count;
            var mergeCount = ((ICollection)mergeField.GetValue(_renderer)).Count;
            Assert.AreEqual(0, activeCount, "0 伤害不应创建飘字对象");
            Assert.AreEqual(0, mergeCount, "0 伤害不应进入合并跟踪");
        }

        [Test]
        public void test_float_text_renderer_negative_damage_no_spawn()
        {
            // Arrange
            var settings = ScriptableObject.CreateInstance<FloatTextSettings>();
            var activeField = typeof(FloatTextRenderer)
                .GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act
            _renderer.ShowDamageText(1, Vector3.zero, settings, -5);

            // Assert
            var activeCount = ((ICollection)activeField.GetValue(_renderer)).Count;
            Assert.AreEqual(0, activeCount, "负数伤害不应创建飘字对象");
        }
    }
}
```

- [ ] **Step 2: 跑测试,预期 FAIL**

Run (Unity MCP `tests-run`): testMode=EditMode, testClass=`FloatTextRendererZeroDamageTests`
预期:2 failed——当前实现 `ShowDamageText` 对 value=0/-5 直接走 `Spawn()` 创建 TMP 对象,`_active` 数量为 1。
(若报"场景未保存"错误,先保存所有打开场景再跑。)

- [ ] **Step 3: 实现 0 值过滤**

修改 `FloatTextRenderer.cs:40`(ShowDamageText 入口):

```csharp
        public void ShowDamageText(int entityId, Vector3 worldPos, FloatTextSettings settings, int value)
        {
            if (!EnableVFX || value <= 0) return;
```

- [ ] **Step 4: 跑测试,预期 PASS**

Run (Unity MCP `tests-run`): testMode=EditMode, testClass=`FloatTextRendererZeroDamageTests`
预期:2 passed

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Tests/Editor/FloatTextRendererZeroDamageTests.cs Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs
git commit -m "feat: skip float text for non-positive damage values"
```

---

### Task 2: FloatTextSettings 描边字段 + 渲染器应用

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextSettings.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs:105-109`

- [ ] **Step 1: FloatTextSettings 加描边字段**

修改 `FloatTextSettings.cs`,在 `Color` 字段后加:

```csharp
        public Color Color = Color.white;
        [Range(0f, 1f)] public float OutlineWidth = 0f;
        public Color OutlineColor = Color.black;
```

- [ ] **Step 2: Spawn() 应用描边**

修改 `FloatTextRenderer.cs` `Spawn()` 中 `tmp.color = settings.Color;` 之后加:

```csharp
            tmp.color = settings.Color;
            tmp.outlineWidth = settings.OutlineWidth;
            tmp.outlineColor = settings.OutlineColor;
```

- [ ] **Step 3: 编译验证**

Run (Unity MCP): `assets-refresh`(强制重编译)
预期:无编译错误,无警告。

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextSettings.cs Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs
git commit -m "feat: data-driven outline support in float text settings"
```

---

### Task 3: DisplayEventBridge 玩家专用配置接线

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs:11-13,37-39`

- [ ] **Step 1: DisplayEventBridge 加玩家配置参数**

修改 `DisplayEventBridge.cs`:

```csharp
    public class DisplayEventBridge
    {
        private readonly FloatTextRenderer _floatText;
        private readonly FloatTextSettings _damageSettings;
        private readonly FloatTextSettings _critDamageSettings;
        private readonly FloatTextSettings _playerDamageSettings;

        public DisplayEventBridge(
            FloatTextRenderer floatText,
            FloatTextSettings damageSettings,
            FloatTextSettings critDamageSettings,
            FloatTextSettings playerDamageSettings)
        {
            _floatText = floatText;
            _damageSettings = damageSettings;
            _critDamageSettings = critDamageSettings;
            _playerDamageSettings = playerDamageSettings;
        }
```

`OnPlayerDamaged` 改为:

```csharp
        private void OnPlayerDamaged(PlayerTakeDamageEvent e)
        {
            // 玩家掉血飘字使用专属配置(灰字+黑描边),与怪物掉血(红字)区分
            _floatText.ShowDamageText(e.EntityId, e.HitPosition, _playerDamageSettings, e.Damage);
        }
```

- [ ] **Step 2: EntityDisplayManager 加字段并传参**

修改 `EntityDisplayManager.cs`:

```csharp
        [SerializeField] private FloatTextSettings _damageSettings;
        [SerializeField] private FloatTextSettings _critDamageSettings;
        [SerializeField] private FloatTextSettings _playerDamageSettings;
        [SerializeField] private FloatTextSettings _skillNameSettings;
```

`Awake()` 中:

```csharp
            _eventBridge = new DisplayEventBridge(
                _floatText,
                _damageSettings, _critDamageSettings, _playerDamageSettings);
```

- [ ] **Step 3: 编译验证**

Run (Unity MCP): `assets-refresh`
预期:无编译错误。

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs
git commit -m "feat: player damage float text uses dedicated settings"
```

---

### Task 4: 创建玩家飘字资产 + 场景接线

**Files:**
- Create: `Assets/PreRes/Settings/Display/FloatText_PlayerDamage.asset`(+ `.meta`)
- Modify: `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`(EntityDisplayManager MonoBehaviour, fileID 937939792, 第 7041 行 `_skillNameSettings` 之后)

- [ ] **Step 1: 用 MCP 创建资产**

Run (Unity MCP `script-execute`, isMethodBody=true):

```csharp
// 注意:body-only 模式自动 using 不含 Hotfix.* 命名空间,使用全限定名
var existing = AssetDatabase.LoadAssetAtPath<Hotfix.GameSystems.Nameplate.FloatTextSettings>("Assets/PreRes/Settings/Display/FloatText_Damage.asset");
var so = ScriptableObject.CreateInstance<Hotfix.GameSystems.Nameplate.FloatTextSettings>();
so.Type = Hotfix.GameSystems.Nameplate.FloatTextType.Normal;
so.Font = existing.Font;
so.FontMaterial = existing.FontMaterial;
so.FontSize = 58f;
so.Color = new Color(0.85f, 0.85f, 0.85f, 1f);
so.OutlineWidth = 0.3f;
so.OutlineColor = Color.black;
so.Duration = 0.8f;
so.MoveUpDistance = 40f;
so.FadeStartRatio = 0.5f;
so.StartScale = 1f;
AssetDatabase.CreateAsset(so, "Assets/PreRes/Settings/Display/FloatText_PlayerDamage.asset");
AssetDatabase.SaveAssets();
```

预期:创建成功。资产文件生成后,`Assets/PreRes/Settings/Display/FloatText_PlayerDamage.asset.meta` 中生成新 GUID。

- [ ] **Step 2: 读取新资产 GUID**

Read `Assets/PreRes/Settings/Display/FloatText_PlayerDamage.asset.meta`,记下 `guid:` 值(后续步骤使用,记为 `<PLAYER_GUID>`)。

- [ ] **Step 3: 场景接线**

修改 `DemoDay.unity`,在 MonoBehaviour 937939792(EntityDisplayManager)的字段区,`_skillNameSettings` 行(第 7041 行)之后加一行:

```
  _playerDamageSettings: {fileID: 11400000, guid: <PLAYER_GUID>, type: 2}
```

(注意缩进为两个空格,与其他 `_xxxSettings` 行一致;`<PLAYER_GUID>` 替换为 Step 2 读到的值。)

- [ ] **Step 4: 验证资产与接线**

1. Read `Assets/PreRes/Settings/Display/FloatText_PlayerDamage.asset`,确认:Color (0.85,0.85,0.85)、FontSize 58、OutlineWidth 0.3、OutlineColor 黑
2. Run (Unity MCP): `assets-refresh`,确认无 Missing/Missing Script 报错
3. 在 Unity 中打开 DemoDay 场景,检查 EntityDisplayManager Inspector:Player Damage Settings 槽已挂 FloatText_PlayerDamage

- [ ] **Step 5: Commit**

```bash
git add Assets/PreRes/Settings/Display/FloatText_PlayerDamage.asset Assets/PreRes/Settings/Display/FloatText_PlayerDamage.asset.meta Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat: player damage float text asset (gray + black outline) wired in scene"
```

---

### Task 5: PlayMode 视觉验证

**Files:** 无代码改动,验证产出截图供人工确认。

- [ ] **Step 1: 进入 Play Mode**

Run (Unity MCP): 打开 `DemoDay.unity` → `editor-application-set-state` 进入 Play Mode。

- [ ] **Step 2: 打怪验证怪物飘字不变**

玩家攻击怪物,截取 Game View(`screenshot-game-view`):
预期:怪物头顶红色 52px 数字上飘,与改动前一致。

- [ ] **Step 3: 挨打验证玩家飘字**

让怪物攻击玩家,截取 Game View:
预期:玩家位置亮灰 (0.85,0.85,0.85) 数字 + 黑描边 + 58px 上飘,与怪物红字一眼可辨。

- [ ] **Step 4: 池化复用验证**

连续执行:打怪 → 挨打 → 再打怪,截取最后打怪瞬间:
预期:怪物数字为红字无描边,不残留玩家灰字/描边样式(证明 outline 每次 Spawn 重置生效)。

- [ ] **Step 5: 0 伤害验证**

Run (Unity MCP `script-execute`, isMethodBody=true, Play Mode 中):

```csharp
// 注意:body-only 模式自动 using 不含 Hotfix.* 命名空间,使用全限定名
Hotfix.GameSystems.Sys3C.Core.Events.EventBus.Emit(
    new Hotfix.GameSystems.Sys3C.Core.Events.PlayerTakeDamageEvent(
        0, Vector3.zero, Hotfix.GameSystems.Sys3C.Core.Combat.HitZonePart.Chest,
        Vector3.forward, 0, Color.white));
```

预期:无任何飘字出现(0 伤害被过滤)。

- [ ] **Step 6: 退出 Play,汇总验证结果**

Run (Unity MCP): `editor-application-set-state` 退出 Play Mode。
将 3 张对比截图保存/汇总给用户确认;全部通过后本计划完成。

---

## 备注

- `git status` 工作区有既有未提交改动(Plugins DLL、packages-lock.json 等),**不要** `git add .`;每个 commit 按任务中列出的精确文件列表提交
- `tests-run` 前置条件:所有打开场景必须已保存
- 描边实现依赖 TMP 运行时 outline 属性,EditMode 无法覆盖,由 Task 5 PlayMode 验证
- `OutlineWidth` 0.3 为初始建议值,视觉验证时可微调资产
