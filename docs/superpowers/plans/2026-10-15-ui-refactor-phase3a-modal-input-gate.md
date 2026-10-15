# UI 框架重构 · 阶段 3a（入口项 + 模态输入闸门）实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 先把阶段 2 留下的 4 条守卫补上，再让"面板打开时角色输入被挡住"从一个**从未存在过的概念**变成有测试钉住的行为 —— 并消灭"按住攻击 → 开面板 → 关面板 → 自动连续攻击"这个只在手机上现形的缺陷。

**架构：** 一个**只有计数、没有逻辑**的静态闸门 `UIInputGate`；`UIManager` 按当前打开的面板**重算**这个计数（不是累加）；`InputManager` 在**每个读口**上短接并每帧清锁存；移动端三个组件把 `OnDisable` 的清理抽成 `ResetState()`；`MobileInputBinder` 在**进入被挡的那个边沿**清一次输入源。

**技术栈：** Unity 2022.3.25f1 / C# 9 / UniTask / NUnit（EditMode: `GameSys.EditorTests`，PlayMode: `KcpNet.PlayModeTests`）

**规格：** `docs/superpowers/specs/2026-10-14-ui-framework-refactor-design.md` §4（模态与输入闸门）、§12.1（阶段 2 入口项）、§12.2（三条前置）

**验收（规格 §8 阶段 3 的输入那半边）：** **按住攻击 → 开面板 → 关面板 → 不得自动攻击。**

**本阶段从 master 的 `42153b1` 起。** 阶段 3 的另一半（显示适配 + 层重排）另立一份计划，与本计划**互不依赖**。

---

## 本阶段不做（防止越界）

| 不做 | 归属 |
|---|---|
| 展示簇嵌套 Canvas、`raycastTarget` 归位、`SafeAreaFitter`、`ScreenAdapter` 收窄、层序号重排 | **阶段 3b**（另一份计划） |
| `OpenAsync`/`CloseAsync` 的状态机、幂等、超时、失败回 `Unloaded` | 阶段 4 |
| 懒加载 / 卸载 / Addressables | 阶段 4 |
| `StatBar` / `BuffBar` 消重 | 阶段 5 |
| 给 `MobileInputBinder` 加"测试里跳过"的开关 | 不做（YAGNI） |
| 改 `ApplyVisibilityOn/Off` 让它碰 `interactable` | **明令禁止** —— 见 §12.2 前置 1：`interactable` 必须当作**闸门自己的状态**管理，否则被闸门关过的面板重新打开后会保持不可交互 |

---

## 文件结构

### 创建

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIInputGate.cs` | 模态闸门。**只有一个计数**，读写各一处 |
| `Assets/Scripts/Tests/Editor/UIManagerRegistrationTests.cs` | 注册校验的接线测试（阶段 2 入口项 1）+ `CountModals` 纯函数测试 + 常驻面板非模态守卫 |
| `Assets/Scripts/Tests/Editor/UISceneContentTests.cs` | 场景文本守卫：两个场景里不得再有 UIManager / EventSystem 对象（入口项 3） |
| `Assets/Scripts/Tests/PlayMode/ModalInputGateTests.cs` | 闸门的端到端保证：模态打开时输入被挡、关闭后不得自动攻击 |

### 修改

| 文件 | 改动 | 任务 |
|---|---|---|
| `UI/Core/UIManager.cs` | `Register` 改 `internal`；发行期分支补 `Destroy`；`_openPanels` + `CountModals` + `RefreshModalGate`；`WhenReady` 注释收紧 | 1, 3, 6 |
| `UI/Core/UIPanel.cs` | 加 `IsModal` 虚属性 | 6 |
| `UI/Panel/HUD/PlayerHudPanel.cs` | `IsModal => false`（**否则输入永久冻住**） | 6 |
| `UI/Panel/HUD/TargetPanel.cs` | `IsModal => false` | 6 |
| `Sys3C/Input/InputManager.cs` | 18 个读口 + 3 个 setter 短接；`SuppressLatches()` | 4 |
| `Sys3C/Input/MobileInputBinder.cs` | 被挡边沿复位输入源；被挡期间喂零 | 5 |
| `UI/Mobile/MobileActionButtons.cs` | 抽 `ResetState()` | 5 |
| `UI/Mobile/VirtualJoystick.cs` | 抽 `ResetState()` | 5 |
| `UI/Mobile/TouchLookArea.cs` | 抽 `ResetState()` | 5 |
| `Tests/Editor/UIPanelContractTests.cs` | 补一行 `Assert.AreSame(first, panel.CanvasGroup)`（入口项 2） | 1 |
| `Tests/PlayMode/UIBootstrapTests.cs` | 两处加固（入口项 3） | 2 |

---

## 已核实的前提（实现者不必重查）

- **`InputManager` 有 18 个读口**需要在闸门里短接：`GetMoveCommand` / `GetCameraRotationInput` / `IsMoving` / `IsJumpPressed` / `IsAttackJustPressed` / `GetAttackReleaseDuration` / `IsAttackHeldOver` / `IsAttackHeld` / `IsSprintHeld` / `IsDefendHeld` / `IsSkill2Pressed` / `IsSkill3Pressed` / `IsSkillFireballPressed` / `IsSkillFireballHeld` / `IsSkillChargeBlastPressed` / `IsSkillChargeBlastHeld` / `IsSkillMeteorPressed` / `IsSprinting`。另有 3 个由 UI 按钮调用的 setter：`SetAttackHeld` / `SetDefendHeld` / `SetJumpPressed`。
- **只有 `Sys3CEntry` 读 `InputManager`**（`_inputManager = GetComponent<InputManager>()`，没有别的消费方）。所以闸门只要保证 `InputManager` 的输出即可，不需要在每个消费方加判断。
- **`MobileActionButtons.AttackHeld` 的 setter 是私有的**，公开入口是 `OnAttackDown/OnAttackUp/OnJumpDown` —— 模拟"抬指事件丢失"就是调 `OnAttackDown()` 之后**不调** `OnAttackUp()`。
- 三个移动组件的清理代码现在**都在 `OnDisable` 里**（`VirtualJoystick` 清 `_value` + `_handle.anchoredPosition`；`TouchLookArea` 清 `_pending` + `_delta`；`MobileActionButtons` 清三个锁存）—— 抽 `ResetState()` 就是把方法体搬过去，**逐字不改**。
- `UIManager.Register` 现在是 `private void Register(System.Type, UIPanel)`；`#else`（发行期）分支只有 `Debug.LogError` + `return`，**没有 `Destroy`**，而同段的另两条错误路径都有（`UIManager.cs:176-177`）。
- `UIPanel.CanvasGroup` 是 `{ get; private set; }`，由 `Awake` 与 `EnsureCanvasGroup()` 赋值。
- 测试程序集 `GameSys.EditorTests` 已有 `InternalsVisibleTo`，能访问 `internal` 成员。
- `System.Array.Empty<AppearanceEntry>()` 之类的**无关既有改动**不在本计划范围内。

### 运行测试的方式

用 Unity MCP 的 `tests-run`，`testClass` 传**裸类名**。**前置条件：所有打开的场景必须已保存。**

