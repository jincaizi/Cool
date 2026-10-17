# HUD 显隐生命周期 & 移动端 UI 上屏验收 & 编辑器默认触屏 实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 修掉「确认角色后 HUD 永久消失」（按状态判据，与帧序无关），并把移动端 UI 的「上屏/可点/模拟量」从人眼验收变成断言；编辑器默认按触屏，让摇杆 UI 可看可用。

**架构：** 三处独立的小改动，各自带自己的测试与提交：①`Sys3CEntry.OnDestroy` 从「实例被销毁」改成「场上还有已启动的本地角色就不收」；②`TouchInputAvailability.Resolve` 的 `Auto` 分支在编辑器判真（`UNITY_EDITOR`，不进包）；③`MobileTouchInputTests` 新增一组纯表征断言（几何/射线/模拟量/抬指/三按钮）。规格见 `docs/superpowers/specs/2026-10-17-hud-lifecycle-and-mobile-ui-onscreen-design.md`（commit `56fcd18`）。

**技术栈：** Unity 2022.3.25f1、C#（Hotfix 程序集）、NUnit（EditMode/PlayMode）、Unity MCP（`unity-mcp-cli run-tool`）、UGUI/EventSystem、UniTask。

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 修改（`OnDestroy` + 新私有 helper） | 销毁边沿的状态判据：场上还有已启动的本地角色 ⇒ 不收起、不解绑 |
| `Assets/Scripts/Tests/PlayMode/HudLifecycleTests.cs` | 创建 | 两条生命周期守卫：重建后仍可见 / 销毁最后一个后收起 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/TouchInputAvailability.cs` | 修改（`Resolve`） | 编辑器里 `Auto` ⇒ 触屏；出包不变 |
| `Assets/Scripts/Tests/Editor/TouchInputAvailabilityTests.cs` | 修改（新增一条用例） | 决策钉：编辑器 `Resolve(Auto) == true` |
| `Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs` | 修改（新增一条用例 + 两处注释） | 触控 UI 上屏/可点/模拟量/抬指/三按钮的断言 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs` | 修改（仅注释） | 让它对「编辑器默认触屏」的说明与事实一致 |
| `docs/superpowers/guides/webgl-mobile-acceptance.md` | 修改（§边界表） | 手机验收清单补「编辑器 Play 默认可见触控 UI」 |
| `docs/superpowers/guides/webgl-demo-boundaries.md` | 修改（可选，一句话） | 面试话术补「编辑器默认按触屏，出包不受影响」 |

**不改**：`UIManager`/`UIPanel`/`PlayerHudPanel` 的任何实现；`PlayerSpawner`；HUD 预制体；`MobileUiRoot` 的场景布局。

---

## 任务 1：HUD 生命周期判据（缺陷修复 + 两条回归守卫）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:760-775`（`OnDestroy`，行号以当前文件为准）
- 创建：`Assets/Scripts/Tests/PlayMode/HudLifecycleTests.cs`（+ Unity 自动生成的 `.meta`）

- [ ] **步骤 1：编写失败的测试**

创建 `Assets/Scripts/Tests/PlayMode/HudLifecycleTests.cs`：

