# 体验改进：相机、观察区、UI 缩放、默认联网与随机昵称设计

日期：2026-11-09
状态：**Draft — 设计已按用户确认的决策冻结，待规格审查；尚未实现。**
工作流：Superpowers brainstorming → 用户确认 → 两轮代码审核（含 Unity 专家视角）→ 本 spec → 规格自检 → 用户审查 → writing-plans。
范围：本文件是两份规格中的第一份，只覆盖 5 个体验需求与支撑布局修复。4 个战斗/AI bug（跳跃闪避、跳跃中死亡复活卡姿势、R 技能偶发无伤害、怪物贴身不攻击）放入第二份《战斗与 AI 修复规格》，不在本文件实现。

相关模块：[Sys3C](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md)、[UI](../../../Assets/Scripts/Hotfix/GameSystems/UI/README.md)、[Nameplate](../../../Assets/Scripts/Hotfix/GameSystems/Nameplate/README.md)、[SyncLayer](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md)、[DataDefinition](../../../Assets/Scripts/AOT/DataDefinition/README.md)。

## 1. 目标与范围

### 1.1 用户已确定

- PC/Editor 端停用鼠标自由视角，改为**按住鼠标右键拖拽**转视角。
- 移动端/WebGL 在**右侧技能栏上方空白区**拖拽转视角；水平 yaw 可 360° 无限转，垂直 pitch 限制在 `[-10°, 25°]`。
- 高分辨率手机上 UI 太小；采用**自动平台倍率 + 可调全局倍率**：移动端/WebGL 默认 `1.15`，桌面/Editor 默认 `1.0`。
- 默认网络模式改为**联网**；连接失败保持联网并提示，玩家可回选人界面切单机。
- 玩家 ID 默认值从 `"Player"` 改为**首次生成的随机中文昵称**，玩家可修改；首次生成后持久化。
- UI 缩放同步作用于名字板、飘字、精力环（`EntityDisplayCanvas`）。
- 已有保存的用户名恰好是 `"Player"` 时**不迁移**；只有没有 `net.username` 存档的新安装生成随机名。
- 移动端观察区增加一个按 DPI 换算的起始拖拽死区，避免高 DPI 手机上轻点误转视角。
- UI 适配验收范围：**16:9–21:9 手机比例**；4:3 平板其余面板的既有裁切记为 backlog，不在本次承诺。

### 1.2 非目标

- 不改攻击输入归属、技能 HUD 交互逻辑、相机碰撞/缩放、动效。
- 不新增玩家可见的设置面板；`UIScale` 是 `GameSettings` 资产上的设计期参数，不是玩家滑块。
- 不做昵称唯一性/重复校验；不做断线自动重连。
- 不给 `MobileUiRoot` 启用安全区（notch/手势条适配留 backlog）。
- 不修 4:3 平板上 `TargetPanel` 等既有裁切；只保证本次重锚定的技能栏与选人面板 ServerSettings 在 16:9–21:9 手机比例下安全。
- 不实现第二份规格中的 4 个 bug。

### 1.3 交付边界

本规格的“完成”指：代码、场景/Prefab/资产改动、新增测试与 README 更新全部落地，且第 7 节验收项在 Editor（逻辑）与 WebGL/移动构建（视觉与输入）上都通过。规格本身不代表实现完成。

## 2. 现状证据

