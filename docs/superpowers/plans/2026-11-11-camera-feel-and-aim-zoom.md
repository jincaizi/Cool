# 相机手感与火球瞄准视角实现计划

> **面向 AI 代理的工作者：** 必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实现此计划。步骤使用复选框（`- [ ]`）语法来跟踪进度。

**目标：** 降低触屏视角灵敏度、阻止触摸合成鼠标输入驱动相机、并在火球瞄准时平滑拉远相机。

**架构：** 三块独立小改动：`TouchLookArea` 只改序列化灵敏度；`KeyboardInputAdapter` 在 `touchCount > 0` 时关闭鼠标兜底；`ThirdPersonCameraController` 新增可缓动的当前距离，由 `Sys3CEntry` 按火球瞄准状态每帧写入。

**技术栈：** Unity 2022.3 LTS、C#、UGUI、NUnit EditMode/PlayMode、Unity MCP、旧 Input Manager。

**规格：** [2026-11-11-camera-feel-and-aim-zoom-design.md](../specs/2026-11-11-camera-feel-and-aim-zoom-design.md)

---

## 文件结构

### 生产代码

| 文件 | 职责 | 改动 |
|---|---|---|
| `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs` | 触屏观察区手势 | 灵敏度常量与只读属性 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs` | 键鼠视角输入 | 触摸活跃时关闭鼠标兜底 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs` | 第三人称相机 | 瞄准距离与缓动 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs` | 本地角色入口 | 写入瞄准拉远；死亡兜底取消蓄力 |

### 场景

| 文件 | 改动 |
|---|---|
| `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity` | LookArea `_sensitivity`；Main Camera `AimDistance` / `ZoomDamping` |

### 测试

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Tests/Editor/TouchLookAreaTests.cs` | 灵敏度断言引用常量；新增默认值用例 |
| `Assets/Scripts/Tests/PlayMode/TouchLookAreaEventSystemTests.cs` | 灵敏度期望改引用常量 |
| `Assets/Scripts/Tests/PlayMode/MobileLookAreaLayoutTests.cs` | 场景灵敏度断言 |
| `Assets/Scripts/Tests/Editor/Sys3C/KeyboardInputAdapterTests.cs` | 三参数门控用例 |
| `Assets/Scripts/Tests/Editor/Sys3C/ThirdPersonCameraControllerTests.cs` | 瞄准距离缓动用例 |
| `Assets/Scripts/Tests/PlayMode/AimZoomCameraTests.cs`（新建） | 瞄准拉远的真实生命周期 |
| `Assets/Scripts/Tests/PlayMode/PlayerDeathRespawnTests.cs` | 死亡取消蓄力回归 |

### 文档

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/UI/README.md` | 观察区灵敏度 |
| `Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md` | 鼠标兜底门控；火球瞄准拉远；死亡取消蓄力 |

---

## 任务 1：触屏观察灵敏度

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs:12-13`
- 修改：`Assets/Scripts/Tests/Editor/TouchLookAreaTests.cs:52-61`
- 修改：`Assets/Scripts/Tests/PlayMode/TouchLookAreaEventSystemTests.cs:131-163`
- 修改：`Assets/Scripts/Tests/PlayMode/MobileLookAreaLayoutTests.cs:69-82`
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`
- 修改：`Assets/Scripts/Hotfix/GameSystems/UI/README.md:53-56`

- [ ] **步骤 1：编写失败的 EditMode 测试**

在 `TouchLookAreaTests` 的 `SetUp/TearDown` 之后、`test_drag_below_threshold_does_not_emit` 之前插入：

```csharp
        [Test]
        public void test_default_sensitivity_is_quarter_degree_per_pixel()
        {
            // Arrange & Act
            float sensitivity = TouchLookArea.DefaultSensitivity;

            // Assert
            Assert.AreEqual(0.125f, sensitivity, 0.0001f,
                "触屏灵敏度应为 0.125（乘相机 MouseSensitivityX 2.0 后为 0.25°/px）");
            Assert.AreEqual(TouchLookArea.DefaultSensitivity, _area.Sensitivity, 0.0001f);
        }
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh EditMode TouchLookAreaTests
```

预期：编译失败，提示 `TouchLookArea` 不含 `DefaultSensitivity` / `Sensitivity`。

- [ ] **步骤 3：实现常量与只读属性**

把 `TouchLookArea.cs` 的字段区改为：

```csharp
        internal const float DefaultSensitivity = 0.125f;

        [SerializeField] private float _sensitivity = DefaultSensitivity;
        [Tooltip("起始拖拽死区（160dpi 基准像素数，运行时按 Screen.dpi 换算）")]
        [SerializeField] private float _minDragPixelsAt160Dpi = 12f;
