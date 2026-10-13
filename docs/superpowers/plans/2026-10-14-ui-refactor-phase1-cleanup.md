# UI 框架重构 · 阶段 1（清创）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 删掉 UI 框架里的死代码，并把"层数硬编码在三处"收敛成一张单一真值表 —— 全程**零行为变化**。

**架构：** 层定义的唯一真值源从"枚举 + 散落的魔数 5 + 独立的排序号数组"变成 `UIConst.Layers` 一张表（行序即层序，下标必须等于枚举值）。Canvas 的创建从 `UIManager.Awake` 的私有循环里抽成 `internal static BuildLayerCanvas`，使它能在 EditMode 里被直接断言 —— 这是框架的第一个可测接缝。

**技术栈：** Unity 2022.3.25f1 / C# 9 / NUnit（EditMode: `GameSys.EditorTests`）/ UniTask

**规格：** `docs/superpowers/specs/2026-10-14-ui-framework-refactor-design.md` §8 阶段 1

**验收（规格 §8）：** 既有测试全绿 + 选人→世界链路跑通。

---

## 本阶段不做（防止越界）

| 不做 | 归属 |
|---|---|
| 改排序号数值、加层（Background/Hud/World…）、`LayerType` 改名 `UILayerId` | 阶段 3 |
| 删 `UIPanel.OnPreShow` 钩子、删 `PlayShow/PlayHideAnimation` 钩子 | 保留 —— 它们是 2026-05-08 设计文档 §6.2 记录的生命周期契约，阶段 5 的动画工作接在这里 |
| 类型寻址 / 删 `PanelId` 字符串 / 注册表 | 阶段 2 |
| `Bag/UI/` 三件套 | 阶段 5 |

**本阶段唯一允许的净效果：代码变少、魔数收敛，运行时行为逐位不变。**

---

## 文件结构

### 创建

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Tests/Editor/UILayerTableTests.cs` | 守住层表的两条不变量：下标 == 枚举值；排序号与重构前逐位相同 |
| `Assets/Scripts/Tests/Editor/UIManagerLayerCanvasTests.cs` | 断言 `BuildLayerCanvas` 产出的名字 / 排序号 / 组件集 |
| `Assets/Scripts/Tests/Editor/UIPanelLifecycleTests.cs` | 表征测试：`UIPanel` 的动画钩子默认不播动画（删除前后都必须通过） |

### 修改

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/UI/Const/UIConst.cs` | 删 `SortOrders`，加 `Layers` 表 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs` | 层循环改表驱动；抽 `BuildLayerCanvas`；删 `_stack`/`_activeOverlays` 与三个 Stack 方法 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs` | 去掉 `UIAnimation` 依赖（属性 + GetComponent）；两个动画钩子改为默认返回 null |

### 删除

```
Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimation.cs      (+ .meta)
Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimPreset.cs     (+ .meta)
Assets/Scripts/Hotfix/GameSystems/UI/Animation/SequenceBuilder.cs  (+ .meta)
Assets/Scripts/Hotfix/GameSystems/UI/Animation/UITweenExtensions.cs (+ .meta)
Assets/Scripts/Hotfix/GameSystems/UI/Animation.meta                 (文件夹 meta)
```

### 已核实的前提（实现者不必重查）