```csharp
using System.Collections;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// HUD 显隐的生命周期守卫（2026-10-17 缺陷的回归测试）。
    ///
    /// 原缺陷：确认角色时 `PlayerSpawner.OnCharacterSelected` **先 Destroy(旧) 再 Instantiate(新)**。
    /// 新玩家 Start 里的 `BindHudWhenReady` 会显示 HUD，而旧玩家的 `Sys3CEntry.OnDestroy`
    /// （帧末才跑）随后又把它收起并解绑 —— 没有任何人再显示 ⇒ HUD 永久消失。
    /// 两条用例各钉一条边沿：重建后仍可见 / 销毁最后一个后收起。
    /// </summary>
    public class HudLifecycleTests
    {
        private GameObject _playerA;
        private GameObject _playerB;
        private PlayerHudPanel _hud;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // 真面板：PlayMode 会话里 UIBootstrap 在 AfterSceneLoad 已建 UIManager（幂等，不重复建）
            UIBootstrap.EnsureUIManager();
            Assert.IsNotNull(UIManager.Instance, "UIBootstrap 应创建 UIManager");

            for (int i = 0; i < 600 && UIManager.Instance.GetPanel<PlayerHudPanel>() == null; i++)
                yield return null;
            _hud = UIManager.Instance.GetPanel<PlayerHudPanel>();
            Assert.IsNotNull(_hud, "PlayerHudPanel 未注册（检查 Resources/Prefabs/UI/PlayerHudPanel）");

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab（含 Sys3CEntry 的测试玩家）");
            _playerA = Object.Instantiate(prefab);

            // 等 A 的 Start 建立数据源并显示 HUD（BindHudWhenReady 在 Start 里同步完成）
            for (int i = 0; i < 600 && !PlayerStarted(_playerA); i++)
                yield return null;
            Assert.IsTrue(PlayerStarted(_playerA), "实例 A 应已 Start（StatsProvider 非空）");
            Assert.IsTrue(_hud.IsVisible, "A 启动后 HUD 应已显示（前置条件）");
        }

        [TearDown]
        public void TearDown()
        {
            // 逐个销毁：最后一个被销毁时 HUD 会自我收起，正好把共享面板留在干净状态
            if (_playerA != null) Object.DestroyImmediate(_playerA);
            _playerA = null;
            if (_playerB != null) Object.DestroyImmediate(_playerB);
            _playerB = null;
        }

        [UnityTest]
        public IEnumerator test_hud_stays_visible_when_player_rebuilt_while_old_dies()
        {
            // Arrange：A 已完成 Start、HUD 可见（SetUp 已断言）
            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");

            // Act：逐字复刻 PlayerSpawner.OnCharacterSelected 的两步（同一帧，先 Destroy 后 Instantiate）。
            // 顺序反了就走不到真实时序，测试会假绿。
            Object.Destroy(_playerA);
            _playerA = null;
            _playerB = Object.Instantiate(prefab);

            // 等 B Start（HUD 由 B 的 Start 重新显示），再等 2 帧让 A 的帧末销毁跑完
            for (int i = 0; i < 600 && !PlayerStarted(_playerB); i++)
                yield return null;
            Assert.IsTrue(PlayerStarted(_playerB), "重建后应存在已 Start 的新玩家");
            yield return Frames(2);

            // Assert
            Assert.IsTrue(_hud.IsVisible,
                "重建玩家后 HUD 必须仍然可见（原缺陷：旧玩家 OnDestroy 把它收了）");
            Assert.AreEqual(1f, _hud.CanvasGroup.alpha, 0.001f,
                "HUD 的 CanvasGroup alpha 必须回到 1");
        }

        [UnityTest]
        public IEnumerator test_hud_hides_when_last_local_entry_dies()
        {
            // Arrange：A 已完成 Start、HUD 可见（SetUp 已断言）
            Assert.IsTrue(_hud.IsVisible, "前置：A 已让 HUD 可见");

            // Act：销毁唯一的本地角色
            Object.Destroy(_playerA);
            _playerA = null;
            yield return Frames(3);   // 帧末销毁 + OnDestroy 里的收起

            // Assert：T12 的意图必须保住（回选人/角色没了 ⇒ 收起 HUD）
            Assert.IsFalse(_hud.IsVisible, "最后一个本地角色销毁后 HUD 必须收起");
            Assert.AreEqual(0f, _hud.CanvasGroup.alpha, 0.001f, "HUD 的 CanvasGroup alpha 必须为 0");
        }

        /// <summary>该实例的 Start 是否已跑完（`_statsProvider` 在 Start 里、绑定 HUD 之前建立）。</summary>
        private static bool PlayerStarted(GameObject go)
        {
            if (go == null) return false;
            var entry = go.GetComponent<Sys3CEntry>();
            return entry != null && entry.StatsProvider != null;
        }

        /// <summary>帧边界与 Update 的先后顺序对协程不保证——多等一帧再断言，避免假红。</summary>
        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败（红）**

运行（`tests-run` 的 `testClass` 用**裸类名**，全限定名会 "No tests found"）：

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"HudLifecycleTests"}' --timeout 420000
```

预期：
- `test_hud_stays_visible_when_player_rebuilt_while_old_dies` **FAIL**，失败信息是「重建玩家后 HUD 必须仍然可见」
- `test_hud_hides_when_last_local_entry_dies` **PASS**（这条今天本来就对，它防的是修复改坏另一端）