```

在 `public Vector2 Delta => _delta;` 之后加：

```csharp
        internal float Sensitivity => _sensitivity;
```

- [ ] **步骤 4：更新现有 EditMode 断言**

把 `test_drag_above_threshold_emits_accumulated_delta` 中的：

```csharp
            Assert.AreEqual((threshold + 1f) * 0.2f, _area.Delta.x, 0.001f);
```

改为：

```csharp
            Assert.AreEqual((threshold + 1f) * TouchLookArea.DefaultSensitivity, _area.Delta.x, 0.001f);
```

- [ ] **步骤 5：运行 EditMode 测试验证通过**

运行：

```bash
bash tools/unity-tests.sh EditMode TouchLookAreaTests
```

预期：全部 PASS。

- [ ] **步骤 6：更新 PlayMode 期望值**

`TouchLookAreaEventSystemTests.cs` 两处：

```csharp
            Assert.AreEqual((threshold + 1f) * 0.2f, _area.Delta.x, 0.02f);
```

改为：

```csharp
            Assert.AreEqual((threshold + 1f) * TouchLookArea.DefaultSensitivity, _area.Delta.x, 0.02f);
```

以及：

```csharp
            Assert.AreEqual(1.2f, _area.Delta.x, 0.02f);
```

改为：

```csharp
            Assert.AreEqual(6f * TouchLookArea.DefaultSensitivity, _area.Delta.x, 0.02f);
```

- [ ] **步骤 7：编写失败的场景值 PlayMode 测试**

在 `MobileLookAreaLayoutTests` 末尾（`test_mobile_look_area_leaves_blank_drag_region_above_skill_cluster` 之前）插入：

```csharp
        [UnityTest]
        public IEnumerator test_mobile_look_area_uses_default_sensitivity()
        {
            yield return null;

            var look = Find("LookArea").GetComponent<TouchLookArea>();
            Assert.IsNotNull(look, "LookArea 必须挂 TouchLookArea");
            Assert.AreEqual(TouchLookArea.DefaultSensitivity, look.Sensitivity, 0.0001f,
                "场景 LookArea 的 _sensitivity 必须与代码默认值同步");
        }
```

- [ ] **步骤 8：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh PlayMode MobileLookAreaLayoutTests
```

预期：`test_mobile_look_area_uses_default_sensitivity` FAIL（场景里仍是 `0.2`）。

- [ ] **步骤 9：修改场景**

在 Unity Editor（或 Unity MCP）中打开 `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`：

| 对象 | 组件 | 字段 | 新值 |
|---|---|---|---|
| `LookArea` | `TouchLookArea` | `Sensitivity`（序列化字段 `_sensitivity`） | `0.125` |

保存场景。不要改 `LookArea` 的锚点、`Image.raycastTarget` 或任何按钮布局。
保存后用 MCP 回读 `LookArea` 的 `Sensitivity` 确认为 `0.125`。

- [ ] **步骤 10：运行 PlayMode 测试验证通过**

运行：

```bash
bash tools/unity-tests.sh PlayMode MobileLookAreaLayoutTests
bash tools/unity-tests.sh PlayMode TouchLookAreaEventSystemTests
```

预期：两个测试类全部 PASS。

- [ ] **步骤 11：更新 UI README**

把 `Assets/Scripts/Hotfix/GameSystems/UI/README.md` 的“观察区手势”条目改为：

