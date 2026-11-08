# 体验改进实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 实现相机输入归属、移动端观察区手势、UI 缩放、默认联网与随机中文昵称，并落地 16:9–21:9 手机比例的布局修复。

**架构：** 输入层用真实触摸/鼠标来源区分驱动相机限位；UI 缩放通过 `GameSettings` 的有效倍率反算 CanvasScaler 参考分辨率，覆盖 UIManager 层、MobileUiRoot 和 EntityDisplayCanvas；选人面板与移动 HUD 通过固定锚点和数值化测试保证 1.15 倍率下不重叠。

**技术栈：** Unity 2022.3 LTS、C#、UGUI（StandaloneInputModule）、NUnit EditMode/PlayMode tests、旧 Input Manager。

**规格：** [2026-11-09-experience-polish-design.md](../specs/2026-11-09-experience-polish-design.md)

---

## 文件结构

### 新增生产代码

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RandomNicknameGenerator.cs` | 生成 4 字中文随机昵称，可注入 `System.Random` |

### 修改生产代码

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs` | 右键按住才透传鼠标视角，纯函数门控 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/JoystickInputAdapter.cs` | `LastLookFromTouch` 来源标记 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs` | `IsLookFromTouch`；释放门控清来源 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 先读视角输入再设置相机限位模式 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs` | 触屏 `[-10,25]` / 桌面 `[-30,60]` 限位 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs` | 真实触摸分类、单指归属、手势生命周期、DPI 死区 |
| `Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs` | 使用 `ResolveReferenceResolution()` |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs` | 使用 `ResolveReferenceResolution()` |
| `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs` | 默认 Online、随机昵称、空用户名兜底 |
| `Assets/Scripts/AOT/DataDefinition/GameSettings.cs` | `UIScale` 三字段与解析器 |
| `Assets/Scripts/Hotfix/GameSystems/AssemblyInfo.cs` | 增加 `InternalsVisibleTo("KcpNet.PlayModeTests")` |

### 场景/Prefab/资产

| 文件 | 改动 |
|---|---|
| `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` | LookArea 右上象限；8 个按钮右下锚点；MobileUiRoot 加 ScreenAdapter、localScale=1；相机移动端俯仰字段 |
| `Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab` | 选人内容统一底部锚点与 16 单位间距 |
| `Assets/Resources/Setting/GameSettings.asset` | `UIScale: 1`、`MobilePlatformUIScale: 1.15`、`DesktopPlatformUIScale: 1` |

### 新增测试

| 文件 | 内容 |
|---|---|
| `Assets/Scripts/Tests/Editor/Sys3C/KeyboardInputAdapterTests.cs` | 右键门控纯函数 |
| `Assets/Scripts/Tests/Editor/Sys3C/InputManagerLookSourceTests.cs` | `IsLookFromTouch` 来源与门控 |
| `Assets/Scripts/Tests/Editor/Sys3C/ThirdPersonCameraControllerTests.cs` | 移动端/桌面俯仰限位 |
| `Assets/Scripts/Tests/Editor/TouchLookAreaTests.cs` | 指针归属、死区、鼠标隔离 |
| `Assets/Scripts/Tests/Editor/UIScaleResolverTests.cs` | 平台/全局倍率与参考分辨率 |
| `Assets/Scripts/Tests/Editor/UIScaleApplicationTests.cs` | ScreenAdapter 应用有效参考分辨率 |
| `Assets/Scripts/Tests/Editor/MobileHudLayoutFitTests.cs` | CanvasScaler 公式 + CharacterSelect Prefab 数据 |
| `Assets/Scripts/Tests/Editor/SyncLayer/NetworkModeServiceTests.cs` | 默认 Online、昵称持久化 |
| `Assets/Scripts/Tests/Editor/SyncLayer/RandomNicknameGeneratorTests.cs` | 中文 4 字与确定性 |
| `Assets/Scripts/Tests/Editor/CharacterSelectPanelLayoutTests.cs` | Prefab 锚点/尺寸 |
| `Assets/Scripts/Tests/PlayMode/Support/FakeBaseInput.cs` | UGUI 测试输入 |
| `Assets/Scripts/Tests/PlayMode/TouchLookAreaEventSystemTests.cs` | 真实 EventSystem 手势生命周期 |
| `Assets/Scripts/Tests/PlayMode/MobileLookAreaLayoutTests.cs` | DemoDay 实际布局 |
| `Assets/Scripts/Tests/PlayMode/CharacterSelectPanelLayoutFitTests.cs` | 选人几何 + 交互 |

### 修改测试

- `Assets/Scripts/Tests/Editor/MobileInputAdapterTests.cs`：`LastLookFromTouch`。
- `Assets/Scripts/Tests/PlayMode/CharacterSelectPanelReopenTests.cs`：PlayerPrefs 隔离。
- `Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs`：PlayerPrefs 隔离。
- `Assets/Scripts/Tests/PlayMode/MobileSkillHudTests.cs`：PlayerPrefs 隔离。

---

## 任务 1：PC 右键拖拽门控

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs:45-51`
- 测试：`Assets/Scripts/Tests/Editor/Sys3C/KeyboardInputAdapterTests.cs`（新建）

- [ ] **步骤 1：编写失败测试**

```csharp
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class KeyboardInputAdapterTests
    {
        [Test]
        public void test_keyboard_look_is_zero_without_right_button()
        {
            // Arrange & Act
            Vector2 look = KeyboardInputAdapter.GateLookToRightDrag(
                new Vector2(3f, 4f), rightButtonHeld: false);

            // Assert
            Assert.AreEqual(Vector2.zero, look, "未按右键时鼠标移动不得转视角");
        }

        [Test]
        public void test_keyboard_look_passes_through_with_right_button()
        {
            // Arrange & Act
            Vector2 look = KeyboardInputAdapter.GateLookToRightDrag(
                new Vector2(3f, 4f), rightButtonHeld: true);

            // Assert
            Assert.AreEqual(new Vector2(3f, 4f), look);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh EditMode KeyboardInputAdapterTests
```

预期：编译失败，提示 `KeyboardInputAdapter` 不含 `GateLookToRightDrag`。

- [ ] **步骤 3：实现最少代码**

把 `KeyboardInputAdapter.GetCameraRotationInput()` 替换为：

```csharp
public Vector2 GetCameraRotationInput()
{
    float mouseX = UnityEngine.Input.GetAxisRaw("Mouse X");
    float mouseY = UnityEngine.Input.GetAxisRaw("Mouse Y");
    return GateLookToRightDrag(new Vector2(mouseX, mouseY),
        UnityEngine.Input.GetMouseButton(1));
}

internal static Vector2 GateLookToRightDrag(Vector2 look, bool rightButtonHeld)
    => rightButtonHeld ? look : Vector2.zero;
```

`HasCameraRotationInput()` 保持调用 `GetCameraRotationInput()`，自动继承门控。

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
bash tools/unity-tests.sh EditMode KeyboardInputAdapterTests
```

预期：2 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs \
        Assets/Scripts/Tests/Editor/Sys3C/KeyboardInputAdapterTests.cs
git commit -m "feat(input): gate mouse look behind right mouse button"
```

---

## 任务 2：视角来源标记与释放门控

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/JoystickInputAdapter.cs:50-55`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs:250-267`
- 测试：`Assets/Scripts/Tests/Editor/Sys3C/InputManagerLookSourceTests.cs`（新建）
- 修改测试：`Assets/Scripts/Tests/Editor/MobileInputAdapterTests.cs`

- [ ] **步骤 1：编写失败测试**

新建 `InputManagerLookSourceTests.cs`：

```csharp
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class InputManagerLookSourceTests
    {
        private GameObject _go;
        private InputManager _input;

        private sealed class FakeLookAdapter : IInputAdapter
        {
            public Vector2 Look;

            public string AdapterName => "FakeLook";
            public Vector3 GetMoveInput() => Vector3.zero;
            public Vector2 GetCameraRotationInput() => Look;
            public bool HasMoveInput() => false;
            public bool HasCameraRotationInput() => Look.sqrMagnitude > 0f;
        }

        [SetUp]
        public void SetUp()
        {
            InputBlockGate.ClearAll();
            _go = new GameObject("InputManagerLookSourceTests");
            _input = _go.AddComponent<InputManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            InputBlockGate.ClearAll();
        }

        [Test]
        public void test_look_source_reports_touch_when_touch_look_active()
        {
            // Arrange
            _input.SetAdapter(new JoystickInputAdapter(
                () => Vector2.zero, () => new Vector2(3f, 0f)));

            // Act
            Vector2 look = _input.GetCameraRotationInput();

            // Assert
            Assert.AreEqual(new Vector2(3f, 0f), look);
            Assert.IsTrue(_input.IsLookFromTouch);
        }

        [Test]
        public void test_look_source_reports_mouse_when_touch_look_idle()
        {
            // Arrange
            _input.SetAdapter(new JoystickInputAdapter(
                () => Vector2.zero, () => Vector2.zero,
                new FakeLookAdapter { Look = new Vector2(5f, 0f) }));

            // Act
            Vector2 look = _input.GetCameraRotationInput();

            // Assert
            Assert.AreEqual(new Vector2(5f, 0f), look);
            Assert.IsFalse(_input.IsLookFromTouch, "键鼠回退不得使用触屏俯仰限位");
        }

        [Test]
        public void test_blocked_or_release_gated_look_clears_touch_source()
        {
            // Arrange
            Vector2 touchLook = new Vector2(3f, 0f);
            _input.SetAdapter(new JoystickInputAdapter(
                () => Vector2.zero, () => touchLook));
            _input.GetCameraRotationInput();
            Assert.IsTrue(_input.IsLookFromTouch);

            // Act：阻断时立即清来源
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
            Assert.AreEqual(Vector2.zero, _input.GetCameraRotationInput());
            Assert.IsFalse(_input.IsLookFromTouch);

            // 让 InputManager 记录释放锁存
            _input.Update();

            // Act：解除阻断后仍按住的触摸必须先释放
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, false);
            Assert.AreEqual(Vector2.zero, _input.GetCameraRotationInput());
            Assert.IsFalse(_input.IsLookFromTouch);

            // Act：释放后归零
            touchLook = Vector2.zero;
            Assert.AreEqual(Vector2.zero, _input.GetCameraRotationInput());
            Assert.IsFalse(_input.IsLookFromTouch);
        }
    }
}
```

在 `MobileInputAdapterTests.cs` 的三个 look 用例中补断言：