- `UIConst.SortOrders` 的**唯一**消费者是 `UIManager.cs`（全项目 grep 确认）
- `UIAnimation` 的脚本 GUID `4ee6cd6feb7d3f6409aaf8884c73d1ca` 在**全部 prefab / scene / asset 中零引用** —— 没有任何面板挂过它
- `UIPanel.Animation` 的引用点只有 `UIPanel.cs` 自身（14 行、21 行）
- UI 命名空间下的 `Direction` 枚举**只被 `UI/Animation/` 内部使用**，随目录一起消失，无外部消费者
- `PushAsync` / `PopAsync` / `PopTo` 全项目**零外部调用者**；`_activeOverlays` 只写不读
- ⚠️ **项目里有两个 `LayerType` 枚举**：`Hotfix.GameSystems.Sys3C.Core.LayerType`（Base/Attack/Hit）与 `Hotfix.GameSystems.UI.LayerType`（Base/Main/Popup/Top/Guide）。**两者都有 `Base`**。本计划动的是 UI 那个；任何同时 `using` 两个命名空间的文件都会产生歧义 —— 本阶段涉及的文件都只 `using Hotfix.GameSystems.UI`
- `AssemblyInfo.cs` 已有 `[assembly: InternalsVisibleTo("GameSys.EditorTests")]` → EditMode 测试可直接访问 `internal` 成员，**无需新增程序集管道**
- `GameSys.EditorTests.asmdef` 的 references 里**没有** `UnityEngine.UI`，但测试可以直接 `using UnityEngine.UI;` —— `Library/PackageCache/com.unity.ugui@1.0.0/Runtime/UnityEngine.UI.asmdef` 带了 `"autoReferenced": true`，自动引用对所有程序集生效（`GameSys.asmdef` 也没列它，却在用 `UnityEngine.UI.Image`，同理）。**不要为此改 asmdef。**
- `UIPanel.Layer` 是表达式体**属性**，不是序列化字段 → 改枚举不影响任何预制体

### 运行测试的方式

本环境用 Unity MCP 的 `tests-run`，传**裸类名**：

```
tests-run(testMode: "EditMode", testClass: "UILayerTableTests")
tests-run(testMode: "PlayMode")
```

**前置条件：所有打开的场景必须已保存**，否则 `tests-run` 直接抛异常。

---

## 任务 1：层表 —— 三处硬编码收敛成一处

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Const/UIConst.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs:13,75-92`
- 测试：`Assets/Scripts/Tests/Editor/UILayerTableTests.cs`（创建）

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/UILayerTableTests.cs`：

```csharp
using System;
using System.Linq;
using Hotfix.GameSystems.UI;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 1（清创）：层表是"层有多少个、叫什么、排序号多少"的唯一真值源。
    ///
    /// 守住的不变量是 **下标 == 枚举值** —— UIManager 用 (int)panel.Layer 当下标去索引
    /// _layers 数组，一旦表的行序与枚举值错位，面板会被静默塞进错误的 Canvas（不报错、
    /// 只是画在错误的层级上）。
    /// </summary>
    public class UILayerTableTests
    {
        [Test]
        public void test_layer_table_covers_every_enum_value_in_index_order()
        {
            // Arrange：Enum.GetValues 按底层值升序返回
            var values = (LayerType[])Enum.GetValues(typeof(LayerType));

            // Assert
            Assert.AreEqual(values.Length, UIConst.Layers.Length,
                "层表行数必须等于 LayerType 的取值个数");

            for (int i = 0; i < values.Length; i++)
            {
                Assert.AreEqual(values[i], UIConst.Layers[i].layer,
                    "层表第 " + i + " 行的层与枚举值不符");
                Assert.AreEqual(i, (int)UIConst.Layers[i].layer,
                    "层表下标必须等于枚举值（UIManager 拿它做数组下标）");
            }
        }

        [Test]
        public void test_layer_sort_orders_are_unique_and_unchanged_in_phase1()
        {
            // Arrange
            var actual = UIConst.Layers.Select(l => l.sortOrder).ToArray();

            // Assert
            Assert.AreEqual(actual.Length, actual.Distinct().Count(), "排序号不得重复");
            CollectionAssert.AreEqual(new[] { 1000, 2000, 3000, 4000, 5000 }, actual,
                "阶段 1 是零行为变化：排序号必须与重构前逐位相同。" +
                "数值重排属于阶段 3 —— 改这里说明改错了批次。");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "UILayerTableTests")`
预期：**编译失败** —— `UIConst` 不含 `Layers` 的定义。（C# 里引用不存在的成员表现为编译错误，这是本步骤期望的红。）

- [ ] **步骤 3：编写最少实现代码**

