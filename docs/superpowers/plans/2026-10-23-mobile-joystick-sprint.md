# 移动端摇杆冲刺实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 手机端不新增按钮，用“摇杆推满并保持 0.2s”触发冲刺（固定 8 m/s），回落到 0.8 以下或松手退出；力竭后必须释放一次才能再次冲刺。

**架构：** 纯 C# `SprintGestureState` 负责阈值/迟滞/保持时间的状态机；`VirtualJoystick` 每帧 Tick 并暴露 `IsSprintHeld`；`MobileInputBinder` 把它汇入现有 `InputManager.SetSprintHeld`；`InputManager.GetMoveCommand` 负责力竭锁存与冲刺方向归一化；`Sys3CEntry` 的两条复活路径清锁存。精力扣减、FSM、服务端均不改。

**技术栈：** Unity 2022.3.25f1、C#、NUnit/Unity Test Framework、UGUI、Unity MCP（`tools/unity-tests.sh` 封装）。

**规格：** `docs/superpowers/specs/2026-10-23-mobile-joystick-sprint-design.md`

---

## 执行前准备

- [ ] 确认工作区。当前 `master` 已有大量未提交改动（NuGet DLL、字体、`CharacterController.cs`、现有测试等）。**不要 `git add -A`**，每个任务只 `git add` 本任务列出的文件。
- [ ] 打开 Unity Editor（项目 `E:/CodeForJob/Cool`），等编译完成、MCP 连接可用。若脚本编译触发域重载后 MCP 工具消失，等编辑器刷新完再重试。
- [ ] 运行测试前保存所有打开的场景（`tests-run` 的前置条件：有未保存场景会直接报错）。
- [ ] 记录基线（改动前跑一次，用于最后判断“无新增失败”）：

```bash
cd E:/CodeForJob/Cool
bash tools/unity-tests.sh EditMode MobileInputAdapterTests
bash tools/unity-tests.sh EditMode MobileInputManagerUiHooksTests
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
bash tools/unity-tests.sh PlayMode StaminaIntegrationTests
```

- [ ] 新建 `.cs` 文件后等 Unity 刷新生成同名 `.meta`；提交时 `.cs` 与 `.meta` 一起 `git add`。

---

## 文件结构

| 文件 | 动作 | 职责 |
|---|---|---|
| `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/SprintGestureState.cs` | 创建 | 满推 + 保持 + 迟滞的纯 C# 状态机 |
| `Assets/Scripts/Tests/Editor/Sys3C/SprintGestureStateTests.cs` | 创建 | 状态机 EditMode 单测 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs` | 修改 | 力竭锁存、复活清锁存方法、冲刺方向归一化 |
| `Assets/Scripts/Tests/Editor/Sys3C/SprintCommandTests.cs` | 创建 | 锁存 / 归一化 / 走路模拟量保持 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs` | 修改 | 每帧 Tick 手势、`IsSprintHeld`、可选变色、重置 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs` | 修改 | 汇入 `SetSprintHeld`、切键鼠/停用清状态 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 修改 | 两条复活路径清冲刺锁存 |
| `Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs` | 修改 | 摇杆手势 → binder → InputManager 的真链路 |
| `Assets/Scripts/Tests/PlayMode/StaminaIntegrationTests.cs` | 修改 | 复活清锁存的集成回归 |
| `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` | 修改 | 把 Handle 的 Image 指给 `_sprintIndicator` |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md` | 修改 | 输入表补手机冲刺说明 |
| `docs/superpowers/specs/2026-10-18-stamina-system-design.md` | 修改 | 加 2026-10-23 力竭锁存修订 |
| `docs/superpowers/guides/webgl-mobile-acceptance.md` | 修改 | 触屏验收项改为“满推 0.2s = 冲刺” |

---

## 任务 1：SprintGestureState（纯状态机）

**文件：**
- 创建：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/SprintGestureState.cs`
- 创建：`Assets/Scripts/Tests/Editor/Sys3C/SprintGestureStateTests.cs`

### 步骤 1：写失败测试 + 临时 stub

先创建临时 stub（让项目能编译，测试在运行时变红；步骤 3 会整文件替换）：

```csharp
// Assets/Scripts/Hotfix/GameSystems/UI/Mobile/SprintGestureState.cs（临时 stub）
namespace Hotfix.GameSystems.UI
{
    public sealed class SprintGestureState
    {
        public SprintGestureState(float enterThreshold = 0.9f, float exitThreshold = 0.8f,
            float holdSeconds = 0.2f) { }

        public bool IsSprinting => false;

        public bool Tick(float magnitude, float deltaTime) => false;

