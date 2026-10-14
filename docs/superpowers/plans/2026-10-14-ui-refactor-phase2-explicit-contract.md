# UI 框架重构 · 阶段 2（显式契约）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 把 UI 框架里三处**隐式契约**改成显式契约 —— 面板寻址不再靠魔法字符串、面板就绪不再靠 Awake 顺序与无超时轮询、面板声明不再与预制体静默不符。

**架构：** `UIManager` 的注册表从 `string → UIPanel` 改为 `Type → UIPanel`，并成为全项目唯一的面板清单；初始化从 `async void Awake` 改为「显式 `InitializeAsync` + 一个必然放行的就绪闸门」，消费方改 `await WhenReady()`；新增 `UIBootstrap` 在 `AfterSceneLoad` 统一创建 `UIManager` 与 `EventSystem`，场景里不再各带一份。

**技术栈：** Unity 2022.3.25f1 / C# 9 / UniTask / NUnit（EditMode: `GameSys.EditorTests`，PlayMode: `KcpNet.PlayModeTests`）

**规格：** `docs/superpowers/specs/2026-10-14-ui-framework-refactor-design.md` §3（契约与生命周期）、§6（面板寻址与元数据）、§8 阶段 2、§11.1（阶段 1 带过来的三条经验）

**验收（规格 §8）：** 负向验收 —— 故意改错一个预制体地址 → 必须有明确报错且**不挂死**。

**阶段 1 已完成并合并到 master**（`5c68a6c`）。本阶段从 master 的 `5c68a6c` 起。

---

## 本阶段不做（防止越界）

| 不做 | 归属 |
|---|---|
| `OpenAsync`/`CloseAsync` 的状态机、幂等、超时、失败回 `Unloaded` | 阶段 4 |
| 懒加载 / 卸载 / `preload` 分类 / Addressables | 阶段 4 |
| 模态闸门、`UIInputGate`、安全区、层序号重排、重建边界 | 阶段 3 |
| `StatBar` / `BuffBar` 消重、测试改用 `InternalsVisibleTo` 替代反射 | 阶段 5 |
| 把注册表改成 ScriptableObject 配置 | 不做（YAGNI：面板的层/模态是程序决策，不是策划数值） |
| 动排序号数值、改 `LayerType` 名字 | 阶段 3 |

**本阶段唯一允许的净效果：三处隐式契约变成显式契约，且运行时行为在「一切正常」的路径上逐位不变。**

---

## 文件结构

### 创建

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/UI/Core/ReadinessGate.cs` | 一次性放行的就绪闸门。等待与放行解耦，Release 幂等 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBootstrap.cs` | `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 统一创建 UIManager / EventSystem |
| `Assets/Scripts/Tests/Editor/UIPanelContractTests.cs` | 面板声明与预制体一致性的纯函数测试 + `EnsureCanvasGroup` 幂等 |
| `Assets/Scripts/Tests/Editor/UIPanelRegistryTests.cs` | 注册表覆盖全部 `UIPanel` 子类、无重复、路径可解析 |
| `Assets/Scripts/Tests/Editor/ReadinessGateTests.cs` | 闸门语义：未放行 Pending、放行后 Succeeded、重复放行幂等 |
| `Assets/Scripts/Tests/PlayMode/UIBootstrapTests.cs` | `EnsureUIManager` / `EnsureEventSystem` 幂等 |

### 修改

| 文件 | 改动 | 任务 |
|---|---|---|
| `UI/Core/UIPanel.cs` | 加 `DescribeModeMismatch` / `EnsureCanvasGroup`；任务 2 再删 `PanelId` | 1, 2 |
| `UI/Core/UIManager.cs` | 任务 1 加校验；任务 2 改注册表为类型键；任务 3 加就绪信号 | 1, 2, 3 |
| `UI/Panel/HUD/PlayerHudPanel.cs` | 删 `PanelId` 覆写 | 2 |
| `UI/Panel/HUD/TargetPanel.cs` | 删 `PanelId` 覆写；`HideAlwaysAsync(PanelId)` → 泛型 | 2 |
| `UI/Panel/CharacterSelect/CharacterSelectPanel.cs` | 删 `PanelId` 覆写；`CloseAsync(PanelId)` → 泛型 | 2 |
| `UI/SelectStageController.cs` | 类型寻址；任务 3 换成就绪信号 | 2, 3 |
| `Sys3C/Sys3CEntry.cs` | 4 处调用点改泛型；任务 3 HUD 绑定改等就绪 | 2, 3 |
| `Sys3C/CharacterAttackHandler.cs` | 4 处调用点改泛型 | 2 |
| `SyncLayer/ClientNetworkHost.cs` | 1 处调用点改泛型 | 2 |
| `Tests/Editor/UIPanelLifecycleTests.cs` | 删 `PanelId` 覆写 | 2 |
| `Tests/Editor/UIPanelContractTests.cs` | 删 `PanelId` 覆写（任务 1 刚建的） | 2 |
| `Packages/manifest.json` | 删 `com.unity.inputsystem` | 5 |
| `Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef` | 删 `"Unity.InputSystem"` | 5 |
| `ProjectSettings/ProjectSettings.asset` | `activeInputHandler: 2` → `0`（**人工步骤，需重启 Unity**） | 5 |

### 场景改动（任务 4，用 MCP 而非文本编辑）

从两个场景各删掉 `UIManager` 与 `EventSystem` 两个 GameObject：`Assets/Scenes/CharacterSelect.unity`、`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`。

> **为什么走 MCP 而不是改 YAML**：Unity 正在运行，文本编辑器改场景文件会弹 "modified externally" 模态框阻塞 MCP 主线程（本项目已踩过），且 Unity 会按自己的理解回写。`gameobject-destroy` + `scene-save` 走的是 Unity 自己的 API。

---

## 已核实的前提（实现者不必重查）

- `UIPanel.PanelId` 的全部使用点：`UIManager.cs:65,67,104,106,110,112,115`、`UIPanel.cs:10`、`CharacterSelectPanel.cs:20,136`、`PlayerHudPanel.cs:13`、`TargetPanel.cs:13,122`、`Tests/Editor/UIPanelLifecycleTests.cs:19`
- `UIPanel` 的**具体子类只有三个**：`PlayerHudPanel` / `TargetPanel` / `CharacterSelectPanel`（全库 grep `: UIPanel` 确认；测试程序集里那个探针类不算，它们不在 `GameSys` 程序集里）
- `UIManager` 公开 API 的外部调用点：`ClientNetworkHost.cs:447`、`CharacterAttackHandler.cs:57,61,83,87`、`Sys3CEntry.cs:182,186,728,732`、`SelectStageController.cs:25,28`、`CharacterSelectPanel.cs:136`、`TargetPanel.cs:122`
  ⚠️ **`KcpClient.CloseAsync` / `IWebSocketChannel.CloseAsync` / `KcpSession.CloseAsync` 是无关类型的同名方法**，grep 命中它们时不要动。同理 `ItemTemplateRegistry.Register` / `PhysicsRegistry.Register` / `_nameplate.Register` 与 `UIManager.Register` 无关。
- `UIPanel` 的 `Awake` 在 **EditMode 下不会执行** ⇒ 任何依赖 `Awake` 缓存（`Canvas` / `CanvasGroup`）的代码在 EditMode 测试里拿到的都是 null。这是 `EnsureCanvasGroup` 每次重新 `GetComponent` 的原因。
- `Sys3CEntry.cs` 已有 `using Cysharp.Threading.Tasks;`（第 1 行）；`CharacterAttackHandler.cs` 有（第 2 行）；`ClientNetworkHost.cs` 有（第 5 行）且已有 `using Hotfix.GameSystems.UI;`（第 6 行）。`CharacterSelectPanel.cs` **没有** UniTask 的 using —— 但它只 `await`、不书写 UniTask 类型，所以不需要加。
- `MobileQualityBootstrap.cs:66` 是本仓库既有的 bootstrap 先例（`RuntimeInitializeOnLoadMethod` + 可单测的纯函数内核）。新 bootstrap **照它的形制写**。
- `com.unity.inputsystem` 在 `Packages/packages-lock.json` 里存在，但**没有任何包反向依赖它**（脚本枚举确认）；代码里 `UnityEngine.InputSystem` / `InputAction` / `EnhancedTouch` 的使用数为 **0**。
- `Packages/manifest.json` **有一处未提交改动**（`com.ivanmurzak.unity.mcp` 0.86.3 → 0.90.0），与本阶段无关。**改文件时不要碰那一行。**
- `GameSys.asmdef` 第 10 行是 `"Unity.InputSystem",`。
- 两个场景的 EventSystem 都用旧的 `StandaloneInputModule`（GUID `4f231c4f…`）。
- ⚠️ **`GameSys.EditorTests.asmdef` 不引用 `UniTask`**（实测其 references 为 `GameSys, DOTween.Modules, DataDefinition, Cool.Protocol, KcpNet, SyncCore, WebSocketNet`），而 `GameSys.asmdef` 引用了 —— **asmdef 引用不传递**。任务 3 的测试要 `using Cysharp.Threading.Tasks;` 并用 `UniTaskStatus` 断言，所以**必须**给测试程序集显式加一行 `"UniTask"`，否则报 `CS0246: 'Cysharp' could not be found`。
  **先例**：阶段 1 的提交 `0d769fb` 为同一原因往同一个 asmdef 加过 `"DOTween.Modules"`。
  （此条由任务 3 的实现者实测发现。计划初版只对 `UnityEngine.UI` 与 `DOTween` 做了引用模型取证，漏了 UniTask —— 正是 §11.1 第 3 条教训自身的又一次重演。）

### 运行测试的方式

用 Unity MCP 的 `tests-run`，`testClass` 传**裸类名**。**前置条件：所有打开的场景必须已保存。**

> **CLI 兜底（工具从宿主工具集消失时用）**：`unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --timeout 420000 --input '{"testMode":"EditMode","includePassingTests":false}'`。**默认超时 60 秒不够跑 PlayMode**，必须加 `--timeout`。另：CLI 调用在客户端报错时，Unity 侧的运行**仍在继续**，紧接着重试会报 "another test run is already in progress"，等一会儿再试。

### 阶段 1 带过来的三条经验（规格 §11.1）

1. **核验步骤必须排在它要核验的状态之前**（阶段 1 的闸门被自己新建文件的注释命中，字面上永远无法通过）。
2. **每个提交步骤的 `git add` 必须覆盖该步骤的全部产出**，含 Unity 自动生成的 `.cs.meta`（阶段 1 有两处配方漏了它，其中一处还会产生**不可编译的提交点**）。
3. **涉及程序集引用模型时先取证再落笔**（阶段 1 对 DOTween 的根因连续误判两次）。

---

## 任务 1：面板声明与预制体的一致性校验

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`（`Register`）
- 测试：`Assets/Scripts/Tests/Editor/UIPanelContractTests.cs`（创建）