```markdown
- **观察区手势**：`TouchLookArea` 只接收真实触摸（鼠标事件忽略），按 `pointerId` 单指归属；
  失焦、暂停或 `OnDisable` 时复位手势。灵敏度 `0.125 × 相机 MouseSensitivityX(2.0) = 0.25°/px`
  （2026-11-11 从 0.4°/px 下调；横纵共用同一系数）。
```

- [ ] **步骤 12：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs \
        Assets/Scripts/Tests/Editor/TouchLookAreaTests.cs \
        Assets/Scripts/Tests/PlayMode/TouchLookAreaEventSystemTests.cs \
        Assets/Scripts/Tests/PlayMode/MobileLookAreaLayoutTests.cs \
        Assets/Scripts/Hotfix/GameSystems/UI/README.md \
        Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat(ui): slow down touch look sensitivity"
```

---

## 任务 2：触摸活跃时关闭鼠标视角兜底

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs:45-54`
- 修改：`Assets/Scripts/Tests/Editor/Sys3C/KeyboardInputAdapterTests.cs`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md:87-94`

- [ ] **步骤 1：更新并新增失败的 EditMode 测试**

把 `KeyboardInputAdapterTests.cs` 整个文件替换为：

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
                new Vector2(3f, 4f), rightButtonHeld: false, touchActive: false);

            // Assert
            Assert.AreEqual(Vector2.zero, look, "未按右键时鼠标移动不得转视角");
        }

        [Test]
        public void test_keyboard_look_passes_through_with_right_button()
        {
            // Arrange & Act
            Vector2 look = KeyboardInputAdapter.GateLookToRightDrag(
                new Vector2(3f, 4f), rightButtonHeld: true, touchActive: false);

            // Assert
            Assert.AreEqual(new Vector2(3f, 4f), look);
        }

        [Test]
        public void test_keyboard_look_is_zero_while_touch_active()
        {
            // Arrange & Act
            Vector2 look = KeyboardInputAdapter.GateLookToRightDrag(
                new Vector2(3f, 4f), rightButtonHeld: true, touchActive: true);

            // Assert
            Assert.AreEqual(Vector2.zero, look, "触摸活跃时合成鼠标输入不得转视角");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh EditMode KeyboardInputAdapterTests
```

预期：编译失败，提示 `GateLookToRightDrag` 没有三参数重载。

- [ ] **步骤 3：实现门控**

把 `KeyboardInputAdapter.GetCameraRotationInput` 与 `GateLookToRightDrag` 替换为：

```csharp
        public Vector2 GetCameraRotationInput()
        {
            float mouseX = UnityEngine.Input.GetAxisRaw("Mouse X");
            float mouseY = UnityEngine.Input.GetAxisRaw("Mouse Y");
            bool rightButtonHeld = UnityEngine.Input.GetMouseButton(1);
            bool touchActive = UnityEngine.Input.touchCount > 0;

            return GateLookToRightDrag(new Vector2(mouseX, mouseY), rightButtonHeld, touchActive);
        }

        /// <summary>
        /// 触摸活跃时鼠标视角必须为零：手机浏览器会把触摸合成为鼠标事件，
        /// 摇杆按住时观察区 Delta 为零，若放行合成 Mouse X 会以 2°/px 灌进相机。
        /// PC/编辑器 touchCount == 0 时行为不变。
        /// </summary>
        internal static Vector2 GateLookToRightDrag(Vector2 look, bool rightButtonHeld, bool touchActive)
            => rightButtonHeld && !touchActive ? look : Vector2.zero;
```

- [ ] **步骤 4：运行测试验证通过**

运行：

```bash
bash tools/unity-tests.sh EditMode KeyboardInputAdapterTests
```

预期：3 个用例 PASS。

- [ ] **步骤 5：加临时诊断日志（不提交）**

在 `GetCameraRotationInput` 的 `return` 之前插入：

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (touchActive && (rightButtonHeld || mouseX != 0f || mouseY != 0f))
            {
                UnityEngine.Debug.Log("[LookGate] touch=" + UnityEngine.Input.touchCount
                    + " rmb=" + rightButtonHeld + " mouseX=" + mouseX + " mouseY=" + mouseY);
            }