        public void Reset() { }
    }
}
```

创建测试：

```csharp
// Assets/Scripts/Tests/Editor/Sys3C/SprintGestureStateTests.cs
using Hotfix.GameSystems.UI;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class SprintGestureStateTests
    {
        [Test]
        public void test_sprint_gesture_below_enter_threshold_stays_walking()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsFalse(state.Tick(0.89f, 1f));
        }

        [Test]
        public void test_sprint_gesture_requires_hold_time()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsFalse(state.Tick(0.95f, 0.19f));
            Assert.IsFalse(state.IsSprinting);
            Assert.IsTrue(state.Tick(0.95f, 0.02f));
        }

        [Test]
        public void test_sprint_gesture_enters_after_hold_and_keeps_through_hysteresis()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsTrue(state.Tick(0.95f, 0.2f));
            Assert.IsTrue(state.Tick(0.85f, 0.016f));
        }

        [Test]
        public void test_sprint_gesture_exits_at_exit_threshold()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);
            state.Tick(0.95f, 0.2f);

            Assert.IsFalse(state.Tick(0.8f, 0.016f));
        }

        [Test]
        public void test_sprint_gesture_below_enter_resets_hold_timer()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsFalse(state.Tick(0.95f, 0.15f));
            Assert.IsFalse(state.Tick(0.85f, 0f));   // 跌破进入阈值，计时归零
            Assert.IsFalse(state.Tick(0.95f, 0.1f)); // 需要重新累计满 0.2s
            Assert.IsTrue(state.Tick(0.95f, 0.1f));
        }

        [Test]
        public void test_sprint_gesture_reset_clears_state()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);
            state.Tick(0.95f, 0.2f);

            state.Reset();

            Assert.IsFalse(state.IsSprinting);
            Assert.IsFalse(state.Tick(0.95f, 0.19f));
        }

        [Test]
        public void test_sprint_gesture_ignores_non_finite_inputs()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsFalse(state.Tick(float.NaN, 1f));
            Assert.IsFalse(state.Tick(0.95f, float.NaN));
            Assert.IsFalse(state.Tick(float.PositiveInfinity, 1f));
        }
    }
}
```

### 步骤 2：运行测试验证失败

运行：

```bash
cd E:/CodeForJob/Cool
bash tools/unity-tests.sh EditMode SprintGestureStateTests
```

预期：编译通过；至少 `test_sprint_gesture_requires_hold_time`、`test_sprint_gesture_enters_after_hold_and_keeps_through_hysteresis`、`test_sprint_gesture_exits_at_exit_threshold`、`test_sprint_gesture_below_enter_resets_hold_timer`、`test_sprint_gesture_reset_clears_state` 失败（stub 永远返回 false）。

### 步骤 3：实现 SprintGestureState

用以下内容**整文件替换** `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/SprintGestureState.cs`：

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 摇杆满推冲刺手势的状态机（纯 C#，无 MonoBehaviour）。
    /// 模长 ≥ enterThreshold 并保持 holdSeconds 秒进入；回落到 ≤ exitThreshold 退出。
    /// 迟滞避免手指停在阈值附近时反复开关；保持时间过滤快速甩杆/微调。
    /// </summary>
    public sealed class SprintGestureState
    {
        private readonly float _enterThreshold;
        private readonly float _exitThreshold;
        private readonly float _holdSeconds;

        private bool _held;
        private float _timer;

        public SprintGestureState(float enterThreshold = 0.9f, float exitThreshold = 0.8f,
            float holdSeconds = 0.2f)
        {
            _enterThreshold = IsFinite(enterThreshold)
                ? Mathf.Clamp(enterThreshold, 0.01f, 1f)
                : 0.9f;

            float clampedExit = IsFinite(exitThreshold)
                ? Mathf.Clamp(exitThreshold, 0f, _enterThreshold)
                : 0.8f;
            _exitThreshold = Mathf.Clamp(Mathf.Min(clampedExit, _enterThreshold - 0.01f), 0f, 1f);

            _holdSeconds = IsFinite(holdSeconds) ? Mathf.Max(0f, holdSeconds) : 0.2f;
        }

        public bool IsSprinting => _held;

        /// <summary>每帧调用；返回是否处于冲刺。</summary>
        public bool Tick(float magnitude, float deltaTime)
        {
            if (!IsFinite(magnitude)) magnitude = 0f;
            if (!IsFinite(deltaTime) || deltaTime <= 0f) deltaTime = 0f;

            if (_held)
            {
                if (magnitude <= _exitThreshold)
                {
                    _held = false;
                    _timer = 0f;
                }
                return _held;
            }

            if (magnitude >= _enterThreshold)
            {
                _timer += deltaTime;
                if (_timer >= _holdSeconds)
                {
                    _held = true;
                    _timer = 0f;
                }
            }
            else
            {
                _timer = 0f;
            }

            return _held;
        }

        public void Reset()
        {
            _held = false;
            _timer = 0f;
        }

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
```