> **CLI 兜底（原生工具从工具集消失时用）**：
> `unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --timeout 420000 --raw --input '{"testMode":"PlayMode","includePassingTests":false,"includeMessages":true}'`
> **默认超时只有 60 秒**，PlayMode（约 2.4 分钟）必须显式加大。CLI 客户端报错时 Unity 侧**可能仍在跑**，紧接着重试会报 `another test run is already in progress`，等一会儿再试。
> **CLI 的 `--raw` 输出尾部会追加两行 "Update available"**，解析 JSON 时要先截到最后一个 `}`。

### 测试基线（本阶段开工前）

- **EditMode：374/374 全绿、0 失败 0 跳过。**
- **PlayMode：30 个总数 / 17 通过 / 13 失败。** 那 13 个**全部**是 `KcpNet.PlayModeTests.*` 里连不上 `127.0.0.1:8000` 的联网用例。
  **判据是"失败集合不变"，不是"数字不变"** —— 每加一批测试 `TotalTests` 就涨。
  **不要为了把 PlayMode 跑绿而启动服务端进程。**

### 阶段 1/2 带过来的四条经验（写计划时都踩过）

1. **核验步骤要排在它要核验的状态之前。**
2. **每个提交步骤的 `git add` 必须覆盖该步骤的**全部**产出**，含 Unity 自动生成的 `.cs.meta`。
3. **涉及程序集引用模型时先取证再落笔**（`UniTask` 与 `DOTween.dll` 各栽过一次）。
4. **写下"没有 / 全部 / 无"之前，先枚举所有可能藏着它的地方**（两次栽在搜索范围比全称判断窄）。

---

## 任务 1：入口项 A —— 注册校验的接线测试

**背景（规格 §12.1 第 1、2 条）**：阶段 2 给 `Register` 加了 10 行校验，但**只覆盖了纯函数** `DescribeModeMismatch` —— 把那 10 行整段删掉，**374 个测试照绿**。而阶段 4 会重写 `Register`，不钉住就会被无声抹掉。

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`（`Register` 的可见性）
- 修改：`Assets/Scripts/Tests/Editor/UIPanelContractTests.cs`（补一行断言）
- 测试：`Assets/Scripts/Tests/Editor/UIManagerRegistrationTests.cs`（创建）

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/UIManagerRegistrationTests.cs`：

```csharp
using System;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 3a 入口项：把阶段 2 那句"声明与预制体不符要报错"从**纯函数**钉到**接线**上。
    ///
    /// 为什么必须补：`DescribeModeMismatch` 单独测过了，但 `Register` 里那 10 行调用它的话
    /// **整段删掉也不会有任何测试报红**。而阶段 4 会重写 `Register` —— 不钉住，
    /// 阶段 2 的修复会被无声抹掉。
    ///
    /// EditMode 下 `AddComponent` 不触发 `Awake`，所以 `UIManager` 是"半成品"、
    /// `GetPanel` 之类的运行时路径走不通 —— 这正好够用：本测试只验注册期那一段。
    /// </summary>
    public class UIManagerRegistrationTests
    {
        /// <summary>声明 CanvasSwitch 但身上没有 Canvas —— 就是那个"永远关不掉"的接线错误。</summary>
        private class CanvasSwitchPanel : UIPanel
        {
            public override LayerType Layer => LayerType.Base;
            public override VisibilityMode Mode => VisibilityMode.CanvasSwitch;
        }

        /// <summary>形状正常的面板。</summary>
        private class HealthyPanel : UIPanel
        {
            public override LayerType Layer => LayerType.Base;
            public override VisibilityMode Mode => VisibilityMode.CanvasGroup;
        }

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_register_throws_when_the_declared_mode_has_no_matching_component()
        {
            // Arrange：面板声明 CanvasSwitch，但 GameObject 上没有 Canvas
            _go = new GameObject("BadPanel");
            var panel = _go.AddComponent<CanvasSwitchPanel>();
            var manager = _go.AddComponent<UIManager>();

            // Act + Assert
            Assert.Throws<InvalidOperationException>(
                () => manager.Register(typeof(CanvasSwitchPanel), panel),
                "声明 CanvasSwitch 而没有 Canvas 的面板永远关不掉 —— Register 必须拦下它");
        }

        [Test]
        public void test_a_rejected_panel_is_not_registered()
        {
            // Arrange
            _go = new GameObject("BadPanel");
            var panel = _go.AddComponent<CanvasSwitchPanel>();
            var manager = _go.AddComponent<UIManager>();

            // Act
            try { manager.Register(typeof(CanvasSwitchPanel), panel); }
            catch (InvalidOperationException) { /* 预期 */ }

            // Assert：校验必须发生在写入字典**之前** —— 否则就是一个"注册了但不可用"的面板
            Assert.IsNull(manager.GetPanel<CanvasSwitchPanel>(),
                "被校验拒绝的面板不得留在注册表里");
        }

        [Test]
        public void test_register_accepts_a_healthy_panel()
        {
            // Arrange
            _go = new GameObject("GoodPanel");
            var panel = _go.AddComponent<HealthyPanel>();
            var manager = _go.AddComponent<UIManager>();

            // Act
            manager.Register(typeof(HealthyPanel), panel);

            // Assert
            Assert.AreSame(panel, manager.GetPanel<HealthyPanel>());
        }

        [Test]
        public void test_register_guarantees_a_canvas_group_on_the_panel_root()
        {
            // Arrange：HealthyPanel 起手没有 CanvasGroup
            _go = new GameObject("GoodPanel");
            var panel = _go.AddComponent<HealthyPanel>();
            var manager = _go.AddComponent<UIManager>();
            Assert.IsNull(_go.GetComponent<CanvasGroup>(), "前置：起手不该有 CanvasGroup");

            // Act
            manager.Register(typeof(HealthyPanel), panel);

            // Assert：阶段 3 的事件层要用 CanvasGroup.interactable
            Assert.IsNotNull(_go.GetComponent<CanvasGroup>(), "注册后面板根必须有 CanvasGroup");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "UIManagerRegistrationTests")`
预期：**编译失败** —— `Register` 是 `private`，测试调不到。这是本步骤期望的红。

- [ ] **步骤 3：把 Register 放开到 internal**

`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`，把

```csharp
        private void Register(System.Type panelType, UIPanel panel)
```

改成

```csharp
        /// <summary>
        /// 注册一个面板。`internal` 而不是 `private`：注册期的校验是**契约的守门人**，
        /// 它必须有接线测试（UIManagerRegistrationTests），而测试程序集靠 InternalsVisibleTo 访问。
        /// 对外仍不可见 —— 唯一的生产调用点是 RegisterFromPrefab。
        /// </summary>
        internal void Register(System.Type panelType, UIPanel panel)
```

- [ ] **步骤 4：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "UIManagerRegistrationTests")`
预期：4 个测试 PASS。

- [ ] **步骤 5：补上阶段 2 漏的那行断言**

`Assets/Scripts/Tests/Editor/UIPanelContractTests.cs` 的 `test_ensure_canvas_group_adds_exactly_one_and_is_idempotent` 里，在 `Assert.AreSame(first, second, ...)` 之后补：

```csharp
                Assert.AreSame(first, panel.CanvasGroup,
                    "EnsureCanvasGroup 必须把组件**缓存**到面板属性上 —— 可见性判断读的是这个属性，" +
                    "只断言返回值的话，`CanvasGroup = cg` 被删时缺陷会复活而无测试报红");