若第一条竟然通过：说明没复现真实时序（检查是否真的同帧先 Destroy 后 Instantiate、是否等了 2 帧）。**停下来查，不要改断言。**

- [ ] **步骤 3：实现最少代码（判据换成状态边沿）**

修改 `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` 的 `OnDestroy`：在 `if (_statsProvider == null) return;` 之后插入 `if (HasOtherStartedLocalEntry()) return;`，并在类内新增私有 helper：

```csharp
        private void OnDestroy()
        {
            PhysicsRegistry.Instance.Unregister(this);

            // T12 甲-3：本地角色没了（阵亡/断线回选人/换角色）→ 收起 HUD 并解绑。
            // **但「本地角色没了」不等于「这个实例被销毁」**：确认角色重建玩家时，新玩家已经
            // Start（并显示 HUD），旧玩家的帧末销毁才跑到这里 —— 无条件收起会把新玩家的 HUD
            // 一起收掉，且再没有别人显示它（2026-10-17 实测：HUD 永久消失）。
            // 判据因此改成状态：场上还有「已启动的本地角色」就把 HUD 交给它。
            if (_statsProvider == null) return;
            if (HasOtherStartedLocalEntry()) return;
            var uiManager = Hotfix.GameSystems.UI.UIManager.Instance;
            if (uiManager == null) return;
            var hud = uiManager.GetPanel<Hotfix.GameSystems.UI.PlayerHudPanel>();
            if (hud != null)
            {
                hud.Unbind();
                uiManager.HideAlwaysAsync<Hotfix.GameSystems.UI.PlayerHudPanel>().Forget();
            }
        }

        /// <summary>
        /// 场上是否还有**别的**已启动的本地角色。
        ///
        /// 判据必须是「已启动」（`_statsProvider != null`）：只有 Start 跑完的 entry 才绑过 HUD，
        /// 也才会在 Start 里重新显示 HUD。`enabled` 单独不够 —— 远端头像预制体自带的
        /// `Sys3CEntry` 是 `enabled=false`（bot 视图），而 `FindObjectsOfType` **会返回
        /// 「激活对象上的禁用组件」**（本项目已在 `MobileInputBinder.PickLocalEntry` 记录过这个
        /// 陷阱）。两者叠加：`enabled` 挡 bot 视图，`_statsProvider` 兜住「enabled 但 Start
        /// 还没跑完」的窗口。
        /// </summary>
        private bool HasOtherStartedLocalEntry()
        {
            foreach (var entry in FindObjectsOfType<Sys3CEntry>())
            {
                if (entry == this) continue;
                if (!entry.enabled) continue;
                if (entry._statsProvider == null) continue;
                return true;
            }
            return false;
        }
```

（`_statsProvider` 是同类私有字段，可直接读另一个实例。）

- [ ] **步骤 4：运行测试验证通过（绿）**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"HudLifecycleTests"}' --timeout 420000
```

预期：2/2 PASS。

- [ ] **步骤 5：跑相邻回归**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"PlayerHudBindingTests"}' --timeout 420000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"UIBootstrapTests"}' --timeout 420000
```

预期：全 PASS（HUD 绑定与 UI 启动没有回归）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/PlayMode/HudLifecycleTests.cs \
        Assets/Scripts/Tests/PlayMode/HudLifecycleTests.cs.meta