修改 `Assets/Scripts/Hotfix/GameSystems/UI/Const/UIConst.cs`，把 `UIConst` 类体整体替换为：

```csharp
    public static class UIConst
    {
        /// <summary>
        /// 层定义的唯一真值源：**行序即层序，数组下标必须等于 (int)LayerType 的值**。
        /// UIManager 按本表建 Canvas —— 层的数量、命名、排序号全部来自这里，
        /// 不再有第二个地方写 "5" 或排序号。
        /// 不变量由 UILayerTableTests 守住。
        /// </summary>
        public static readonly (LayerType layer, int sortOrder)[] Layers =
        {
            (LayerType.Base,  1000),
            (LayerType.Main,  2000),
            (LayerType.Popup, 3000),
            (LayerType.Top,   4000),
            (LayerType.Guide, 5000),
        };
    }
```

（`LayerType` 与 `VisibilityMode` 两个枚举保持原样不动。删除原来的 `SortOrders` 数组。）

- [ ] **步骤 4：改 UIManager 用表**

修改 `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`：

13 行，把

```csharp
        private readonly Canvas[] _layers = new Canvas[5];
```

改为

```csharp
        private readonly Canvas[] _layers = new Canvas[UIConst.Layers.Length];
```

75-92 行的 `CreateCanvasLayers()` 整体替换为：

```csharp
        private void CreateCanvasLayers()
        {
            for (int i = 0; i < UIConst.Layers.Length; i++)
            {
                var (layer, sortOrder) = UIConst.Layers[i];
                _layers[i] = BuildLayerCanvas(transform, layer, sortOrder);
            }
        }

        /// <summary>
        /// 建一层 Canvas。抽成 static 是为了能在 EditMode 里直接断言层表契约 ——
        /// 走 UIManager.Awake 会连带触发 Resources 加载与面板注册，测不动。
        /// </summary>
        internal static Canvas BuildLayerCanvas(Transform parent, LayerType layer, int sortOrder)
        {
            var go = new GameObject("Canvas_Layer_" + layer);
            go.transform.SetParent(parent);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            go.AddComponent<UnityEngine.UI.CanvasScaler>();
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            go.AddComponent<ScreenAdapter>();

            return canvas;
        }
```

> **零行为变化的核对点**：原代码的游戏对象名是 `$"Canvas_Layer_{(LayerType)i}"`，即 "Canvas_Layer_Base"。新代码是 `"Canvas_Layer_" + layer`，枚举字符串拼接结果**逐字相同**。

- [ ] **步骤 5：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "UILayerTableTests")`
预期：2 个测试 PASS。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Const/UIConst.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs \
        Assets/Scripts/Tests/Editor/UILayerTableTests.cs
git commit -m "refactor(ui): drive canvas layers from a single table

Layer count, names and sort orders lived in three places: a bare 5 in
the array size, another 5 in the loop bound, and a separate SortOrders
array. They now come from UIConst.Layers, whose row index must equal
the LayerType value -- guarded by UILayerTableTests.

Sort orders are deliberately unchanged; renumbering is phase 3."
```

---

## 任务 2：抽出 Canvas 工厂并测住层表契约

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`（工厂已在任务 1 建好，本任务只补测试）
- 测试：`Assets/Scripts/Tests/Editor/UIManagerLayerCanvasTests.cs`（创建）

> **说明**：`BuildLayerCanvas` 在任务 1 已随调用点抽出。本任务把它**测住** —— 一个没被断言过的抽取等于没抽。若你按顺序执行任务 1，本任务就是"给已存在的接缝补测试"；若单独执行本任务，请先确认该方法存在。

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/UIManagerLayerCanvasTests.cs`：