**背景（修复规格 R2）**：三个面板预制体**没有一个带 Canvas 组件**，而框架提供 `CanvasSwitch` 模式 —— 声明它就会得到一个「永远关不掉、且零日志」的面板。今天三个面板都恰好选了 `CanvasGroup` 所以侥幸没中。

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/UIPanelContractTests.cs`：

```csharp
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 2：面板声明的显隐模式必须与预制体实际组件相符。
    ///
    /// 修的是"静默失效"：`ApplyVisibilityOff/On` 每个分支都判空，于是声明与预制体不符时
    /// 表现为**什么都不做**——选 CanvasSwitch 而没有 Canvas，面板永远关不掉且零日志。
    /// 实测三个面板预制体没有一个带 Canvas 组件，第 4 个面板中招只是时间问题。
    /// </summary>
    public class UIPanelContractTests
    {
        /// <summary>探针面板：形状取最普通的一种，只为拿到一个具体子类。</summary>
        private class ProbePanel : UIPanel
        {
            public override LayerType Layer => LayerType.Base;
            public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
            public override string PanelId => "ProbePanel";
        }

        [Test]
        public void test_canvas_switch_without_a_canvas_component_is_reported()
        {
            // Act
            var reason = UIPanel.DescribeModeMismatch(
                VisibilityMode.CanvasSwitch, hasCanvas: false, hasCanvasGroup: true);

            // Assert
            Assert.IsNotNull(reason, "选 CanvasSwitch 而预制体没有 Canvas，就是那个'关不掉'的场景，必须报出来");
            StringAssert.Contains("Canvas", reason);
        }

        [Test]
        public void test_matching_declarations_report_no_mismatch()
        {
            Assert.IsNull(UIPanel.DescribeModeMismatch(VisibilityMode.CanvasSwitch, hasCanvas: true,  hasCanvasGroup: false),
                "有 Canvas 的 CanvasSwitch 是合法的");
            Assert.IsNull(UIPanel.DescribeModeMismatch(VisibilityMode.CanvasGroup,  hasCanvas: false, hasCanvasGroup: true),
                "有 CanvasGroup 的 CanvasGroup 是合法的");
            Assert.IsNull(UIPanel.DescribeModeMismatch(VisibilityMode.CanvasGroup,  hasCanvas: false, hasCanvasGroup: false),
                "缺 CanvasGroup 不算不匹配——Register 会补齐（见 EnsureCanvasGroup）");
            Assert.IsNull(UIPanel.DescribeModeMismatch(VisibilityMode.ToggleActive, hasCanvas: false, hasCanvasGroup: false),
                "ToggleActive 不需要额外组件");
        }

        [Test]
        public void test_ensure_canvas_group_adds_exactly_one_and_is_idempotent()
        {
            // Arrange
            var go = new GameObject("EnsureCanvasGroupProbe");
            try
            {
                var panel = go.AddComponent<ProbePanel>();
                Assert.IsNull(go.GetComponent<CanvasGroup>(), "前置：起手不该有 CanvasGroup");

                // Act
                var first = panel.EnsureCanvasGroup();
                var second = panel.EnsureCanvasGroup();

                // Assert
                Assert.IsNotNull(first);
                Assert.AreSame(first, second, "重复调用必须复用同一个，不能越加越多");
                Assert.AreEqual(1, go.GetComponents<CanvasGroup>().Length);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "UIPanelContractTests")`
预期：**编译失败** —— `UIPanel` 不含 `DescribeModeMismatch` / `EnsureCanvasGroup`。这是本步骤期望的红。

- [ ] **步骤 3：给 UIPanel 加两个成员**

修改 `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs`，在 `ApplyVisibilityOn` 之前插入：

```csharp
        /// <summary>
        /// 声明的显隐模式与预制体实际组件是否匹配。返回 null = 匹配，否则返回人话的原因。
        ///
        /// 为什么要有这个：`ApplyVisibilityOff/On` 每个分支都判空，于是**声明与预制体不符时
        /// 是静默失效** —— 选 CanvasSwitch 而预制体没有 Canvas，面板就永远关不掉，且零日志。
        /// </summary>
        internal static string DescribeModeMismatch(VisibilityMode mode, bool hasCanvas, bool hasCanvasGroup)
        {
            switch (mode)
            {
                case VisibilityMode.CanvasSwitch:
                    return hasCanvas
                        ? null
                        : "声明了 Mode=CanvasSwitch，但预制体上没有 Canvas 组件 —— 该面板将永远关不掉";
                case VisibilityMode.CanvasGroup:
                    // 缺 CanvasGroup 不算「不匹配」：Register 会补齐（见 EnsureCanvasGroup）
                    return null;
                case VisibilityMode.ToggleActive:
                    return null;
                default:
                    return "未知的 VisibilityMode: " + mode;
            }
        }

        /// <summary>
        /// 保证面板根上有一个 CanvasGroup。**由 UIManager.Register 调用** ——
        /// 阶段 3 的事件层要用 `CanvasGroup.interactable` 挡住模态面板下方的交互，
        /// 那不能依赖「恰好这个面板选了 CanvasGroup 模式」。
        ///
        /// 每次重新 GetComponent 而不用 Awake 的缓存：EditMode 下 Awake 不执行，
        /// 而本方法要能被 EditMode 测试直接调。
        /// </summary>
        internal CanvasGroup EnsureCanvasGroup()
        {
            var cg = GetComponent<CanvasGroup>();
            if (cg == null)
                cg = gameObject.AddComponent<CanvasGroup>();

            CanvasGroup = cg;
            return cg;
        }
```

- [ ] **步骤 4：让 Register 调用校验**

修改 `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs` 的 `Register`，在 `_registry[panel.PanelId] = panel;` **之前**插入校验：

```csharp
            var mismatch = UIPanel.DescribeModeMismatch(
                panel.Mode,
                hasCanvas: panel.GetComponent<Canvas>() != null,
                hasCanvasGroup: panel.GetComponent<CanvasGroup>() != null);

            if (mismatch != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new System.InvalidOperationException(
                    $"UIManager: 面板 '{panel.GetType().Name}' {mismatch}");
#else
                Debug.LogError($"UIManager: 面板 '{panel.GetType().Name}' {mismatch} —— 已跳过注册");
                return;
#endif
            }
```

并在重挂父节点之前补上 CanvasGroup 保证，即在 `// Start hidden` 那行**之前**插入：

```csharp
            // 阶段 3 的事件层要用 CanvasGroup.interactable，这里先保证它存在
            panel.EnsureCanvasGroup();
```

- [ ] **步骤 5：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "UIPanelContractTests")`
预期：3 个测试 PASS。

- [ ] **步骤 6：跑全量确认没影响既有用例**

运行：`tests-run(testMode: "EditMode")`
预期：368/368 全绿（阶段 1 结束时是 365，本任务新增 3）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs \
        Assets/Scripts/Tests/Editor/UIPanelContractTests.cs \
        Assets/Scripts/Tests/Editor/UIPanelContractTests.cs.meta
git commit -m "fix(ui): fail loudly when a panel's visibility mode has no matching component

Every branch of ApplyVisibilityOff/On null-checks its component, so a
declaration that does not match the prefab fails silently: declare
CanvasSwitch on a prefab with no Canvas and the panel can never be
hidden, with no log at all. None of the three panel prefabs carries a
Canvas component today.

Register now throws in the editor (logs and skips in a release build)
and guarantees a CanvasGroup on every panel root, which is what the
phase 3 modal gate will use to block input below."
```

---

## 任务 2：类型寻址与注册表（删 `PanelId` 字符串）

**文件：** 见上方「修改」表中标注任务 2 的 11 个文件；测试 `Assets/Scripts/Tests/Editor/UIPanelRegistryTests.cs`（创建）

> **这是本阶段最大的一个任务，且**不可再拆** —— 删掉 `PanelId` 会让所有调用点在同一个提交点里编译不过。审查者应当把它当作一个整体把关。

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/UIPanelRegistryTests.cs`：

```csharp
using System.Linq;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 2：`UIManager.PanelRegistry` 是全项目唯一的面板清单。
    ///
    /// 这一条替代了规格 §6 说的"编辑器校验器"：漏注册的面板**在 EditMode 里当场报红**，
    /// 而不是等到运行时某个 `GetPanel&lt;T&gt;()` 静默返回 null。
    /// </summary>
    public class UIPanelRegistryTests
    {
        [Test]
        public void test_every_concrete_panel_type_is_in_the_registry()
        {
            // Arrange
            var registered = UIManager.PanelRegistry.Select(e => e.panelType).ToArray();
            var panelTypes = typeof(UIPanel).Assembly.GetTypes()
                .Where(t => t.IsSubclassOf(typeof(UIPanel)) && !t.IsAbstract)
                .ToArray();

            // Assert
            Assert.IsNotEmpty(panelTypes, "反射一个 UIPanel 子类都没找到，测试本身有问题");
            foreach (var type in panelTypes)
            {
                CollectionAssert.Contains(registered, type,
                    type.Name + " 是 UIPanel 子类但不在 UIManager.PanelRegistry 里 —— 漏注册的面板永远打不开");
            }
        }

        [Test]
        public void test_registry_entries_are_well_formed()
        {
            var types = UIManager.PanelRegistry.Select(e => e.panelType).ToArray();

            Assert.AreEqual(types.Length, types.Distinct().Count(), "注册表里有重复的类型");

            foreach (var (panelType, prefabPath) in UIManager.PanelRegistry)
            {
                Assert.IsTrue(panelType.IsSubclassOf(typeof(UIPanel)),
                    panelType.Name + " 不是 UIPanel 子类");
                Assert.IsFalse(string.IsNullOrWhiteSpace(prefabPath),
                    panelType.Name + " 的预制体路径为空");
            }
        }

        [Test]
        public void test_every_registry_prefab_path_resolves()
        {
            // Resources.Load 在 EditMode 下可用 —— 于是路径打错在测试期就红，
            // 而不是等到运行时某次 Awake 里打一条 LogError
            foreach (var (panelType, prefabPath) in UIManager.PanelRegistry)
            {
                var prefab = Resources.Load<GameObject>(prefabPath);
                Assert.IsNotNull(prefab,
                    "Resources/" + prefabPath + "（注册表里 " + panelType.Name + " 的预制体）加载不到");
                Assert.IsNotNull(prefab.GetComponent<UIPanel>(),
                    prefabPath + " 上没有 UIPanel 组件");
                Assert.IsTrue(panelType.IsInstanceOfType(prefab.GetComponent<UIPanel>()),
                    prefabPath + " 上的组件不是注册表声明的 " + panelType.Name);
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "UIPanelRegistryTests")`
预期：**编译失败** —— `UIManager` 不含 `PanelRegistry`。这是期望的红。

- [ ] **步骤 3：改 UIManager 的注册表与 API**

对 `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs` 做以下替换。

**(a)** 字段：把

```csharp
        private readonly Dictionary<string, UIPanel> _registry = new Dictionary<string, UIPanel>();
```

换成

```csharp
        /// <summary>
        /// 全项目唯一的面板清单。**加一个面板 = 这里加一行 + 加一个预制体。**
        /// 漏注册由 UIPanelRegistryTests 在 EditMode 里当场报红（它反射全部 UIPanel 子类）。
        ///
        /// 路由用**类型**而不是字符串：改名、打错、复制粘贴都会变成编译错误，
        /// 不再有 `GetPanel&lt;T&gt;() == null` 这种静默失败。
        /// </summary>
        internal static readonly (System.Type panelType, string prefabPath)[] PanelRegistry =
        {
            (typeof(PlayerHudPanel),       "Prefabs/UI/PlayerHudPanel"),
            (typeof(TargetPanel),          "Prefabs/UI/TargetPanel"),
            (typeof(CharacterSelectPanel), "Prefabs/UI/CharacterSelectPanel"),
        };

        private readonly Dictionary<System.Type, UIPanel> _panels = new Dictionary<System.Type, UIPanel>();
```

**(b)** `LoadBuiltInPanels` 整个方法换成：

```csharp
        private async UniTask LoadBuiltInPanels()
        {
            foreach (var (panelType, prefabPath) in PanelRegistry)
            {
                await RegisterFromPrefab(panelType, prefabPath);
            }

            if (_panels.TryGetValue(typeof(PlayerHudPanel), out var hud))
            {
                await ShowPanelAsync(hud);
            }

            // 启动流程：先选人，再进世界（选人面板全屏覆盖 HUD）
            await OpenAsync<CharacterSelectPanel>();
        }
```

**(c)** `RegisterFromPrefab` 整个方法换成：

```csharp
        private async UniTask RegisterFromPrefab(System.Type panelType, string resourcePath)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogError($"UIManager: Prefab not found at Resources/{resourcePath}");
                return;
            }

            var instance = Instantiate(prefab);
            var panel = instance.GetComponent<UIPanel>();
            if (panel == null)
            {
                Debug.LogError($"UIManager: Prefab at {resourcePath} has no UIPanel component");
                Destroy(instance);
                return;
            }

            // 注册表说这个路径是 A，预制体上却是 B —— 这是接线错误，不是警告
            if (!panelType.IsInstanceOfType(panel))
            {
                Debug.LogError($"UIManager: 注册表把 {resourcePath} 标成 {panelType.Name}，" +
                               $"但预制体上的组件是 {panel.GetType().Name}");
                Destroy(instance);
                return;
            }

            Register(panelType, panel);
        }
```

**(d)** `Register` 的签名与键：`public void Register(UIPanel panel)` → `private void Register(System.Type panelType, UIPanel panel)`，并把方法体开头换成：

```csharp
            if (panel == null || panelType == null)
            {
                Debug.LogError("UIManager: Cannot register null panel");
                return;
            }

            var mismatch = UIPanel.DescribeModeMismatch(
                panel.Mode,
                hasCanvas: panel.GetComponent<Canvas>() != null,
                hasCanvasGroup: panel.GetComponent<CanvasGroup>() != null);

            if (mismatch != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new System.InvalidOperationException(
                    $"UIManager: 面板 '{panelType.Name}' {mismatch}");
#else
                Debug.LogError($"UIManager: 面板 '{panelType.Name}' {mismatch} —— 已跳过注册");
                return;
#endif
            }

            if (_panels.ContainsKey(panelType))
            {
                Debug.LogWarning($"UIManager: Panel '{panelType.Name}' already registered, replacing.");
            }

            _panels[panelType] = panel;
```

（删除原本的 `panel.PanelId` 判空、`_registry.ContainsKey`、`_registry[...] = panel` 三处。）

**(e)** 四个公开方法与查找换成泛型：

```csharp
        // ===== Open / Close =====

        public async UniTask OpenAsync<T>() where T : UIPanel
        {
            if (!_panels.TryGetValue(typeof(T), out var panel))
            {
                Debug.LogError($"UIManager: Panel '{typeof(T).Name}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
        }

        public async UniTask CloseAsync<T>() where T : UIPanel
        {
            if (!_panels.TryGetValue(typeof(T), out var panel))
                return;

            await HidePanelAsync(panel);
        }

        // ===== Always (Base) =====

        public async UniTask ShowAlwaysAsync<T>() where T : UIPanel
        {
            if (!_panels.TryGetValue(typeof(T), out var panel))
            {
                Debug.LogError($"UIManager: Panel '{typeof(T).Name}' not registered");
                return;
            }

            await ShowPanelAsync(panel);
        }

        public async UniTask HideAlwaysAsync<T>() where T : UIPanel
        {
            if (!_panels.TryGetValue(typeof(T), out var panel))
                return;

            await HidePanelAsync(panel);
        }

        // ===== Lookup =====

        public T GetPanel<T>() where T : UIPanel
        {
            _panels.TryGetValue(typeof(T), out var panel);
            return panel as T;
        }
```

- [ ] **步骤 4：删除 `UIPanel.PanelId`**

`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs` 第 10 行整行删除：

```csharp
        public abstract string PanelId { get; }
```

- [ ] **步骤 5：更新全部调用点**

| 文件 | 改法 |
|---|---|
| `UI/Panel/HUD/PlayerHudPanel.cs:13` | 删除 `public override string PanelId => "PlayerHudPanel";` |
| `UI/Panel/HUD/TargetPanel.cs:13` | 删除该覆写行 |
| `UI/Panel/HUD/TargetPanel.cs:122` | `UIManager.Instance?.HideAlwaysAsync(PanelId)` → `UIManager.Instance?.HideAlwaysAsync<TargetPanel>()` |
| `UI/Panel/CharacterSelect/CharacterSelectPanel.cs:20` | 删除该覆写行 |
| `UI/Panel/CharacterSelect/CharacterSelectPanel.cs:136` | `await UIManager.Instance.CloseAsync(PanelId)` → `await UIManager.Instance.CloseAsync<CharacterSelectPanel>()` |
| `UI/SelectStageController.cs:25,28` | `GetPanel<CharacterSelectPanel>("CharacterSelectPanel")` → `GetPanel<CharacterSelectPanel>()`（第 25 行那个 `while` 条件保留，任务 3 会整段换掉） |
| `Sys3C/CharacterAttackHandler.cs:57,83` | `?.GetPanel<TargetPanel>("TargetPanel")` → `?.GetPanel<TargetPanel>()` |
| `Sys3C/CharacterAttackHandler.cs:61,87` | `ShowAlwaysAsync("TargetPanel")` → `ShowAlwaysAsync<TargetPanel>()`；`HideAlwaysAsync("TargetPanel")` → `HideAlwaysAsync<TargetPanel>()` |
| `Sys3C/Sys3CEntry.cs:182,728` | `GetPanel<Hotfix.GameSystems.UI.PlayerHudPanel>("PlayerHudPanel")` → `GetPanel<Hotfix.GameSystems.UI.PlayerHudPanel>()` |
| `Sys3C/Sys3CEntry.cs:186,732` | `ShowAlwaysAsync("PlayerHudPanel")` → `ShowAlwaysAsync<Hotfix.GameSystems.UI.PlayerHudPanel>()`；`HideAlwaysAsync("PlayerHudPanel")` → 同理 |
| `SyncLayer/ClientNetworkHost.cs:447` | `ui.OpenAsync("CharacterSelectPanel")` → `ui.OpenAsync<CharacterSelectPanel>()` |
| `Tests/Editor/UIPanelLifecycleTests.cs:19` | 删除 `public override string PanelId => "NoAnimPanel";` |
| `Tests/Editor/UIPanelContractTests.cs:19` | 删除 `public override string PanelId => "ProbePanel";` |

> **注**：`CharacterAttackHandler` 用的是 `ShowAlwaysAsync`/`HideAlwaysAsync` 而不是规格 §3.3 shorthand 里写的 `OpenAsync` —— 因为 `TargetPanel` 声明的是 `LayerType.Base`，`ShowAlwaysAsync` 才是语义正确的那一个。本阶段这两个方法实现相同，区别要到阶段 3/4 才显现。

- [ ] **步骤 6：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "UIPanelRegistryTests")`
预期：3 个测试 PASS。

运行：`tests-run(testMode: "EditMode")`
预期：371/371 全绿（368 + 本任务 3）。

- [ ] **步骤 7：全库核验字符串已绝迹（这是本阶段的核心不变量）**

```bash
# 预期：只应命中本计划与规格文档；Assets/ 下零命中
grep -rn "PanelId" Assets/ --include=*.cs
```

若 `Assets/` 下还有命中，**停下来**回报 —— 说明有调用点没改到。

- [ ] **步骤 8：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/ \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/ClientNetworkHost.cs \
        Assets/Scripts/Tests/Editor/UIPanelRegistryTests.cs \
        Assets/Scripts/Tests/Editor/UIPanelRegistryTests.cs.meta \
        Assets/Scripts/Tests/Editor/UIPanelLifecycleTests.cs \
        Assets/Scripts/Tests/Editor/UIPanelContractTests.cs
git commit -m "refactor(ui): address panels by type instead of a magic string

PanelId was a string that had to match a literal at every call site;
GetPanel<T> swallowed a mismatch by returning null via 'as T'. The
registry is now the single list of panels and every lookup is generic,
so a rename is a compile error rather than a silent no-op.

PanelRegistryTests reflects over every UIPanel subclass and fails if one
is missing from the registry, and loads each prefab so a typo'd path
fails at test time instead of in a runtime Awake."
```

---

## 任务 3：就绪信号 `WhenReady()`

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/ReadinessGate.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/SelectStageController.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- 测试：`Assets/Scripts/Tests/Editor/ReadinessGateTests.cs`（创建）

**背景（修复规格 R1/§3.1/§3.3）**：`UIManager.Awake` 是 `async void`（异常无人接管）；`SelectStageController` 用两个**无超时**的 `while (…) await UniTask.Delay(50)` 轮询等面板 —— 面板加载失败就永久空转。今天 `Resources.Load` 是同步的所以轮询从未真正循环过，但阶段 4 换成 Addressables 后它会真的开始循环。

- [ ] **步骤 1：给测试程序集加 UniTask 引用**

`Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef` 的 `references` 数组里加一行 `"UniTask"`（放在 `"GameSys"` 之后即可）：

```json
    "references": [
        "GameSys",
        "UniTask",
        "DOTween.Modules",
        "DataDefinition",
        "Cool.Protocol",
        "KcpNet",
        "SyncCore",
        "WebSocketNet"
    ],
```

**理由（实测定案）**：本任务的测试要 `using Cysharp.Threading.Tasks;` 并用 `UniTaskStatus` 断言（EditMode 测试跑不了协程，所以靠 `Status` 断言而不 await）。`GameSys.asmdef` 引用了 `UniTask`，但 **asmdef 引用不传递**，而拼写 `UniTaskStatus` 必须能解析到类型 ⇒ 测试程序集不显式引用就报 `CS0246: 'Cysharp' could not be found`。**先例**：阶段 1 的 `0d769fb` 为同一原因往同一个 asmdef 加过 `"DOTween.Modules"`。

**这是本任务要求的唯一一处 asmdef 改动** —— 其他 asmdef 一律不动。

- [ ] **步骤 2：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/ReadinessGateTests.cs`：

```csharp
using Cysharp.Threading.Tasks;
using Hotfix.GameSystems.UI;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 2：就绪闸门的语义。它是 `while (…) await UniTask.Delay(50)` 轮询的替代品 ——
    /// 靠"等待与放行解耦"消除顺序依赖，而不是靠反复试探。
    /// 用 `UniTask.Status` 断言，所以不需要 await（EditMode 测试跑不了协程）。
    /// </summary>
    public class ReadinessGateTests
    {
        [Test]
        public void test_gate_pends_until_released()
        {
            // Arrange
            var gate = new ReadinessGate();

            // Assert
            Assert.AreEqual(UniTaskStatus.Pending, gate.WaitAsync().Status,
                "未放行前必须是 Pending —— 这正是'先到者等后来者'的那一半");
            Assert.IsFalse(gate.IsReleased);
        }

        [Test]
        public void test_gate_completes_for_waiters_registered_before_and_after_release()
        {
            // Arrange
            var gate = new ReadinessGate();
            var before = gate.WaitAsync();   // 放行之前就登记

            // Act
            gate.Release();

            // Assert
            Assert.AreEqual(UniTaskStatus.Succeeded, before.Status, "放行前登记的等待者必须被放行");
            Assert.AreEqual(UniTaskStatus.Succeeded, gate.WaitAsync().Status,
                "放行之后才来的等待者必须立刻通过 —— 这是解耦的另一半");
            Assert.IsTrue(gate.IsReleased);
        }

        [Test]
        public void test_release_is_idempotent()
        {
            var gate = new ReadinessGate();

            gate.Release();
            gate.Release();
            gate.Release();

            Assert.AreEqual(UniTaskStatus.Succeeded, gate.WaitAsync().Status);
            Assert.IsTrue(gate.IsReleased);
        }
    }
}
```

- [ ] **步骤 3：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "ReadinessGateTests")`
预期：**编译失败** —— `ReadinessGate` 不存在。这是期望的红。

- [ ] **步骤 4：实现 ReadinessGate**

创建 `Assets/Scripts/Hotfix/GameSystems/UI/Core/ReadinessGate.cs`：

```csharp
using Cysharp.Threading.Tasks;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 一次性放行的就绪闸门。
    ///
    /// **关键性质：等待与放行解耦** —— 放行之前 await 会挂起，放行之后 await 立刻通过。
    /// 于是"谁先谁后"不再影响正确性，这正是不再需要
    /// `while (…) await UniTask.Delay(50)` 轮询的原因。
    ///
    /// Release 幂等：重复放行不会出错，也不会有第二次效果。
    /// </summary>
    internal sealed class ReadinessGate
    {
        private readonly UniTaskCompletionSource _tcs = new UniTaskCompletionSource();

        public bool IsReleased { get; private set; }

        public void Release()
        {
            if (IsReleased) return;

            IsReleased = true;
            _tcs.TrySetResult();
        }

        public UniTask WaitAsync() => _tcs.Task;
    }
}
```

- [ ] **步骤 5：把 UIManager 的 Awake 改成显式初始化**

对 `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`：

**(a)** 在 `_panels` 字段之后加：

```csharp
        private readonly ReadinessGate _ready = new ReadinessGate();

        /// <summary>全部面板加载成功。失败时为 false —— 具体缺哪块用 GetPanel&lt;T&gt;() == null 判断。</summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// 初始化完成的信号。**必然完成** —— InitializeAsync 的 finally 一定会放行，
        /// 所以调用方永远不会永久挂起。这正是它取代轮询的地方：
        /// `while (…) await UniTask.Delay(50)` 在加载失败时是无限空转，没有任何出口。
        /// </summary>
        public UniTask WhenReady() => _ready.WaitAsync();
```

**(b)** `Awake` 换成（去掉 `async void`）：

```csharp
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
            InitializeAsync().Forget();
        }

        /// <summary>
        /// 显式初始化。不再是 `async void Awake` —— 那样异常无人接管，
        /// 而且"Awake 什么时候算完"对外没有出口。
        /// `finally` 里无条件放行，是"加载失败也不会挂死"的结构性保证。
        /// </summary>
        private async UniTask InitializeAsync()
        {
            try
            {
                await LoadBuiltInPanels();
                // 不能写 IsReady = true —— 主失败路径**不抛异常**：预制体缺失 / 无 UIPanel 组件 /
                // 类型不匹配，RegisterFromPrefab 一律 LogError + return。写死 true 会让
                // "HUD 预制体缺失、HUD 从未注册"这种情况下 IsReady 仍为 true，与它自己的文档契约
                // （"全部面板加载成功"）相反。按实际注册数量判，代码才配得上那句注释。
                IsReady = _panels.Count == PanelRegistry.Length;
            }
            catch (Exception e)
            {
                Debug.LogError("UIManager: 初始化中断 —— " + e.Message);
            }
            finally
            {
                _ready.Release();
            }
        }