git commit -m "fix(hud): HUD 收起改状态判据 —— 重建玩家时不再被旧实例收走"
```

提交信息正文需写明：现象（确认角色后 HUD 永久消失）、根因（旧玩家帧末 OnDestroy 跑在新玩家 Start 之后）、判据（场上还有已启动的本地角色就不收；`enabled` 挡 bot 视图、`_statsProvider` 兜 Start 未跑完的窗口）、证据（修复前 1 红 1 绿、修复后 2/2）。

---

## 任务 2：编辑器默认按触屏（产品决定 + 决策钉 + 同批话术）

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/TouchInputAvailability.cs:71-78`（`Resolve`）
- 修改：`Assets/Scripts/Tests/Editor/TouchInputAvailabilityTests.cs`（新增一条用例）
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs`（仅注释，两处）
- 修改：`docs/superpowers/guides/webgl-mobile-acceptance.md`（§边界表一行）
- 修改（可选）：`docs/superpowers/guides/webgl-demo-boundaries.md:71-76`（面试话术一句话）

- [ ] **步骤 1：编写失败的测试**

在 `Assets/Scripts/Tests/Editor/TouchInputAvailabilityTests.cs` 的 `TouchInputAvailabilityTests` 类内新增：

```csharp
        [Test]
        public void test_editor_auto_resolves_to_touch()
        {
            // 产品决定（2026-10-17）：编辑器默认按触屏 —— 摇杆 UI 在编辑器里可看可用。
            // 这是**决策钉**、不是行为保证：若将来要改回键鼠默认，必须同时改这条测试与
            // MobileInputBinder 的注释，否则摇杆会静默从编辑器里消失。
            // 出包不受影响：UNITY_EDITOR 分支不进包，桌面浏览器仍是 isMobilePlatform/特性检测。
            Assert.IsTrue(TouchInputAvailability.Resolve(TouchOverride.Auto),
                "编辑器里 Auto 必须判为触屏");
        }
```

- [ ] **步骤 2：运行测试验证失败（红）**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"EditMode","testClass":"TouchInputAvailabilityTests"}'
```

预期：新增用例 FAIL（`Expected: True But was: False`），原有 3 条 PASS。

- [ ] **步骤 3：实现 + 同批更新注释**

`TouchInputAvailability.cs` 的 `Resolve` 改为：

```csharp
        /// <summary>
        /// 最终裁决：URL > 人工档位 > 自动判定。
        ///
        /// 自动判定在**编辑器**里恒为触屏（2026-10-17 产品决定）：摇杆与三按钮因此在编辑器里
        /// 默认可见可用，方便迭代。`UNITY_EDITOR` 是编译期的，出包后仍走
        /// `isMobilePlatform || 特性检测`（桌面浏览器里的面试 demo 保持键鼠）。
        /// 编辑器里要键鼠：运行时把 `MobileInputBinder.TouchOverride` 拨成 `ForceNoTouch`。
        /// </summary>
        public static bool Resolve(TouchOverride mode)
        {
            bool? fromUrl = FromUrlQuery();
            if (fromUrl.HasValue) return fromUrl.Value;
            if (mode == TouchOverride.ForceTouch) return true;
            if (mode == TouchOverride.ForceNoTouch) return false;
#if UNITY_EDITOR
            return true;   // 编辑器默认按触屏：摇杆 UI 可看可用（出包不含本分支）
#else
            return Application.isMobilePlatform || TouchFeatureDetected();
#endif
        }
```

同批修改的注释与话术（**逐条**，缺一条就留下假话）：

1. `MobileInputBinder.cs` 类注释第 12 行：
   - 原：`/// PC 浏览器与编辑器维持键鼠，不影响既有验收。`
   - 改：`/// PC **浏览器**维持键鼠；**编辑器默认按触屏**（摇杆 UI 可看可用，便于迭代），`
     `/// 要键鼠就在运行时把 TouchOverride 拨成 ForceNoTouch。`

2. `MobileInputBinder.cs` 的 `TouchOverride` 文档块（约第 28-35 行，其中第 33 行是失效句）：
   - 原：`/// （否则 `Application.isMobilePlatform` 在编辑器恒 false，那条分支按构造测不到）。`
   - 改：`/// （编辑器现在**默认**就进那条分支，所以这条接缝仍是"显式档位"而非"Auto 的结果"）。`
   - 该块末尾「出包后的 WebGL 产物没有 Inspector —— 那边唯一的入口是 URL」**保留不动**。

3. `TouchInputAvailability.cs` 类注释里那句「因此判定顺序为：**URL 参数 > 人工档位 > (isMobilePlatform || 特性检测)**。」
   - 改：`/// 因此判定顺序为：**URL 参数 > 人工档位 > 编辑器⇒真 > (isMobilePlatform || 特性检测)**。`

4. `docs/superpowers/guides/webgl-mobile-acceptance.md`：在「为什么这一块特别需要人工」的表格**之后**、
   「正因为第一条不可靠…」那段**之前**，补一句：
   `注意（2026-10-17 起）：编辑器 Play 默认就进触屏分支，摇杆在编辑器里可见可用；上表列的是`
   `**出包后**仍只能靠人眼确认的边界。`