```csharp
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 1：Canvas 的创建从 UIManager.Awake 的私有循环里抽出来后，第一次可以被直接断言。
    /// 断言的是**层表到 Canvas 的契约**：名字、排序号、组件集 —— 这些都是阶段 3 重排层号时
    /// 会碰到的东西，先钉死当前值。
    /// </summary>
    public class UIManagerLayerCanvasTests
    {
        private GameObject _parent;

        [SetUp]
        public void SetUp()
        {
            _parent = new GameObject("LayerHost");
        }

        [TearDown]
        public void TearDown()
        {
            if (_parent != null) Object.DestroyImmediate(_parent);
        }

        [Test]
        public void test_every_layer_builds_a_named_canvas_with_the_layer_sort_order()
        {
            foreach (var (layer, sortOrder) in UIConst.Layers)
            {
                // Act
                var canvas = UIManager.BuildLayerCanvas(_parent.transform, layer, sortOrder);

                // Assert
                Assert.IsNotNull(canvas, layer + " 未生成 Canvas");
                Assert.AreEqual("Canvas_Layer_" + layer, canvas.gameObject.name,
                    "Canvas 命名必须保持 Canvas_Layer_<层名>（阶段 1 零行为变化）");
                Assert.AreEqual(sortOrder, canvas.sortingOrder, layer + " 的排序号与层表不符");
                Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode,
                    layer + " 必须是 ScreenSpaceOverlay");
                Assert.AreEqual(_parent.transform, canvas.transform.parent, "Canvas 未挂到父节点下");
            }
        }

        [Test]
        public void test_every_layer_canvas_carries_scaler_raycaster_and_screen_adapter()
        {
            foreach (var (layer, sortOrder) in UIConst.Layers)
            {
                // Act
                var canvas = UIManager.BuildLayerCanvas(_parent.transform, layer, sortOrder);

                // Assert
                Assert.IsNotNull(canvas.GetComponent<CanvasScaler>(),
                    layer + " 缺 CanvasScaler —— 分辨率适配会失效");
                Assert.IsNotNull(canvas.GetComponent<GraphicRaycaster>(),
                    layer + " 缺 GraphicRaycaster —— 该层将无法接收点击");
                Assert.IsNotNull(canvas.GetComponent<ScreenAdapter>(),
                    layer + " 缺 ScreenAdapter —— 安全区与缩放配置不会生效");
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "UIManagerLayerCanvasTests")`
预期：若 `BuildLayerCanvas` 尚不存在 → 编译错误；若已存在（任务 1 已执行）→ 直接 PASS 也可接受，此时**降级为表征测试**：它的职责是钉住当前契约，删除前后都必须通过。请如实记录你看到的是哪一种。

- [ ] **步骤 3：确认实现满足断言**

若步骤 2 出现失败（而非编译错误），检查 `BuildLayerCanvas` 是否漏加了 `CanvasScaler` / `GraphicRaycaster` / `ScreenAdapter` 三者之一，或排序号没有用传入的 `sortOrder`。

- [ ] **步骤 4：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "UIManagerLayerCanvasTests")`
预期：2 个测试 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/Editor/UIManagerLayerCanvasTests.cs
git commit -m "test(ui): pin the layer-canvas contract

The canvas factory was extracted so the table could be asserted without
booting UIManager.Awake. This pins its name, sort order and component
set before phase 3 renumbers the layers."
```

---

## 任务 3：删除动画子系统