#endif
```

- [ ] **步骤 6：真机诊断检查点（需要人类配合）**

构建 WebGL 并在手机上按住摇杆复现“另一只手点按钮/空白”。把日志贴回。判定：

| 日志表现 | 结论 | 动作 |
|---|---|---|
| 落指帧 `touch >= 2`、`rmb=True`、`mouseX` 很大 | 合成鼠标输入进入兜底 | 继续步骤 7，删除日志 |
| 落指帧 `touch >= 2`、`rmb=False`、`mouseX=0`，但视角仍跳 | 病因不在鼠标兜底 | 停止本任务，回到规格 §4.2 修订 |
| 观察区首帧位移异常大 | WebGL 指针位置异常 | 停止本任务，在 `TouchLookArea` 增加首帧异常位移保护后再验证 |

如果当下无法真机验证：记录为“未验证”并继续；任务 5 的真机验收必须覆盖这一项，
验收前不得宣称问题 2 已修复。

- [ ] **步骤 7：删除临时日志**

移除步骤 5 插入的 `#if` 代码块，只保留门控实现。日志不进入提交。

- [ ] **步骤 8：更新 Sys3C README**

在 `Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md` 的“相机视角输入”列表末尾追加：

```markdown
- **触摸活跃时鼠标视角兜底为零**（2026-11-11）：`Input.touchCount > 0` 时
  `KeyboardInputAdapter.GetCameraRotationInput` 返回零向量。手机浏览器会把触摸合成为鼠标事件；
  摇杆按住时观察区 `Delta` 为零，若无此门控，第二根手指落下会让合成 `Mouse X`
  以 `2°/px` 灌进相机，表现为一次大角度跳转。PC/编辑器在 `touchCount == 0` 时右键拖拽不变。
```

- [ ] **步骤 9：运行测试验证通过**

运行：

```bash
bash tools/unity-tests.sh EditMode KeyboardInputAdapterTests
```

预期：3 个用例 PASS。

- [ ] **步骤 10：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs \
        Assets/Scripts/Tests/Editor/Sys3C/KeyboardInputAdapterTests.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md
git commit -m "fix(input): ignore synthesized mouse look while touching"
```

---

## 任务 3：相机瞄准距离与缓动

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs:28-39`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs:115-169`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs:193-209`
- 修改：`Assets/Scripts/Tests/Editor/Sys3C/ThirdPersonCameraControllerTests.cs`
- 修改：`Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`

- [ ] **步骤 1：编写失败的 EditMode 测试**

在 `ThirdPersonCameraControllerTests` 的 `test_desktop_pitch_limits_stay_minus30_and_60` 之后插入：

```csharp
        [Test]
        public void test_aim_zoom_eases_toward_aim_distance()
        {
            var go = new GameObject("CameraAimZoomIn");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.AimZoomActive = true;

                for (int i = 0; i < 120; i++) cam.TickZoom(1f / 60f);

                Assert.AreEqual(11.5f, cam.CurrentDistance, 0.05f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_aim_zoom_returns_to_distance_when_disabled()
        {
            var go = new GameObject("CameraAimZoomOut");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.AimZoomActive = true;
                for (int i = 0; i < 120; i++) cam.TickZoom(1f / 60f);

                cam.AimZoomActive = false;
                for (int i = 0; i < 120; i++) cam.TickZoom(1f / 60f);

                Assert.AreEqual(8f, cam.CurrentDistance, 0.05f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_aim_zoom_target_is_clamped_to_min_camera_distance()
        {
            var go = new GameObject("CameraAimZoomClamp");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.AimDistance = 0.1f;
                cam.AimZoomActive = true;

                Assert.GreaterOrEqual(cam.TargetDistance, 0.8f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_snap_to_target_resets_aim_zoom_distance()
        {
            var go = new GameObject("CameraAimZoomSnap");
            var targetGo = new GameObject("CameraAimZoomTarget");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.Target = targetGo.transform;
                cam.AimZoomActive = true;
                for (int i = 0; i < 120; i++) cam.TickZoom(1f / 60f);

                cam.SnapToTarget();

                Assert.AreEqual(8f, cam.CurrentDistance, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(targetGo);
            }
        }
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh EditMode ThirdPersonCameraControllerTests
```