### 步骤 4：运行测试验证通过

运行：

```bash
bash tools/unity-tests.sh EditMode SprintGestureStateTests
```

预期：7 个用例全部 PASS。

### 步骤 5：Commit

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Mobile/SprintGestureState.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/Mobile/SprintGestureState.cs.meta \
        Assets/Scripts/Tests/Editor/Sys3C/SprintGestureStateTests.cs \
        Assets/Scripts/Tests/Editor/Sys3C/SprintGestureStateTests.cs.meta
git commit -m "feat(input): 增加摇杆满推冲刺手势状态机"
```

---

## 任务 2：InputManager 力竭锁存 + 冲刺方向归一化

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs:20-29,64-88,93-122,189-192`
- 创建：`Assets/Scripts/Tests/Editor/Sys3C/SprintCommandTests.cs`
- 修改：`docs/superpowers/specs/2026-10-18-stamina-system-design.md`（顶部修订行 + §5.1）

### 步骤 1：写失败测试

创建：

```csharp
// Assets/Scripts/Tests/Editor/Sys3C/SprintCommandTests.cs
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 冲刺命令层：力竭锁存 + 冲刺时方向归一化。
    /// 既有门禁用例 test_input_sprint_gate_respects_allowed（PlayMode）保持不动。
    /// </summary>
    public class SprintCommandTests
    {
        private GameObject _go;
        private InputManager _input;

        private sealed class FakeMoveAdapter : IInputAdapter
        {
            private readonly Vector3 _move;

            public FakeMoveAdapter(Vector3 move) => _move = move;

            public string AdapterName => "Fake";
            public Vector3 GetMoveInput() => _move;
            public Vector2 GetCameraRotationInput() => Vector2.zero;
            public bool HasMoveInput() => _move.sqrMagnitude > 0f;
            public bool HasCameraRotationInput() => false;
        }

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SprintCommandTests");
            _input = _go.AddComponent<InputManager>();
            _input.SetAdapter(new FakeMoveAdapter(Vector3.forward));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_sprint_gate_requires_release_after_exhaustion()
        {
            // Arrange
            _input.SetSprintHeld(true);
            _input.SprintAllowed = true;
            Assert.IsTrue(_input.GetMoveCommand(Vector3.forward).IsSprint);

            // Act & Assert：力竭当帧退出
            _input.SprintAllowed = false;
            Assert.IsFalse(_input.GetMoveCommand(Vector3.forward).IsSprint);

            // 精力恢复但手指/键仍按住 ⇒ 不得自动再冲刺（锁存）
            _input.SprintAllowed = true;
            Assert.IsFalse(_input.GetMoveCommand(Vector3.forward).IsSprint,
                "力竭后仍按住不得自动恢复冲刺");

            // 释放一次清锁存
            _input.SetSprintHeld(false);
            _input.GetMoveCommand(Vector3.forward);

            // 再按 ⇒ 可以冲刺
            _input.SetSprintHeld(true);
            Assert.IsTrue(_input.GetMoveCommand(Vector3.forward).IsSprint,
                "释放再按后应恢复冲刺");
        }

        [Test]
        public void test_sprint_command_normalizes_move_direction()
        {
            // Arrange：半推（0.5）但已触发冲刺
            _input.SetAdapter(new FakeMoveAdapter(new Vector3(0.5f, 0f, 0f)));
            _input.SetSprintHeld(true);
            _input.SprintAllowed = true;

            // Act
            var command = _input.GetMoveCommand(Vector3.forward);

            // Assert
            Assert.IsTrue(command.IsSprint);
            Assert.AreEqual(1f, command.MoveDir.magnitude, 0.001f,
                "冲刺时方向必须归一化，否则半推冲刺比满推走路还慢");
        }

        [Test]
        public void test_walk_command_keeps_analog_magnitude()
        {
            // Arrange：半推、不冲刺
            _input.SetAdapter(new FakeMoveAdapter(new Vector3(0.5f, 0f, 0f)));
            _input.SetSprintHeld(false);
            _input.SprintAllowed = true;

            // Act
            var command = _input.GetMoveCommand(Vector3.forward);

            // Assert
            Assert.IsFalse(command.IsSprint);
            Assert.AreEqual(0.5f, command.MoveDir.magnitude, 0.001f,
                "非冲刺必须保留模拟量，不能被顺手归一化");
        }
    }
}
```

### 步骤 2：运行测试验证失败

```bash
bash tools/unity-tests.sh EditMode SprintCommandTests
```

预期：`test_sprint_gate_requires_release_after_exhaustion` 与 `test_sprint_command_normalizes_move_direction` FAIL；`test_walk_command_keeps_analog_magnitude` PASS（它是防止“把所有移动都归一化”的回归护栏）。