```

- [ ] **步骤 6：跑全量确认没影响既有用例**

运行：`tests-run(testMode: "EditMode")`
预期：378/378 全绿（阶段 2 结束时是 374，本任务新增 4）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs \
        Assets/Scripts/Tests/Editor/UIManagerRegistrationTests.cs \
        Assets/Scripts/Tests/Editor/UIManagerRegistrationTests.cs.meta \
        Assets/Scripts/Tests/Editor/UIPanelContractTests.cs
git commit -m "test(ui): pin the registration guard to its wiring, not just its predicate

DescribeModeMismatch was unit-tested but nothing exercised Register's
ten-line call to it -- deleting that block left all 374 tests green, and
phase 4 is about to rewrite Register. Register becomes internal so the
test assembly can drive it, and EnsureCanvasGroup now also asserts the
cached property, which is what visibility actually reads."
```

---

## 任务 2：入口项 B —— bootstrap 测试加固 + 场景文本守卫

**背景（规格 §12.1 第 3 条）**：`UIBootstrapTests` 两条用例都先调 `Ensure*` 再断言 ⇒ **删掉 `RuntimeInitializeOnLoadMethod` 属性、或 bootstrap 根本不跑，两条照样全绿**。而 R8 的原 bug 正是"场景各带一份"。**这一条时效性很强** —— 阶段 3b 要重开 `DemoDay.unity` 做层序号重排，那正是可能把对象带回来的动作。

**文件：**
- 修改：`Assets/Scripts/Tests/PlayMode/UIBootstrapTests.cs`
- 测试：`Assets/Scripts/Tests/Editor/UISceneContentTests.cs`（创建）

- [ ] **步骤 1：编写失败的场景守卫测试**

创建 `Assets/Scripts/Tests/Editor/UISceneContentTests.cs`：

```csharp
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 3a 入口项：**场景里不得再有 UIManager / EventSystem 对象**。
    ///
    /// 这是 R8 唯一"能抓住原 bug"的守卫 —— `UIBootstrapTests` 做不到（它先建再断言，
    /// 属性被删也照样绿）。做法是先例：`RemoteEntityRegistryTests` 就是读场景文本的。
    ///
    /// 为什么急：阶段 3b 要重开 `DemoDay.unity` 做层序号重排，那正是可能把对象带回来的动作。
    /// </summary>
    public class UISceneContentTests
    {
        private static readonly string[] Scenes =
        {
            "Assets/Scenes/CharacterSelect.unity",
            "Assets/SimpleLowPolyNature/Scenes/DemoDay.unity",
        };

        private static readonly string[] ForbiddenNames = { "UIManager", "EventSystem" };

        [Test]
        public void test_no_scene_carries_its_own_ui_manager_or_event_system()
        {
            foreach (var scenePath in Scenes)
            {
                // 从仓库根解析：EditMode 测试的 cwd 就是项目根
                Assert.IsTrue(File.Exists(scenePath),
                    "找不到场景文件 " + scenePath + "（cwd = " + Directory.GetCurrentDirectory() + "）");

                var text = File.ReadAllText(scenePath);
                foreach (var name in ForbiddenNames)
                {
                    var pattern = @"m_Name:\s*" + Regex.Escape(name) + @"\s*$";
                    Assert.IsFalse(Regex.IsMatch(text, pattern, RegexOptions.Multiline),
                        scenePath + " 里还有名为 " + name + " 的对象 —— " +
                        "R8 的原 bug 就是场景各带一份，它该由 UIBootstrap 在 AfterSceneLoad 建");
                }
            }
        }
    }
}
```

- [ ] **步骤 2：运行测试 —— 预期通过（表征测试）**

运行：`tests-run(testMode: "EditMode", testClass: "UISceneContentTests")`
预期：**PASS**。阶段 2 已经把那四个对象删干净了，所以这条现在就应该是绿的 —— 它的价值是**从现在起锁住**，而不是修什么。

- [ ] **步骤 3：验证这条守卫真的有牙齿**

把 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 用 `grep` 找一处现有的 `m_Name:`，临时把它改成 `m_Name: UIManager`，重跑上面的命令。

预期：**FAIL**，且报错里点名 `DemoDay.unity`。**确认之后把改动逐字还原**，并 `git diff -- Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` 确认无输出。

> 这一步不能省：一条永远绿的守卫等于没有守卫（阶段 2 的任务 6 就是因为这个被审查判过一回）。

- [ ] **步骤 4：给 UIBootstrapTests 加前置断言**

`Assets/Scripts/Tests/PlayMode/UIBootstrapTests.cs` 的两条用例，在**第一次** `Ensure*` 之前各加一行：

```csharp
            // bootstrap 是 PlayMode 会话里 UIManager 的唯一创建者 —— 断言它真的跑了。
            // 没有这一行，删掉 UIBootstrap 的 [RuntimeInitializeOnLoadMethod] 属性、
            // 或者 bootstrap 根本不跑，这两条用例照样全绿（它们会自己建一个出来）。
            Assert.IsNotNull(UIManager.Instance, "bootstrap 没有创建 UIManager —— AfterSceneLoad 没跑？");
```

第二条用例对应改成 `Assert.IsNotNull(EventSystem.current, "bootstrap 没有创建 EventSystem");`（需要在文件顶部加 `using UnityEngine.EventSystems;`）。

- [ ] **步骤 5：运行测试验证通过**

运行：`tests-run(testMode: "PlayMode", testClass: "UIBootstrapTests")`
预期：2 个测试 PASS。

- [ ] **步骤 6：跑全量**

运行：`tests-run(testMode: "EditMode")`
预期：379/379 全绿（378 + 本任务 1）。

运行：`tests-run(testMode: "PlayMode")`
预期：31/18/13（总数 30+1、通过 17+1、失败仍是那 13 个连不上 `127.0.0.1:8000` 的联网用例）—— **失败集合不变**。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/UIBootstrapTests.cs \
        Assets/Scripts/Tests/Editor/UISceneContentTests.cs \
        Assets/Scripts/Tests/Editor/UISceneContentTests.cs.meta
git commit -m "test(ui): add guards that can actually catch the per-scene UI bug

UIBootstrapTests calls Ensure* before asserting, so deleting the
RuntimeInitializeOnLoadMethod attribute -- or not running the bootstrap
at all -- left both cases green. Two additions fix that: an
Assert.IsNotNull before the first Ensure*, and a scene-text guard
asserting neither scene carries its own UIManager or EventSystem.

The scene guard matters now rather than later: phase 3b reopens
DemoDay.unity to renumber layers, which is exactly the edit that could
bring the objects back."
```

---

## 任务 3：入口项 C —— `WhenReady` 注释收紧 + 发行期泄漏修复

**背景（规格 §12.1 第 4 条 + 泄漏那条）**：
- 注释写"**必然完成**……调用方永远不会永久挂起"，但 `finally` 保证的是"**try 块退出时**一定放行"，不是"一定到达 finally" —— 链上的 `WaitForSequence` 是 `while (seq.IsPlaying()) await UniTask.Yield()`。
- `Register` 的发行期分支 `return` 前**既不 Destroy 也不隐藏**，留下一个框架永不管理、也永远关不掉的面板。

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs`