预期：编译失败，提示 `AimZoomActive` / `TickZoom` / `CurrentDistance` / `TargetDistance` 不存在。

- [ ] **步骤 3：实现控制器字段与缓动**

在 `ThirdPersonCameraController.cs` 的 `public bool UseMobilePitchLimits { get; set; }` 之后加：

```csharp
        [Header("Aim Zoom")]
        [Tooltip("火球瞄准手势激活时的相机距离")]
        public float AimDistance = 11.5f;
        [Tooltip("距离缓动系数，与 PositionDamping 同形")]
        public float ZoomDamping = 8.0f;

        /// <summary>瞄准拉远开关，由 Sys3CEntry 每帧写入。</summary>
        public bool AimZoomActive { get; set; }
```

在 `private bool _anglesInitialized;` 之后加：

```csharp
        private float _currentDistance;
        private bool _distanceInitialized;
```

在 `HandleRotationInput` 之后、`LateUpdate` 之前加：

```csharp
        internal float CurrentDistance
        {
            get
            {
                EnsureDistanceInitialized();
                return _currentDistance;
            }
        }

        internal float TargetDistance
            => Mathf.Max(MinCameraDistance, AimZoomActive ? AimDistance : Distance);

        private void EnsureDistanceInitialized()
        {
            if (_distanceInitialized) return;
            _currentDistance = Distance;
            _distanceInitialized = true;
        }

        /// <summary>
        /// 推进距离缓动。独立成 internal 方法：EditMode 测试用固定 dt 驱动，
        /// 不依赖 Time.deltaTime。
        /// </summary>
        internal void TickZoom(float deltaTime)
        {
            EnsureDistanceInitialized();
            _currentDistance = Mathf.Lerp(_currentDistance, TargetDistance,
                Mathf.Clamp01(ZoomDamping * deltaTime));
        }
```

- [ ] **步骤 4：让 LateUpdate 使用当前距离**

把 `LateUpdate` 中的：

```csharp
            // 计算目标位置（球坐标）
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -Distance);
            offset.y = Height;
```

改为：

```csharp
            TickZoom(Time.deltaTime);

            // 计算目标位置（球坐标）
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);
            offset.y = Height;
```

- [ ] **步骤 5：让 SnapToTarget 重置距离**

把 `SnapToTarget` 中的：

```csharp
            EnsureAnglesInitialized();

            // 立即设置相机位置
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -Distance);
            offset.y = Height;
```

改为：

```csharp
            EnsureAnglesInitialized();
            _currentDistance = Distance;
            _distanceInitialized = true;

            // 立即设置相机位置
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -_currentDistance);
            offset.y = Height;
```

- [ ] **步骤 6：运行 EditMode 测试验证通过**

运行：

```bash
bash tools/unity-tests.sh EditMode ThirdPersonCameraControllerTests
```

预期：6 个用例全部 PASS。

- [ ] **步骤 7：修改场景相机参数**

在 Unity Editor（或 Unity MCP）中打开 `DemoDay.unity`，选中 `Main Camera`：

| 字段 | 新值 |
|---|---|
| `AimDistance` | `11.5` |
| `ZoomDamping` | `8` |

保存场景。保持 `Distance = 8`、`Height = 2`、`MobileMinPitch = -10`、`MobileMaxPitch = 25` 不变。

- [ ] **步骤 8：更新 Sys3C README 相机段**

在“相机视角输入”之后新增：

```markdown
### 火球瞄准拉远

- 移动端火球进入 `Charging` 且瞄准手势激活时，`ThirdPersonCameraController` 的距离从
  `Distance = 8` 平滑拉到 `AimDistance = 11.5`（`ZoomDamping = 8`，约 0.2s 完成主要行程）。
- 松手发射、拖到取消区、受击/精力耗尽打断、失焦/横屏、死亡都会让
  `AimZoomActive` 变 false，距离缓动回 8。
- 拉远开关只由 `Sys3CEntry` 每帧写入；键盘 1 键蓄力没有瞄准手势，不拉远。
  防穿墙使用同一个当前距离，贴墙只把实际相机拉近，不改当前距离。
```