```

（`using System;` 已在第 1 行，`Exception` 可直接用。）

- [ ] **步骤 6：SelectStageController 换成就绪信号**

`Assets/Scripts/Hotfix/GameSystems/UI/SelectStageController.cs` 的 `Start` 整个方法换成：

```csharp
        private async void Start()
        {
            var ui = UIManager.Instance;
            if (ui == null)
            {
                Debug.LogError("SelectStageController: 场景里没有 UIManager");
                return;
            }

            // 阶段 2：等就绪信号，不再轮询。WhenReady 必然完成 ⇒ 不会挂死；
            // 面板没起来时下面那条 null 检查会明确报出来（而不是原来的无限空转）。
            await ui.WhenReady();

            _panel = ui.GetPanel<CharacterSelectPanel>();
            if (_panel == null)
            {
                Debug.LogError("SelectStageController: CharacterSelectPanel 未注册");
                return;
            }

            ApplyPreview(CharacterSelectionService.Load());
            _panel.OnSelectionChanged += ApplyPreview;
            _panel.OnClosed += LoadGameScene;
        }
```

（同时删掉文件顶部若因此变成多余的 `using Cysharp.Threading.Tasks;` —— `Async` 泛型仍需要它，**保留**。）

- [ ] **步骤 7：Sys3CEntry 的 HUD 绑定改等就绪**

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`：