### 步骤 3：修改 InputManager

先把键盘 Shift 的读取从 `IsSprintHeld()` 移到 `Update()` 缓存 —— 这样 EditMode 用例不会因为测试机恰好按着 Shift 而误判；游戏内 `Sys3CEntry.Update` 先调 `_inputManager.Update()` 再 `GetMoveCommand`，行为不变。

在 `Update()` 的 `_attackJustPressed = false;` 之后加：

```csharp
            _sprintHeldFromKeyboard = UnityInput.GetKey(KeyCode.LeftShift);
```

把现有的 `private bool _sprintHeldFromUI;` 替换为下面三行：

```csharp
        private bool _sprintHeldFromUI;
        private bool _sprintHeldFromKeyboard;

        // 力竭锁存：SprintAllowed 变 false 时若仍按住冲刺，必须释放一次才能再次冲刺。
        private bool _sprintBlockedUntilRelease;
```

把 `IsSprintHeld()` 整方法替换为：

```csharp
        public bool IsSprintHeld()
        {
            return _sprintHeldFromUI || _sprintHeldFromKeyboard;
        }
```

把 `SetSprintHeld` 的注释与方法改为：

```csharp
        /// <summary>由移动端摇杆手势 / UI 按键调用（手机端由 MobileInputBinder 每帧汇入）。</summary>
        public void SetSprintHeld(bool held)
        {
            _sprintHeldFromUI = held;
        }

        /// <summary>复活等“满精力重置”场景清掉力竭锁存。</summary>
        public void ResetSprintLatch()
        {
            _sprintBlockedUntilRelease = false;
        }
```

把 `GetMoveCommand` 整方法替换为：

```csharp
        public MoveCommand GetMoveCommand(Vector3 cameraForward)
        {
            Vector3 moveInput = _adapter.GetMoveInput();
            bool rawSprintHeld = IsSprintHeld();

            if (!SprintAllowed && rawSprintHeld)
                _sprintBlockedUntilRelease = true;
            else if (!rawSprintHeld)
                _sprintBlockedUntilRelease = false;

            bool sprintHeld = rawSprintHeld && SprintAllowed && !_sprintBlockedUntilRelease;
            float speed = sprintHeld ? SprintSpeed : MoveSpeed;

            if (moveInput.sqrMagnitude > 0.01f)
            {
                // 将输入从相机视角转换为世界方向
                Vector3 worldMoveDir = ConvertToWorldDirection(moveInput, cameraForward);

                // 冲刺是二元状态：忽略推杆深度。真实速度由 CharacterController.SprintSpeed（8.0f）决定；
                // 不归一化的话，半推冲刺 4 m/s 会比满推走路 5 m/s 还慢。
                if (sprintHeld) worldMoveDir = worldMoveDir.normalized;

                Quaternion targetRotation = Quaternion.LookRotation(worldMoveDir);

                return new MoveCommand
                {
                    MoveDir = worldMoveDir,
                    Speed = speed,
                    Rotation = targetRotation,
                    IsSprint = sprintHeld
                };
            }

            return new MoveCommand
            {
                MoveDir = Vector3.zero,
                Speed = 0f,
                Rotation = Quaternion.identity,
                IsSprint = false
            };
        }
```

### 步骤 4：运行测试验证通过

```bash
bash tools/unity-tests.sh EditMode SprintCommandTests
```

预期：3 个用例全部 PASS。

### 步骤 5：同步文档

在 `docs/superpowers/specs/2026-10-18-stamina-system-design.md` 顶部 `**修订（2026-10-18）：** ...` 之后加一行：

```markdown
**修订（2026-10-23）：** 新增冲刺力竭锁存：`SprintAllowed` 变 false 时若仍按住冲刺，必须释放一次才能再次冲刺（键盘 + 摇杆一致）。见 `2026-10-23-mobile-joystick-sprint-design.md`。
```

把 §5.1 的：

```markdown
  - 精力归零当帧仍可能以冲刺速度移动一帧；下一帧 `SprintAllowed=false`，自动回到走路。
```

改为：

```markdown
  - 精力归零当帧仍可能以冲刺速度移动一帧；下一帧 `SprintAllowed=false` 回到走路。**力竭后仍按住冲刺不会自动恢复**：必须释放一次（2026-10-23 修订）。
```

### 步骤 6：Commit

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs \
        Assets/Scripts/Tests/Editor/Sys3C/SprintCommandTests.cs \
        Assets/Scripts/Tests/Editor/Sys3C/SprintCommandTests.cs.meta \
        docs/superpowers/specs/2026-10-18-stamina-system-design.md