5. （可选）`docs/superpowers/guides/webgl-demo-boundaries.md:71-76` 的面试话术末尾加一句：
   「编辑器里我默认按触屏走，方便迭代；`UNITY_EDITOR` 分支不进包，出包不受影响。」

- [ ] **步骤 4：运行测试验证通过（绿）**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"EditMode","testClass":"TouchInputAvailabilityTests"}'
```

预期：4/4 PASS。

- [ ] **步骤 5：跑全量 EditMode**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode"}' --timeout 240000
```

预期：0 失败（基线 399 + 本任务 1 条）。

- [ ] **步骤 6：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/TouchInputAvailability.cs \
        Assets/Scripts/Tests/Editor/TouchInputAvailabilityTests.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs \
        docs/superpowers/guides/webgl-mobile-acceptance.md \
        docs/superpowers/guides/webgl-demo-boundaries.md
git commit -m "feat(input): 编辑器默认按触屏 —— 摇杆 UI 可看可用，出包不变"
```

提交信息正文需说明：这是产品决定；出包不受影响（`UNITY_EDITOR` 编译期分支）；代价是编辑器里整条键鼠通道被整体替换（适配器互斥、不叠加），`ForceNoTouch` 可切回；以及「同批更新的 5 处话术/注释」。

---

## 任务 3：移动端 UI 上屏断言（触控 UI 真的画在屏上、可点、保留模拟量）

**文件：**
- 修改：`Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs`（新增 1 个 `[UnityTest]` + 2 个私有 helper；更新两处注释）

- [ ] **步骤 1：新增用例（表征测试，预期直接通过）**

在 `MobileTouchInputTests` 类内新增（`FindRoot` 已存在，直接复用）：

```csharp
        /// <summary>
        /// 触控 UI 真的落在屏上、且摇杆保留了模拟量（2026-10-17 补的验收缺口）。
        ///
        /// 本用例是**表征测试**：预期直接通过 —— 守的是「以后别坏」，不是「现在坏了要修」。
        /// 现有断言只到 `MobileUiRoot.activeSelf`，而「激活」不等于「可见可点」：节点可以
        /// 0 尺寸、在屏外、或被别的 raycast 目标盖住（T6 当年只有程序化探针，没有这条）。
        /// 显式档位而非 Auto：与本文件既有约定一致，不依赖宿主有没有触摸屏。
        /// </summary>
        [UnityTest]
        public IEnumerator test_touch_ui_lands_on_screen_and_joystick_stays_analog()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;   // DemoDay 可能起网络，容忍其报错
            MobileInputBinder binder = null;
            try
            {
                SceneManager.LoadScene("DemoDay");
                yield return null;
                yield return null;

                var host = FindRoot("MobileInput");
                Assert.IsNotNull(host, "场景里应有常驻激活的 MobileInput 宿主");
                binder = host.GetComponent<MobileInputBinder>();
                Assert.IsNotNull(binder, "MobileInput 上应有 MobileInputBinder");
                binder.TouchOverride = TouchOverride.ForceTouch;

                GameObject mobileRoot = null;
                for (int i = 0; i < 600; i++)
                {
                    mobileRoot = FindRoot("MobileUiRoot");
                    if (mobileRoot != null && mobileRoot.activeSelf) break;
                    yield return null;
                }
                Assert.IsNotNull(mobileRoot, "场景里应有 MobileUiRoot");
                Assert.IsTrue(mobileRoot.activeSelf, "ForceTouch 后移动端 UI 根应激活");

                var joystick = mobileRoot.GetComponentInChildren<VirtualJoystick>(true);
                Assert.IsNotNull(joystick, "MobileUiRoot 下应有 VirtualJoystick");
                var joyRect = (RectTransform)joystick.transform;

                // ① 几何：摇杆完整在屏内，中心在左下象限（验收清单 3.2「左下摇杆」）
                var corners = new Vector3[4];
                joyRect.GetWorldCorners(corners);
                foreach (var c in corners)
                    Assert.IsTrue(c.x >= 0f && c.x <= Screen.width && c.y >= 0f && c.y <= Screen.height,
                        "摇杆必须完整落在屏幕内，实际角点 " + c + "（屏 " + Screen.width + "x" + Screen.height + "）");
                var center = new Vector2((corners[0].x + corners[2].x) * 0.5f,
                                         (corners[0].y + corners[2].y) * 0.5f);
                Assert.IsTrue(center.x < Screen.width * 0.5f, "摇杆中心必须在左半屏");
                Assert.IsTrue(center.y < Screen.height * 0.5f, "摇杆中心必须在下半屏");

                // ② 射线：摇杆中心必须命中摇杆自身（activeSelf==true 保证不了可点）
                var eventSystem = EventSystem.current;
                Assert.IsNotNull(eventSystem, "应有 EventSystem（UIBootstrap 在 AfterSceneLoad 建）");
                var hits = new List<RaycastResult>();
                eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = center }, hits);
                bool hitsJoystick = false;
                foreach (var hit in hits)
                    if (hit.gameObject == joystick.gameObject ||
                        hit.gameObject.transform.IsChildOf(joystick.transform)) { hitsJoystick = true; break; }
                Assert.IsTrue(hitsJoystick, "摇杆中心必须能命中摇杆自身（屏幕上真的可点）");

                // ③ 模拟量：小位移 < 大位移（半推=慢走、满推=快走；只断大小关系，不断言饱和）
                float small = DragAndGetMagnitude(joystick, center, 20f);
                float big = DragAndGetMagnitude(joystick, center, 90f);
                Assert.IsTrue(small > 0f, "小位移拖拽后 Value 必须非零 —— 模拟量没被丢弃");
                Assert.IsTrue(small < big, "小位移的 Value 必须小于大位移，实际 " + small + " vs " + big);
                Assert.IsTrue(big <= 1.0001f, "Value 必须被钳在 0–1，实际 " + big);

                // ④ 抬指：立刻归零（松手立刻停）
                var up = new PointerEventData(eventSystem) { position = center + new Vector2(90f, 0f) };
                ExecuteEvents.Execute(joystick.gameObject, up, ExecuteEvents.pointerUpHandler);
                Assert.AreEqual(Vector2.zero, joystick.Value, "pointerUp 后 Value 必须归零");

                // ⑤ 三按钮：各在屏内，且中心射线命中自身（不被 LookArea 盖住）
                int buttonsChecked = 0;
                for (int i = 0; i < mobileRoot.transform.childCount; i++)
                {
                    var child = mobileRoot.transform.GetChild(i);
                    if (!child.name.EndsWith("Button")) continue;
                    buttonsChecked++;
                    var rect = (RectTransform)child;
                    var cs = new Vector3[4];
                    rect.GetWorldCorners(cs);
                    foreach (var c in cs)
                        Assert.IsTrue(c.x >= 0f && c.x <= Screen.width && c.y >= 0f && c.y <= Screen.height,
                            child.name + " 必须完整落在屏幕内");
                    var bc = new Vector2((cs[0].x + cs[2].x) * 0.5f, (cs[0].y + cs[2].y) * 0.5f);
                    var bhits = new List<RaycastResult>();
                    eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = bc }, bhits);
                    Assert.IsTrue(bhits.Count > 0 && bhits[0].gameObject == child.gameObject,
                        child.name + " 中心射线的首位命中必须是按钮自身（被观察区盖住就是不可点）");
                }
                Assert.AreEqual(3, buttonsChecked, "应有三个动作按钮（AttackButton/DefendButton/JumpButton）");

                // 收尾：清档位 + 卸载场景（与既有用例同一套清理）
                binder.TouchOverride = TouchOverride.Auto;
                yield return null;
                SceneManager.SetActiveScene(SceneManager.CreateScene("T7CleanupScene"));
                var unload = SceneManager.UnloadSceneAsync("DemoDay");
                while (unload != null && !unload.isDone) yield return null;
            }
            finally
            {
                if (binder != null) binder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }

        /// <summary>合成一次 pointerDown + drag，返回摇杆 Value 的模长；最后合成 pointerUp 复位。</summary>
        private static float DragAndGetMagnitude(VirtualJoystick joystick, Vector2 start, float dx)
        {
            var es = EventSystem.current;
            var down = new PointerEventData(es) { position = start };
            ExecuteEvents.Execute(joystick.gameObject, down, ExecuteEvents.pointerDownHandler);
            var drag = new PointerEventData(es) { position = start + new Vector2(dx, 0f) };
            ExecuteEvents.Execute(joystick.gameObject, drag, ExecuteEvents.dragHandler);
            float magnitude = joystick.Value.magnitude;
            var up = new PointerEventData(es) { position = drag.position };
            ExecuteEvents.Execute(joystick.gameObject, up, ExecuteEvents.pointerUpHandler);
            return magnitude;
        }