**(a)** 把 `Start` 里这段（约 174-189 行）

```csharp
            // T12 甲-3：HUD 绑定（PlayerHudPanel.Bind 此前全项目零调用者 ⇒ 血条是死的）。
            // 与相机同为 Start 期的表现层装配——PlayerSpawner 的注释把"相机/HUD 绑定"都记在这里。
            // 用显式判空而不是 ?.：UnityEngine.Object 的伪 null 不认 null 传播
            //（UIManager 是 DontDestroyOnLoad 的，跨场景时可能已被销毁）。
            _statsProvider = new Sys3CEntryStatsProvider(this);
            var uiManager = Hotfix.GameSystems.UI.UIManager.Instance;
            if (uiManager != null)
            {
                var hud = uiManager.GetPanel<Hotfix.GameSystems.UI.PlayerHudPanel>("PlayerHudPanel");
                if (hud != null)
                {
                    hud.Bind(_statsProvider);
                    uiManager.ShowAlwaysAsync("PlayerHudPanel").Forget();   // 回选人时被收起过，进世界要再显示
                }
            }
            _hudHp = _currentHP;
```

换成

```csharp
            // T12 甲-3：HUD 绑定（PlayerHudPanel.Bind 此前全项目零调用者 ⇒ 血条是死的）。
            // 阶段 2：改成"等就绪信号"而不是"假设面板已注册"。今天面板是同步加载的，
            // 这个 await 立刻返回；阶段 4 换成 Addressables 之后面板会真的异步加载 ——
            // 那时若还假设 Start 时就绪，HUD 会静默绑不上（血条死在预制体烘焙值上，
            // 即 T12 修掉的那个缺陷）。放在一个独立方法里是因为 Start 本身必须保持同步。
            _statsProvider = new Sys3CEntryStatsProvider(this);
            BindHudWhenReady().Forget();
            _hudHp = _currentHP;
```