git commit -m "feat(input): 冲刺命令支持力竭锁存与方向归一化"
```

---

## 任务 3：VirtualJoystick + MobileInputBinder + 场景装配 + PlayMode 用例

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs`
- 修改：`Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs`
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md`
- 修改：`docs/superpowers/guides/webgl-mobile-acceptance.md`

### 步骤 1：写失败 PlayMode 用例

在 `Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs` 里、`private static float DragAndGetMagnitude(...)` **之前**插入：

```csharp
        /// <summary>
        /// 2026-10-23：摇杆满推保持 0.2s 必须进入冲刺；松手立即退出。
        /// 独立用例（各自 LoadScene + 清理），不塞进几何用例的叙事。
        /// 迟滞区间（0.85 保持 / 0.79 退出）由 EditMode 的 SprintGestureStateTests 覆盖，
        /// 这里只验“手势 → binder → InputManager”这条真链路。
        /// </summary>
        [UnityTest]
        public IEnumerator test_joystick_full_push_sprints_after_hold_and_release_exits()
        {
            bool prevIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;   // DemoDay 起网络但没有服务器，容忍其报错
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

                // 等“被 binder 绑定为 Joystick”的本地 entry；不要直接取第一个
                // （远端视图上也有一个被禁用的 Sys3CEntry）。
                Sys3CEntry entry = null;
                InputManager input = null;
                for (int i = 0; i < 600; i++)
                {
                    foreach (var candidate in Object.FindObjectsOfType<Sys3CEntry>())
                    {
                        if (candidate == null) continue;
                        if (AdapterNameOf(candidate) != "Joystick") continue;
                        entry = candidate;
                        input = candidate.GetComponent<InputManager>();
                        break;
                    }
                    if (entry != null && input != null) break;
                    yield return null;
                }
                Assert.IsNotNull(entry, "应有一个被绑定为 Joystick 的本地玩家");
                Assert.IsNotNull(input, "本地玩家应有 InputManager");

                var mobileRoot = FindRoot("MobileUiRoot");
                Assert.IsNotNull(mobileRoot, "场景里应有 MobileUiRoot");
                var joystick = mobileRoot.GetComponentInChildren<VirtualJoystick>(true);
                Assert.IsNotNull(joystick, "MobileUiRoot 下应有 VirtualJoystick");

                var eventSystem = EventSystem.current;
                Assert.IsNotNull(eventSystem, "应有 EventSystem");
                var joyRect = (RectTransform)joystick.transform;
                var corners = new Vector3[4];
                joyRect.GetWorldCorners(corners);
                var center = new Vector2((corners[0].x + corners[2].x) * 0.5f,
                                         (corners[0].y + corners[2].y) * 0.5f);

                // 按下并把摇杆推到远超半径的屏幕点：Value 必然钳到 1.0，
                // 不依赖 Canvas.scaleFactor / Game View 尺寸。
                var down = new PointerEventData(eventSystem) { position = center };
                ExecuteEvents.Execute(joystick.gameObject, down, ExecuteEvents.pointerDownHandler);
                var drag = new PointerEventData(eventSystem)
                {
                    position = center + new Vector2(Screen.width, Screen.height)
                };
                ExecuteEvents.Execute(joystick.gameObject, drag, ExecuteEvents.dragHandler);
                Assert.AreEqual(1f, joystick.Value.magnitude, 0.001f,
                    "拖到屏外后 Value 必须钳到 1");

                // 满推保持 0.2s；多等一帧让 binder 的 LateUpdate 汇入 InputManager。
                yield return new WaitForSeconds(0.2f);
                yield return null;

                Assert.IsTrue(input.GetMoveCommand(Vector3.forward).IsSprint,
                    "满推保持 0.2s 后应进入冲刺");

                // 抬手：立即退出冲刺。
                var up = new PointerEventData(eventSystem) { position = drag.position };
                ExecuteEvents.Execute(joystick.gameObject, up, ExecuteEvents.pointerUpHandler);
                yield return null;

                Assert.IsFalse(input.GetMoveCommand(Vector3.forward).IsSprint,
                    "松手后必须退出冲刺");

                // 收尾：清档位 + 卸载场景（与既有用例同一套清理）。
                binder.TouchOverride = TouchOverride.Auto;
                yield return null;
                SceneManager.SetActiveScene(SceneManager.CreateScene("T7SprintCleanupScene"));
                var unload = SceneManager.UnloadSceneAsync("DemoDay");
                while (unload != null && !unload.isDone) yield return null;
            }
            finally
            {
                if (binder != null) binder.TouchOverride = TouchOverride.Auto;
                LogAssert.ignoreFailingMessages = prevIgnore;
            }
        }