```

顶部 using 需补齐（现有文件**没有**这三条，缺一条就编译不过）：

```csharp
using System.Collections.Generic;   // List<RaycastResult>
using UnityEngine.EventSystems;     // PointerEventData / ExecuteEvents / RaycastResult
using Hotfix.GameSystems.UI;        // VirtualJoystick
```

（`MobileInputBinder` / `TouchOverride` 在已引用的 `Hotfix.GameSystems.Sys3C.Input` 里，不用加。）

- [ ] **步骤 2：更新本文件的两处注释**

1. 类注释（约第 18-19 行）：
   - 原：``/// `Application.isMobilePlatform` 在编辑器恒 false，所以这条分支此前**按构造测不到** ——``
   - 改：``/// 编辑器现在**默认**就进触屏分支（2026-10-17 产品决定），本文件的断言仍建立在``
     ``/// **显式档位**上，不依赖 `Auto` 的结果。``
2. 清理段注释（约第 114-117 行）：
   - 原：「本用例是 Tests/ 下**唯一** LoadScene 的用例……」
   - 改：「本文件的两条用例**各自** `LoadScene("DemoDay")` 并各自清理……」

- [ ] **步骤 3：运行用例（预期绿）**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"MobileTouchInputTests"}' --timeout 420000
```

预期：2/2 PASS（既有 1 条 + 新增 1 条）。若几何断言红：先确认 Game View 尺寸非零、`MobileUiRoot` 已激活；**不要**为了让测试通过去改断言。