```csharp
[Test]
public void test_look_area_delta_marks_touch_source()
{
    var adapter = new JoystickInputAdapter(
        () => Vector2.zero, () => new Vector2(3f, -2f));

    adapter.GetCameraRotationInput();

    Assert.IsTrue(adapter.LastLookFromTouch);
}

[Test]
public void test_look_falls_back_to_keyboard_when_touch_look_idle()
{
    var fallback = new FakeAdapter(Vector3.zero, new Vector2(5f, -2f));
    var adapter = new JoystickInputAdapter(
        () => Vector2.zero, () => Vector2.zero, fallback);

    Assert.AreEqual(new Vector2(5f, -2f), adapter.GetCameraRotationInput());
    Assert.IsFalse(adapter.LastLookFromTouch);
}
```

（`test_touch_look_wins_over_fallback_when_active` 也补 `Assert.IsTrue(adapter.LastLookFromTouch);`。）

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode InputManagerLookSourceTests
bash tools/unity-tests.sh EditMode MobileInputAdapterTests
```

预期：编译失败，提示缺少 `IsLookFromTouch` / `LastLookFromTouch`。

- [ ] **步骤 3：实现**

`JoystickInputAdapter` 增加属性并替换 `GetCameraRotationInput()`：

```csharp
public bool LastLookFromTouch { get; private set; }

public Vector2 GetCameraRotationInput()
{
    Vector2 look = _lookSource();
    if (look.sqrMagnitude > 0f)
    {
        LastLookFromTouch = true;
        return look;
    }

    LastLookFromTouch = false;
    return _fallback != null ? _fallback.GetCameraRotationInput() : Vector2.zero;
}
```

`InputManager` 增加属性并替换 `GetCameraRotationInput()`：

```csharp
public bool IsLookFromTouch { get; private set; }

public Vector2 GetCameraRotationInput()
{
    if (InputBlockGate.IsBlocked)
    {
        IsLookFromTouch = false;
        return Vector2.zero;
    }

    Vector2 look = _adapter.GetCameraRotationInput();
    IsLookFromTouch = _adapter is JoystickInputAdapter joystick
        && joystick.LastLookFromTouch;

    if (_lookNeedsRelease)
    {
        IsLookFromTouch = false;
        if (look.sqrMagnitude > 0f) return Vector2.zero;
        _lookNeedsRelease = false;
    }

    return look;
}
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode InputManagerLookSourceTests
bash tools/unity-tests.sh EditMode MobileInputAdapterTests
```

预期：全部 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/JoystickInputAdapter.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs \
        Assets/Scripts/Tests/Editor/Sys3C/InputManagerLookSourceTests.cs \
        Assets/Scripts/Tests/Editor/MobileInputAdapterTests.cs
git commit -m "feat(input): track touch look source and clear it on input gates"
```

---

## 任务 3：移动端俯仰限位

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs:22-27,83-92`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:278-282`
- 测试：`Assets/Scripts/Tests/Editor/Sys3C/ThirdPersonCameraControllerTests.cs`（新建）

- [ ] **步骤 1：编写失败测试**

```csharp
using Hotfix.GameSystems.Sys3C.Camera;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class ThirdPersonCameraControllerTests
    {
        [Test]
        public void test_mobile_pitch_limits_clamp_to_minus10_and_25()
        {
            var go = new GameObject("CameraMobilePitch");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.UseMobilePitchLimits = true;

                cam.HandleRotationInput(new Vector2(0f, -1000f));
                Assert.AreEqual(25f, cam.VerticalAngle, 0.001f);

                cam.HandleRotationInput(new Vector2(0f, 1000f));
                Assert.AreEqual(-10f, cam.VerticalAngle, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_desktop_pitch_limits_stay_minus30_and_60()
        {
            var go = new GameObject("CameraDesktopPitch");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.UseMobilePitchLimits = false;

                cam.HandleRotationInput(new Vector2(0f, -1000f));
                Assert.AreEqual(60f, cam.VerticalAngle, 0.001f);

                cam.HandleRotationInput(new Vector2(0f, 1000f));
                Assert.AreEqual(-30f, cam.VerticalAngle, 0.001f);
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

```bash
bash tools/unity-tests.sh EditMode ThirdPersonCameraControllerTests
```

预期：编译失败，提示缺少 `UseMobilePitchLimits` / `VerticalAngle`。

- [ ] **步骤 3：实现**

在 `ThirdPersonCameraController` 的 Rotation Limits 之后增加：

```csharp
[Header("Mobile Pitch Limits")]
[Tooltip("触屏拖拽时的最低俯仰角")]
public float MobileMinPitch = -10f;
[Tooltip("触屏拖拽时的最高俯仰角")]
public float MobileMaxPitch = 25f;

public bool UseMobilePitchLimits { get; set; }

internal float VerticalAngle => _verticalAngle;
```

把 `HandleRotationInput` 的 clamp 改为：

```csharp
public void HandleRotationInput(Vector2 input)
{
    if (input.sqrMagnitude < 0.001f) return;

    _horizontalAngle += input.x * MouseSensitivityX;
    _verticalAngle -= input.y * MouseSensitivityY;

    float minPitch = UseMobilePitchLimits
        ? Mathf.Min(MobileMinPitch, MobileMaxPitch)
        : MinPitch;
    float maxPitch = UseMobilePitchLimits
        ? Mathf.Max(MobileMinPitch, MobileMaxPitch)
        : MaxPitch;
    _verticalAngle = Mathf.Clamp(_verticalAngle, minPitch, maxPitch);
}
```

把 `Sys3CEntry.Update` 中的相机接线改为：

```csharp
if (_camera != null)
{
    Vector2 lookInput = _inputManager.GetCameraRotationInput();
    _camera.UseMobilePitchLimits = _inputManager.IsLookFromTouch;
    _camera.HandleRotationInput(lookInput);
}
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode ThirdPersonCameraControllerTests
```

预期：2 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/Editor/Sys3C/ThirdPersonCameraControllerTests.cs
git commit -m "feat(camera): use mobile pitch limits for touch look"
```

---