**(b)** 在同类的 `OnDestroy` 之前插入新方法：

```csharp
        /// <summary>
        /// 等 UI 就绪再绑 HUD。用显式判空而不是 ?.：UnityEngine.Object 的伪 null 不认 null 传播
        ///（UIManager 是 DontDestroyOnLoad 的，跨场景时可能已被销毁）。
        /// </summary>
        private async Cysharp.Threading.Tasks.UniTask BindHudWhenReady()
        {
            var uiManager = Hotfix.GameSystems.UI.UIManager.Instance;
            if (uiManager == null) return;

            await uiManager.WhenReady();

            var hud = uiManager.GetPanel<Hotfix.GameSystems.UI.PlayerHudPanel>();
            if (hud == null)
            {
                Debug.LogError("Sys3CEntry: PlayerHudPanel 未注册，HUD 不会更新");
                return;
            }

            hud.Bind(_statsProvider);
            uiManager.ShowAlwaysAsync<Hotfix.GameSystems.UI.PlayerHudPanel>().Forget();   // 回选人时被收起过，进世界要再显示
        }
```

**(c)** `OnDestroy` 里那两行同样去掉字符串：

```csharp
            var hud = uiManager.GetPanel<Hotfix.GameSystems.UI.PlayerHudPanel>();
            ...
                uiManager.HideAlwaysAsync<Hotfix.GameSystems.UI.PlayerHudPanel>().Forget();
```