- [ ] **步骤 1：收紧 WhenReady 的注释**

把

```csharp
        /// <summary>
        /// 初始化完成的信号。**必然完成** —— InitializeAsync 的 finally 一定会放行，
        /// 所以调用方永远不会永久挂起。这正是它取代轮询的地方：
        /// `while (…) await UniTask.Delay(50)` 在加载失败时是无限空转，没有任何出口。
        /// </summary>
```

改成

```csharp
        /// <summary>
        /// 初始化完成的信号。**异常或提前返回也一定放行** —— InitializeAsync 的 finally 无条件
        /// 放行，所以加载失败、面板缺失都不会让调用方永久挂起。这正是它取代轮询的地方：
        /// `while (…) await UniTask.Delay(50)` 在加载失败时是无限空转，没有任何出口。
        ///
        /// **注意这句话的边界**：finally 保证的是"try 块**退出时**放行"，不是"**必然到达** finally"。
        /// 被闸门包住的链里有 `WaitForSequence` 的 `while (seq.IsPlaying()) await UniTask.Yield()`
        /// —— 若某个 show 序列永不结束（时间缩放为 0、被暂停的 tween），到不了 finally。
        /// 今天不活（两个动画钩子默认 `return null`，整条链同步完成），但**阶段 4 把加载改成真异步时，
        /// 异步加载必须自带超时**。
        /// </summary>
```

- [ ] **步骤 2：修掉发行期分支的泄漏**

同文件 `Register` 里的

```csharp
#else
                Debug.LogError($"UIManager: 面板 '{panelType.Name}' {mismatch} —— 已跳过注册");
                return;
#endif
```

改成

```csharp
#else
                Debug.LogError($"UIManager: 面板 '{panelType.Name}' {mismatch} —— 已跳过注册并销毁实例");
                // 必须销毁：这是 RegisterFromPrefab 刚 Instantiate 出来的对象，
                // 而它的 Mode 分支本来就什么都不做 ⇒ 留着就是一个框架永不管理、
                // 也永远关不掉的面板（正是 R2 要消灭的那个形态，只是多了一条日志）。
                // 同段的另两条错误路径（无组件 / 类型不符）都 Destroy 了，这条漏了。
                UnityEngine.Object.Destroy(panel.gameObject);
                return;
#endif
```

- [ ] **步骤 3：确认没有引入编译问题**

运行：`tests-run(testMode: "EditMode", testClass: "UIManagerRegistrationTests")`
预期：4 个测试 PASS（`#if UNITY_EDITOR` 走的是 throw 那一支，本测试不受影响；这一步是确认改动没把文件改坏）。

- [ ] **步骤 4：跑全量**

运行：`tests-run(testMode: "EditMode")`
预期：379/379 全绿。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs
git commit -m "fix(ui): destroy the panel the release build refuses to register

The #else branch logged and returned while the two sibling error paths
both Destroy(instance) -- since RegisterFromPrefab has just instantiated
that panel, the leak left behind an object the framework never manages
and, given its mode has no working component, can never hide.

Also tightens WhenReady's contract: the finally guarantees release when
the try block exits, not that the try block is ever exited --
WaitForSequence has an unbounded loop, which phase 4's async loading
will need to bound."
```

---

## 任务 4：`UIInputGate` + `InputManager` 短接

**背景（规格 §4.1/§4.2）**：今天**全项目没有任何代码问过"现在是不是有面板开着"** —— 弹窗打开时摇杆仍在喂输入、攻击键仍会触发。

- [ ] **步骤 1：编写失败的测试**

先建最小的测试文件 `Assets/Scripts/Tests/Editor/UIInputGateTests.cs`：

```csharp
using Hotfix.GameSystems.UI;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 3a：闸门的语义。**它是一个计数，不是一个布尔** ——
    /// 因为"谁挡了输入"必须能按当前打开的面板**重算**，而不是靠加减配平。
    /// </summary>
    public class UIInputGateTests
    {
        [TearDown]
        public void TearDown() => UIInputGate.ModalCount = 0;   // 静态状态，用完必须复位

        [Test]
        public void test_gate_is_open_when_nothing_is_modal()
        {
            UIInputGate.ModalCount = 0;
            Assert.IsFalse(UIInputGate.IsBlocked, "没有模态面板时输入必须是通的");
        }

        [Test]
        public void test_gate_blocks_while_any_modal_is_open()
        {
            UIInputGate.ModalCount = 1;
            Assert.IsTrue(UIInputGate.IsBlocked);

            UIInputGate.ModalCount = 2;
            Assert.IsTrue(UIInputGate.IsBlocked, "多层模态仍然是被挡");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "UIInputGateTests")`
预期：**编译失败** —— `UIInputGate` 不存在。这是期望的红。

- [ ] **步骤 3：实现闸门**

创建 `Assets/Scripts/Hotfix/GameSystems/UI/Core/UIInputGate.cs`：

```csharp
namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 模态输入闸门。**只有一个计数，没有逻辑** —— 谁挡了输入、挡了几层，
    /// 由 `UIManager` 按当前打开的面板**重算**（见 `UIManager.CountModals`）：
    /// 累加必然在某个异常路径上漏减，而漏减会让输入**永久冻住**，重算把那一整类 bug 消灭。
    ///
    /// 为什么放在 UI 程序集：Sys3C 引用 UI 已有先例（`MobileInputBinder` 就在用 UI 的摇杆组件），
    /// 反向则没有。读它的只有 `InputManager` 一处。
    /// </summary>
    public static class UIInputGate
    {
        public static int ModalCount { get; internal set; }

        public static bool IsBlocked => ModalCount > 0;
    }
}
```

（`internal set`：只有 GameSys 程序集能写；测试程序集有 `InternalsVisibleTo`，所以上面的测试能设它。）

- [ ] **步骤 4：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "UIInputGateTests")`
预期：2 个测试 PASS。

- [ ] **步骤 5：给 InputManager 的 18 个读口加短接**

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs` 顶部（类内）加：

```csharp
        /// <summary>
        /// 模态闸门：被挡住时所有**读口**直接返回"没有输入"。
        ///
        /// **这是保证，不是优化** —— 只在 `Update` 里清标志是不够的：好几个读口直接问
        /// `UnityEngine.Input`（键盘/鼠标/技能键），被挡住的那一帧照样会返回 true。
        /// </summary>
        private static bool InputBlocked => Hotfix.GameSystems.UI.UIInputGate.IsBlocked;