## 任务 4：TouchLookArea 手势生命周期与死区

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs`（整体替换）
- 测试：`Assets/Scripts/Tests/Editor/TouchLookAreaTests.cs`（新建）

- [ ] **步骤 1：编写失败测试**

```csharp
using System.Reflection;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameSys.EditorTests
{
    public class TouchLookAreaTests
    {
        private GameObject _go;
        private TouchLookArea _area;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TouchLookAreaTests");
            _area = _go.AddComponent<TouchLookArea>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private static PointerEventData Pointer(int id, Vector2 position)
            => new PointerEventData(null)
            {
                pointerId = id,
                button = PointerEventData.InputButton.Left,
                position = position,
            };

        private static void Publish(TouchLookArea area)
            => typeof(TouchLookArea)
                .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(area, null);

        private static float Threshold()
            => TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

        [Test]
        public void test_drag_below_threshold_does_not_emit()
        {
            var press = new Vector2(100f, 100f);
            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(Threshold() - 1f, 0f)));
            Publish(_area);

            Assert.AreEqual(Vector2.zero, _area.Delta);
        }

        [Test]
        public void test_drag_above_threshold_emits_accumulated_delta()
        {
            var press = new Vector2(100f, 100f);
            float threshold = Threshold();
            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold, 0f)));
            Publish(_area);

            Assert.AreEqual(threshold * 0.2f, _area.Delta.x, 0.001f);
        }

        [Test]
        public void test_pointer_up_resets_dead_zone()
        {
            var press = new Vector2(100f, 100f);
            float threshold = Threshold();

            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold, 0f)));
            Publish(_area);
            _area.OnPointerUp(Pointer(0, press));

            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold - 1f, 0f)));
            Publish(_area);

            Assert.AreEqual(Vector2.zero, _area.Delta);
        }

        [Test]
        public void test_mouse_pointers_do_not_change_touch_gesture()
        {
            var press = new Vector2(100f, 100f);
            float threshold = Threshold();

            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold, 0f)));
            Publish(_area);
            Vector2 before = _area.Delta;

            _area.OnDrag(Pointer(-1, press + new Vector2(threshold * 10f, 0f)));
            _area.OnPointerUp(Pointer(-1, press));
            Publish(_area);

            Assert.AreEqual(before, _area.Delta);
        }

        [Test]
        public void test_only_active_touch_pointer_can_drag_or_release()
        {
            var press = new Vector2(100f, 100f);
            float threshold = Threshold();

            _area.OnPointerDown(Pointer(0, press));
            _area.OnPointerDown(Pointer(1, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold, 0f)));
            Publish(_area);
            Vector2 before = _area.Delta;

            _area.OnDrag(Pointer(1, press + new Vector2(threshold * 10f, 0f)));
            _area.OnPointerUp(Pointer(1, press));
            Publish(_area);

            Assert.AreEqual(before, _area.Delta);
        }

        [Test]
        public void test_initialize_potential_drag_disables_only_owned_touch_threshold()
        {
            var press = new Vector2(100f, 100f);
            _area.OnPointerDown(Pointer(0, press));

            var touchEvent = Pointer(0, press);
            _area.OnInitializePotentialDrag(touchEvent);
            Assert.IsFalse(touchEvent.useDragThreshold);

            var mouseEvent = Pointer(-1, press);
            _area.OnInitializePotentialDrag(mouseEvent);
            Assert.IsTrue(mouseEvent.useDragThreshold);
        }

        [Test]
        public void test_drag_threshold_scales_with_dpi()
        {
            Assert.AreEqual(12f, TouchLookArea.ResolveDragThresholdPixels(12f, 160f), 0.001f);
            Assert.AreEqual(24f, TouchLookArea.ResolveDragThresholdPixels(12f, 320f), 0.001f);
            Assert.AreEqual(12f, TouchLookArea.ResolveDragThresholdPixels(12f, 0f), 0.001f);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode TouchLookAreaTests
```

预期：编译失败，提示缺少 `ResolveDragThresholdPixels` / 新事件处理。

- [ ] **步骤 3：实现**

把 `TouchLookArea.cs` 替换为：

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 触屏观察区：只接管真实触摸；从按下位置量起始死区，鼠标事件完全忽略。
    /// </summary>
    public class TouchLookArea : MonoBehaviour, IPointerDownHandler,
        IInitializePotentialDragHandler, IDragHandler, IPointerUpHandler, IEndDragHandler
    {
        [SerializeField] private float _sensitivity = 0.2f;
        [Tooltip("起始拖拽死区（160dpi 基准像素数，运行时按 Screen.dpi 换算）")]
        [SerializeField] private float _minDragPixelsAt160Dpi = 12f;

        private Vector2 _pending;
        private Vector2 _delta;
        private int _activePointerId = -1;
        private Vector2 _pressPosition;
        private Vector2 _lastPosition;
        private bool _dragStarted;

        public Vector2 Delta => _delta;

        internal static float ResolveDragThresholdPixels(float minPixelsAt160Dpi, float dpi)
        {
            float safeDpi = dpi > 0f && float.IsFinite(dpi) ? dpi : 160f;
            return minPixelsAt160Dpi * Mathf.Max(1f, safeDpi / 160f);
        }

        private float DragThresholdPixels
            => ResolveDragThresholdPixels(_minDragPixelsAt160Dpi, Screen.dpi);

        internal static bool IsTouchPointer(PointerEventData e)
            => e.pointerId >= 0 && e.button == PointerEventData.InputButton.Left;

        private bool OwnsPointer(PointerEventData e)
            => _activePointerId >= 0 && IsTouchPointer(e)
                && e.pointerId == _activePointerId;

        public void OnPointerDown(PointerEventData e)
        {
            if (!IsTouchPointer(e) || _activePointerId >= 0) return;

            ResetGesture();
            _activePointerId = e.pointerId;
            _pressPosition = e.position;
            _lastPosition = e.position;
        }

        public void OnInitializePotentialDrag(PointerEventData e)
        {
            if (OwnsPointer(e)) e.useDragThreshold = false;
        }

        public void OnDrag(PointerEventData e)
        {
            if (!OwnsPointer(e)) return;

            if (!_dragStarted)
            {
                Vector2 total = e.position - _pressPosition;
                float threshold = DragThresholdPixels;
                if (total.sqrMagnitude < threshold * threshold) return;

                _dragStarted = true;
                _pending += total * _sensitivity;
            }
            else
            {
                _pending += (e.position - _lastPosition) * _sensitivity;
            }

            _lastPosition = e.position;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (OwnsPointer(e)) ResetGesture();
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (OwnsPointer(e)) ResetGesture();
        }

        private void OnDisable() => ResetGesture();

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) ResetGesture();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) ResetGesture();
        }

        private void ResetGesture()
        {
            _pending = Vector2.zero;
            _delta = Vector2.zero;
            _activePointerId = -1;
            _pressPosition = Vector2.zero;
            _lastPosition = Vector2.zero;
            _dragStarted = false;
        }

        /// <summary>
        /// 帧末发布本帧累积的拖拽量并清空。手指停住时 OnDrag 不再触发，
        /// 若无此步 Delta 会保持上一次的值，视角会持续旋转。
        /// </summary>
        private void LateUpdate()
        {
            _delta = _pending;
            _pending = Vector2.zero;
        }
    }
}
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode TouchLookAreaTests
```

预期：7 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs \
        Assets/Scripts/Tests/Editor/TouchLookAreaTests.cs
git commit -m "feat(ui): track touch pointer ownership and dpi dead zone in look area"
```

---

## 任务 5：TouchLookArea EventSystem 集成测试

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/AssemblyInfo.cs`
- 新增：`Assets/Scripts/Tests/PlayMode/Support/FakeBaseInput.cs`
- 新增：`Assets/Scripts/Tests/PlayMode/TouchLookAreaEventSystemTests.cs`

- [ ] **步骤 1：编写失败测试与测试输入**

`FakeBaseInput.cs`：

```csharp
using UnityEngine;
using UnityEngine.EventSystems;

namespace KcpNet.PlayModeTests.Support
{
    public sealed class FakeBaseInput : BaseInput
    {
        public Touch[] Touches = System.Array.Empty<Touch>();
        public bool MousePresent = true;
        public Vector2 MousePosition;

        private readonly bool[] _down = new bool[3];
        private readonly bool[] _held = new bool[3];
        private readonly bool[] _up = new bool[3];

        public override bool mousePresent => MousePresent;
        public override bool touchSupported => true;
        public override int touchCount => Touches.Length;
        public override Touch GetTouch(int index) => Touches[index];
        public override Vector2 mousePosition => MousePosition;
        public override bool GetMouseButtonDown(int button) => _down[button];
        public override bool GetMouseButton(int button) => _held[button];
        public override bool GetMouseButtonUp(int button) => _up[button];

        public void SetTouch(int fingerId, Vector2 position, TouchPhase phase)
            => Touches = new[] { MakeTouch(fingerId, position, phase) };

        public void SetTouches(params Touch[] touches) => Touches = touches;

        public void ClearTouches() => Touches = System.Array.Empty<Touch>();

        public void SetMouse(int button, bool down, bool held, bool up)
        {
            _down[button] = down;
            _held[button] = held;
            _up[button] = up;
        }

        public static Touch MakeTouch(int fingerId, Vector2 position, TouchPhase phase)
            => new Touch
            {
                fingerId = fingerId,
                position = position,
                phase = phase,
                type = TouchType.Direct,
                pressure = 1f,
                maximumPossiblePressure = 1f,
                radius = Vector2.one,
                radiusVariance = Vector2.zero,
                deltaPosition = Vector2.zero,
                deltaTime = 1f / 60f,
                tapCount = 1,
            };
    }
}
```

在 `Assets/Scripts/Hotfix/GameSystems/AssemblyInfo.cs` 末尾增加：

```csharp
[assembly: InternalsVisibleTo("KcpNet.PlayModeTests")]
```

`TouchLookAreaEventSystemTests.cs` 的主要代码：

```csharp
using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.UI;
using KcpNet.PlayModeTests.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    public class TouchLookAreaEventSystemTests
    {
        private EventSystem _eventSystem;
        private StandaloneInputModule _module;
        private BaseInput _previousOverride;
        private FakeBaseInput _input;
        private GameObject _inputGo;
        private GameObject _canvasGo;
        private GameObject _eventSystemGo;
        private TouchLookArea _area;
        private NetworkMode _previousMode;
        private string _previousHost;
        private int _previousPort;
        private string _previousUser;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            _previousHost = NetworkModeService.LoadHost();
            _previousPort = NetworkModeService.LoadPort();
            _previousUser = NetworkModeService.LoadUsername();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            _eventSystem = EventSystem.current;
            if (_eventSystem == null)
            {
                _eventSystemGo = new GameObject("TouchLookAreaEventSystemTests_EventSystem");
                _eventSystem = _eventSystemGo.AddComponent<EventSystem>();
            }

            _module = _eventSystem.GetComponent<StandaloneInputModule>();
            if (_module == null)
                _module = _eventSystem.gameObject.AddComponent<StandaloneInputModule>();

            _previousOverride = _module.inputOverride;
            _inputGo = new GameObject("TouchLookAreaEventSystemTests_Input");
            _input = _inputGo.AddComponent<FakeBaseInput>();
            _module.inputOverride = _input;

            _canvasGo = new GameObject("TouchLookAreaEventSystemTests_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var lookGo = new GameObject("LookArea",
                typeof(RectTransform), typeof(Image), typeof(TouchLookArea));
            lookGo.transform.SetParent(_canvasGo.transform, false);
            var rect = lookGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = lookGo.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            _area = lookGo.GetComponent<TouchLookArea>();

            yield return null;
            Assert.IsTrue(_eventSystem.isFocused, "UGUI 事件测试需要 Game View 焦点");
        }

        [TearDown]
        public void TearDown()
        {
            if (_module != null) _module.inputOverride = _previousOverride;
            if (_inputGo != null) Object.DestroyImmediate(_inputGo);
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            if (_eventSystemGo != null) Object.DestroyImmediate(_eventSystemGo);
            NetworkModeService.Save(_previousMode);
            NetworkModeService.SaveConnection(
                _previousHost, _previousPort.ToString(), _previousUser);
        }

        private Vector2 Center()
        {
            var rect = (RectTransform)_area.transform;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return new Vector2((corners[0].x + corners[2].x) * 0.5f,
                (corners[0].y + corners[2].y) * 0.5f);
        }

        private IEnumerator Touch(int fingerId, Vector2 position, TouchPhase phase)
        {
            _input.SetTouch(fingerId, position, phase);
            yield return null;
        }

        private IEnumerator ReleaseTouch(int fingerId, Vector2 position)
        {
            yield return Touch(fingerId, position, TouchPhase.Ended);
            _input.ClearTouches();
            yield return null;
        }

        private IEnumerator Mouse(Vector2 position, int button, bool down, bool held, bool up)
        {
            _input.ClearTouches();
            _input.MousePosition = position;
            _input.SetMouse(button, down, held, up);
            yield return null;
        }

        private static void SetPrivate(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        [UnityTest]
        public IEnumerator test_touch_dead_zone_starts_at_total_displacement_threshold()
        {
            var center = Center();
            float threshold = TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold - 1f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved);
            Assert.AreEqual(threshold * 0.2f, _area.Delta.x, 0.02f);

            yield return ReleaseTouch(0, center + new Vector2(threshold, 0f));
        }

        [UnityTest]
        public IEnumerator test_touch_dead_zone_can_start_below_global_drag_threshold()
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            SetPrivate(_area, "_minDragPixelsAt160Dpi",
                5f / Mathf.Max(1f, dpi / 160f));
            var center = Center();

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(4f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center + new Vector2(5f, 0f), TouchPhase.Moved);
            Assert.AreEqual(1f, _area.Delta.x, 0.02f);

            yield return ReleaseTouch(0, center + new Vector2(5f, 0f));
        }

        [UnityTest]
        public IEnumerator test_second_touch_gesture_requires_threshold_again()
        {
            var center = Center();
            float threshold = TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);

            yield return ReleaseTouch(0, center + new Vector2(threshold, 0f));
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold - 1f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return ReleaseTouch(0, center + new Vector2(threshold - 1f, 0f));
        }

        [UnityTest]
        public IEnumerator test_touch_cancel_focus_loss_and_disable_clear_gesture()
        {
            var center = Center();
            float threshold = TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);

            yield return Touch(0, center + new Vector2(threshold, 0f), TouchPhase.Canceled);
            _input.ClearTouches();
            yield return null;
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);
            typeof(TouchLookArea)
                .GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_area, new object[] { false });
            yield return null;
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);
            typeof(TouchLookArea)
                .GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_area, new object[] { true });
            yield return null;
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            _area.gameObject.SetActive(false);
            _area.gameObject.SetActive(true);
            yield return null;
            yield return Touch(0, center + new Vector2(threshold * 2f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);
            _input.ClearTouches();
        }

        [UnityTest]
        public IEnumerator test_mouse_drag_over_look_area_never_becomes_touch_look()
        {
            var center = Center();

            for (int button = 0; button < 3; button++)
            {
                yield return Mouse(center, button, down: true, held: true, up: false);
                yield return Mouse(center + new Vector2(100f, 0f), button,
                    down: false, held: true, up: false);
                Assert.AreEqual(0f, _area.Delta.x, 0.001f);

                yield return Mouse(center + new Vector2(100f, 0f), button,
                    down: false, held: false, up: true);
                yield return Mouse(center, button, down: false, held: false, up: false);
            }
        }

        [UnityTest]
        public IEnumerator test_unowned_pointer_release_preserves_active_touch()
        {
            var center = Center();
            float threshold = TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);

            // 第二根手指按下并抬起，不得影响第一根
            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved),
                FakeBaseInput.MakeTouch(1, center, TouchPhase.Began));
            yield return null;
            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved),
                FakeBaseInput.MakeTouch(1, center, TouchPhase.Ended));
            yield return null;

            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold + 10f, 0f),
                    TouchPhase.Moved));
            yield return null;
            Assert.Greater(_area.Delta.x, 0f,
                "第二根手指释放后第一根手指仍应能继续拖拽");

            // 第三根手指在第一根按住时按下；第一根释放后不得自动接棒
            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold + 10f, 0f),
                    TouchPhase.Moved),
                FakeBaseInput.MakeTouch(2, center, TouchPhase.Began));
            yield return null;
            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold + 10f, 0f),
                    TouchPhase.Ended),
                FakeBaseInput.MakeTouch(2, center + new Vector2(threshold, 0f),
                    TouchPhase.Moved));
            yield return null;
            _input.ClearTouches();
            yield return null;
            Assert.AreEqual(0f, _area.Delta.x, 0.001f,
                "第一个触摸释放后，已按住的第二根手指不得自动接棒");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh PlayMode TouchLookAreaEventSystemTests
```

预期：编译失败，提示缺少 `InternalsVisibleTo`/`FakeBaseInput`/新事件处理；修好编译后，若 `TouchLookArea` 仍是旧实现，生命周期用例失败。

- [ ] **步骤 3：实现/接线**

- `AssemblyInfo.cs` 的 `InternalsVisibleTo` 已加。
- `FakeBaseInput` 已加。
- `TouchLookArea` 已在任务 4 实现。
- 不需要生产代码额外改动。

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh PlayMode TouchLookAreaEventSystemTests
```

预期：6 个用例 PASS。若 `EventSystem.isFocused` 断言失败，先聚焦 Game View 再跑。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/AssemblyInfo.cs \
        Assets/Scripts/Tests/PlayMode/Support/FakeBaseInput.cs \
        Assets/Scripts/Tests/PlayMode/TouchLookAreaEventSystemTests.cs
git commit -m "test(ui): verify touch look area gesture lifecycle through EventSystem"
```

---

## 任务 6：UI 缩放解析器

**文件：**
- 修改：`Assets/Scripts/AOT/DataDefinition/GameSettings.cs:17-23`
- 修改：`Assets/Resources/Setting/GameSettings.asset`
- 测试：`Assets/Scripts/Tests/Editor/UIScaleResolverTests.cs`（新建）

- [ ] **步骤 1：编写失败测试**

```csharp
using DataDefinition;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class UIScaleResolverTests
    {
        [Test]
        public void test_platform_scale_webgl_and_mobile_is_115()
        {
            Assert.AreEqual(1.15f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.WebGLPlayer, false, 1.15f, 1f), 0.0001f);
            Assert.AreEqual(1.15f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.Android, true, 1.15f, 1f), 0.0001f);
            Assert.AreEqual(1.15f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.IPhonePlayer, true, 1.15f, 1f), 0.0001f);
        }

        [Test]
        public void test_platform_scale_desktop_and_editor_is_1()
        {
            Assert.AreEqual(1f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.WindowsPlayer, false, 1.15f, 1f), 0.0001f);
            Assert.AreEqual(1f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.OSXPlayer, false, 1.15f, 1f), 0.0001f);
            Assert.AreEqual(1f, GameSettings.ResolvePlatformUIScale(
                RuntimePlatform.WindowsEditor, false, 1.15f, 1f), 0.0001f);
        }

        [Test]
        public void test_global_scale_multiplies_and_clamps()
        {
            Assert.AreEqual(1.38f, GameSettings.ResolveEffectiveUIScale(
                RuntimePlatform.WebGLPlayer, false, 1.15f, 1f, 1.2f), 0.0001f);
            Assert.AreEqual(1.5f, GameSettings.ResolveEffectiveUIScale(
                RuntimePlatform.WebGLPlayer, false, 1.15f, 1f, 2f), 0.0001f);
            Assert.AreEqual(1.15f, GameSettings.ResolveEffectiveUIScale(
                RuntimePlatform.WebGLPlayer, false, 1.15f, 1f, 0f), 0.0001f,
                "global 0 回退为 1.0 后仍乘平台倍率 1.15");
        }

        [Test]
        public void test_reference_resolution_divides_by_effective_scale()
        {
            Vector2 result = GameSettings.ScaleReferenceResolution(
                new Vector2(1920f, 1080f), 1.15f);

            Assert.AreEqual(1669.5652f, result.x, 0.01f);
            Assert.AreEqual(939.1304f, result.y, 0.01f);
        }

        [Test]
        public void test_game_settings_asset_has_ui_scale_defaults()
        {
            var settings = Resources.Load<GameSettings>("Setting/GameSettings");
            Assert.IsNotNull(settings);
            Assert.AreEqual(1f, settings.UIScale, 0.0001f);
            Assert.AreEqual(1.15f, settings.MobilePlatformUIScale, 0.0001f);
            Assert.AreEqual(1f, settings.DesktopPlatformUIScale, 0.0001f);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode UIScaleResolverTests
```

预期：编译失败，提示缺少字段/方法。

- [ ] **步骤 3：实现**

在 `GameSettings` 的 Display 区增加：

```csharp
[Header("UI Scale")]
[Tooltip("UI 全局倍率，叠加在平台默认倍率上")]
[Range(0.8f, 1.5f)]
public float UIScale = 1f;

[Tooltip("移动端/WebGL 平台默认倍率")]
[Min(0.8f)]
public float MobilePlatformUIScale = 1.15f;

[Tooltip("桌面/编辑器平台默认倍率")]
[Min(0.8f)]
public float DesktopPlatformUIScale = 1f;

public float ResolveUIScale()
    => ResolveEffectiveUIScale(Application.platform, Application.isMobilePlatform,
        MobilePlatformUIScale, DesktopPlatformUIScale, UIScale);

public Vector2 ResolveReferenceResolution()
    => ScaleReferenceResolution(ReferenceResolution, ResolveUIScale());

public static float ResolvePlatformUIScale(RuntimePlatform platform, bool isMobilePlatform,
    float mobileScale, float desktopScale)
    => platform == RuntimePlatform.WebGLPlayer || isMobilePlatform
        ? SanitizeScale(mobileScale)
        : SanitizeScale(desktopScale);

public static float ResolveEffectiveUIScale(RuntimePlatform platform, bool isMobilePlatform,
    float mobileScale, float desktopScale, float globalScale)
    => Mathf.Clamp(
        ResolvePlatformUIScale(platform, isMobilePlatform, mobileScale, desktopScale)
        * SanitizeScale(globalScale),
        0.8f, 1.5f);

public static Vector2 ScaleReferenceResolution(Vector2 referenceResolution, float uiScale)
    => referenceResolution / Mathf.Clamp(SanitizeScale(uiScale), 0.8f, 1.5f);

private static float SanitizeScale(float value)
    => float.IsFinite(value) && value > 0f ? value : 1f;
```

在 `Assets/Resources/Setting/GameSettings.asset` 中 `ReferenceResolution` 后写入：

```yaml
  UIScale: 1
  MobilePlatformUIScale: 1.15
  DesktopPlatformUIScale: 1
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode UIScaleResolverTests
```

预期：5 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/AOT/DataDefinition/GameSettings.cs \
        Assets/Resources/Setting/GameSettings.asset \
        Assets/Scripts/Tests/Editor/UIScaleResolverTests.cs
git commit -m "feat(ui): add platform-aware ui scale resolution"
```

---

## 任务 7：把有效倍率接到 Canvas

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs:53-61`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs:62-65`
- 测试：`Assets/Scripts/Tests/Editor/UIScaleApplicationTests.cs`（新建）

- [ ] **步骤 1：编写失败测试**

```csharp
using DataDefinition;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    public class UIScaleApplicationTests
    {
        [Test]
        public void test_screen_adapter_applies_resolved_reference_resolution()
        {
            var go = new GameObject("UIScaleApplicationTests");
            try
            {
                var scaler = go.AddComponent<CanvasScaler>();
                ScreenAdapter.ApplyReferenceResolution(scaler, GameSettings.Instance);

                Vector2 expected = GameSettings.Instance.ResolveReferenceResolution();
                Assert.AreEqual(expected.x, scaler.referenceResolution.x, 0.01f);
                Assert.AreEqual(expected.y, scaler.referenceResolution.y, 0.01f);
                Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
                Assert.AreEqual(0.5f, scaler.matchWidthOrHeight, 0.001f);
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

```bash
bash tools/unity-tests.sh EditMode UIScaleApplicationTests
```

预期：编译失败，提示缺少 `ApplyReferenceResolution`。

- [ ] **步骤 3：实现**

`ScreenAdapter` 增加 internal static 方法，并让 `ConfigureCanvasScaler` 调用：

```csharp
private void ConfigureCanvasScaler()
    => ApplyReferenceResolution(_scaler, DataDefinition.GameSettings.Instance);

internal static void ApplyReferenceResolution(
    CanvasScaler scaler, DataDefinition.GameSettings settings)
{
    if (scaler == null || settings == null) return;

    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = settings.ResolveReferenceResolution();
    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
    scaler.matchWidthOrHeight = 0.5f;
}
```

`EntityDisplayManager.CreateCanvas()` 中把 `scaler.referenceResolution` 改为：

```csharp
scaler.referenceResolution =
    DataDefinition.GameSettings.Instance.ResolveReferenceResolution();
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode UIScaleApplicationTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs \
        Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs \
        Assets/Scripts/Tests/Editor/UIScaleApplicationTests.cs
git commit -m "feat(ui): apply resolved reference resolution to canvases"
```

---

## 任务 8：DemoDay 布局与 MobileUiRoot 缩放

**文件：**
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`
- 测试：`Assets/Scripts/Tests/PlayMode/MobileLookAreaLayoutTests.cs`（新建）

- [ ] **步骤 1：编写失败测试**

```csharp
using System.Collections;
using DataDefinition;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.UI;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KcpNet.PlayModeTests
{
    public class MobileLookAreaLayoutTests
    {
        private NetworkMode _previousMode;
        private string _previousHost;
        private int _previousPort;
        private string _previousUser;
        private bool _previousIgnore;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            _previousHost = NetworkModeService.LoadHost();
            _previousPort = NetworkModeService.LoadPort();
            _previousUser = NetworkModeService.LoadUsername();
            NetworkModeService.Save(NetworkMode.SinglePlayer);
            InputBlockGate.ClearAll();
            _previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;

            SceneManager.LoadScene("DemoDay");
            yield return null;
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            InputBlockGate.ClearAll();
            LogAssert.ignoreFailingMessages = _previousIgnore;
            NetworkModeService.Save(_previousMode);
            NetworkModeService.SaveConnection(
                _previousHost, _previousPort.ToString(), _previousUser);
        }

        private static Transform Find(string name)
        {
            var root = GameObject.Find("MobileUiRoot");
            Assert.IsNotNull(root, "场景里应有 MobileUiRoot");
            var child = root.transform.Find(name);
            Assert.IsNotNull(child, "MobileUiRoot 下缺少 " + name);
            return child;
        }

        [UnityTest]
        public IEnumerator test_mobile_look_area_uses_top_right_quadrant()
        {
            yield return null;

            var rect = (RectTransform)Find("LookArea");
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rect.anchorMin);
            Assert.AreEqual(Vector2.one, rect.anchorMax);

            var image = Find("LookArea").GetComponent<Image>();
            Assert.IsNotNull(image);
            Assert.IsTrue(image.raycastTarget);
            Assert.IsNotNull(Find("LookArea").GetComponent<TouchLookArea>());
        }

        [UnityTest]
        public IEnumerator test_mobile_action_buttons_are_bottom_right_anchored()
        {
            yield return null;

            string[] names =
            {
                "AttackButton", "DefendButton", "JumpButton", "SkillQButton",
                "SkillRButton", "FireballButton", "ChargeBlastButton", "MeteorButton",
            };

            foreach (string name in names)
            {
                var rect = (RectTransform)Find(name);
                Assert.AreEqual(new Vector2(1f, 0f), rect.anchorMin, name);
                Assert.AreEqual(new Vector2(1f, 0f), rect.anchorMax, name);
            }

            string[] allMobileButtons =
            {
                "AttackButton", "DefendButton", "JumpButton", "SkillQButton",
                "SkillRButton", "FireballButton", "ChargeBlastButton", "MeteorButton",
                "TargetSwitchButton", "Joystick",
            };

            foreach (string name in allMobileButtons)
            {
                var rect = (RectTransform)Find(name);
                var corners = new Vector3[4];
                rect.GetWorldCorners(corners);
                foreach (var c in corners)
                {
                    Assert.IsTrue(c.x >= 0f && c.x <= Screen.width
                        && c.y >= 0f && c.y <= Screen.height,
                        name + " 角点超出屏幕: " + c);
                }
            }
        }

        [UnityTest]
        public IEnumerator test_mobile_ui_root_applies_effective_reference_resolution()
        {
            yield return null;

            var root = GameObject.Find("MobileUiRoot");
            Assert.Greater(root.transform.lossyScale.x, 0.0001f,
                "MobileUiRoot 不得是零缩放");

            var adapter = root.GetComponent<ScreenAdapter>();
            Assert.IsNotNull(adapter, "MobileUiRoot 必须挂 ScreenAdapter");
            Assert.IsFalse(adapter.ApplySafeArea, "MobileUiRoot 不启用安全区");

            var scaler = root.GetComponent<CanvasScaler>();
            Assert.IsNotNull(scaler);
            Vector2 expected = GameSettings.Instance.ResolveReferenceResolution();
            Assert.AreEqual(expected.x, scaler.referenceResolution.x, 0.01f);
            Assert.AreEqual(expected.y, scaler.referenceResolution.y, 0.01f);
        }

        [UnityTest]
        public IEnumerator test_mobile_look_area_leaves_blank_drag_region_above_skill_cluster()
        {
            yield return null;

            var canvasRect = GameObject.Find("MobileUiRoot").GetComponent<RectTransform>();
            var meteor = (RectTransform)Find("MeteorButton");
            float meteorTop = meteor.anchoredPosition.y + meteor.rect.height * 0.5f;
            float canvasHeight = canvasRect.rect.height;

            Assert.GreaterOrEqual(canvasHeight - meteorTop, 100f,
                "技能栏上方必须留出可拖拽空白区");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh PlayMode MobileLookAreaLayoutTests
```

预期：`LookArea` anchor、按钮 anchor、MobileUiRoot 缩放等断言失败。

- [ ] **步骤 3：修改场景**

在 Unity Editor（或 MCP）中打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`，按表修改，保存场景：

| 对象 | 字段 | 新值 |
|---|---|---|
| `MobileUiRoot` | `RectTransform.localScale` | `(1,1,1)` |
| `MobileUiRoot` | 新增组件 `ScreenAdapter` | `ApplySafeArea=false`、`AutoRefreshOnResize=true` |
| `LookArea` | `anchorMin` / `anchorMax` | `(0.5,0.5)` / `(1,1)` |
| `AttackButton` | anchor / anchoredPosition | `(1,0)` / `(-220,196)` |
| `DefendButton` | anchor / anchoredPosition | `(1,0)` / `(-350,150)` |
| `JumpButton` | anchor / anchoredPosition | `(1,0)` / `(-340,246)` |
| `SkillQButton` | anchor / anchoredPosition | `(1,0)` / `(-287,333)` |
| `SkillRButton` | anchor / anchoredPosition | `(1,0)` / `(-180,320)` |
| `FireballButton` | anchor / anchoredPosition | `(1,0)` / `(-440,240)` |
| `ChargeBlastButton` | anchor / anchoredPosition | `(1,0)` / `(-375,370)` |
| `MeteorButton` | anchor / anchoredPosition | `(1,0)` / `(-225,416)` |
| 场景相机 `ThirdPersonCameraController` | `MobileMinPitch` / `MobileMaxPitch` | `-10` / `25` |

注意：
- 不要改 `LookArea` 的 `anchoredPosition`/`sizeDelta`（保持 0）和 `Image` 的 `raycastTarget=1`。
- `TargetSwitchButton` 与 `Joystick` 已正确锚定，不动。
- 使用 Unity MCP `rql-mutate` / `gameobject-component-add` 或 Inspector；不要手改场景 YAML。保存后回读 `MobileUiRoot` 的 `lossyScale` 和 `CanvasScaler.referenceResolution`。

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh PlayMode MobileLookAreaLayoutTests
```

预期：4 个用例 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity \
        Assets/Scripts/Tests/PlayMode/MobileLookAreaLayoutTests.cs
git commit -m "feat(ui): move look area to top-right and scale mobile hud"
```

---

## 任务 9：选人面板 Prefab 底部锚点

**文件：**
- 修改：`Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab`
- 测试：`Assets/Scripts/Tests/Editor/CharacterSelectPanelLayoutTests.cs`（新建）

- [ ] **步骤 1：编写失败测试**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class CharacterSelectPanelLayoutTests
    {
        [Test]
        public void test_character_select_content_uses_shared_bottom_anchors()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
            Assert.IsNotNull(prefab);
            var root = Object.Instantiate(prefab);
            try
            {
                AssertBottom(root, "ServerSettings", 105f, new Vector2(640f, 132f));
                AssertBottom(root, "ConfirmButton", 222f, new Vector2(220f, 70f));
                AssertBottom(root, "SoloButton", 303f, new Vector2(200f, 60f), 130f);
                AssertBottom(root, "NetButton", 303f, new Vector2(200f, 60f), -130f);
                AssertBottom(root, "MaleCard", 499f, new Vector2(200f, 280f), -130f);
                AssertBottom(root, "FemaleCard", 499f, new Vector2(200f, 280f), 130f);
                AssertBottom(root, "MaleHighlight", 499f, new Vector2(220f, 300f), -130f);
                AssertBottom(root, "FemaleHighlight", 499f, new Vector2(220f, 300f), 130f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertBottom(GameObject root, string name, float y,
            Vector2 size, float x = 0f)
        {
            var child = root.transform.Find(name);
            Assert.IsNotNull(child, name);
            var rect = (RectTransform)child;
            Assert.AreEqual(new Vector2(0.5f, 0f), rect.anchorMin, name);
            Assert.AreEqual(new Vector2(0.5f, 0f), rect.anchorMax, name);
            Assert.AreEqual(x, rect.anchoredPosition.x, 0.01f, name);
            Assert.AreEqual(y, rect.anchoredPosition.y, 0.01f, name);
            Assert.AreEqual(size.x, rect.sizeDelta.x, 0.01f, name);
            Assert.AreEqual(size.y, rect.sizeDelta.y, 0.01f, name);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), rect.pivot, name);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode CharacterSelectPanelLayoutTests
```

预期：锚点/位置断言失败。

- [ ] **步骤 3：修改 Prefab**

在 Unity Editor 中打开 `Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab`（Prefab Mode），把下列对象改为 `anchorMin = anchorMax = (0.5,0)`、pivot `(0.5,0.5)`，设置 `anchoredPosition` 与 `sizeDelta`：

| 对象 | anchoredPosition | sizeDelta |
|---|---:|---:|
| `ServerSettings` | `(0,105)` | `(640,132)` |
| `ConfirmButton` | `(0,222)` | `(220,70)` |
| `SoloButton` | `(-130,303)` | `(200,60)` |
| `NetButton` | `(130,303)` | `(200,60)` |
| `MaleCard` | `(-130,499)` | `(200,280)` |
| `FemaleCard` | `(130,499)` | `(200,280)` |
| `MaleHighlight` | `(-130,499)` | `(220,300)` |
| `FemaleHighlight` | `(130,499)` | `(220,300)` |

保持 `Bg` 全屏拉伸、父子关系、序列化引用、按钮/输入组件不变。保存 Prefab。

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode CharacterSelectPanelLayoutTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab \
        Assets/Scripts/Tests/Editor/CharacterSelectPanelLayoutTests.cs
git commit -m "feat(ui): bottom-anchor character select content"
```

---

## 任务 10：选人面板几何 fit 测试

**文件：**
- 测试：`Assets/Scripts/Tests/PlayMode/CharacterSelectPanelLayoutFitTests.cs`（新建）

- [ ] **步骤 1：编写失败测试（几何）**

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KcpNet.PlayModeTests
{
    public class CharacterSelectPanelLayoutFitTests
    {
        private GameObject _prefab;

        [SetUp]
        public void SetUp()
        {
            _prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
            Assert.IsNotNull(_prefab);
        }

        private static float ScaleFactor(Vector2 screen, Vector2 reference, float match)
        {
            float logWidth = Mathf.Log(screen.x / reference.x, 2f);
            float logHeight = Mathf.Log(screen.y / reference.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, match));
        }

        private static Bounds BoundsOf(RectTransform parent, RectTransform child)
            => RectTransformUtility.CalculateRelativeRectTransformBounds(parent, child);

        [UnityTest]
        public IEnumerator test_character_select_controls_fit_without_overlap_at_phone_aspects()
        {
            Vector2[] screens =
            {
                new Vector2(1920f, 1080f),
                new Vector2(2400f, 1080f),
                new Vector2(2520f, 1080f),
            };
            Vector2 reference = new Vector2(1920f, 1080f) / 1.15f;

            foreach (Vector2 screen in screens)
            {
                float scale = ScaleFactor(screen, reference, 0.5f);
                Vector2 canvasSize = new Vector2(screen.x / scale, screen.y / scale);

                var host = new GameObject("FitHost", typeof(RectTransform));
                try
                {
                    var hostRect = host.GetComponent<RectTransform>();
                    hostRect.sizeDelta = canvasSize;

                    var panel = Object.Instantiate(_prefab, host.transform, false);
                    var panelRect = (RectTransform)panel.transform;
                    panelRect.anchorMin = Vector2.zero;
                    panelRect.anchorMax = Vector2.one;
                    panelRect.offsetMin = Vector2.zero;
                    panelRect.offsetMax = Vector2.zero;
                    Find(panelRect, "ServerSettings").gameObject.SetActive(true);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);

                    Bounds server = BoundsOf(panelRect, Find(panelRect, "ServerSettings"));
                    Bounds confirm = BoundsOf(panelRect, Find(panelRect, "ConfirmButton"));
                    Bounds solo = BoundsOf(panelRect, Find(panelRect, "SoloButton"));
                    Bounds net = BoundsOf(panelRect, Find(panelRect, "NetButton"));
                    Bounds maleCard = BoundsOf(panelRect, Find(panelRect, "MaleCard"));
                    Bounds femaleCard = BoundsOf(panelRect, Find(panelRect, "FemaleCard"));
                    Bounds maleHigh = BoundsOf(panelRect, Find(panelRect, "MaleHighlight"));
                    Bounds femaleHigh = BoundsOf(panelRect, Find(panelRect, "FemaleHighlight"));

                    Assert.GreaterOrEqual(confirm.min.y - server.max.y, 16f - 0.01f);
                    Assert.GreaterOrEqual(solo.min.y - confirm.max.y, 16f - 0.01f);
                    Assert.GreaterOrEqual(maleHigh.min.y - solo.max.y, 16f - 0.01f);
                    Assert.GreaterOrEqual(net.min.x - solo.max.x, 60f - 0.01f,
                        "两个模式按钮横向净距至少 60");
                    Assert.GreaterOrEqual(femaleHigh.min.x - maleHigh.max.x, 40f - 0.01f,
                        "两个角色外框横向净距至少 40");

                    Rect panelBounds = panelRect.rect;
                    foreach (Bounds r in new[] { server, confirm, solo, net, maleCard, femaleCard, maleHigh, femaleHigh })
                    {
                        Assert.GreaterOrEqual(r.min.x, panelBounds.xMin - 0.01f);
                        Assert.LessOrEqual(r.max.x, panelBounds.xMax + 0.01f);
                        Assert.GreaterOrEqual(r.min.y, panelBounds.yMin - 0.01f);
                        Assert.LessOrEqual(r.max.y, panelBounds.yMax + 0.01f);
                    }

                    float soloCenterY = solo.center.y;
                    Find(panelRect, "ServerSettings").gameObject.SetActive(false);
                    LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
                    Assert.AreEqual(soloCenterY,
                        BoundsOf(panelRect, Find(panelRect, "SoloButton")).center.y, 0.01f,
                        "单机隐藏 ServerSettings 后其他控件不得跳位");
                    Find(panelRect, "ServerSettings").gameObject.SetActive(true);

                    Object.DestroyImmediate(host);
                }
                finally
                {
                    if (host != null) Object.DestroyImmediate(host);
                }

                yield return null;
            }
        }

        private static RectTransform Find(RectTransform root, string name)
        {
            var child = root.Find(name);
            Assert.IsNotNull(child, name);
            return (RectTransform)child;
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh PlayMode CharacterSelectPanelLayoutFitTests
```

预期：净距/边界断言失败（Prefab 尚未改）。

- [ ] **步骤 3：实现**

本任务依赖任务 9 的 Prefab 改动；若任务 9 已完成，无需额外生产代码。几何测试使用固定尺寸父 `RectTransform`，不依赖 `Screen.SetResolution`。

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh PlayMode CharacterSelectPanelLayoutFitTests
```

预期：PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/CharacterSelectPanelLayoutFitTests.cs
git commit -m "test(ui): verify character select layout fits phone aspects"
```

---

## 任务 10.5：MobileHudLayoutFitTests（EditMode 公式 + 实际 Prefab 数据）

**文件：**
- 测试：`Assets/Scripts/Tests/Editor/MobileHudLayoutFitTests.cs`（新建）

- [ ] **步骤 1：编写测试**

```csharp
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MobileHudLayoutFitTests
    {
        [Test]
        public void test_mobile_hud_and_character_select_fit_phone_aspects_at_115_scale()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
            Assert.IsNotNull(prefab);
            var root = (RectTransform)prefab.transform;

            Vector2[] screens =
            {
                new Vector2(1920f, 1080f),
                new Vector2(2400f, 1080f),
                new Vector2(2520f, 1080f),
            };
            Vector2 reference = new Vector2(1920f, 1080f) / 1.15f;

            foreach (Vector2 screen in screens)
            {
                float scale = ScaleFactor(screen, reference, 0.5f);
                Vector2 canvas = new Vector2(screen.x / scale, screen.y / scale);

                Rect server = RectOf(root, "ServerSettings", canvas);
                Rect confirm = RectOf(root, "ConfirmButton", canvas);
                Rect solo = RectOf(root, "SoloButton", canvas);
                Rect net = RectOf(root, "NetButton", canvas);
                Rect maleCard = RectOf(root, "MaleCard", canvas);
                Rect femaleCard = RectOf(root, "FemaleCard", canvas);
                Rect maleHigh = RectOf(root, "MaleHighlight", canvas);
                Rect femaleHigh = RectOf(root, "FemaleHighlight", canvas);

                Assert.GreaterOrEqual(confirm.yMin - server.yMax, 16f - 0.01f);
                Assert.GreaterOrEqual(solo.yMin - confirm.yMax, 16f - 0.01f);
                Assert.GreaterOrEqual(maleHigh.yMin - solo.yMax, 16f - 0.01f);
                Assert.GreaterOrEqual(net.xMin - solo.xMax, 60f - 0.01f,
                    "两个模式按钮横向净距至少 60");
                Assert.GreaterOrEqual(femaleHigh.xMin - maleHigh.xMax, 40f - 0.01f,
                    "两个角色外框横向净距至少 40");

                foreach (Rect r in new[]
                {
                    server, confirm, solo, net, maleCard, femaleCard, maleHigh, femaleHigh,
                })
                {
                    Assert.GreaterOrEqual(r.xMin, 0f);
                    Assert.LessOrEqual(r.xMax, canvas.x);
                    Assert.GreaterOrEqual(r.yMin, 0f);
                    Assert.LessOrEqual(r.yMax, canvas.y);
                }
            }
        }

        private static float ScaleFactor(Vector2 screen, Vector2 reference, float match)
        {
            float logWidth = Mathf.Log(screen.x / reference.x, 2f);
            float logHeight = Mathf.Log(screen.y / reference.y, 2f);
            return Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, match));
        }

        private static Rect RectOf(RectTransform root, string name, Vector2 canvas)
        {
            var child = (RectTransform)root.Find(name);
            Assert.IsNotNull(child, name);
            Vector2 anchor = child.anchorMin;
            Vector2 center = new Vector2(
                canvas.x * anchor.x + child.anchoredPosition.x,
                canvas.y * anchor.y + child.anchoredPosition.y);
            return new Rect(center - child.sizeDelta * 0.5f, child.sizeDelta);
        }
    }
}
```

- [ ] **步骤 2：运行测试验证**

```bash
bash tools/unity-tests.sh EditMode MobileHudLayoutFitTests
```

预期：任务 9 的 Prefab 已改后 PASS；若在任务 9 之前运行，净距/边界断言失败。

- [ ] **步骤 3：Commit**

```bash
git add Assets/Scripts/Tests/Editor/MobileHudLayoutFitTests.cs
git commit -m "test(ui): verify phone aspect layout with canvas scaler formula"
```

---

## 任务 11：选人面板交互与高亮覆盖

**文件：**
- 修改：`Assets/Scripts/Tests/PlayMode/CharacterSelectPanelLayoutFitTests.cs`
- 在文件头补：

```csharp
using System.Reflection;
using TMPro;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.UI;
using UnityEngine.EventSystems;
using UnityEngine.UI;
```

- [ ] **步骤 1：增加交互测试**

在 `CharacterSelectPanelLayoutFitTests` 中增加：

```csharp
private EventSystem _eventSystem;
private GameObject _eventSystemGo;
private GameObject _canvasGo;
private GameObject _panel;
private CharacterSelectPanel _panelComponent;
private NetworkMode _previousMode;
private string _previousHost;
private int _previousPort;
private string _previousUser;

[UnitySetUp]
public IEnumerator InteractionSetUp()
{
    _previousMode = NetworkModeService.Load();
    _previousHost = NetworkModeService.LoadHost();
    _previousPort = NetworkModeService.LoadPort();
    _previousUser = NetworkModeService.LoadUsername();
    NetworkModeService.Save(NetworkMode.Online);

    if (_prefab == null)
        _prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
    Assert.IsNotNull(_prefab);

    _eventSystem = EventSystem.current;
    if (_eventSystem == null)
    {
        _eventSystemGo = new GameObject("CharacterSelectTests_EventSystem");
        _eventSystem = _eventSystemGo.AddComponent<EventSystem>();
        _eventSystem.gameObject.AddComponent<StandaloneInputModule>();
    }

    _canvasGo = new GameObject("CharacterSelectTests_Canvas",
        typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
    var canvas = _canvasGo.GetComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    var scaler = _canvasGo.GetComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f) / 1.15f;
    scaler.matchWidthOrHeight = 0.5f;

    _panel = Object.Instantiate(_prefab, _canvasGo.transform, false);
    var rect = (RectTransform)_panel.transform;
    rect.anchorMin = Vector2.zero;
    rect.anchorMax = Vector2.one;
    rect.offsetMin = Vector2.zero;
    rect.offsetMax = Vector2.zero;

    _panelComponent = _panel.GetComponent<CharacterSelectPanel>();
    var show = _panelComponent.GetType()
        .GetMethod("OnShow", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(UIPanel).GetMethod("OnShow", BindingFlags.Instance | BindingFlags.NonPublic);
    Assert.IsNotNull(show);
    show.Invoke(_panelComponent, null);

    yield return null;
    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_panel.transform);
}

[TearDown]
public void InteractionTearDown()
{
    if (_panel != null) Object.DestroyImmediate(_panel);
    if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
    if (_eventSystemGo != null) Object.DestroyImmediate(_eventSystemGo);
    NetworkModeService.Save(_previousMode);
    NetworkModeService.SaveConnection(
        _previousHost, _previousPort.ToString(), _previousUser);
}

private static Vector2 ScreenPoint(RectTransform rect)
    => RectTransformUtility.WorldToScreenPoint(
        null, rect.TransformPoint(rect.rect.center));

private GameObject FirstHit(Vector2 screenPoint)
{
    var data = new PointerEventData(_eventSystem) { position = screenPoint };
    var results = new System.Collections.Generic.List<RaycastResult>();
    _canvasGo.GetComponent<GraphicRaycaster>().Raycast(data, results);
    return results.Count > 0 ? results[0].gameObject : null;
}

private static void Click(RectTransform rect)
{
    var target = rect.gameObject;
    var data = new PointerEventData(EventSystem.current)
    {
        position = ScreenPoint(rect),
        button = PointerEventData.InputButton.Left,
    };
    ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
}

[UnityTest]
public IEnumerator test_character_select_inputs_and_buttons_receive_expected_pointer()
{
    yield return null;

    string[] inputs = { "HostInput", "PortInput", "UsernameInput" };
    foreach (string name in inputs)
    {
        var rect = (RectTransform)_panel.transform.Find(name);
        Assert.IsNotNull(rect, name);
        GameObject hit = FirstHit(ScreenPoint(rect));
        Assert.IsNotNull(hit, name + " 未被射线命中");
        Assert.IsNotNull(hit.GetComponentInParent<TMP_InputField>(),
            name + " 首命中不是输入框");
    }

    foreach (string name in inputs)
    {
        var field = _panel.transform.Find(name).GetComponent<TMP_InputField>();
        Assert.IsNotNull(field, name);
        Click((RectTransform)field.transform);
        yield return null;
        Assert.IsTrue(field.isFocused, name + " 点击后应可编辑");
    }

    var username = _panel.transform.Find("UsernameInput").GetComponent<TMP_InputField>();
    username.text = "测试昵称";
    Assert.AreEqual("测试昵称", username.text);

    int selected = 0;
    _panelComponent.OnSelectionChanged += _ => selected++;
    Click((RectTransform)_panel.transform.Find("MaleCard"));
    yield return null;
    Assert.AreEqual(1, selected);
    Click((RectTransform)_panel.transform.Find("FemaleCard"));
    yield return null;
    Assert.AreEqual(2, selected);

    int confirmed = 0;
    void OnSelected(CharacterSelectedEvent e) => confirmed++;
    EventBus.Subscribe<CharacterSelectedEvent>(OnSelected);
    try
    {
        Click((RectTransform)_panel.transform.Find("ConfirmButton"));
        yield return null;
        Assert.AreEqual(1, confirmed, "确认按钮必须触发 CharacterSelectedEvent");
    }
    finally
    {
        EventBus.Unsubscribe<CharacterSelectedEvent>(OnSelected);
    }
}

[UnityTest]
public IEnumerator test_character_select_highlights_do_not_block_card_clicks()
{
    yield return null;

    _panel.transform.Find("MaleHighlight").gameObject.SetActive(true);
    _panel.transform.Find("FemaleHighlight").gameObject.SetActive(true);

    int selected = 0;
    _panelComponent.OnSelectionChanged += _ => selected++;

    string[] cards = { "MaleCard", "FemaleCard" };
    foreach (string name in cards)
    {
        var card = (RectTransform)_panel.transform.Find(name);
        GameObject hit = FirstHit(ScreenPoint(card));
        Assert.IsNotNull(hit, name);
        Assert.IsNotNull(hit.GetComponentInParent<Button>(),
            name + " 高亮激活后首命中仍应是卡片按钮");

        Click(card);
        yield return null;
    }

    Assert.AreEqual(2, selected);
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh PlayMode CharacterSelectPanelLayoutFitTests
```

预期：缺少 `using`/字段/`OnSelectionChanged` 订阅失败；补齐后，若高亮 `raycastTarget` 不为 0 或布局错误，断言失败。

- [ ] **步骤 3：实现/调整**

- 补 `using System.Reflection;`、`using TMPro;`、`using UnityEngine.EventSystems;`、`using UnityEngine.UI;`、`using Hotfix.GameSystems.SyncLayer;`、`using Hotfix.GameSystems.Sys3C.Core.Events;`、`using Hotfix.GameSystems.Skills;`。
- `MaleHighlight`/`FemaleHighlight` 的 `Image.raycastTarget` 必须为 0（当前 Prefab 已是 0；若改动，先修正）。
- `Confirm` 点击只验证事件；不启动连接、不加载场景。

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh PlayMode CharacterSelectPanelLayoutFitTests
```

预期：几何 + 交互用例全部 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/CharacterSelectPanelLayoutFitTests.cs
git commit -m "test(ui): verify character select clicks and highlight hit testing"
```

---

## 任务 12：默认联网与随机中文昵称

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs:26-31,95-112`
- 新增：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/RandomNicknameGenerator.cs`
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/NetworkModeServiceTests.cs`（新建）
- 测试：`Assets/Scripts/Tests/Editor/SyncLayer/RandomNicknameGeneratorTests.cs`（新建）

- [ ] **步骤 1：编写失败测试**

`RandomNicknameGeneratorTests.cs`：

```csharp
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class RandomNicknameGeneratorTests
    {
        [Test]
        public void test_generated_name_is_four_chinese_characters()
        {
            for (int seed = 0; seed < 200; seed++)
            {
                string name = RandomNicknameGenerator.Generate(new System.Random(seed));
                Assert.AreEqual(4, name.Length);
                foreach (char c in name)
                    Assert.IsTrue(c >= '一' && c <= '鿿', name);
                Assert.AreNotEqual("Player", name);
            }
        }

        [Test]
        public void test_seeded_generator_is_deterministic()
        {
            Assert.AreEqual(
                RandomNicknameGenerator.Generate(new System.Random(42)),
                RandomNicknameGenerator.Generate(new System.Random(42)));
        }
    }
}
```

`NetworkModeServiceTests.cs`：

```csharp
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class NetworkModeServiceTests
    {
        private NetworkMode _previousMode;
        private string _previousHost;
        private int _previousPort;
        private string _previousUser;

        [SetUp]
        public void SetUp()
        {
            _previousMode = NetworkModeService.Load();
            _previousHost = NetworkModeService.LoadHost();
            _previousPort = NetworkModeService.LoadPort();
            _previousUser = NetworkModeService.LoadUsername();
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            NetworkModeService.SaveConnection(
                _previousHost, _previousPort.ToString(), _previousUser);
        }

        [Test]
        public void test_default_mode_is_online_without_save()
        {
            PlayerPrefs.DeleteKey("net.mode");
            Assert.AreEqual(NetworkMode.Online, NetworkModeService.Load());
        }

        [Test]
        public void test_saved_single_player_wins()
        {
            NetworkModeService.Save(NetworkMode.SinglePlayer);
            Assert.AreEqual(NetworkMode.SinglePlayer, NetworkModeService.Load());
        }

        [Test]
        public void test_username_is_generated_once_and_persisted()
        {
            PlayerPrefs.DeleteKey("net.username");

            string first = NetworkModeService.LoadUsername();
            string second = NetworkModeService.LoadUsername();

            Assert.AreEqual(first, second);
            Assert.AreEqual(4, first.Length);
            Assert.AreNotEqual("Player", first);
            Assert.AreEqual(first, PlayerPrefs.GetString("net.username"));
        }

        [Test]
        public void test_empty_connection_username_falls_back_to_saved_name()
        {
            PlayerPrefs.DeleteKey("net.username");
            string generated = NetworkModeService.LoadUsername();

            NetworkModeService.SaveConnection("127.0.0.1", "8000", "");

            Assert.AreEqual(generated, NetworkModeService.LoadUsername());
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

```bash
bash tools/unity-tests.sh EditMode RandomNicknameGeneratorTests
bash tools/unity-tests.sh EditMode NetworkModeServiceTests
```

预期：编译失败/断言失败（默认 SinglePlayer、`Player`）。

- [ ] **步骤 3：实现**

`RandomNicknameGenerator.cs`：

```csharp
namespace Hotfix.GameSystems.SyncLayer
{
    public static class RandomNicknameGenerator
    {
        private static readonly string[] Prefixes =
        {
            "疾风", "烈焰", "寒霜", "幽影", "星尘", "月华",
            "赤焰", "青岚", "苍穹", "破晓", "孤狼", "银月",
            "铁壁", "流云", "惊雷", "夜雨", "霜刃", "烈阳",
            "玄冰", "飞雪", "碧海", "紫电", "暗星", "白露",
        };

        private static readonly string[] Suffixes =
        {
            "之狼", "之鹰", "之刃", "之影", "之翼", "之心",
            "之魂", "之盾", "之弓", "之剑", "之虎", "之龙",
            "之狐", "之熊", "之雀", "之豹", "之蛇", "之鹿",
            "之狮", "之凤", "骑士", "游侠", "法师", "守卫",
        };

        public static string Generate(System.Random rng)
        {
            rng ??= new System.Random();
            return Prefixes[rng.Next(Prefixes.Length)]
                 + Suffixes[rng.Next(Suffixes.Length)];
        }

        public static string Generate() => Generate(new System.Random());
    }
}
```

`NetworkModeService.Load()`：

```csharp
public static NetworkMode Load()
{
    if (!PlayerPrefs.HasKey(PrefsKey)) return NetworkMode.Online;
    int raw = PlayerPrefs.GetInt(PrefsKey);
    return raw == (int)NetworkMode.Online ? NetworkMode.Online : NetworkMode.SinglePlayer;
}
```

`NetworkModeService.LoadUsername()`：

```csharp
public static string LoadUsername()
{
    string saved = PlayerPrefs.HasKey(UserKey)
        ? PlayerPrefs.GetString(UserKey)
        : string.Empty;

    if (!string.IsNullOrWhiteSpace(saved)) return saved;

    string generated = RandomNicknameGenerator.Generate();
    PlayerPrefs.SetString(UserKey, generated);
    PlayerPrefs.Save();
    return generated;
}
```

`SaveConnection` 的用户名兜底：

```csharp
string safeUser = string.IsNullOrWhiteSpace(username)
    ? LoadUsername()
    : username.Trim();
```

- [ ] **步骤 4：运行测试验证通过**

```bash
bash tools/unity-tests.sh EditMode RandomNicknameGeneratorTests
bash tools/unity-tests.sh EditMode NetworkModeServiceTests
```

预期：全部 PASS。

- [ ] **步骤 5：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/RandomNicknameGenerator.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/NetworkModeServiceTests.cs \
        Assets/Scripts/Tests/Editor/SyncLayer/RandomNicknameGeneratorTests.cs
git commit -m "feat(login): default online and generate random chinese nickname"
```

---

## 任务 13：现有测试的 PlayerPrefs 隔离

**文件：**
- 修改：`Assets/Scripts/Tests/PlayMode/CharacterSelectPanelReopenTests.cs`
- 修改：`Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs`
- 修改：`Assets/Scripts/Tests/PlayMode/MobileSkillHudTests.cs`

- [ ] **步骤 1：先运行受影响的现有测试**

```bash
bash tools/unity-tests.sh PlayMode CharacterSelectPanelReopenTests
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
bash tools/unity-tests.sh PlayMode MobileSkillHudTests
```

预期：在默认 Online/随机昵称改动后，某些测试会写 PlayerPrefs 或走联网分支；记录失败/污染。

- [ ] **步骤 2：修改测试隔离**

`CharacterSelectPanelReopenTests` 的 `[UnitySetUp]`/`[TearDown]` 增加（并在文件头补 `using Hotfix.GameSystems.SyncLayer;`）：

```csharp
private NetworkMode _previousMode;
private string _previousHost;
private int _previousPort;
private string _previousUser;

[UnitySetUp]
public IEnumerator SetUp()
{
    _previousMode = NetworkModeService.Load();
    _previousHost = NetworkModeService.LoadHost();
    _previousPort = NetworkModeService.LoadPort();
    _previousUser = NetworkModeService.LoadUsername();
    NetworkModeService.Save(NetworkMode.SinglePlayer);

    var prefab = Resources.Load<GameObject>("Prefabs/UI/CharacterSelectPanel");
    Assert.IsNotNull(prefab, "选人面板预制体缺失：Resources/Prefabs/UI/CharacterSelectPanel");
    _instance = Object.Instantiate(prefab);
    _panel = _instance.GetComponent<CharacterSelectPanel>();
    Assert.IsNotNull(_panel, "预制体上没有 CharacterSelectPanel 组件");
    yield return null;
}

[TearDown]
public void TearDown()
{
    if (_instance != null) Object.DestroyImmediate(_instance);
    NetworkModeService.Save(_previousMode);
    NetworkModeService.SaveConnection(
        _previousHost, _previousPort.ToString(), _previousUser);
}
```

`MobileTouchInputTests` 增加（并补 `using Hotfix.GameSystems.SyncLayer;`）：

```csharp
private NetworkMode _previousMode;
private string _previousHost;
private int _previousPort;
private string _previousUser;

[UnitySetUp]
public IEnumerator SavePrefsAndForceSinglePlayer()
{
    _previousMode = NetworkModeService.Load();
    _previousHost = NetworkModeService.LoadHost();
    _previousPort = NetworkModeService.LoadPort();
    _previousUser = NetworkModeService.LoadUsername();
    NetworkModeService.Save(NetworkMode.SinglePlayer);
    yield return null;
}

[TearDown]
public void RestorePrefs()
{
    NetworkModeService.Save(_previousMode);
    NetworkModeService.SaveConnection(
        _previousHost, _previousPort.ToString(), _previousUser);
}
```

`MobileSkillHudTests` 增加（并补 `using Hotfix.GameSystems.SyncLayer;`）：

```csharp
private NetworkMode _previousMode;
private string _previousHost;
private int _previousPort;
private string _previousUser;

[SetUp]
public void SavePrefsAndForceSinglePlayer()
{
    _previousMode = NetworkModeService.Load();
    _previousHost = NetworkModeService.LoadHost();
    _previousPort = NetworkModeService.LoadPort();
    _previousUser = NetworkModeService.LoadUsername();
    NetworkModeService.Save(NetworkMode.SinglePlayer);
}

[TearDown]
public void RestorePrefs()
{
    NetworkModeService.Save(_previousMode);
    NetworkModeService.SaveConnection(
        _previousHost, _previousPort.ToString(), _previousUser);
}
```

`MobileTouchInputTests` 的现有 `LogAssert.ignoreFailingMessages` 保持。

- [ ] **步骤 3：运行测试验证通过**

```bash
bash tools/unity-tests.sh PlayMode CharacterSelectPanelReopenTests
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
bash tools/unity-tests.sh PlayMode MobileSkillHudTests
```

预期：全部 PASS，且测试结束后 PlayerPrefs 恢复。

- [ ] **步骤 4：Commit**

```bash
git add Assets/Scripts/Tests/PlayMode/CharacterSelectPanelReopenTests.cs \
        Assets/Scripts/Tests/PlayMode/MobileTouchInputTests.cs \
        Assets/Scripts/Tests/PlayMode/MobileSkillHudTests.cs
git commit -m "test(login): isolate player prefs in selection and mobile ui tests"
```

---

## 任务 14：README 与最终验证

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Nameplate/README.md`
- 修改：`Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`
- 修改：`Assets/Scripts/AOT/DataDefinition/README.md`

- [ ] **步骤 1：更新 README**

`Sys3C/README.md` 增加：
- PC/Editor 右键拖拽；鼠标移动不转视角。
- `TouchLookArea` 只认真实触摸；观察区 DPI 死区；触屏 `[-10,25]`、桌面 `[-30,60]`。

`UI/README.md` 增加：
- UI 倍率解析与 `MobileUiRoot` 的 `ScreenAdapter`。
- LookArea 右上象限、技能/动作按钮右下锚点、选人内容底部锚点与 16 间距。
- 观察区只接收真实触摸、单指归属与生命周期。

`Nameplate/README.md` 增加：
- 名字板、飘字、精力环跟随 `EntityDisplayCanvas` 的解析后 UI 倍率。

`SyncLayer/README.md` 增加：
- 无 `net.mode` 默认 Online，已有存档优先；连接失败保持联网并回选人。
- 随机中文昵称首次生成并持久化，不迁移旧 `"Player"`。

`DataDefinition/README.md` 增加：
- `GameSettings` 的 `UIScale` / `MobilePlatformUIScale` / `DesktopPlatformUIScale` 一句说明。

- [ ] **步骤 2：运行完整相关测试**

```bash
bash tools/unity-tests.sh EditMode KeyboardInputAdapterTests
bash tools/unity-tests.sh EditMode MobileInputAdapterTests
bash tools/unity-tests.sh EditMode TouchLookAreaTests
bash tools/unity-tests.sh EditMode InputManagerLookSourceTests
bash tools/unity-tests.sh EditMode ThirdPersonCameraControllerTests
bash tools/unity-tests.sh EditMode UIScaleResolverTests
bash tools/unity-tests.sh EditMode UIScaleApplicationTests
bash tools/unity-tests.sh EditMode NetworkModeServiceTests
bash tools/unity-tests.sh EditMode RandomNicknameGeneratorTests
bash tools/unity-tests.sh EditMode CharacterSelectPanelLayoutTests
bash tools/unity-tests.sh EditMode MobileHudLayoutFitTests
bash tools/unity-tests.sh PlayMode TouchLookAreaEventSystemTests
bash tools/unity-tests.sh PlayMode MobileLookAreaLayoutTests
bash tools/unity-tests.sh PlayMode CharacterSelectPanelLayoutFitTests
bash tools/unity-tests.sh PlayMode CharacterSelectPanelReopenTests
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
bash tools/unity-tests.sh PlayMode MobileSkillHudTests
```

预期：全部 PASS。若有失败，先确认 Game View 聚焦（EventSystem 测试）和 PlayerPrefs 隔离。

- [ ] **步骤 3：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md \
        Assets/Scripts/Hotfix/GameSystems/UI/README.md \
        Assets/Scripts/Hotfix/GameSystems/Nameplate/README.md \
        Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md \
        Assets/Scripts/AOT/DataDefinition/README.md
git commit -m "docs: document experience polish behavior"
```

---

## 规格覆盖自检

| 规格章节 | 对应任务 |
|---|---|
| 4.1.1 PC 右键拖拽 | 任务 1 |
| 4.1.2 触屏来源与俯仰限位 | 任务 2、3 |
| 4.1.3 观察区手势与死区 | 任务 4、5 |
| 4.2.1 LookArea 右上象限 | 任务 8 |
| 4.2.2 技能/动作按钮右下锚点 | 任务 8 |
| 4.2.3 选人内容底部锚点 | 任务 9、10、10.5、11 |
| 4.3 UI 缩放 | 任务 6、7、8 |
| 4.4 默认联网 | 任务 12 |
| 4.5 随机中文昵称 | 任务 12 |
| 5.1/5.2 测试 | 任务 1–13 |
| 8.1/8.2 改动清单 | 任务 1–12 |
| 8.4 文档 | 任务 14 |

**占位符扫描：** 本计划未使用 TODO/待定/“类似任务 N”；所有代码块均为可落地的实际代码。

**类型一致性：** `UIScale`/`MobilePlatformUIScale`/`DesktopPlatformUIScale`、`ResolveUIScale()`、`ResolveReferenceResolution()`、`TouchLookArea.ResolveDragThresholdPixels`、`LastLookFromTouch`、`IsLookFromTouch` 在各任务中命名一致。