```

### 步骤 2：运行测试验证失败

```bash
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
```

预期：既有两条用例 PASS；新用例在“满推保持 0.2s 后应进入冲刺”处 FAIL（`IsSprint` 仍为 false）。

### 步骤 3：修改 VirtualJoystick

用以下内容**整文件替换** `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs`：

```csharp
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 虚拟摇杆。Value 是**归一化前的**方向（模长 0–1），
    /// 由 JoystickInputAdapter 读取 —— 它负责死区，本类只管几何。
    /// 满推冲刺手势也在这里判定，输出 IsSprintHeld。
    /// </summary>
    public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _background;
        [SerializeField] private RectTransform _handle;
        [SerializeField] private float _radius = 80f;

        [Header("Sprint Gesture")]
        [SerializeField] private float _sprintEnterThreshold = 0.9f;
        [SerializeField] private float _sprintExitThreshold = 0.8f;
        [SerializeField] private float _sprintHoldSeconds = 0.2f;
        [SerializeField, Tooltip("可选：冲刺时变色的 Graphic（手柄或外环）；为空则只有逻辑")]
        private Graphic _sprintIndicator;
        [SerializeField] private Color _sprintColor = new Color(1f, 0.6f, 0.1f, 1f);

        private Vector2 _value;
        private SprintGestureState _sprintState;
        private Color _idleColor;
        private bool _sprintVisual;

        public Vector2 Value => _value;
        public bool IsSprintHeld => _sprintState != null && _sprintState.IsSprinting;

        private void Awake()
        {
            _sprintState = new SprintGestureState(
                _sprintEnterThreshold, _sprintExitThreshold, _sprintHoldSeconds);
            if (_sprintIndicator != null) _idleColor = _sprintIndicator.color;
        }

        public void OnPointerDown(PointerEventData e) => OnDrag(e);

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _background, e.position, e.pressEventCamera, out Vector2 local);
            Vector2 clamped = Vector2.ClampMagnitude(local, _radius);
            _handle.anchoredPosition = clamped;
            _value = clamped / _radius;        // 0–1 的模拟量，**不 normalize**
        }

        public void OnPointerUp(PointerEventData e)
        {
            _value = Vector2.zero;
            _handle.anchoredPosition = Vector2.zero;
            ResetSprint();
        }

        /// <summary>
        /// 每帧推进冲刺手势 —— 手指按住不动时不会再有 OnDrag，
        /// 只在事件里计时会永远进不了冲刺。
        /// </summary>
        private void Update()
        {
            if (_sprintState == null) return;
            ApplySprintVisual(_sprintState.Tick(_value.magnitude, Time.deltaTime));
        }

        /// <summary>
        /// 停用/失焦时清空 —— 浏览器抢焦点会让抬指事件丢失，
        /// 此时 OnPointerUp 不会来，Value 会锁存 ⇒ 角色一直跑。
        /// </summary>
        private void OnDisable()
        {
            _value = Vector2.zero;
            // 场景卸载/teardown 时子 RectTransform 可能先被销毁（本条路径可达，故判空）
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
            ResetSprint();
        }

        private void ResetSprint()
        {
            if (_sprintState != null) _sprintState.Reset();
            ApplySprintVisual(false);
        }

        private void ApplySprintVisual(bool sprinting)
        {
            if (_sprintVisual == sprinting) return;
            _sprintVisual = sprinting;
            if (_sprintIndicator != null)
                _sprintIndicator.color = sprinting ? _sprintColor : _idleColor;
        }
    }
}
```

### 步骤 4：修改 MobileInputBinder

在 `Update` 的 `!_touch` 分支里，先清冲刺再换适配器：

```csharp
            if (!_touch)
            {
                // 回退：切到"无触控"必须把适配器还原成键鼠 —— 否则只是把 UI 藏了，
                // Joystick 适配器还挂在玩家身上，而 LateUpdate 也还在喂它。
                if (_inputManager != null)
                {
                    _inputManager.SetSprintHeld(false);
                    _inputManager.SetAdapter(new KeyboardInputAdapter());
                    _inputManager = null;
                }
                return;
            }
```

把 `LateUpdate` 替换为：

```csharp
        private void LateUpdate()
        {
            if (_inputManager == null) return;

            // 摇杆手势不依赖 MobileActionButtons：_buttons 未配置时也要喂。
            _inputManager.SetSprintHeld(_joystick != null && _joystick.IsSprintHeld);

            if (_buttons == null) return;

            _inputManager.SetAttackHeld(_buttons.AttackHeld);
            _inputManager.SetDefendHeld(_buttons.DefendHeld);
            _inputManager.SetJumpPressed(_buttons.JumpPressed);
            _buttons.ConsumeJump();
        }

        /// <summary>
        /// 停用/失焦时清掉 UI 冲刺 —— 否则 InputManager 会保留最后一次 true，
        /// 切回键鼠或场景卸载后角色仍在冲刺。
        /// </summary>
        private void OnDisable()
        {
            if (_inputManager != null) _inputManager.SetSprintHeld(false);
        }