- [ ] **步骤 9：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs \
        Assets/Scripts/Tests/Editor/Sys3C/ThirdPersonCameraControllerTests.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md \
        Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat(camera): add aim zoom distance to third-person camera"
```

---

## 任务 4：火球瞄准接线与死亡兜底

**文件：**
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:278-283`
- 修改：`Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs:373-374`
- 新建：`Assets/Scripts/Tests/PlayMode/AimZoomCameraTests.cs`
- 修改：`Assets/Scripts/Tests/PlayMode/PlayerDeathRespawnTests.cs`

- [ ] **步骤 1：编写失败的 PlayMode 接线测试**

新建 `Assets/Scripts/Tests/PlayMode/AimZoomCameraTests.cs`：

```csharp
using System.Collections;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 火球瞄准拉远的真实生命周期：验证 Sys3CEntry 接线与恢复路径。
    /// </summary>
    public class AimZoomCameraTests
    {
        private NetworkMode _previousMode;
        private GameObject _cameraGo;
        private GameObject _playerGo;
        private Sys3CEntry _entry;
        private ThirdPersonCameraController _camera;
        private InputManager _input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            _cameraGo = new GameObject("AimZoomCameraTests_Camera");
            _camera = _cameraGo.AddComponent<ThirdPersonCameraController>();

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab,
                "测试用玩家 Prefab 缺失：Assets/Resources/PlayerEntryForTest.prefab");
            _playerGo = Object.Instantiate(prefab);
            _entry = _playerGo.GetComponent<Sys3CEntry>();

            yield return null; // 等 Start：相机 Target / InputManager / 技能注册
            yield return null;

            _input = _playerGo.GetComponent<InputManager>();
            Assert.IsNotNull(_input, "玩家必须有 InputManager");
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
        }

        private IEnumerator WaitForSubState(SkillSubState state)
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (_entry.SkillCoordinator.CurrentSubState != state
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private void PressFireball()
        {
            _input.SetSkillFireballPressedFromUI(true);
            _input.SetSkillFireballHeldFromUI(true);
            _input.SetSkillFireballAimFromUI(true, new Vector2(0f, 1f), false);
        }

        private void ReleaseFireball()
        {
            _input.SetSkillFireballHeldFromUI(false);
            _input.SetSkillFireballAimFromUI(false, Vector2.zero, false);
        }

        [UnityTest]
        public IEnumerator test_fireball_aim_zooms_camera_out()
        {
            // Arrange
            PressFireball();
            yield return WaitForSubState(SkillSubState.Charging);
            Assert.AreEqual(SkillSubState.Charging, _entry.SkillCoordinator.CurrentSubState,
                "火球应进入 Charging");

            // Act：等缓动推进
            for (int i = 0; i < 30; i++) yield return null;

            // Assert
            Assert.IsTrue(_camera.AimZoomActive, "瞄准期间应开启拉远");
            Assert.Greater(_camera.CurrentDistance, 8.5f, "瞄准期间相机距离应大于普通距离");
        }

        [UnityTest]
        public IEnumerator test_fireball_release_restores_camera_distance()
        {
            // Arrange
            PressFireball();
            yield return WaitForSubState(SkillSubState.Charging);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.Greater(_camera.CurrentDistance, 8.5f);

            // Act
            ReleaseFireball();
            for (int i = 0; i < 60; i++) yield return null;

            // Assert
            Assert.IsFalse(_camera.AimZoomActive, "松手后应关闭拉远");
            Assert.AreEqual(8f, _camera.CurrentDistance, 0.2f, "松手后应回到普通距离");
        }

        [UnityTest]
        public IEnumerator test_death_restores_camera_distance()
        {
            // Arrange
            PressFireball();
            yield return WaitForSubState(SkillSubState.Charging);
            for (int i = 0; i < 30; i++) yield return null;
            Assert.Greater(_camera.CurrentDistance, 8.5f);

            // Act：致死伤害走 ApplyServerDamage（单机模式本地判死）
            _entry.ApplyServerDamage(150f, Vector3.back);
            for (int i = 0; i < 60; i++) yield return null;

            // Assert
            Assert.IsTrue(_entry.IsDead, "150 伤害应致死");
            Assert.IsFalse(_camera.AimZoomActive, "死亡后必须关闭拉远");
            Assert.AreEqual(8f, _camera.CurrentDistance, 0.2f, "死亡后应回到普通距离");
        }
    }
}
```