**文件：**
- 删除：`Assets/Scripts/Hotfix/GameSystems/UI/Animation/`（4 个 .cs + 4 个 .meta + 文件夹 .meta）
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs:12-34`
- 测试：`Assets/Scripts/Tests/Editor/UIPanelLifecycleTests.cs`（创建）

> **为什么不是红→绿**：这是删除任务，没有"新行为"要测。这里写的是**表征测试** —— 先钉住"钩子存在且默认不播动画"，删除前后都必须通过。删除本身的判据是"零引用 + 编译通过 + 既有套件全绿"。

- [ ] **步骤 1：编写表征测试**

创建 `Assets/Scripts/Tests/Editor/UIPanelLifecycleTests.cs`：

```csharp
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 1：动画子系统（UIAnimation/UIAnimPreset/SequenceBuilder/UITweenExtensions）被删除后，
    /// UIPanel 的两个动画钩子必须**保留**并默认不播任何动画 —— 它们是 2026-05-08 设计文档 §6.2
    /// 记录的生命周期契约，阶段 5 的动画工作接在这里。
    /// 本测试是表征测试：删除前后都必须通过。
    /// </summary>
    public class UIPanelLifecycleTests
    {
        private class NoAnimPanel : UIPanel
        {
            public override LayerType Layer => LayerType.Base;
            public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
            public override string PanelId => "NoAnimPanel";
        }

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_panel_animation_hooks_default_to_no_animation()
        {
            // Arrange
            _go = new GameObject("NoAnimPanel");
            var panel = _go.AddComponent<NoAnimPanel>();

            // Assert：尚未接动画的面板必须返回 null，UIManager 据此跳过等待
            Assert.IsNull(panel.PlayShowAnimation(), "默认入场钩子必须返回 null");
            Assert.IsNull(panel.PlayHideAnimation(), "默认出场钩子必须返回 null");
        }
    }
}
```

- [ ] **步骤 2：运行测试 —— 预期通过（表征测试）**

运行：`tests-run(testMode: "EditMode", testClass: "UIPanelLifecycleTests")`
预期：**PASS**。此时 `UIPanel` 里仍是 `Animation != null ? Animation.PlayShow() : null`，而测试面板没挂 UIAnimation，所以返回 null。这一步的作用是把"默认不播动画"钉住，让接下来的删除有回归网。

- [ ] **步骤 3：确认零引用（删除的前置判据）**

```bash
# 1) 脚本 GUID 在任何 prefab / scene / asset 中零引用（预期：无输出）
grep -rl "4ee6cd6feb7d3f6409aaf8884c73d1ca" Assets/ --include=*.prefab --include=*.unity --include=*.asset

# 2) 类型名的引用只出现在 Animation 目录与 UIPanel 内（预期：只有 UIPanel.cs）
grep -rn "UIAnimation\|UIAnimPreset\|SequenceBuilder\|UITweenExtensions" Assets/Scripts/ --include=*.cs \
  | grep -v "Assets/Scripts/Hotfix/GameSystems/UI/Animation/"
```

若第 2 条列出了 `UIPanel.cs` 以外的文件，**停下来**：本计划的删除范围需要扩大，先回报再动手。

- [ ] **步骤 4：删除 4 个文件与文件夹 meta**

```bash
git rm Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimation.cs \
       Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimation.cs.meta \
       Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimPreset.cs \
       Assets/Scripts/Hotfix/GameSystems/UI/Animation/UIAnimPreset.cs.meta \
       Assets/Scripts/Hotfix/GameSystems/UI/Animation/SequenceBuilder.cs \
       Assets/Scripts/Hotfix/GameSystems/UI/Animation/SequenceBuilder.cs.meta \
       Assets/Scripts/Hotfix/GameSystems/UI/Animation/UITweenExtensions.cs \
       Assets/Scripts/Hotfix/GameSystems/UI/Animation/UITweenExtensions.cs.meta \
       Assets/Scripts/Hotfix/GameSystems/UI/Animation.meta
```

- [ ] **步骤 5：解除 UIPanel 对 UIAnimation 的依赖**

修改 `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs`：

把 14 行的属性、21 行的 `GetComponent`、25-28 行与 31-34 行两个钩子，一起改成：

```csharp
        public Canvas Canvas { get; private set; }
        public CanvasGroup CanvasGroup { get; private set; }
        public bool IsVisible { get; private set; }

        protected virtual void Awake()
        {
            Canvas = GetComponent<Canvas>();
            CanvasGroup = GetComponent<CanvasGroup>();
        }

        internal virtual void OnPreShow() { }

        /// <summary>入场动画钩子。默认不播；阶段 5 在此接入实现。</summary>
        internal virtual Sequence PlayShowAnimation() => null;

        internal virtual void OnShow() { }
        internal virtual void OnHide() { }

        /// <summary>出场动画钩子。默认不播；阶段 5 在此接入实现。</summary>
        internal virtual Sequence PlayHideAnimation() => null;