```

### 步骤 5：运行 PlayMode 用例验证通过

```bash
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
```

预期：3 条用例全部 PASS（新增用例的“满推 0.2s → 冲刺；松手 → 退出”通过）。

### 步骤 6：场景装配 `_sprintIndicator`

1. 在 Unity 里打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`，设为活动场景。
2. 确认脚本已编译（`VirtualJoystick` 的 Inspector 出现 `Sprint Gesture` 分组）。
3. 用 MCP `script-execute` 运行以下片段（`usings: ["UnityEngine", "Hotfix.GameSystems.UI"]`）：

```csharp
var joystick = Object.FindObjectOfType<VirtualJoystick>(true);
if (joystick == null) throw new System.Exception("VirtualJoystick not found — open DemoDay first");
var handle = joystick.transform.Find("Background/Handle");
if (handle == null) throw new System.Exception("Background/Handle not found");
var image = handle.GetComponent<UnityEngine.UI.Image>();
if (image == null) throw new System.Exception("Handle has no Image");
var so = new UnityEditor.SerializedObject(joystick);
var prop = so.FindProperty("_sprintIndicator");
if (prop == null) throw new System.Exception("_sprintIndicator not found — wait for compilation");
prop.objectReferenceValue = image;
so.ApplyModifiedProperties();
UnityEditor.EditorUtility.SetDirty(joystick);
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(joystick.gameObject.scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(joystick.gameObject.scene);
"wired: " + image.name
```

预期返回 `wired: Handle`。若 MCP 工具因域重载暂时不可用，等编辑器刷新后重试；也可在 Inspector 里把 `Joystick/Background/Handle` 的 `Image` 手动拖到 `Sprint Indicator` 并保存场景。

4. 读回验证（可选）：

```csharp
var joystick = Object.FindObjectOfType<VirtualJoystick>(true);
var so = new UnityEditor.SerializedObject(joystick);
var prop = so.FindProperty("_sprintIndicator");
prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "NULL"
```

预期：`Handle`。

### 步骤 7：同步文档

`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md:138`，把：

```markdown
| Shift (按住) | IsSprint | 冲刺 |
```

改为：

```markdown
| Shift (按住) / 手机摇杆满推保持 0.2s | IsSprint | 冲刺 |
```

`docs/superpowers/guides/webgl-mobile-acceptance.md` §3.3，把：

```markdown
- [ ] **摇杆半推 = 慢走，满推 = 快走**
      这是"模拟量没被丢弃"的判据。若半推与满推一样快，说明输入被归一化了（重写适配器就是为了修这个）
```

改为：

```markdown
- [ ] **摇杆半推 = 慢走；满推保持约 0.2s = 冲刺（8 m/s）**
      半推/满推的模拟量仍保留（走路速度随推杆深度变化）；冲刺时速度固定 8 m/s 是设计，不是模拟量丢失
- [ ] **冲刺力竭后**：自动回走路；保持推满不会自动再冲刺；回杆再推可恢复
```

文末勾选表把：

```markdown
3.3 半推/满推有区别：☐ 是  ☐ 否
```

改为：

```markdown
3.3 半推/满推有区别：☐ 是  ☐ 否      满推 0.2s 进冲刺：☐ 是  ☐ 否      力竭后回杆再推可恢复：☐ 是  ☐ 否
```

### 步骤 8：跑相关 EditMode 回归

```bash
bash tools/unity-tests.sh EditMode MobileInputAdapterTests
bash tools/unity-tests.sh EditMode MobileInputManagerUiHooksTests
bash tools/unity-tests.sh EditMode SprintGestureStateTests
bash tools/unity-tests.sh EditMode SprintCommandTests
```

预期：全部 PASS。

### 步骤 9：Commit

提交前先 `git diff --stat Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`，确认场景 diff 只有 `_sprintIndicator` 一处（编辑器有时会顺带改序列化顺序）。

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Mobile/VirtualJoystick.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/MobileInputBinder.cs \
        Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs \
        Assets/SimpleLowPolyNature/Scenes/DemoDay.unity \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3C-CurrentDesign.md \
        docs/superpowers/guides/webgl-mobile-acceptance.md
git commit -m "feat(input): 手机摇杆满推触发冲刺"
```

---

## 任务 4：Sys3CEntry 复活清锁存

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:803-816,829-840`
- 修改：`Assets/Scripts/Tests/PlayMode/StaminaIntegrationTests.cs`

### 步骤 1：写失败测试

在 `StaminaIntegrationTests` 的 `test_input_sprint_gate_respects_allowed` 之后插入：