- [ ] **步骤 4：变异检查（证明断言有牙齿，然后还原）**

临时把 `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs` 的 `OnDrag` 里
`_value = clamped / _radius;` 改成 `_value = clamped.normalized;`（这正是「半推=满推」的历史缺陷），
重跑步骤 3 的命令：**断言「小位移的 Value 必须小于大位移」必须变红**。

确认变红后，**立即还原**该文件（`git checkout -- Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs`），
再跑一遍确认回到绿。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs
git commit -m "test(mobile): 触控 UI 上屏/可点/模拟量/抬指/三按钮的断言"
```

提交信息正文需写明：这是**表征测试**（预期直接绿，守的是以后别坏）；补的缺口是「`activeSelf==true` ≠ 可见可点」；变异检查（把 `_value` 归一化后断言变红）证明有牙齿；显式档位、不依赖 `Auto`。

---

## 任务 4：验收留档 + 全量回归

**文件：**
- 产出（不提交）：`production/verify/hud_joystick_acceptance.png`
- 无代码改动

- [ ] **步骤 1：编辑器内验收（编辑器默认触屏后应当开箱可见）**

1. 打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`，进 PlayMode。
2. 用 MCP 取 Game View 截图：

```bash
unity-mcp-cli run-tool screenshot-game-view --path "E:/CodeForJob/Cool" --timeout 90000 > /tmp/shot.json
```

3. 解码落盘（`screenshot-game-view` 返回 base64）：

```bash
python -c "
import re,base64
txt=open(r'/tmp/shot.json',encoding='utf-8').read()
m=re.search(r'\"data\":\s*\"([A-Za-z0-9+/=]+)\"', txt)
open(r'production/verify/hud_joystick_acceptance.png','wb').write(base64.b64decode(m.group(1)))
"
```

4. 用 Read 工具看这张图，确认**三件事同时成立**：左上角 HUD（名字/等级/血蓝条）、左下摇杆、右下三个按钮。
5. 用探针确认输入通道已切到 Joystick（编辑器默认触屏）：