- [ ] **步骤 2：运行测试验证失败**

运行：

```bash
bash tools/unity-tests.sh PlayMode AimZoomCameraTests
```

预期：三项 FAIL——`AimZoomActive` 始终为 false、`CurrentDistance` 保持 8（`Sys3CEntry` 还没有写入）。

- [ ] **步骤 3：在 Sys3CEntry 写入拉远开关**

把 `Sys3CEntry.Update` 中的：

```csharp
            if (_camera != null)
            {
                Vector2 lookInput = _inputManager.GetCameraRotationInput();
                _camera.UseMobilePitchLimits = _inputManager.IsLookFromTouch;
                _camera.HandleRotationInput(lookInput);
            }
```

改为：

```csharp
            if (_camera != null)
            {
                Vector2 lookInput = _inputManager.GetCameraRotationInput();
                _camera.UseMobilePitchLimits = _inputManager.IsLookFromTouch;
                _camera.HandleRotationInput(lookInput);
                _camera.AimZoomActive = IsMobileAimActive() && !_isDead;
            }
```

- [ ] **步骤 4：在死亡分支兜底取消蓄力**

把 `HandleInput` 中的：

```csharp
            // P6 ⑦：死亡期间不接受任何战斗输入（起手技能/普攻/跳跃）
            if (_isDead) return;
```

改为：

```csharp
            // P6 ⑦：死亡期间不接受任何战斗输入（起手技能/普攻/跳跃）。
            // 致死伤害路径已通过 InterruptCurrentSkill(Stun) 取消技能；这里再显式兜底，
            // 覆盖快速死亡帧与未来新增的 _isDead 路径，并让相机拉远条件立即转为 false。
            if (_isDead)
            {
                CancelAimingSkill();
                return;
            }
```

- [ ] **步骤 5：运行接线测试验证通过**

运行：

```bash
bash tools/unity-tests.sh PlayMode AimZoomCameraTests
```

预期：三项 PASS。若 `WaitForSubState` 超时，先确认 `PlayerEntryForTest.prefab` 的 `_characterSkills` 含 `Fireball_SkillQ.asset`（20003）。

- [ ] **步骤 6：编写死亡取消蓄力的 PlayMode 回归**

在 `PlayerDeathRespawnTests` 末尾插入（文件已有 `using Hotfix.GameSystems.Skills.Data;`）：

```csharp
        [UnityTest]
        public IEnumerator test_death_cancels_charging_fireball()
        {
            // Arrange：直接激活火球（绕过输入层），推进到 Charging
            _entry.SkillCoordinator.HandleInput(
                SkillInput.SkillToPosition((int)SkillID.Fireball,
                    _entry.transform.position + Vector3.forward * 5f));

            float deadline = Time.realtimeSinceStartup + 2f;
            while (_entry.SkillCoordinator.CurrentSubState != SkillSubState.Charging
                   && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.AreEqual(SkillSubState.Charging, _entry.SkillCoordinator.CurrentSubState,
                "火球应进入 Charging");

            // Act
            _entry.ApplyServerDamage(LethalDamage, Vector3.back);
            yield return null;
            yield return null;

            // Assert
            Assert.IsTrue(_entry.IsDead);
            Assert.AreNotEqual(SkillSubState.Charging, _entry.SkillCoordinator.CurrentSubState,
                "死亡必须取消蓄力中的火球");
        }
```

在文件头部补两个 using：

```csharp
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
```

- [ ] **步骤 7：运行死亡回归验证通过**

运行：

```bash
bash tools/unity-tests.sh PlayMode PlayerDeathRespawnTests
```