| 现状 | 证据 |
|---|---|
| PC/Editor 自由视角来自 `KeyboardInputAdapter` 直接读 `Mouse X/Y` | [KeyboardInputAdapter.cs:45](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs#L45) |
| 相机角度由 `Sys3CEntry.Update` 每帧喂给 `ThirdPersonCameraController.HandleRotationInput` | [Sys3CEntry.cs:278](../../../Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs#L278) |
| 场景相机 `MinPitch=-30`、`MaxPitch=60`、`MouseSensitivity=2` | [DemoDay.unity:1340](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity#L1340) |
| 触屏观察区 `LookArea` 当前锚定右半屏全高 `(0.5,0)–(1,1)`，Image 透明但 `raycastTarget=1` | [DemoDay.unity:3462](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity#L3462)、[DemoDay.unity:3424](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity#L3424) |
| 移动端 HUD 的 `MobileUiRoot` 自带 `Canvas + CanvasScaler + GraphicRaycaster`，参考分辨率 1920×1080，排序 2500 | [DemoDay.unity:18958](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity#L18958)、[DemoDay.unity:19002](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity#L19002) |
| `MobileUiRoot` 序列化 `localScale` 为 `(0,0,0)` | [DemoDay.unity:18920](../../../Assets/SimpleLowPolyNature/Scenes/DemoDay.unity#L18920) |
| UIManager 层 Canvas 已有 `CanvasScaler + ScreenAdapter`，参考分辨率 1920×1080 | [UIManager.cs:146](../../../Assets/Scripts/Hotfix/GameSystems/UI/Core/UIManager.cs#L146)、[ScreenAdapter.cs:53](../../../Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs#L53) |
| `EntityDisplayCanvas` 独立创建 CanvasScaler，参考分辨率 1920×1080 | [EntityDisplayManager.cs:55](../../../Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs#L55) |
| 技能/动作按钮全部为中心锚点；最高按钮 Meteor 顶边在画布中心下方 87px（1920×1080） | `DemoDay.unity` 的 MobileUiRoot 子节点 RectTransform |
| 选人面板 `ServerSettings` 为中心锚点 `(0, -435)`，尺寸 640×132 | [CharacterSelectPanel.prefab](../../../Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab) |
| `NetworkModeService.Load()` 无存档时返回 `SinglePlayer`；`DefaultUsername = "Player"` | [NetworkModeService.cs:26](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs#L26)、[NetworkModeService.cs:39](../../../Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs#L39) |
| TMP 字体 `ZhunYuan SDF` 为 Dynamic、单 1024 图集 | `Assets/PreRes/Font/ZhunYuan SDF.asset` 的 `m_AtlasPopulationMode: 1`、`m_IsMultiAtlasTexturesEnabled: 0` |
| 项目使用旧 Input Manager（`activeInputHandler: 0`），`Mouse X/Y` 轴存在 | `ProjectSettings/ProjectSettings.asset`、`ProjectSettings/InputManager.asset` |
| `PlayerHudPanel`、`TargetPanel` 位于 Base 层（排序 1000），均无可交互 Button；名字板/飘字 `raycastTarget=false` | `PlayerHudPanel.cs:11`、`TargetPanel.cs:11`、`NameplateRenderer.cs:308`、`FloatTextRenderer.cs:194` |

## 3. 决策摘要

| 决策 | 选择 | 理由 |
|---|---|---|
| 交付拆分 | 先体验、后 bug，两份规格 | 每份可独立验收/回滚；本文件只做体验 |
| PC/Editor 视角 | 按住鼠标右键拖拽 | 不跟随鼠标移动；左键继续留给 UI/交互 |
| 触屏观察区 | 右上象限；水平 360°、垂直 `[-10,25]` | 技能栏在右下，右上象限天然避开；水平自由、垂直小幅 |
| 垂直限位来源 | 按本帧视角输入来源切换 | PC 右键用 `[-30,60]`，触屏用 `[-10,25]`，互不牺牲 |
| 观察区死区 | 12px@160dpi，按 DPI 换算 | 高 DPI 轻点不误转；只影响 LookArea |
| UI 缩放 | 反算参考分辨率 + 平台/全局倍率 | CanvasScaler 继续用 ScaleWithScreenSize；安全区逻辑不变 |
| 平台倍率 | WebGL/移动端 1.15；桌面/Editor 1.0 | 按用户选择；Editor 不伪装成移动端 |
| 手机 HUD 缩放 | 给 `MobileUiRoot` 加 `ScreenAdapter`（`ApplySafeArea=false`） | 手机 HUD 有自己的 Canvas，不改它就不会缩放 |
| 布局修复 | 技能/动作按钮改右下锚点；ServerSettings 改底部锚点 | 1.15 倍率下 16:9–21:9 不被裁切；16:9 绝对坐标不变 |
| 默认联网 | 无 `net.mode` → Online | 新安装默认联网；已有存档优先 |
| 随机昵称 | 首次 `LoadUsername()` 生成中文 4 字并持久化 | 面板、网络、HUD 读到同一个名字；玩家修改后确认保存 |
| 昵称迁移 | 不迁移旧 `"Player"` | 不覆盖玩家自定义值；新安装才生效 |
| 名字板/飘字/精力环 | 跟随 UI 倍率 | 用户确认；高分辨率下可读性一起提升 |
| 验收比例 | 16:9–21:9 手机 | 4:3 平板其余布局是既有限制，不在本次承诺 |

## 4. 设计

### 4.1 相机输入

#### 4.1.1 PC/Editor 右键拖拽

`KeyboardInputAdapter` 增加纯函数门控，`GetCameraRotationInput()` 只在右键按住时透传鼠标轴：

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

- `HasCameraRotationInput()` 继续调用 `GetCameraRotationInput()`，自动继承门控。
- 不锁定/隐藏光标；不改 `MouseSensitivityX/Y`。
- 触屏观察区仍优先：`JoystickInputAdapter` 先读 `TouchLookArea.Delta`，空闲时回退到键鼠适配器；右键拖拽在触屏 UI 显示时仍可用。
- `InputBlockGate` 阻断时 `InputManager.GetCameraRotationInput()` 已返回零；本改动不改变阻断语义。

#### 4.1.2 触屏来源标记与俯仰限位

`JoystickInputAdapter` 记录本帧视角是否来自触屏：

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

`InputManager` 暴露只读标记；在 `GetCameraRotationInput()` 中先取输入、再写标记：

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
        if (look.sqrMagnitude > 0f) return Vector2.zero;
        _lookNeedsRelease = false;
    }

    return look;
}
```

`Sys3CEntry.Update` 必须**先读输入，再设置相机模式**：

```csharp
if (_camera != null)
{
    Vector2 lookInput = _inputManager.GetCameraRotationInput();
    _camera.UseMobilePitchLimits = _inputManager.IsLookFromTouch;
    _camera.HandleRotationInput(lookInput);
}
```

`ThirdPersonCameraController` 增加移动端限位：

```csharp
[Header("Mobile Pitch Limits")]
[Tooltip("触屏拖拽时的最低俯仰角")]
public float MobileMinPitch = -10f;
[Tooltip("触屏拖拽时的最高俯仰角")]
public float MobileMaxPitch = 25f;

public bool UseMobilePitchLimits { get; set; }

internal float VerticalAngle => _verticalAngle;

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

- 水平 yaw 不设上下限，保持 360°。
- 场景相机显式写入 `MobileMinPitch: -10`、`MobileMaxPitch: 25`；`UseMobilePitchLimits` 是运行时属性，不入序列化。
- 从 PC 右键切到触屏拖拽时，首次触屏输入会把俯仰角钳到 `[-10,25]`；反向切回不自动扩回，由后续输入决定。
- 阻断/释放门控分支把 `IsLookFromTouch` 置 false，避免沿用上一帧触屏标记。

#### 4.1.3 移动端观察区与起始死区

`TouchLookArea` 增加 DPI 换算的起始死区；只影响 LookArea，不影响摇杆与火球瞄准：

```csharp
[SerializeField] private float _sensitivity = 0.2f;
[Tooltip("起始拖拽死区（160dpi 基准像素数，运行时按 Screen.dpi 换算）")]
[SerializeField] private float _minDragPixelsAt160Dpi = 12f;

private Vector2 _pending;
private Vector2 _delta;
private Vector2 _dragAccum;
private bool _dragStarted;

public Vector2 Delta => _delta;

internal static float ResolveDragThresholdPixels(float minPixelsAt160Dpi, float dpi)
{
    float safeDpi = dpi > 0f && float.IsFinite(dpi) ? dpi : 160f;
    return minPixelsAt160Dpi * Mathf.Max(1f, safeDpi / 160f);
}

private float DragThresholdPixels
    => ResolveDragThresholdPixels(_minDragPixelsAt160Dpi, Screen.dpi);

public void OnDrag(PointerEventData e)
{
    if (!_dragStarted)
    {
        _dragAccum += e.delta;
        if (_dragAccum.magnitude < DragThresholdPixels) return;

        _dragStarted = true;
        _pending += _dragAccum * _sensitivity;
        _dragAccum = Vector2.zero;
        return;
    }

    _pending += e.delta * _sensitivity;
}

public void OnPointerUp(PointerEventData e) => ResetGesture();

private void OnDisable() => ResetGesture();

private void ResetGesture()
{
    _pending = Vector2.zero;
    _delta = Vector2.zero;
    _dragAccum = Vector2.zero;
    _dragStarted = false;
}

private void LateUpdate()
{
    _delta = _pending;
    _pending = Vector2.zero;
}
```

- 死区按屏幕物理距离换算（12px@160dpi ≈ 1.9mm）；UI 缩放不改变该阈值。
- 超过阈值的首帧把累计位移一并输出，不丢开头一段。
- `OnPointerUp`、`OnDisable` 重置；失焦/隐藏 UI 的既有清理路径继续有效。
- EventSystem 默认 10px 拖拽阈值仍在，死区是在其之上的观察区专属门槛。

### 4.2 移动端 HUD 布局

#### 4.2.1 LookArea 改为右上象限

场景 `DemoDay.unity` 的 `LookArea`（RectTransform fileID `312999717`）：

| 字段 | 当前 | 修改后 |
|---|---|---|
| `anchorMin` | `(0.5, 0)` | `(0.5, 0.5)` |
| `anchorMax` | `(1, 1)` | `(1, 1)`（不变） |
| `anchoredPosition` | `(0, 0)` | `(0, 0)`（不变） |
| `sizeDelta` | `(0, 0)` | `(0, 0)`（不变） |
| pivot | `(0.5, 0.5)` | 不变 |

- 右上象限在 16:9/20:9/21:9 都位于技能栏上方；技能栏按钮是后兄弟节点，重叠区域仍由按钮优先接收触摸。
- `TouchLookArea` 与 `Image(raycastTarget=1)` 保留；透明拖拽区不需要 Graphic 颜色。

#### 4.2.2 技能/动作按钮改右下锚点

把右侧 8 个按钮从中心锚点 `(0.5,0.5)` 改为右下锚点 `(1,0)`。16:9（1920×1080）绝对坐标完全不变；其他比例自动贴右下角。

| 对象 | 场景 fileID | 旧 anchoredPosition | 新 anchoredPosition | size |
|---|---:|---:|---:|---:|
| AttackButton | 1377499757 | `(740, -344)` | `(-220, 196)` | 124×124 |
| DefendButton | 1039151933 | `(610, -390)` | `(-350, 150)` | 82×82 |
| JumpButton | 550654423 | `(620, -294)` | `(-340, 246)` | 82×82 |
| SkillQButton | 84789699 | `(673, -207)` | `(-287, 333)` | 82×82 |
| SkillRButton | 1030168184 | `(780, -220)` | `(-180, 320)` | 82×82 |
| FireballButton | 1752636657 | `(520, -300)` | `(-440, 240)` | 74×74 |
| ChargeBlastButton | 2108524315 | `(585, -170)` | `(-375, 370)` | 74×74 |
| MeteorButton | 1248091039 | `(735, -124)` | `(-225, 416)` | 74×74 |

- 所有按钮 `anchorMin = anchorMax = (1,0)`，pivot 保持 `(0.5,0.5)`，size 不变。
- `TargetSwitchButton` 已是右下锚点 `(-123,141)`，`Joystick` 已是左下锚点 `(220,220)`，均不动。
- 新偏移的计算：`newX = oldX - 960`，`newY = oldY + 540`；1920×1080 下逐值等价。

#### 4.2.3 选人面板 ServerSettings 改底部锚点

`Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab` 的 `ServerSettings`：

| 字段 | 当前 | 修改后 |
|---|---|---|
| `anchorMin` / `anchorMax` | `(0.5, 0.5)` | `(0.5, 0)` |
| `anchoredPosition` | `(0, -435)` | `(0, 105)` |
| `sizeDelta` | `(640, 132)` | 不变 |
| pivot | `(0.5, 0.5)` | 不变 |

- 1920×1080 下绝对位置不变（原中心 `y=105`）；底部保留 39px 边距。
- `ConfirmButton` 等其余中心元素在 16:9–21:9、1.15 下仍有正边距，本次不动。

### 4.3 UI 缩放

#### 4.3.1 `GameSettings` 字段与解析器

在 `Assets/Scripts/AOT/DataDefinition/GameSettings.cs` 的 Display 区新增：

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

- 平台判定：`RuntimePlatform.WebGLPlayer || Application.isMobilePlatform` 取移动端倍率；其余（Windows/Mac/Linux/Editor）取桌面倍率。
- `Application.isMobilePlatform` 在 WebGL 移动浏览器上不可靠，因此 WebGL 用 `RuntimePlatform.WebGLPlayer` 显式覆盖。
- `<=0`、NaN、Infinity 的倍率回退 1.0；最终有效倍率钳制 `[0.8, 1.5]`。
- 参考分辨率 = `ReferenceResolution / 有效倍率`；1920×1080、1.15 时是 `1669.5652 × 939.1304`。

#### 4.3.2 应用点

1. **UIManager 层 Canvas**：`ScreenAdapter.ConfigureCanvasScaler()` 改用 `GameSettings.Instance.ResolveReferenceResolution()`。已有所有层 Canvas 都挂 `ScreenAdapter`，无需新增接线。
2. **MobileUiRoot**：场景对象自带 CanvasScaler，新增 `ScreenAdapter` 组件（script GUID `1ebd4d9030696e847b2b508c90734e36`），序列化 `ApplySafeArea: 0`、`AutoRefreshOnResize: 1`。这样它只复用 CanvasScaler 配置，不改变安全区行为。
3. **EntityDisplayCanvas**：`EntityDisplayManager.CreateCanvas()` 把 `scaler.referenceResolution` 改为 `ResolveReferenceResolution()`；名字板、飘字、精力环跟随 UI 倍率。
4. **MobileUiRoot 序列化检查**：场景编辑时把 `MobileUiRoot` 的 `m_LocalScale` 显式设为 `(1,1,1)`，并在 PlayMode 断言 `lossyScale` 非零。当前序列化为 `(0,0,0)`，不要依赖 Canvas 对零缩放的运行时覆盖。

`ScreenAdapter` 与 `EntityDisplayManager` 都继续使用 `ScreenMatchMode.MatchWidthOrHeight`、`matchWidthOrHeight = 0.5`，不改变现有比例策略。

#### 4.3.3 资产值与零值防护

`Assets/Resources/Setting/GameSettings.asset` 显式写入：

```yaml
UIScale: 1
MobilePlatformUIScale: 1.15
DesktopPlatformUIScale: 1
```

- 旧资产缺字段时可能读到 0；上面的 `SanitizeScale` 与 clamp 是运行时防线，资产值仍必须显式写。
- Editor 的 `Application.platform` 是 `WindowsEditor/OSXEditor`，默认倍率 1.0；UI 1.15 只在 WebGL/移动构建生效。要在 Editor 预览，临时把 `UIScale` 设为 1.15，验收后改回 1。

### 4.4 默认联网

`NetworkModeService.Load()` 改为：

```csharp
if (!PlayerPrefs.HasKey(PrefsKey)) return NetworkMode.Online;
int raw = PlayerPrefs.GetInt(PrefsKey);
return raw == (int)NetworkMode.Online ? NetworkMode.Online : NetworkMode.SinglePlayer;
```

- 无 `net.mode` 存档 → Online；已有存档（包括 SinglePlayer）优先，不迁移。
- 非法整数保持原语义：不是 Online 值就按 SinglePlayer。
- 连接失败沿用 `ClientNetworkHost.HandleError`：屏幕提示实际尝试的端点与原因，3 秒后回选人界面；面板仍可切单机。该流程已实现，本规格只改默认值。
- 验收需清空 `net.mode` 才能看到新默认；已有 Editor PlayerPrefs 会压过默认。

### 4.5 随机中文昵称

新增 `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RandomNicknameGenerator.cs`：

```csharp
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
```

- 生成结果是 4 个常用汉字（2 字前缀 + 2 字后缀），不使用生僻字；TMP 字体为 Dynamic，单 1024 图集对该词池足够。
- 不保证唯一；同名由服务端/名册按现有语义处理。

`NetworkModeService.LoadUsername()` 改为：

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

- 首次调用生成并立即持久化；之后每次返回同一个名字。
- `"Player"` 是普通已保存值，不迁移；只有没有存档才生成。
- `SaveConnection` 的用户名兜底从 `DefaultUsername` 改为当前 `LoadUsername()`：

```csharp
string safeUser = string.IsNullOrWhiteSpace(username)
    ? LoadUsername()
    : username.Trim();
```

- `DefaultUsername` 常量保留（`Sys3CEntryStatsProvider` 等既有兜底仍在；`LoadUsername` 实际不会再返回空）。
- 输入框留空确认时保留当前随机/已保存名，不再回落 `"Player"`。

### 4.6 场景/Prefab/资产编辑流程

RectTransform 的 `anchorMin/anchorMax/anchoredPosition` **不手改 YAML**：容易和 `m_LocalPosition`、prefab override 不一致。实现时使用 Unity Editor/MCP：

- 场景：`rql-mutate` / `gameobject-component-add` → 修改 → `scene-save`。
- Prefab：`assets-prefab-open` → 修改 → `assets-prefab-save` / `assets-prefab-close`。
- `GameSettings.asset`：`assets-modify` 或 Inspector；修改后回读字段确认。
- 所有改动完成后跑第 6 节测试；`MobileLookAreaLayoutTests` 会验证锚点与 CanvasScaler 实际值。

## 5. 测试

### 5.1 新增/修改测试

| 测试文件 | 用例（`test_...` 命名） | 断言 |
|---|---|---|
| `Assets/Scripts/Tests/Editor/Sys3C/KeyboardInputAdapterTests.cs` | `test_keyboard_look_is_zero_without_right_button` | `GateLookToRightDrag((3,4), false) == zero` |
| 同上 | `test_keyboard_look_passes_through_with_right_button` | `GateLookToRightDrag((3,4), true) == (3,4)` |
| `Assets/Scripts/Tests/Editor/TouchLookAreaTests.cs` | `test_drag_below_threshold_does_not_emit` | 小于死区的拖拽经 `LateUpdate` 后 `Delta == zero` |
| 同上 | `test_drag_above_threshold_emits_accumulated_delta` | 超过死区后 `Delta` 非零且包含首段累计 |
| 同上 | `test_pointer_up_resets_dead_zone` | 抬起后再拖小位移仍不输出 |
| 同上 | `test_drag_threshold_scales_with_dpi` | `ResolveDragThresholdPixels(12,160)=12`、`(12,320)=24`、`(12,0)=12` |
| `Assets/Scripts/Tests/Editor/Sys3C/InputManagerLookSourceTests.cs` | `test_look_source_reports_touch_when_touch_look_active` | `IsLookFromTouch == true` |
| 同上 | `test_look_source_reports_mouse_when_touch_look_idle` | `IsLookFromTouch == false` |
| `Assets/Scripts/Tests/Editor/Sys3C/ThirdPersonCameraControllerTests.cs` | `test_mobile_pitch_limits_clamp_to_minus10_and_25` | `VerticalAngle` 钳到 `25`/`-10` |
| 同上 | `test_desktop_pitch_limits_stay_minus30_and_60` | `VerticalAngle` 钳到 `60`/`-30` |
| `Assets/Scripts/Tests/Editor/UIScaleResolverTests.cs` | `test_platform_scale_webgl_and_mobile_is_115` | WebGL/Android/iOS → 1.15 |
| 同上 | `test_platform_scale_desktop_and_editor_is_1` | Windows/Mac/Editor → 1.0 |
| 同上 | `test_global_scale_multiplies_and_clamps` | 1.2×1.15=1.38；2.0 钳到 1.5；0 回退 1.0 |
| 同上 | `test_reference_resolution_divides_by_effective_scale` | 1920×1080 / 1.15 = 1669.565×939.130 |
| `Assets/Scripts/Tests/Editor/SyncLayer/NetworkModeServiceTests.cs` | `test_default_mode_is_online_without_save` | 删除 `net.mode` → Online |
| 同上 | `test_saved_single_player_wins` | 保存 SinglePlayer → SinglePlayer |
| 同上 | `test_username_is_generated_once_and_persisted` | 两次返回同一 4 字中文名，不是 `"Player"` |
| 同上 | `test_empty_connection_username_falls_back_to_saved_name` | `SaveConnection("", "", "")` 保留当前名 |
| `Assets/Scripts/Tests/Editor/SyncLayer/RandomNicknameGeneratorTests.cs` | `test_generated_name_is_four_chinese_characters` | 匹配 `^[一-鿿]{4}$` |
| 同上 | `test_seeded_generator_is_deterministic` | 同种子两次结果相同 |
| `Assets/Scripts/Tests/Editor/CharacterSelectPanelLayoutTests.cs` | `test_server_settings_is_bottom_anchored` | anchor `(0.5,0)`、pos `(0,105)`、size 640×132 |
| `Assets/Scripts/Tests/Editor/MobileHudLayoutFitTests.cs` | `test_mobile_hud_fits_phone_aspects_at_115_scale` | 16:9/20:9/21:9 下技能栏与 ServerSettings 边距为正（复刻 CanvasScaler log 空间公式） |
| `Assets/Scripts/Tests/PlayMode/MobileLookAreaLayoutTests.cs` | `test_mobile_look_area_uses_top_right_quadrant` | LookArea anchor `(0.5,0.5)–(1,1)`，`TouchLookArea` 存在，Image raycastTarget |
| 同上 | `test_mobile_action_buttons_are_bottom_right_anchored` | 8 个按钮 anchor `(1,0)`，世界角点在屏内 |
| 同上 | `test_mobile_ui_root_applies_effective_reference_resolution` | `MobileUiRoot.lossyScale` 非零；CanvasScaler.referenceResolution == `ResolveReferenceResolution()` |
| 同上 | `test_mobile_look_area_leaves_blank_drag_region_above_skill_cluster` | 最上方技能按钮顶边之上仍有 ≥100 Canvas 单位的空白高度；按钮与 LookArea 矩形重叠时由按钮优先 |

`MobileHudLayoutFitTests` 的按钮偏移/尺寸常量与 DemoDay 场景一致；场景布局改动必须同步更新该测试，避免测试通过但场景已漂移。

### 5.2 现有测试调整

- `MobileInputAdapterTests`：补 `LastLookFromTouch` 在触屏/键鼠回退两种路径的断言。
- `CharacterSelectPanelReopenTests`：保存/恢复 `net.mode`、`net.host`、`net.port`、`net.username`；测试开始设 SinglePlayer。原因：`OnShow` 现在会生成并保存随机昵称，`Confirm` 会写连接设置，默认联网也会改变 `_mode`。
- `MobileTouchInputTests`、`MobileSkillHudTests`：加载 DemoDay 前保存/恢复 `net.mode` 与 `net.username`，并设 SinglePlayer，避免默认联网与随机昵称生成污染后续测试。
- `ClientRespawnWaitTests`：已保存/恢复模式与连接名，无需改动；它显式设 Online。
- 所有新增测试遵循 `.claude/rules/test-standards.md` 的 `test_[system]_[scenario]_[expected_result]` 命名与 Arrange/Act/Assert 结构。

### 5.3 命令

```bash
bash tools/unity-tests.sh EditMode KeyboardInputAdapterTests
bash tools/unity-tests.sh EditMode TouchLookAreaTests
bash tools/unity-tests.sh EditMode InputManagerLookSourceTests
bash tools/unity-tests.sh EditMode ThirdPersonCameraControllerTests
bash tools/unity-tests.sh EditMode UIScaleResolverTests
bash tools/unity-tests.sh EditMode NetworkModeServiceTests
bash tools/unity-tests.sh EditMode RandomNicknameGeneratorTests
bash tools/unity-tests.sh EditMode CharacterSelectPanelLayoutTests
bash tools/unity-tests.sh EditMode MobileHudLayoutFitTests
bash tools/unity-tests.sh PlayMode MobileLookAreaLayoutTests
bash tools/unity-tests.sh PlayMode MobileTouchInputTests
```

## 6. 验收标准（数值）

### 6.1 相机与输入

- `GateLookToRightDrag((3,4), false) == Vector2.zero`；`GateLookToRightDrag((3,4), true) == (3,4)`。
- PC/Editor：不按右键移动鼠标，相机 yaw/pitch 不变；按住右键拖拽，yaw/pitch 变化。
- 移动端触屏拖拽：`VerticalAngle` 不超过 `25`、不低于 `-10`；水平 yaw 可连续超过 360°。
- 观察区死区：12px@160dpi；`ResolveDragThresholdPixels(12,160)=12`、`(12,320)=24`、`(12,0)=12`；低于阈值拖拽 `Delta == zero`。
- 阻断/失焦后恢复：`IsLookFromTouch` 不沿用旧值，必须重新按下/拖拽。

### 6.2 布局

- LookArea anchor `(0.5,0.5)–(1,1)`；8 个技能/动作按钮 anchor `(1,0)`；ServerSettings anchor `(0.5,0)`、pos `(0,105)`。
- 在 1.15 倍率、`matchWidthOrHeight=0.5` 下，以下屏幕的 Canvas 尺寸与边距必须为正：

| 屏幕 | 有效参考分辨率 | Canvas 尺寸（约） | 技能栏底边距 | ServerSettings 底边距 |
|---|---:|---:|---:|---:|
| 1920×1080 | 1669.57×939.13 | 1669.57×939.13 | Attack 底边 134 | 39 |
| 2400×1080 | 1669.57×939.13 | 1866.6×840.0 | Attack 底边 134 | 39 |
| 2520×1080 | 1669.57×939.13 | 1913.3×819.9 | Attack 底边 134 | 39 |

- 技能栏距右边最大偏移 477、距底边最大顶高 453（相对右下锚点）；上述 Canvas 尺寸均大于这些值。
- 4:3 平板不在本表验收范围；其 `TargetPanel` 等既有裁切记 backlog。

### 6.3 UI 缩放

- WebGL/Android/iOS：`ResolveUIScale() = 1.15 × UIScale`；桌面/Editor：`1.0 × UIScale`；有效倍率钳制 `[0.8,1.5]`。
- 1920×1080、1.15 → `ResolveReferenceResolution() = (1669.5652, 939.1304)`（容差 0.01）。
- `MobileUiRoot`、UIManager 层 Canvas、`EntityDisplayCanvas` 三处使用同一参考分辨率；`MobileUiRoot.lossyScale` 非零。
- `GameSettings.asset` 实际序列化值为 `UIScale: 1`、`MobilePlatformUIScale: 1.15`、`DesktopPlatformUIScale: 1`。
- Editor 默认预览是 1.0；视觉验收必须在 WebGL/移动构建中做。

### 6.4 默认联网与昵称

- 删除 `net.mode` 后 `NetworkModeService.Load() == Online`；保存 SinglePlayer 后返回 SinglePlayer。
- 删除 `net.username` 后连续两次 `LoadUsername()` 返回同一个 4 字中文名，且不等于 `"Player"`；PlayerPrefs 中已持久化。
- 已有 `net.username = "Player"` 保持 `"Player"`（不迁移）。
- 输入框留空确认时，`SaveConnection` 保留当前 `LoadUsername()` 的值。
- 连接失败时仍保持 Online，屏幕提示端点/原因，3 秒后回选人；玩家可切 SinglePlayer。

## 7. 风险与已知限制

- **4:3 平板**：本次只修技能栏与 ServerSettings；`TargetPanel` 等既有裁切不在范围。
- **安全区/notch**：`MobileUiRoot` 不启用 `ApplySafeArea`；右上观察区可能被右侧刘海遮挡一部分，留 backlog。
- **WebGL 桌面右键**：浏览器可能弹右键菜单；本功能目标 PC/Editor，WebGL 主要面向移动端。
- **TMP 单图集**：Dynamic 字体可在运行时补字，但只有一个 1024 图集；昵称词池只用常用字，验收检查输入框/名字板无缺字。
- **UIScale 极值**：有效倍率钳制 `[0.8,1.5]`；本次只验收默认 1.15，更高倍率不在验收范围。
- **LookArea 拦截**：透明观察区在 MobileUiRoot Canvas（2500）上，高于 HUD Base（1000）；当前 HUD 无可交互按钮，未来在右上角加可交互 HUD 会被观察区挡住。
- **Editor 预览**：Editor 走桌面 1.0，不能直接看到 1.15；临时把 `UIScale` 设为 1.15 预览。
- **MobileUiRoot localScale**：当前序列化为 0；本规格在场景编辑时显式设 `(1,1,1)`，并用 PlayMode 断言 `lossyScale` 非零，不依赖 Canvas 覆盖。
- **名字板/飘字/精力环**：跟随 UI 倍率一起放大；精力环 offset 也随 `canvas.scaleFactor` 放大，验收时确认位置合理。
- **昵称唯一性**：不保证不重名；不迁移旧 `"Player"`。
- **PC 右键拖拽**：不锁光标，光标到屏幕边缘后该方向拖拽受限；本次不处理。

## 8. 改动清单

### 8.1 代码

- `Assets/Scripts/AOT/DataDefinition/GameSettings.cs`：`UIScale` 字段与解析器。
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/KeyboardInputAdapter.cs`：右键门控。
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/JoystickInputAdapter.cs`：`LastLookFromTouch`。
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Input/InputManager.cs`：`IsLookFromTouch`。
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`：相机模式设置顺序。
- `Assets/Scripts/Hotfix/GameSystems/Sys3C/Camera/ThirdPersonCameraController.cs`：移动端俯仰限位。
- `Assets/Scripts/Hotfix/GameSystems/UI/Mobile/TouchLookArea.cs`：DPI 死区。
- `Assets/Scripts/Hotfix/GameSystems/UI/Core/ScreenAdapter.cs`：使用解析后的参考分辨率。
- `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs`：使用解析后的参考分辨率。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/NetworkModeService.cs`：默认联网、随机昵称、空用户名兜底。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/RandomNicknameGenerator.cs`：新增。

### 8.2 场景/Prefab/资产

- `Assets/SimpleLowPolyNature/Scenes/DemoDay.unity`：
  - LookArea 右上象限。
  - 8 个技能/动作按钮右下锚点。
  - MobileUiRoot 新增 `ScreenAdapter`（`ApplySafeArea=0`）。
  - 相机新增 `MobileMinPitch=-10`、`MobileMaxPitch=25`。
  - `MobileUiRoot` 的 `m_LocalScale` 显式设为 `(1,1,1)`（当前序列化为 0）。
- `Assets/Resources/Prefabs/UI/CharacterSelectPanel.prefab`：ServerSettings 底部锚点。
- `Assets/Resources/Setting/GameSettings.asset`：三个 UI 倍率字段。

### 8.3 测试

- 新增：`KeyboardInputAdapterTests`、`TouchLookAreaTests`、`InputManagerLookSourceTests`、`ThirdPersonCameraControllerTests`、`UIScaleResolverTests`、`NetworkModeServiceTests`、`RandomNicknameGeneratorTests`、`CharacterSelectPanelLayoutTests`、`MobileHudLayoutFitTests`、`MobileLookAreaLayoutTests`。
- 修改：`MobileInputAdapterTests`、`CharacterSelectPanelReopenTests`、`MobileTouchInputTests`、`MobileSkillHudTests`。

### 8.4 文档

- `Assets/Scripts/Hotfix/GameSystems/Sys3C/README.md`：右键拖拽、触屏俯仰限位、观察区死区。
- `Assets/Scripts/Hotfix/GameSystems/UI/README.md`：UI 倍率、MobileUiRoot ScreenAdapter、LookArea 右上象限、技能按钮右下锚点、ServerSettings 底部锚点。
- `Assets/Scripts/Hotfix/GameSystems/SyncLayer/README.md`：默认联网、随机中文昵称、已有存档优先。
- `Assets/Scripts/AOT/DataDefinition/README.md`：`GameSettings` 的 UI 倍率字段（一句说明）。

## 9. 后续

- 本规格实现并验收后，按用户选择进入第二份《战斗与 AI 修复规格》，覆盖 4 个 bug：跳跃闪避、跳跃中死亡复活卡姿势、R 技能多 tick 伤害、怪物贴身不攻击。
- 4:3 平板其余面板布局、`MobileUiRoot` 安全区、WebGL 桌面右键菜单、玩家可调 UI 大小均记为 backlog，不在本规格实现。