```

即：删掉 `public UIAnimation Animation { get; private set; }` 与 `Animation = GetComponent<UIAnimation>();`，两个钩子的函数体由 `Animation != null ? ... : null` 改为直接 `=> null`。**`using DG.Tweening;` 保留**（`Sequence` 返回类型仍需要它）。

- [ ] **步骤 6：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "UIPanelLifecycleTests")`
预期：**PASS**（表征测试，删除前后一致）。同时确认 Unity 控制台无编译错误。

- [ ] **步骤 7：Commit**

```bash
git add -A Assets/Scripts/Hotfix/GameSystems/UI/ Assets/Scripts/Tests/Editor/UIPanelLifecycleTests.cs
git commit -m "chore(ui): delete the unused UI animation subsystem

UIAnimation was never attached to any prefab (its script GUID has zero
references across all prefabs, scenes and assets), so UIPanel.Animation
was always null and PlayShowAnimation always returned null -- panel
transitions have never animated.

UIAnimPreset, SequenceBuilder and UITweenExtensions had no callers at
all. The two lifecycle hooks stay: they are the documented seam from
the 2026-05-08 design and phase 5 plugs animation back in there."
```

---

## 任务 4：删除 Stack 与 Overlay 死状态

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs:15-16,120-165,178,187`

> **本任务没有单元测试，判据是零引用 + 编译 + 既有套件。** 理由要写清楚，免得被读成偷懒：
> `PushAsync`/`PopAsync`/`PopTo` 全项目零外部调用者（已 grep 确认），`_activeOverlays` 只写不读 ——
> 删除它们**没有行为可断言**。给"删掉一个没人调用的方法"写反射式的存在性测试，测的是写死的期望而非行为。
> 可验证的判据是删除后"引用计数为零、编译通过、既有套件全绿"，下面逐步执行。

- [ ] **步骤 1：删除前的零引用核验**

```bash
# 预期：只有 UIManager.cs 自身的定义与内部使用；没有业务代码调用这三个方法
grep -rn "PushAsync\|PopAsync\|PopTo" Assets/Scripts/ --include=*.cs

# 预期：只有 UIManager.cs 的 Add / Remove 两行，没有任何读取
grep -rn "_activeOverlays" Assets/Scripts/ --include=*.cs
```

若出现 UIManager 之外的文件，**停下来**并回报 —— 说明本计划的删除面需要重新评估。

- [ ] **步骤 2：删除字段**

`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`，删掉 15-16 两行：

```csharp
        private readonly Stack<UIPanel> _stack = new Stack<UIPanel>();
        private readonly HashSet<UIPanel> _activeOverlays = new HashSet<UIPanel>();
```

- [ ] **步骤 3：删除 Stack 三个方法与它的注释段**

删掉 120 行起的整个 `// ===== Stack (Main / Popup) =====` 段（`PushAsync` / `PopAsync` / `PopTo`，约 120-165 行）。

- [ ] **步骤 4：删除 _activeOverlays 的两处写入**

在 `OpenAsync` 里删掉 `_activeOverlays.Add(panel);`，在 `CloseAsync` 里删掉 `_activeOverlays.Remove(panel);`。

改完后这两个方法应是：

```csharp
        public async UniTask OpenAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
            {
                Debug.LogError($"UIManager: Panel '{panelId}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
        }

        public async UniTask CloseAsync(string panelId)
        {
            if (!_registry.TryGetValue(panelId, out var panel))
                return;

            await HidePanelAsync(panel);
        }
```

- [ ] **步骤 5：清理因本次删除而变成多余的 using**

`using System.Collections.Generic;` **保留**（`Dictionary` 仍在用）。若编译器提示 `Stack` 相关的多余 using，一并处理；`using DG.Tweening;` 保留。