```

然后在下面**每一个**读口的第一行插入对应的提前返回（**一个都不能漏** —— 漏一个就是一条仍然通着的输入）：

| 方法 | 提前返回 |
|---|---|
| `GetMoveCommand` | `if (InputBlocked) return new MoveCommand { MoveDir = Vector3.zero, Speed = 0f, Rotation = Quaternion.identity, IsSprint = false };` |
| `GetCameraRotationInput` | `if (InputBlocked) return Vector2.zero;` |
| `IsMoving` | `if (InputBlocked) return false;` |
| `IsJumpPressed` | `if (InputBlocked) return false;` |
| `IsAttackJustPressed` | `if (InputBlocked) return false;` |
| `GetAttackReleaseDuration` | `if (InputBlocked) return -1f;` |
| `IsAttackHeldOver` | `if (InputBlocked) return false;` |
| `IsAttackHeld` | `if (InputBlocked) return false;` |
| `IsSprintHeld` | `if (InputBlocked) return false;` |
| `IsDefendHeld` | `if (InputBlocked) return false;` |
| `IsSkill2Pressed` | `if (InputBlocked) return false;` |
| `IsSkill3Pressed` | `if (InputBlocked) return false;` |
| `IsSkillFireballPressed` | `if (InputBlocked) return false;` |
| `IsSkillFireballHeld` | `if (InputBlocked) return false;` |
| `IsSkillChargeBlastPressed` | `if (InputBlocked) return false;` |
| `IsSkillChargeBlastHeld` | `if (InputBlocked) return false;` |
| `IsSkillMeteorPressed` | `if (InputBlocked) return false;` |
| `IsSprinting` | `if (InputBlocked) return false;` |

三个 setter 同样短接（**闸门不能依赖调用方守规矩**）：

```csharp
        public void SetAttackHeld(bool held) => _attackHeldFromUI = !InputBlocked && held;
        public void SetDefendHeld(bool held) => _defendHeldFromUI = !InputBlocked && held;
        public void SetJumpPressed(bool pressed) => _jumpPressedFromUI = !InputBlocked && pressed;
```

- [ ] **步骤 6：每帧清锁存**

`Update()` 改成（**在最前面**加分支）：

```csharp
        public void Update()
        {
            if (InputBlocked)
            {
                SuppressLatches();
                return;
            }

            _jumpConsumed = false;
            // …以下原样不动…
        }

        /// <summary>
        /// 被挡住期间**每帧**清掉所有锁存。不清的话，面板关掉的那一刻这些"攒下来的"输入会
        /// 一次性放出去 —— 手机上最典型的是：按住攻击 → 开面板 → 手指在面板背后抬起
        /// （按钮的 `OnPointerUp` 收不到）→ 关面板 → **自动连续攻击**。
        ///
        /// `_xxxConsumed = true` 是"本帧已消费"，让走消费位模式的读口返回 false。
        /// </summary>
        private void SuppressLatches()
        {
            _jumpConsumed = true;
            _skill2Consumed = true;
            _skill3Consumed = true;
            _skillFireballConsumed = true;
            _skillChargeBlastConsumed = true;
            _skillMeteorConsumed = true;
            _lastReleaseDuration = -1f;
            _attackJustPressed = false;
            _attackHeld = false;
            _attackHoldStart = -1f;
            _attackHeldFromUI = false;
            _defendHeldFromUI = false;
            _jumpPressedFromUI = false;
        }
```

- [ ] **步骤 7：运行测试与全量**

运行：`tests-run(testMode: "EditMode", testClass: "UIInputGateTests")`
预期：2/2 PASS。

运行：`tests-run(testMode: "EditMode")`
预期：381/381 全绿（379 + 本任务 2）。

- [ ] **步骤 8：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIInputGate.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/UIInputGate.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs \
        Assets/Scripts/Tests/Editor/UIInputGateTests.cs \
        Assets/Scripts/Tests/Editor/UIInputGateTests.cs.meta
git commit -m "feat(ui): add the modal input gate and short-circuit every input reader

Nothing in the project ever asked whether a panel was open, so a popup
would leave the joystick and every action button feeding the character.

The gate is a counter rather than a bool so UIManager can recompute it
from the open panels instead of incrementing -- a missed decrement would
freeze input permanently. InputManager gains an early return on all 18
readers plus the three UI setters, and clears its latches every blocked
frame: otherwise releasing a finger behind a panel would arm an attack
that fires the moment the panel closes."
```

---

## 任务 5：移动端边沿复位

**背景（规格 §4.3）**：`MobileActionButtons.AttackHeld` 由 UI 按钮回调置位。面板盖住按钮时 `OnPointerUp` **收不到** ⇒ 锁存为 true ⇒ 关面板后 `MobileInputBinder` 把它重新喂进去 ⇒ **自动连续攻击**。摇杆同理（抬指事件丢失 ⇒ 角色一直跑）。