> **注**：`OnDestroy` 里仍假设面板已注册 —— 这是可以的：`OnDestroy` 只会在角色已被创建出来之后发生，而那时 HUD 绑定早已跑过。

- [ ] **步骤 8：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "ReadinessGateTests")`
预期：3 个测试 PASS。

运行：`tests-run(testMode: "EditMode")`
预期：374/374 全绿。

- [ ] **步骤 9：Commit**

```bash
git add Assets/Scripts/Tests/Editor/GameSys.EditorTests.asmdef \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/ReadinessGate.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/ReadinessGate.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/SelectStageController.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/Editor/ReadinessGateTests.cs \
        Assets/Scripts/Tests/Editor/ReadinessGateTests.cs.meta
git commit -m "feat(ui): replace the unbounded readiness polling with a released gate

UIManager.Awake was async void, so nothing observed its failures, and
SelectStageController waited for panels with two untimed
'while (…) await UniTask.Delay(50)' loops -- a permanent spin if a
prefab ever fails to load. It never actually looped because
Resources.Load is synchronous, which is exactly why the latent hang
would only appear once phase 4 makes loading asynchronous.

Initialization is now explicit and its finally always releases the
gate, so WhenReady() cannot hang by construction. Sys3CEntry binds the
HUD after the gate, so the T12 fix survives the phase 4 switch."
```

---

## 任务 4：统一 bootstrap（R8）

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBootstrap.cs`
- 修改（**用 MCP，不是文本编辑**）：两个场景各删 `UIManager` 与 `EventSystem`
- 测试：`Assets/Scripts/Tests/PlayMode/UIBootstrapTests.cs`（创建）

**背景（修复规格 R8）**：两个场景各带一份 `UIManager` 与 `EventSystem`，靠"先加载的场景先 Awake"裁决单例 —— 隐式顺序依赖，且场景里漏放一份就没有 UI。