- [ ] **步骤 6：验证编译与全量测试**

运行：`tests-run(testMode: "EditMode")`
预期：全部 PASS，无编译错误。

运行：`tests-run(testMode: "PlayMode")`
预期：全部 PASS。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs
git commit -m "chore(ui): delete the unused panel stack and overlay bookkeeping

PushAsync/PopAsync/PopTo had no callers anywhere in the project, and
_activeOverlays was only ever written, never read. The stack semantics
are also wrong for an MMO: they force panels to close in reverse order,
which breaks the moment a player closes any panel with its X button.

Open/Close and ShowAlways/HideAlways keep working unchanged."
```

---

## 任务 5：端到端验证（阶段 1 验收）

- [ ] **步骤 1：确认前置条件**

保存所有打开的 Unity 场景（`tests-run` 在场景有未保存改动时会直接抛异常）。

- [ ] **步骤 2：运行全量 EditMode 测试**

运行：`tests-run(testMode: "EditMode")`
预期：全绿，且包含本阶段新增的 5 个测试（`UILayerTableTests` 2 个 + `UIManagerLayerCanvasTests` 2 个 + `UIPanelLifecycleTests` 1 个）。

- [ ] **步骤 3：运行全量 PlayMode 测试**

运行：`tests-run(testMode: "PlayMode")`
预期：全绿 —— 这一步保护的是 `PlayerHudBindingTests` 与 `CharacterSelectPanelReopenTests`，它们是"选人→世界"链路的自动化代表。

- [ ] **步骤 4：链路验证 · 进世界**

打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`，进入播放模式，截图确认：
- HUD（血条/蓝条/名字）显示正常
- 移动端摇杆与三个动作按钮按平台判定正确显隐
- 控制台无 UI 相关报错

> 该场景自带一份 `UIManager`，它是本场景里第一个实例，因此会正常建层并加载面板 —— 无需先跑选人场景。

- [ ] **步骤 5：链路验证 · 选人**

打开 `Assets/Scenes/CharacterSelect.unity`，进入播放模式，截图确认：选人面板正常渲染、按钮可点、点击"确认"后能进入 DemoDay。

- [ ] **步骤 6：记录结论**

若上述全部通过，阶段 1 完成，无新增改动则无需 commit。
若任一环节失败，**回滚该环节对应的任务**（阶段 1 的每个任务都是独立的 commit），修好后再继续 —— 不要在失败的提交上叠加后续任务。

---

## 自检记录（作者已执行）

**1. 规格覆盖度**：规格 §8 阶段 1 的三项 ——「删死代码（`UI/Animation/` 4 文件 / Stack 三方法 / `_activeOverlays`）」→ 任务 3 与任务 4；「层数硬编码收敛成表驱动，排序号数值先不动」→ 任务 1，并由任务 1 的第二个测试与任务 2 逐位钉住；「既有测试全绿 + 选人→世界链路跑通」→ 任务 5。无遗漏。

**2. 占位符扫描**：无"待定/TODO/后续实现"；每个测试与实现步骤都带可直接粘贴的代码块；运行命令与预期结果逐条写明。

**3. 类型一致性**：`UIConst.Layers`（`(LayerType layer, int sortOrder)[]`）在任务 1 定义，任务 2、任务 3 的测试与实现均按此签名使用；`BuildLayerCanvas(Transform, LayerType, int)` 在任务 1 定义、任务 2 断言，签名一致；`UIPanel.PlayShowAnimation()` / `PlayHideAnimation()` 返回 `DG.Tweening.Sequence`，在任务 3 的测试与实现中一致。

**4. 一处已知的规格偏差（有意为之）**：规格 §2.1 把层表放在新文件 `UI/Const/UILayers.cs` 并改名为 `UILayerId`；本阶段把它放在既有 `UIConst.cs` 里并保留 `LayerType` 名字 —— 因为改名与重排属于阶段 3，阶段 1 只收敛魔数、不动名字与数值。阶段 3 再搬文件与改名。