**文件：**
- 修改：`UI/Mobile/MobileActionButtons.cs`、`UI/Mobile/VirtualJoystick.cs`、`UI/Mobile/TouchLookArea.cs`
- 修改：`Sys3C/Input/MobileInputBinder.cs`

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/Editor/MobileResetStateTests.cs`：

```csharp
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 阶段 3a：三个移动组件的 `ResetState()` 必须清掉各自的锁存。
    ///
    /// 抽它是为了让**模态闸门和 OnDisable 共用同一条清理路径** ——
    /// 浏览器抢焦点丢抬指、面板盖住按钮丢抬指，是同一类缺陷。
    /// </summary>
    public class MobileResetStateTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_action_buttons_reset_clears_every_latch()
        {
            // Arrange：按下攻击与防御，且**不调**对应的 Up —— 模拟抬指事件丢失
            _go = new GameObject("Buttons");
            var buttons = _go.AddComponent<MobileActionButtons>();
            buttons.OnAttackDown();
            buttons.OnDefendDown();
            buttons.OnJumpDown();
            Assert.IsTrue(buttons.AttackHeld, "前置：按下后必须锁存为 true");

            // Act
            buttons.ResetState();

            // Assert
            Assert.IsFalse(buttons.AttackHeld, "抬指事件丢失后必须能被主动清掉，否则关面板就自动连击");
            Assert.IsFalse(buttons.DefendHeld);
            Assert.IsFalse(buttons.JumpPressed);
        }

        [Test]
        public void test_joystick_reset_zeroes_the_value()
        {
            // Arrange：直接构造"锁存"状态 —— Value 只能由拖拽写入，这里用反射设私有字段
            _go = new GameObject("Joystick");
            var stick = _go.AddComponent<VirtualJoystick>();
            var field = typeof(VirtualJoystick).GetField("_value",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "VirtualJoystick 的实现变了（找不到 _value）");
            field.SetValue(stick, new Vector2(1f, 0f));
            Assert.AreNotEqual(Vector2.zero, stick.Value, "前置：先造出非零值");

            // Act
            stick.ResetState();

            // Assert
            Assert.AreEqual(Vector2.zero, stick.Value, "抬指事件丢失后摇杆必须能归零，否则角色一直跑");
        }

        [Test]
        public void test_look_area_reset_zeroes_the_delta()
        {
            // Arrange
            _go = new GameObject("LookArea");
            var look = _go.AddComponent<TouchLookArea>();
            var pending = typeof(TouchLookArea).GetField("_pending",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(pending, "TouchLookArea 的实现变了（找不到 _pending）");
            pending.SetValue(look, new Vector2(5f, 0f));
            look.LateUpdate();          // 让 _pending 发布成 Delta

            // Act
            look.ResetState();

            // Assert
            Assert.AreEqual(Vector2.zero, look.Delta, "抬指事件丢失后观察量必须能归零，否则视角一直转");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "MobileResetStateTests")`
预期：**编译失败** —— 三个 `ResetState()` 都不存在。这是期望的红。

- [ ] **步骤 3：三个组件各抽一个 ResetState**

三个文件都是同一手法：**把 `OnDisable` 的方法体原样搬进 `ResetState()`，`OnDisable` 改成调用它**。

`UI/Mobile/MobileActionButtons.cs`：

```csharp
        /// <summary>
        /// 清掉所有锁存。**OnDisable 与模态闸门共用这一条路径** —— 浏览器抢焦点、
        /// 以及面板盖住按钮导致 `OnPointerUp` 收不到，是同一类缺陷（抬指事件丢失 ⇒ 状态锁存）。
        /// </summary>
        public void ResetState()
        {
            AttackHeld = false;
            DefendHeld = false;
            JumpPressed = false;
        }

        private void OnDisable() => ResetState();
```

`UI/Mobile/VirtualJoystick.cs`：

```csharp
        /// <summary>清空摇杆值。OnDisable 与模态闸门共用（见 MobileActionButtons.ResetState 的说明）。</summary>
        public void ResetState()
        {
            _value = Vector2.zero;
            // 场景卸载/teardown 时子 RectTransform 可能先被销毁（本条路径可达，故判空）
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
        }

        private void OnDisable() => ResetState();
```

`UI/Mobile/TouchLookArea.cs`：

```csharp
        /// <summary>清空累积与已发布的拖拽量。OnDisable 与模态闸门共用。</summary>
        public void ResetState()
        {
            _pending = Vector2.zero;
            _delta = Vector2.zero;
        }

        private void OnDisable() => ResetState();
```

- [ ] **步骤 4：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "MobileResetStateTests")`
预期：3 个测试 PASS。

- [ ] **步骤 5：MobileInputBinder 在被挡边沿复位并在被挡期间喂零**

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs`：

**(a)** 加字段：

```csharp
        private bool _wasBlocked;
```

**(b)** `LateUpdate` 整个方法换成：

```csharp
        private void LateUpdate()
        {
            bool blocked = Hotfix.GameSystems.UI.UIInputGate.IsBlocked;

            if (blocked)
            {
                // 只在**进入**被挡的那个边沿清一次输入源。之后手指即使还按着，
                // 也不会有新的拖拽事件（面板盖住了），所以清一次就够。
                if (!_wasBlocked) ResetInputSources();
                _wasBlocked = true;

                if (_inputManager != null && _buttons != null)
                {
                    _inputManager.SetAttackHeld(false);
                    _inputManager.SetDefendHeld(false);
                    _inputManager.SetJumpPressed(false);
                    _buttons.ConsumeJump();
                }
                return;
            }

            _wasBlocked = false;

            if (_inputManager == null || _buttons == null) return;

            _inputManager.SetAttackHeld(_buttons.AttackHeld);
            _inputManager.SetDefendHeld(_buttons.DefendHeld);
            _inputManager.SetJumpPressed(_buttons.JumpPressed);
            _buttons.ConsumeJump();
        }

        /// <summary>
        /// 清掉三个输入源各自的锁存。**这是"关面板后不得自动攻击"的最后一块** ——
        /// 闸门只管住 InputManager 那一侧；按钮/摇杆自己锁存的值不清，闸门一开就会重新喂进去。
        /// </summary>
        private void ResetInputSources()
        {
            if (_joystick != null) _joystick.ResetState();
            if (_lookArea != null) _lookArea.ResetState();
            if (_buttons != null) _buttons.ResetState();
        }
```

- [ ] **步骤 6：跑全量**

运行：`tests-run(testMode: "EditMode")`
预期：384/384 全绿（381 + 本任务 3）。

- [ ] **步骤 7：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Mobile/MobileActionButtons.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs \
        Assets/Scripts/Tests/Editor/MobileResetStateTests.cs \
        Assets/Scripts/Tests/Editor/MobileResetStateTests.cs.meta
git commit -m "fix(ui): clear the mobile input latches when a modal opens

The gate only covers InputManager's side. The buttons and the joystick
latch their own state from pointer callbacks, and a panel covering them
means OnPointerUp never arrives -- so the moment the gate reopens, the
binder would feed the stale value straight back in. That is the
auto-attack-after-closing-a-panel bug, on a phone only.

ResetState() is extracted from OnDisable so the modal path and the
focus-loss path share one implementation."
```

---

## 任务 6：模态状态源 —— `IsModal` + 重算计数 + 事件层

**背景（规格 §4.1/§4.2）**：闸门的计数必须由 `UIManager` 按**当前打开的面板**重算。而"哪些面板算模态"由面板自己声明。

> ⚠️ **本任务最大的坑**：`IsModal` 默认 `true`，而 **HUD 与目标面板是常驻显示、从不关闭** —— 它们若按默认值算模态，`ModalCount` 会永远 ≥1，**输入被永久冻住**。所以两个常驻面板必须显式覆写为 `false`，而且要有一条测试守着它。

**文件：**
- 修改：`UI/Core/UIPanel.cs`（加 `IsModal`）
- 修改：`UI/Panel/HUD/PlayerHudPanel.cs`、`UI/Panel/HUD/TargetPanel.cs`（覆写 `false`）
- 修改：`UI/Core/UIManager.cs`（`_openPanels` + `CountModals` + `RefreshModalGate` + 事件层）
- 测试：`Assets/Scripts/Tests/Editor/UIManagerRegistrationTests.cs`（追加三个用例）

- [ ] **步骤 1：编写失败的测试**

在 `Assets/Scripts/Tests/Editor/UIManagerRegistrationTests.cs` 里追加一个测试类：

```csharp
    public class UIModalPolicyTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_always_on_display_panels_are_not_modal()
        {
            // HUD 与目标面板是常驻显示、从不关闭 —— 它们要是模态，ModalCount 永远 ≥1，
            // 输入会被**永久冻住**。这条测试守的就是那个坑。
            _go = new GameObject("PolicyProbe");
            Assert.IsFalse(_go.AddComponent<PlayerHudPanel>().IsModal,
                "HUD 常驻显示、从不关闭 —— 它要是模态，输入会被永久冻住");
            Assert.IsFalse(_go.AddComponent<TargetPanel>().IsModal, "目标面板同理");
        }

        [Test]
        public void test_on_demand_panels_are_modal_by_default()
        {
            _go = new GameObject("PolicyProbe");
            Assert.IsTrue(_go.AddComponent<CharacterSelectPanel>().IsModal,
                "按需打开的面板默认模态 —— 点开背包角色该站住（与主流手游一致）");
        }
    }
```

- [ ] **步骤 2：运行测试验证失败**

运行：`tests-run(testMode: "EditMode", testClass: "UIModalPolicyTests")`
预期：**编译失败** —— `IsModal` 不存在。这是期望的红。

- [ ] **步骤 3：给 UIPanel 加 IsModal，并覆写两个常驻面板**

`UI/Core/UIPanel.cs` 的抽象属性之后加：

```csharp
        /// <summary>
        /// 打开时是否挡住游戏输入（模态）。**默认 true** —— 与主流手游一致：
        /// 点开背包角色站住。需要"边走边看"的面板（聊天、地图）覆写为 false。
        ///
        /// ⚠️ **常驻显示、从不关闭的面板必须覆写为 false** —— 否则 ModalCount 永远 ≥1，
        /// 输入被永久冻住。当下有两个：PlayerHudPanel 与 TargetPanel。
        /// </summary>
        public virtual bool IsModal => true;
```

`UI/Panel/HUD/PlayerHudPanel.cs` 与 `UI/Panel/HUD/TargetPanel.cs` 各加：

```csharp
        /// <summary>常驻显示、从不关闭 ⇒ 不是模态（见 UIPanel.IsModal 的警告）。</summary>
        public override bool IsModal => false;
```

- [ ] **步骤 4：运行测试验证通过**

运行：`tests-run(testMode: "EditMode", testClass: "UIModalPolicyTests")`
预期：2 个测试 PASS。

- [ ] **步骤 5：UIManager 重算闸门计数**

`UI/Core/UIManager.cs`：

**(a)** 字段区加：

```csharp
        /// <summary>当前**可见**的面板。闸门计数由它重算，不是靠加减配平。</summary>
        private readonly HashSet<UIPanel> _visiblePanels = new HashSet<UIPanel>();
```

**(b)** 加两个成员：

```csharp
        /// <summary>
        /// 数出"当前可见且声明为模态"的面板个数。**纯函数**，所以能单测 ——
        /// 闸门的正确性不该只能靠端到端跑出来。
        /// </summary>
        internal static int CountModals(IEnumerable<UIPanel> panels)
        {
            int count = 0;
            foreach (var panel in panels)
            {
                if (panel != null && panel.IsVisible && panel.IsModal) count++;
            }
            return count;
        }

        /// <summary>
        /// 重算闸门计数。**重算而不是累加**：累加必然在某个异常路径上漏减，
        /// 而漏减会让输入**永久冻住**。
        /// </summary>
        private void RefreshModalGate()
        {
            UIInputGate.ModalCount = CountModals(_visiblePanels);
        }
```

**(c)** 在 `ShowPanelAsync` 末尾（`panel.SetVisible(true);` 之后）与 `HidePanelAsync` 末尾（`panel.SetVisible(false);` 之后）各加：

```csharp
            _visiblePanels.Add(panel);      // ShowPanelAsync 里
            RefreshModalGate();
```

```csharp
            _visiblePanels.Remove(panel);   // HidePanelAsync 里
            RefreshModalGate();
```

- [ ] **步骤 6：事件层 —— 模态打开时挡住下层交互**

同文件加：

```csharp
        /// <summary>
        /// 事件层的防线：有模态面板可见时，把**同层及更低层**的其他面板置为不可交互。
        /// 视觉不变、点击不达 —— 这挡住的是"点在面板背后的按钮上"，
        /// 与语义层的闸门（挡住"输入在关闭瞬间释放"）解决的是**两类不同的问题**。
        ///
        /// **只碰 `interactable`，绝不碰 `alpha` 或 `blocksRaycasts`** ——
        /// 那两个是 ApplyVisibilityOn/Off 的领地，越界会让面板的显隐状态错乱。
        /// </summary>
        private void RefreshLowerPanelInteractivity()
        {
            bool modalOpen = UIInputGate.IsBlocked;

            foreach (var panel in _visiblePanels)
            {
                if (panel == null || panel.CanvasGroup == null) continue;

                bool shouldBeInteractive = !modalOpen || panel.IsModal;
                if (panel.CanvasGroup.interactable != shouldBeInteractive)
                    panel.CanvasGroup.interactable = shouldBeInteractive;
            }
        }
```

并在 `RefreshModalGate()` 末尾调用它：

```csharp
        private void RefreshModalGate()
        {
            UIInputGate.ModalCount = CountModals(_visiblePanels);
            RefreshLowerPanelInteractivity();
        }
```

> **为什么 `ApplyVisibilityOn/Off` 一个字都不改**：规格 §12.2 前置 1 明确要求 ——
> 那两个方法**不碰 `interactable`**，否则被闸门关过的面板重新打开后会保持不可交互。
> `interactable` 是**闸门自己的状态**。

- [ ] **步骤 7：给事件层补一个测试**

在 `UIManagerRegistrationTests.cs` 的 `UIModalPolicyTests` 里追加：

```csharp
        [Test]
        public void test_visibility_toggle_does_not_touch_interactable()
        {
            // 规格 §12.2 前置 1：interactable 归闸门管，ApplyVisibilityOn/Off 一个字都不许碰。
            // 越界的话，被闸门关过的面板在重新打开后会保持不可交互。
            _go = new GameObject("InteractProbe");
            var panel = _go.AddComponent<PlayerHudPanel>();
            var cg = _go.AddComponent<CanvasGroup>();
            cg.interactable = false;          // 模拟"闸门把它关了"

            panel.ApplyVisibilityOn();         // CanvasGroup 模式下会设 alpha 与 blocksRaycasts

            Assert.IsFalse(cg.interactable,
                "ApplyVisibilityOn 碰了 interactable —— 那会让闸门关过的面板重开后永久不可交互");
        }

        [Test]
        public void test_count_modals_ignores_invisible_and_non_modal_panels()
        {
            // 闸门的计数是"重算"出来的，所以这个纯函数就是闸门的正确性所在 ——
            // 端到端跑只能证明某一条路径，这里证明的是**规则本身**。
            _go = new GameObject("CountProbe");
            var hud = _go.AddComponent<PlayerHudPanel>();            // IsModal = false
            var select = _go.AddComponent<CharacterSelectPanel>();    // IsModal = true（默认）
            var panels = new UIPanel[] { hud, select };

            Assert.AreEqual(0, UIManager.CountModals(panels), "都不可见时不该算模态");

            hud.SetVisible(true);
            Assert.AreEqual(0, UIManager.CountModals(panels),
                "非模态的 HUD 可见也不算 —— 它从不关闭，算了就会把输入永久冻住");

            select.SetVisible(true);
            Assert.AreEqual(1, UIManager.CountModals(panels), "模态且可见才计 1");

            select.SetVisible(false);
            Assert.AreEqual(0, UIManager.CountModals(panels),
                "关掉后必须归零 —— 归不了零，输入就永久冻住");
        }
```

- [ ] **步骤 8：跑全量**

运行：`tests-run(testMode: "EditMode")`
预期：388/388 全绿（384 + 本任务 4：IsModal 策略 2 条 + interactable 边界 1 条 + CountModals 1 条）。

- [ ] **步骤 9：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/UIPanel.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/PlayerHudPanel.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Panel/HUD/TargetPanel.cs \
        Assets/Scripts/Tests/Editor/UIManagerRegistrationTests.cs
git commit -m "feat(ui): derive the modal gate from the open panels, and block lower layers

IsModal defaults to true, so the two always-on display panels override it
to false -- had they not, ModalCount would never drop to zero and input
would freeze permanently. A test guards that.

The count is recomputed from the visible set rather than incremented;
a missed decrement would freeze input the same way. On top of the
semantic gate, a modal panel now also clears interactable on the panels
below it, which is a different problem (taps landing on what is behind
the panel) with a different fix."
```

---

## 任务 7：阶段 3a 验收

**产物是证据，不是代码。** 判据三条：**端到端行为**、**全量测试与基线一致**、**编辑器状态干净**。

- [ ] **步骤 1：确认前置条件**

保存所有打开的 Unity 场景（`tests-run` 在场景有未保存改动时会直接抛异常）。

- [ ] **步骤 2：端到端 —— 闸门的核心保证（自动化）**

创建 `Assets/Scripts/Tests/PlayMode/ModalInputGateTests.cs`：

```csharp
using System.Collections;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 阶段 3a 的验收：**按住攻击 → 开面板 → 关面板 → 不得自动攻击。**
    ///
    /// 这条链的每一环都能单独成立却合起来失效，所以必须端到端跑：
    /// 闸门短接读口、每帧清锁存、setter 也短接 —— 少了任何一环，关面板的那一刻
    /// 都会把"攒下来的"输入一次性放出去。
    /// </summary>
    public class ModalInputGateTests
    {
        private GameObject _probe;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_probe != null) Object.DestroyImmediate(_probe);
            UIInputGate.ModalCount = 0;
            yield return null;
        }

        [UnityTest]
        public IEnumerator test_attack_does_not_survive_a_modal_panel()
        {
            // Arrange：bootstrap 已经建好 UIManager；再挂一个 InputManager 当探针
            var ui = UIBootstrap.EnsureUIManager();
            Assert.IsNotNull(ui, "bootstrap 没有建 UIManager");
            yield return ui.WhenReady().ToCoroutine();

            // ⚠️ 启动流程的最后一步**就是打开选人面板**（LoadBuiltInPanels → OpenAsync<CharacterSelectPanel>），
            // 所以此刻闸门本来就是挡住的。必须先把它关掉，拿到一个"没有模态面板"的干净起点 ——
            // 否则下面第一个断言（按住攻击必须读到 true）必红，而且红得让人以为是闸门的 bug。
            yield return ui.CloseAsync<CharacterSelectPanel>().ToCoroutine();
            yield return null;
            Assert.IsFalse(UIInputGate.IsBlocked,
                "前置：关掉选人面板后闸门必须是通的（不通说明 RefreshModalGate 没归零）");

            _probe = new GameObject("InputProbe");
            var input = _probe.AddComponent<InputManager>();

            // Act 1：按住攻击（等价于手机上按住攻击按钮）
            input.SetAttackHeld(true);
            yield return null;

            // Assert 1
            Assert.IsTrue(input.IsAttackHeld(), "前置：按住时必须是 true");

            // Act 2：打开模态面板（CharacterSelectPanel 用默认值 ⇒ IsModal = true）
            yield return ui.OpenAsync<CharacterSelectPanel>().ToCoroutine();

            // Assert 2：闸门必须已经挡住
            Assert.IsFalse(input.IsAttackHeld(), "模态面板打开后，攻击必须读不到");

            // Act 3：手指在面板背后抬起 —— 按钮的 OnPointerUp 收不到，这里**不**调 SetAttackHeld(false)
            yield return null;
            yield return null;

            // Act 4：关面板
            yield return ui.CloseAsync<CharacterSelectPanel>().ToCoroutine();
            yield return null;

            // Assert 3：**这就是本阶段要钉住的缺陷**
            Assert.IsFalse(input.IsAttackHeld(),
                "关面板后不得自动攻击 —— 锁存必须在被挡期间就被清掉，而不是攒到闸门重开");
        }
    }
}
```

- [ ] **步骤 3：运行端到端测试**

运行：`tests-run(testMode: "PlayMode", testClass: "ModalInputGateTests")`
预期：PASS。**若失败，先查 `_visiblePanels` 有没有在 Show/Hide 里正确增删** —— 最常见的坏法是计数没重算，闸门从未真正打开或从未真正关闭。

- [ ] **步骤 4：手动确认移动端链路（把自动化覆盖不到的那一段走一遍）**

自动化只覆盖了 `InputManager` 那一侧。**三个移动组件自己锁存的值**由任务 5 的 `ResetState` 处理，而它需要真实的按钮状态才能端到端验：

1. 进 CharacterSelect 场景播放（或在 DemoDay 里用 `?touch=1` 强制触控）
2. 用 `script-execute` 打印 `MobileActionButtons.AttackHeld`（先调 `OnAttackDown()` 造出锁存）
3. 打开模态面板 → 观察它被清成 false
4. 关面板 → 再打印一次，**必须仍是 false**
5. 截图留证

- [ ] **步骤 5：全量 EditMode**

运行：`tests-run(testMode: "EditMode")`
预期：388/388 全绿，0 失败 0 跳过。

- [ ] **步骤 6：全量 PlayMode**

运行：`tests-run(testMode: "PlayMode")`
预期：**失败集合不变**。基线是 13 个失败（全是 `KcpNet.PlayModeTests.*` 连不上 `127.0.0.1:8000`）；本任务新增 1 个用例，所以总数与通过数各 +1。**逐个列出失败用例名**并确认仍是同一批。

**不要为了把 PlayMode 跑绿而启动服务端进程。**

- [ ] **步骤 7：记录结论**

把每一步的原始证据写进报告。若全部通过，本阶段完成，无新增改动则无需 commit。
若任一环节失败，**回滚该环节对应的任务**（每个任务都是独立 commit），修好后再继续。

---

## 自检记录（作者已执行）

**1. 规格覆盖度**

| 规格条目 | 落在哪个任务 |
|---|---|
| §12.1 第 1 条：`Register` 校验的接线测试 | 任务 1 |
| §12.1 第 2 条：`EnsureCanvasGroup` 缓存断言 | 任务 1 步骤 5 |
| §12.1 第 3 条：`UIBootstrapTests` 加固 + 场景文本守卫 | 任务 2 |
| §12.1 第 4 条：`WhenReady` 注释收紧 | 任务 3 步骤 1 |
| §12.1 泄漏那条：发行期分支 `Destroy` | 任务 3 步骤 2 |
| §4.1 语义层：`UIInputGate` + 短接 + 清锁存 | 任务 4 |
| §4.2 计数**重算**而非累加 | 任务 6 步骤 5 |
| §4.3 移动端锁存清理 | 任务 5 |
| §4.1 事件层：模态挡住下层交互 | 任务 6 步骤 6 |
| §4.4 `IsModal` 默认 true、可声明例外 | 任务 6 步骤 3 |
| §12.2 前置 1：`ApplyVisibilityOn/Off` 不碰 `interactable` | 任务 6 步骤 7 的守卫测试 |
| §8 阶段 3 验收的输入那半边 | 任务 7 |

**§12.2 前置 2**（重开 DemoDay 时退掉 missing-script）与**前置 3**（PlayMode 共享对象纪律）属**阶段 3b**（前者要在动那个场景时做）与本计划的验收记录（后者）。**显示适配与层重排整体不在本计划**，见「本阶段不做」。

**2. 占位符扫描**：无"待定/TODO/后续实现"；每个代码步骤都带可直接粘贴的代码块；运行命令与预期结果逐条写明。

**3. 类型一致性**：`UIInputGate.ModalCount`（`int`，`internal set`）与 `IsBlocked`（`bool`）在任务 4 定义，任务 6 与任务 7 一致使用；`UIPanel.IsModal`（`virtual bool`）在任务 6 定义并由同任务的两个覆写与测试使用；`CountModals(IEnumerable<UIPanel>)` 在任务 6 定义为 `internal static`；三个 `ResetState()` 在任务 5 定义为 `public void` 并被 `MobileInputBinder` 调用。

**4. 已知的顺序耦合**：任务 4 建的 `UIInputGate` 是任务 5、6 的前提；任务 6 的 `RefreshModalGate` 是任务 7 端到端测试的前提。三个任务的先后不能换。任务 1 建的 `UIModalPolicyTests` 与任务 6 追加的用例在同一个文件里。