```csharp
        [Test]
        public void test_respawn_clears_sprint_exhaustion_latch()
        {
            // Arrange：按住冲刺，力竭后精力恢复，锁存生效
            _input.SetAdapter(new FakeInputAdapter());
            _input.SetSprintHeld(true);
            _input.SprintAllowed = true;
            Assert.IsTrue(_input.GetMoveCommand(Vector3.forward).IsSprint);

            _input.SprintAllowed = false;
            Assert.IsFalse(_input.GetMoveCommand(Vector3.forward).IsSprint);

            _input.SprintAllowed = true;
            Assert.IsFalse(_input.GetMoveCommand(Vector3.forward).IsSprint,
                "力竭后仍按住不得自动恢复冲刺");

            // Act：服务端复活（与 TickRespawn 共用 Refill + 清锁存语义）
            _entry.ApplyServerRespawn(100f);

            // Assert
            Assert.IsTrue(_input.GetMoveCommand(Vector3.forward).IsSprint,
                "复活后应清冲刺锁存，按住即可再次冲刺");
        }
```

### 步骤 2：运行测试验证失败

```bash
bash tools/unity-tests.sh PlayMode StaminaIntegrationTests
```

预期：新用例在“复活后应清冲刺锁存”处 FAIL（`ApplyServerRespawn` 尚未清锁存）。

### 步骤 3：修改两条复活路径

`ApplyServerRespawn`（`Sys3CEntry.cs:803-816`），在 `_stamina.Refill();` 之后加：

```csharp
            _currentHP = hp;
            _stamina.Refill();
            if (_inputManager != null) _inputManager.ResetSprintLatch();
            _cc.TryExitDefend();
```

`TickRespawn`（`Sys3CEntry.cs:829-840`），在 `_stamina.Refill();` 之后加：

```csharp
            _isDead = false;
            _currentHP = _maxHP;
            _stamina.Refill();
            if (_inputManager != null) _inputManager.ResetSprintLatch();
            _cc.TryExitDefend();
```

### 步骤 4：运行测试验证通过

```bash
bash tools/unity-tests.sh PlayMode StaminaIntegrationTests
```

预期：该类的全部用例 PASS（含新增的复活清锁存用例）。

### 步骤 5：Commit

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/PlayMode/StaminaIntegrationTests.cs
git commit -m "fix(3c): 复活时清除冲刺力竭锁存"
```

---

## 任务 5：全量回归与手工验收

**文件：** 无新增代码；只验证与记录。

### 步骤 1：全量 EditMode

```bash
bash tools/unity-tests.sh EditMode
```

预期：与“执行前准备”的基线相比**无新增失败**。

### 步骤 2：受影响的 PlayMode 类

```bash
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
bash tools/unity-tests.sh PlayMode StaminaIntegrationTests
```

预期：两个类全部 PASS。

### 步骤 3：编辑器手工验收

在 Unity 里 Play `DemoDay`（编辑器默认触屏），确认：

- [ ] 推满保持约 0.2s → 明显加速；摇杆手柄变橙；精力环下降。
- [ ] 推回一半（<0.8）→ 回到走路；精力停止下降。
- [ ] 快速甩杆到满并立即松开 → 不进入冲刺。
- [ ] 力竭 → 自动回走路；保持推满不会自动再冲刺；回杆再推 → 恢复冲刺。
- [ ] 按攻击/防御/跳跃 → 与改动前一致（摇杆手势不影响三键）。
- [ ] 把 `MobileInput.TouchOverride` 拨到 `ForceNoTouch` → 键鼠可用，Shift 冲刺正常。

### 步骤 4：真机验收（可选，按现有清单）

按 `docs/superpowers/guides/webgl-mobile-acceptance.md` 的 §3.3 与文末勾选表执行；本次新增两项：满推 0.2s 进冲刺、力竭后回杆再推可恢复。

---

## 计划自检

- **规格覆盖度：** §3 交互 → 任务 1/3；§3.2 视觉 → 任务 3；§3.3 参数 → 任务 3；§4 核心模型 → 任务 1；§5.1 → 任务 3；§5.2 → 任务 3；§5.3 → 任务 2；§5.4 → 任务 4；§6 速度/精力 → 任务 2/4；§7 边界 → 任务 2/3/4 的代码；§8 测试 → 任务 1/2/3/4；§9 文档 → 任务 2/3；§10/§11 → 不实现（风险与非目标）。
- **占位符扫描：** 无 TODO / 待定 / “类似任务 N”；每个代码步骤都有完整代码。
- **类型一致性：** `SprintGestureState.Tick/Reset/IsSprinting`、`VirtualJoystick.IsSprintHeld`、`InputManager.ResetSprintLatch`、`_sprintBlockedUntilRelease`、`_sprintIndicator` 在各任务中名称一致。