```bash
unity-mcp-cli run-tool script-execute --path "E:/CodeForJob/Cool" --input '{"isMethodBody":true,"csharpCode":"var entry = UnityEngine.Object.FindObjectOfType<Hotfix.GameSystems.Sys3C.Sys3CEntry>(); var im = entry != null ? entry.GetComponent<Hotfix.GameSystems.Sys3C.Input.InputManager>() : null; var f = im != null ? typeof(Hotfix.GameSystems.Sys3C.Input.InputManager).GetField(\"_adapter\", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance) : null; UnityEngine.Debug.Log(\"[ACC] adapter=\" + (f != null && f.GetValue(im) != null ? ((Hotfix.GameSystems.Sys3C.Input.IInputAdapter)f.GetValue(im)).AdapterName : \"NONE\"));"}' --timeout 60000
```

再读日志：预期 `[ACC] adapter=Joystick`。

6. 退出 PlayMode。

- [ ] **步骤 2：全量 EditMode**

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" --input '{"testMode":"EditMode"}' --timeout 240000
```

预期：0 失败（基线 399 + 任务 2 的 1 条 = 400）。

- [ ] **步骤 3：PlayMode 回归**

先确认服务器的启动参数（`--bots N` / `--monsters N` / 干净）与要跑的套件匹配（见 CLAUDE.md 与 `docs/superpowers/guides/` 的记录）。**非本任务启动的服务器不要擅自重启。**

- 若服务器模式已知且匹配 → 全量：`--input '{"testMode":"PlayMode"}' --timeout 600000`
- 若不确定 → 只跑受影响的类（它们不依赖服务器）：

```bash
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"HudLifecycleTests"}' --timeout 420000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"MobileTouchInputTests"}' --timeout 420000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"PlayerHudBindingTests"}' --timeout 420000
unity-mcp-cli run-tool tests-run --path "E:/CodeForJob/Cool" \
  --input '{"testMode":"PlayMode","testClass":"UIBootstrapTests"}' --timeout 420000
```

预期：全 PASS。把「跑了哪些、没跑哪些、为什么」写进最终汇报——**不要声称跑过没跑的套件**。

- [ ] **步骤 4：（可选）记录验收结论**

若仓库惯例要求留档，把步骤 1 的截图路径、探针读数、测试结果追加到 `production/verify/` 下的一份短记录（不提交，与既有 verify 产物一致）。

---

## 自检（写计划后已执行）

**1. 规格覆盖度：**

| 规格条目 | 对应任务 |
|---|---|
| §1.4 HUD 判据（`HasOtherStartedLocalEntry`、`enabled` + `_statsProvider` 双层过滤） | 任务 1 步骤 3 |
| §1.5 两条回归守卫（装置/等待配方/private/顺序） | 任务 1 步骤 1-2 |
| §2.2 摇杆四组断言 + 三按钮 + 不过度声称的边界 | 任务 3 |
| §3.1 `Resolve` 编辑器分支 + 代价说明 | 任务 2 步骤 3 |
| §3.2 同批 7 处话术/注释 | 任务 2 步骤 3（5 处）+ 任务 3 步骤 2（2 处） |
| §3.3 决策钉 `test_editor_auto_resolves_to_touch` | 任务 2 步骤 1 |
| §4 验收判据 1-2 | 任务 1 步骤 4 |
| §4 验收判据 3 | 任务 2 步骤 4 |
| §4 验收判据 4 | 任务 3 步骤 3（+ 步骤 4 变异检查） |
| §4 验收判据 5（手工/截图） | 任务 4 步骤 1 |
| §4 验收判据 6（全量回归） | 任务 4 步骤 2-3 |
| §5 明确不做（外观/F9/阶段 3a 等） | 无任务（正确：不做） |

**2. 占位符扫描：** 无「待定/TODO/后续实现」；每个代码步骤都给了可直接落盘的代码；文档步骤给了逐条原文与改后文字。

**3. 类型与命名一致性：**
- `HasOtherStartedLocalEntry()`（任务 1 定义，仅本文件使用）—— 全部任务内的引用一致。
- `PlayerHudPanel.IsVisible` / `CanvasGroup`（公开属性）、`Sys3CEntry.StatsProvider`（公开属性）、
  `MobileInputBinder.TouchOverride`、`VirtualJoystick.Value` —— 均为现有公开成员，已核对。
- 测试类名/方法名在三处（任务 1、任务 2、任务 3）与规格 §4 逐字一致。
- `HudLifecycleTests` 的命名空间 `GameSys.PlayModeTests` 与同目录 `PlayerHudBindingTests` 一致。