预期：全部 PASS。该用例是行为回归：现有致死伤害已走 `InterruptCurrentSkill(Stun)`，
新增的死亡分支兜底保证未来路径不退化。

- [ ] **步骤 8：更新 Sys3C README 死亡段**

在任务 3 新增的“火球瞄准拉远”小节末尾追加：

```markdown
- 死亡时 `HandleInput` 的死亡分支会兜底调用 `CancelAimingSkill()`（不发射、不进冷却）；
  现有致死伤害路径本身已通过 `InterruptCurrentSkill(Stun)` 取消技能，这条兜底覆盖快速死亡帧
  与未来新增的 `_isDead` 路径，同时让相机拉远条件立即转为 false。
```

- [ ] **步骤 9：Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Tests/PlayMode/AimZoomCameraTests.cs \
        Assets/Scripts/Tests/PlayMode/PlayerDeathRespawnTests.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md
git commit -m "feat(camera): zoom out during fireball aim"
```

---

## 任务 5：全量验证与真机验收

**文件：**
- 只读验证；若发现文档或场景值遗漏，补改后提交。

- [ ] **步骤 1：运行受影响的 EditMode 测试**

运行：

```bash
bash tools/unity-tests.sh EditMode TouchLookAreaTests
bash tools/unity-tests.sh EditMode KeyboardInputAdapterTests
bash tools/unity-tests.sh EditMode ThirdPersonCameraControllerTests
```

预期：三个类全部 PASS。

- [ ] **步骤 2：运行受影响的 PlayMode 测试**

运行：

```bash
bash tools/unity-tests.sh PlayMode MobileLookAreaLayoutTests
bash tools/unity-tests.sh PlayMode TouchLookAreaEventSystemTests
bash tools/unity-tests.sh PlayMode AimZoomCameraTests
bash tools/unity-tests.sh PlayMode PlayerDeathRespawnTests
```

预期：四个类全部 PASS。

- [ ] **步骤 3：检查场景序列化值**

用 Unity MCP 回读：

| 对象 | 字段 | 期望 |
|---|---|---|
| `LookArea` | `TouchLookArea.Sensitivity` | `0.125` |
| `Main Camera` | `ThirdPersonCameraController.AimDistance` | `11.5` |
| `Main Camera` | `ThirdPersonCameraController.ZoomDamping` | `8` |
| `Main Camera` | `ThirdPersonCameraController.Distance` | `8`（未变） |

- [ ] **步骤 4：真机验收（WebGL / 手机）**

按规格 §6 执行并逐项记录：

1. 观察区横向拖拽：180° 约半屏，松手立即停。
2. 按住摇杆，另一只手依次点普攻/技能按钮/右上空白：相机不跳；移动方向随视角正常变化。
3. 按住火球按钮拖动落点：相机平滑拉远到约 11.5，地面圈可辨；松手发射、拖到取消区松手、
   受击打断、切后台/横屏、死亡：都平滑回到 8。
4. 触屏关闭（`?touch=0`）或 PC：右键拖拽视角不变；键盘 1 键蓄力不拉远
   （编辑器里按住火球按钮拖动仍会拉远）。

如果第 2 项仍复现，按规格 §4.2 的判定表处理，不得把任务 2 的门控当作已修复。

- [ ] **步骤 5：检查工作区**

运行：

```bash
git status --short
```

预期：没有未提交的代码/场景改动；只剩与本功能无关的既有改动。

- [ ] **步骤 6：收尾 Commit（如有）**

如果步骤 3/4 之后补改了文档或场景值：

```bash
git add <changed files>
git commit -m "chore(camera): align aim zoom acceptance notes"
```

---

## 验收对照

| 规格判据 | 对应任务 |
|---|---|
| G1 触屏灵敏度 0.25°/px | 任务 1 |
| G2 第二根手指点按不转视角 | 任务 2 + 任务 5 真机 |
| G3 火球瞄准拉远与恢复 | 任务 3、4 + 任务 5 真机 |
| G4 死亡取消蓄力 | 任务 4 |
| 文档同步 | 任务 1、2、3 |