> ⚠️ **本任务有一处必须盯住的风险：它会改变 PlayMode 套件的运行环境。**
>
> 此前测试场景里**从来没有** UIManager（`PlayerHudBindingTests.cs:19` 的注释明写"测试场景没有 UIManager"）。bootstrap 在 `AfterSceneLoad` 无条件创建之后，**每一个 PlayMode 用例都会凭空多出一套 UI**：加载 3 个面板、并把 `CharacterSelectPanel` 的 `OnShow` 跑一遍（它会读 `CharacterSelectionService` 与 `NetworkModeService`，而 `PlayerHudBindingTests` 恰好对 `NetworkModeService` 做了 Save/Restore —— 说明它知道这类副作用）。
>
> **不预先加"测试里跳过 bootstrap"的开关** —— 那种守卫要么靠探测测试程序集（在编辑器里恒真，等于把 bootstrap 整体关掉），要么靠猜场景名，都是投机。**先让证据说话**：任务 6 步骤 4 会把 PlayMode 结果与基线逐条比对，若有新增失败，bootstrap 的测试期创建就是第一嫌疑。
>
> **任务 6 步骤 4 因此额外要求**：明确确认这 4 个非联网用例仍在通过名单里 —— `PlayerHudBindingTests`、`CharacterSelectPanelReopenTests`、`MobileTouchInputTests`、`RemoteRenderBranchTests`。

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/PlayMode/UIBootstrapTests.cs`：

```csharp
using System.Collections;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 阶段 2：bootstrap 的幂等性。它要在"场景里什么都没有"的前提下把
    /// UIManager 与 EventSystem 各建一份，且重复调用不能越建越多。
    ///
    /// 必须是 PlayMode：EditMode 下 AddComponent 不会触发 Awake，
    /// `UIManager.Instance` 永远是 null，幂等性根本测不出来。
    /// </summary>
    public class UIBootstrapTests
    {
        [UnityTest]
        public IEnumerator test_ensure_ui_manager_is_idempotent()
        {
            var first = UIBootstrap.EnsureUIManager();
            yield return null;

            Assert.IsNotNull(first, "第一次调用必须建出 UIManager");
            Assert.AreSame(first, UIManager.Instance, "Instance 必须指向它");

            var second = UIBootstrap.EnsureUIManager();
            Assert.AreSame(first, second, "重复调用必须复用同一个");

            var count = Object.FindObjectsOfType<UIManager>(includeInactive: true).Length;
            Assert.AreEqual(1, count, "场景里只能有一个 UIManager，实际 " + count);
        }

        [UnityTest]
        public IEnumerator test_ensure_event_system_is_idempotent()
        {
            var first = UIBootstrap.EnsureEventSystem();
            yield return null;

            Assert.IsNotNull(first);

            var second = UIBootstrap.EnsureEventSystem();
            Assert.AreSame(first, second, "重复调用必须复用同一个");

            Assert.IsNotNull(first.GetComponent<StandaloneInputModule>(),
                "EventSystem 必须带输入模块，否则 UI 收不到点击");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "PlayMode", testClass: "UIBootstrapTests")`
预期：**编译失败** —— `UIBootstrap` 不存在。（若 CLI 报 60 秒超时，加 `--timeout 420000`。）

- [ ] **步骤 3：实现 UIBootstrap**

创建 `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBootstrap.cs`：

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 启动期统一创建 UIManager 与 EventSystem（规格 R8）。
    ///
    /// 为什么必须做：此前**两个场景各带一份** UIManager 与 EventSystem，跨场景时靠
    /// "先加载的场景先 Awake"裁决单例 —— 隐式顺序依赖，且场景里漏放一份就没有 UI。
    /// 改为一处创建、`DontDestroyOnLoad`，场景里不再放。
    ///
    /// 用 `AfterSceneLoad` 而不是 `BeforeSceneLoad`：此时场景里的对象都已 Awake，
    /// 于是 `Instance` / `EventSystem.current` 的判空能真正起作用 —— 既容忍场景里还留着
    /// 一份（过渡期安全），也容忍测试场景自带 EventSystem（不会建出第二个）。
    ///
    /// 形制follow `MobileQualityBootstrap`：RuntimeInitializeOnLoadMethod 薄壳 + 可单测的 Ensure* 内核。
    /// </summary>
    public static class UIBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Ensure()
        {
            EnsureUIManager();
            EnsureEventSystem();
        }

        /// <summary>返回当前 UIManager，没有就建一个。幂等。</summary>
        public static UIManager EnsureUIManager()
        {
            if (UIManager.Instance != null)
                return UIManager.Instance;

            var go = new GameObject("UIManager");
            return go.AddComponent<UIManager>();   // Awake 里会 DontDestroyOnLoad
        }

        /// <summary>返回当前 EventSystem，没有就建一个。幂等。</summary>
        public static EventSystem EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return EventSystem.current;

            var go = new GameObject("EventSystem");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            return go.GetComponent<EventSystem>();
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

运行：`tests-run(testMode: "PlayMode", testClass: "UIBootstrapTests")`
预期：2 个测试 PASS。

- [ ] **步骤 5：从两个场景删掉 UIManager 与 EventSystem（走 MCP）**

对 `Assets/Scenes/CharacterSelect.unity` 与 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 各做一次：

1. `scene-open` 打开该场景
2. `gameobject-find` 找 `UIManager` → `gameobject-destroy`
3. `gameobject-find` 找 `EventSystem` → `gameobject-destroy`
4. `scene-save`

**不要**直接文本编辑 `.unity` 文件：Unity 正在运行，那会弹 "modified externally" 模态框阻塞 MCP 主线程，且 Unity 会按自己的理解回写。

- [ ] **步骤 6：核验场景里已无这两者**

```bash
# 预期：四条命令全部无输出
grep -c "m_Name: UIManager" Assets/Scenes/CharacterSelect.unity
grep -c "m_Name: EventSystem" Assets/Scenes/CharacterSelect.unity
grep -c "m_Name: UIManager" Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
grep -c "m_Name: EventSystem" Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
```

再把两个场景分别进一次播放模式，`screenshot-game-view` 确认 UI 仍然正常（这一步验的就是 bootstrap 真的接上了）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBootstrap.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/UIBootstrap.cs.meta \
        Assets/Scripts/Tests/PlayMode/UIBootstrapTests.cs \
        Assets/Scripts/Tests/PlayMode/UIBootstrapTests.cs.meta \
        Assets/Scenes/CharacterSelect.unity \
        Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "refactor(ui): create the UIManager and EventSystem once, not per scene

Both scenes carried their own copy, so which one won was decided by
which scene's Awake ran first -- an implicit ordering dependency, and a
scene that forgot its copy simply had no UI. They are now created once
at AfterSceneLoad and marked DontDestroyOnLoad."
```

---

## 任务 5：R7 输入栈收口

**文件：**
- 修改：`Packages/manifest.json`
- 修改：`Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef`
- 修改（**人工步骤，需重启 Unity**）：`ProjectSettings/ProjectSettings.asset`

- [ ] **步骤 1：删包之前先取证**

```bash
# 1) 项目代码里对新输入 API 的使用数（预期 0）
grep -rn "UnityEngine.InputSystem\|InputAction\|EnhancedTouch" Assets/Scripts/ --include=*.cs | wc -l

# 2) 有没有包依赖它（预期：无输出）
python -c "
import json
d=json.load(open('Packages/packages-lock.json'))
for n,i in d['dependencies'].items():
    for dep in (i.get('dependencies') or {}):
        if 'inputsystem' in dep: print('反向依赖:', n, '->', dep)
"

# 3) manifest 里那一行的确切内容
grep -n "com.unity.inputsystem" Packages/manifest.json
```

三条都要符合预期才继续。若第 1 条非 0 或第 2 条有输出，**停下来**回报。

> ⚠️ `Packages/manifest.json` **有一处未提交改动**（`com.ivanmurzak.unity.mcp` 0.86.3 → 0.90.0），与本任务无关。编辑时只删 `com.unity.inputsystem` 那一行，**不要碰其他行**。

- [ ] **步骤 2：删包与 asmdef 引用**

`Packages/manifest.json`：删除整行 `    "com.unity.inputsystem": "1.7.0",`

`Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef`：删除 references 数组里的整行 `        "Unity.InputSystem",`

- [ ] **步骤 3：让 Unity 重新解析包**

运行 MCP 的 `assets-refresh`（会触发包解析与域重载）。

- [ ] **步骤 4：核验包真的走了**

```bash
# 预期：两条都无输出
grep -n "com.unity.inputsystem" Packages/manifest.json
grep -n "com.unity.inputsystem" Packages/packages-lock.json
```

若 lock 里仍有：说明有别的包把它拉了回来，**停下来**回报（规格 §10 列的正是这条风险）。

- [ ] **步骤 5：跑全量测试**

运行：`tests-run(testMode: "EditMode")`
预期：374/374 全绿。

运行：`tests-run(testMode: "PlayMode")`
预期：与基线逐条相同、零新增失败（基线为 15/28，13 个失败全是连不上 `127.0.0.1:8000` 的联网用例）。

- [ ] **步骤 6：Commit（包与 asmdef 部分）**

```bash
# ⚠️ manifest 与 lock 都另有**与本任务无关**的未提交改动（com.ivanmurzak.unity.mcp 0.86.3 → 0.90.0），
#    所以**不能整文件 `git add`** —— 那会把无关改动一起提交。
#    用 hunk 级暂存：筛出只含目标改动的补丁，`--cached` 应用。
git diff -- Packages/manifest.json > /tmp/m.patch
#   把 /tmp/m.patch 裁成只含 "com.unity.inputsystem" 那一个 hunk 的文件 → /tmp/m_target.patch
git apply --cached /tmp/m_target.patch

# lock 同理：只取 com.unity.inputsystem 那个条目块的 hunk，
# **不要**取 com.ivanmurzak.unity.mcp 的版本行。lock 必须一起提交 ——
# 否则已提交的快照里 manifest 已经删了这个包、lock 却还声明着它，两者不一致。
git diff -- Packages/packages-lock.json > /tmp/l.patch
#   裁成只含 inputsystem 条目块的文件 → /tmp/l_target.patch
git apply --cached /tmp/l_target.patch

git add Assets/Scripts/Hotfix/GameSystems/GameSys.asmdef
git commit -m "chore(ui): drop the unused Input System package

Nothing in Assets/Scripts uses UnityEngine.InputSystem (0 hits), there
are no .inputactions assets, and no package depends on it. Gameplay and
UI both run on the legacy input manager, so the package and the asmdef
reference were pure overhead."
```

- [ ] **步骤 7：** ⛔ **人工步骤 —— 需要人类伙伴配合，实现者不要自己做**

`ProjectSettings/ProjectSettings.asset` 的 `activeInputHandler` 要由 `2`（Both）改成 `0`（旧）。**这个文件不能在 Unity 运行时改** —— 项目自己的 CLAUDE.md 记过同类事故（`TagManager.asset` 会被编辑器按缓存回写，"关掉 Unity 再改文件并复核"）。而且 Unity 改这一项时会弹"需要重启编辑器"的提示。

流程：

1. 实现者报告"等待人类伙伴关闭 Unity"
2. 人类伙伴：关闭 Unity 编辑器
3. 实现者：把 `ProjectSettings/ProjectSettings.asset` 第 866 行左右的 `activeInputHandler: 2` 改成 `activeInputHandler: 0`，并 `git diff` 确认**只有这一行**变了
4. 人类伙伴：重新打开 Unity 项目
5. 实现者：等 MCP 重连（域重载后原生 MCP 工具可能从工具集里消失，那就走 `unity-mcp-cli`），然后跑：
   - `tests-run(testMode: "EditMode")` → 预期 374/374
   - `tests-run(testMode: "PlayMode")` → 预期与基线逐条相同
6. 实现者：在两个场景各进一次播放模式，确认点击与摇杆仍然工作（`screenshot-game-view`）

- [ ] **步骤 8：Commit（ProjectSettings 部分）**

```bash
git add ProjectSettings/ProjectSettings.asset
git commit -m "chore(ui): pin active input handling to the legacy input manager

The package is gone, so 'Both' degenerates to legacy anyway; pinning it
makes the state explicit and stops a re-added package from silently
re-enabling a second input path. Requires an editor restart, hence the
separate commit."
```

---

## 任务 6：阶段 2 验收（含负向验收）

**这个任务的产物是证据，不是代码。** 判据三条：**负向验收通过**、**全量测试与基线一致**、**两条链路仍通**。

- [ ] **步骤 1：确认前置条件**

保存所有打开的 Unity 场景（`tests-run` 在场景有未保存改动时会直接抛异常）。

- [ ] **步骤 2：负向验收 —— 故意改错一个预制体地址**

这是规格 §8 给阶段 2 定的验收条件，验的是"**明确报错且不挂死**"，两者缺一不可。

1. 把 `UIManager.PanelRegistry` 里 `CharacterSelectPanel` 的路径临时改成 `"Prefabs/UI/DoesNotExist"`
2. 打开 `Assets/Scenes/CharacterSelect.unity`，进入播放模式
3. `console-get-logs` 取证，必须同时看到：
   - `UIManager: Prefab not found at Resources/Prefabs/UI/DoesNotExist`
   - `SelectStageController: CharacterSelectPanel 未注册`
4. **不挂死的客观判据**：用 `script-execute` 打一次 `Time.frameCount`，等 2 秒再打一次，**两次必须不同**（帧在推进 = 没有卡死）。同时 `screenshot-game-view` 应能正常返回图像。
5. 退出播放模式，把路径改回去，`git diff` 确认工作区已干净

> 这一条之所以有分量：改动前的实现会在同一个场景里**无限空转** `await UniTask.Delay(50)`，那条路径没有任何出口。

- [ ] **步骤 3：全量 EditMode**

运行：`tests-run(testMode: "EditMode")`
预期：374/374 全绿，0 失败 0 跳过。

- [ ] **步骤 4：全量 PlayMode**

运行：`tests-run(testMode: "PlayMode")`
预期：与基线**逐条同名同消息**，零新增失败。基线为 28 个总数 / 15 通过 / 13 失败（全是 `KcpNet.PlayModeTests.*` 连不上 `127.0.0.1:8000` 的联网用例）。

**另外必须逐名确认这 4 个非联网用例仍在通过名单里** —— 任务 4 的 bootstrap 会改变 PlayMode 的运行环境（每个用例凭空多出一套 UI），它们是第一承受面：

| 用例 | 为什么盯它 |
|---|---|
| `PlayerHudBindingTests` | 它的注释写明"测试场景没有 UIManager"——这个前提没了 |
| `CharacterSelectPanelReopenTests` | 它 `Confirm()` 会走 `UIManager.Instance.CloseAsync`，而此前那条路径因 `Instance == null` 被跳过 |
| `MobileTouchInputTests` | 输入适配器接线 |
| `RemoteRenderBranchTests` | 渲染分支 |

若其中任何一个从通过变成失败，**先查 bootstrap 的测试期创建**，而不是怀疑被测代码。

**不要为了把 PlayMode 跑绿而启动服务端进程** —— 那属环境决策，不在本计划范围内。

- [ ] **步骤 5：链路验证 · 进世界**

打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` → 播放 → `screenshot-game-view` 确认：HUD 正常显示、控制台无 UI 报错。

> 这个场景现在**没有** UIManager 对象了 —— 它由 bootstrap 建出来，所以这一步同时验了任务 4。

- [ ] **步骤 6：链路验证 · 选人**

打开 `Assets/Scenes/CharacterSelect.unity` → 播放 → 截图确认选人面板正常渲染、按钮可点 → 点"确认"后能进入 DemoDay。

- [ ] **步骤 7：记录结论**

把每一步的原始证据写进报告。若全部通过，本阶段完成，无新增改动则无需 commit。
若任一环节失败，**回滚该环节对应的任务**（每个任务都是独立 commit），修好后再继续 —— 不要在失败的提交上叠加后续任务。

---

## 自检记录（作者已执行）

**1. 规格覆盖度**

| 规格条目 | 落在哪个任务 |
|---|---|
| §3.1 就绪信号 `WhenReady()` / `IsReady` | 任务 3 |
| §3.1 `Awake` 不再 `async void`，改显式初始化 | 任务 3 |
| §3.2 注册时校验 Mode vs 组件（开发期抛、发行期禁用） | 任务 1 |
| §3.2 注册后保证面板根有 `CanvasGroup` | 任务 1 |
| §3.3 三个消费方改显式依赖 | 任务 3（`Sys3CEntry`、`SelectStageController`）+ 任务 2（`CharacterAttackHandler` 改泛型；见任务 2 步骤 5 的注） |
| §6 删 `PanelId` 字符串、类型寻址 | 任务 2 |
| §6 注册表是唯一面板清单 | 任务 2 |
| §6 编辑器校验器（漏注册报错） | 任务 2（以 EditMode 测试实现，理由见该测试的注释） |
| §8 R8 bootstrap 统一创建 UIManager/EventSystem | 任务 4 |
| §8 R7 输入栈收口（删包 + asmdef + `activeInputHandler`） | 任务 5 |
| §8 负向验收 | 任务 6 步骤 2 |

无遗漏。规格里属于阶段 3/4/5 的条目已在「本阶段不做」中显式排除。

**2. 占位符扫描**：无"待定/TODO/后续实现"；每个代码步骤都带可直接粘贴的代码块；运行命令与预期结果逐条写明。

**3. 类型一致性**：`ReadinessGate`（`IsReleased` / `Release()` / `WaitAsync()`）在任务 3 定义、由同任务的测试与 `UIManager` 一致使用；`UIManager.PanelRegistry`（`(System.Type panelType, string prefabPath)[]`）在任务 2 定义、由任务 2 的测试与 `LoadBuiltInPanels` 一致使用；`UIPanel.DescribeModeMismatch(VisibilityMode, bool, bool)` 与 `EnsureCanvasGroup()` 在任务 1 定义并由同任务测试使用；`UIBootstrap.EnsureUIManager()` / `EnsureEventSystem()` 在任务 4 定义并由同任务测试使用，均为 `public static`（PlayMode 测试程序集不在 `InternalsVisibleTo` 名单里，这是它们不设 `internal` 的原因）。

**4. 已知的顺序耦合（必须按序执行）**：任务 1 新建的测试里有一个带 `PanelId` 覆写的探针类，任务 2 必须一并删掉它（已列进任务 2 步骤 5 的表格）。任务 2 与任务 3 都改 `Sys3CEntry.cs` 与 `UIManager.cs`，必须串行。
