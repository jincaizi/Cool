# UI —— UGUI 框架

## 运行时入口

- `Core/UIBootstrap.cs`：`RuntimeInitializeOnLoadMethod(AfterSceneLoad)` 统一创建 `UIManager` 与
  `EventSystem`，并 `DontDestroyOnLoad`；场景里不再放这两者。
- `Core/UIManager.cs`：面板注册、打开 / 关闭、层级 Canvas、就绪门。
- `Core/UIPanel.cs`：面板基类与生命周期。
- `Core/ReadinessGate.cs`：启动就绪门。
- `Core/ScreenAdapter.cs`：屏幕适配。
- `Const/UIConst.cs`：层定义唯一真值源（`Base` / `Main` / `Popup` / `Top` / `Guide`）。

## 面板

3 个内置面板全部通过 `UIManager.RegisterFromPrefab` 从 `Assets/Resources/Prefabs/UI/` 加载，
**不在场景里**：

- `Panel/CharacterSelect/CharacterSelectPanel`
- `Panel/HUD/PlayerHudPanel`
- `Panel/HUD/TargetPanel`

已无 `UIDataBinding` / `ViewModelBase` / Loading / Toast / Tips / Confirm。

HUD 联网读数：`PlayerHudPanel` 在 HP/MP 与 buff 行下方预留 `NetStatsText`（TMP 子物体，默认隐藏）。
联网时由 `ClientNetworkHost` 调用 `SetNetStats(string?)` 写入 `FPS xx  延迟 xxms`；传 null/空串隐藏。
单机模式不显示。

## 移动端输入

`Mobile/VirtualJoystick`、`Mobile/TouchLookArea`、`Mobile/MobileActionButtons`、`Mobile/SprintGestureState`。

- **普攻只认普攻按钮（所有平台）**：鼠标左键点击场景或 UI 都不再产生普攻；触控 UI 关闭时也没有
  攻击入口（`?touch=0` / `ForceNoTouch` 仅用于开发调试）。`TouchLookArea` 只是透明拖拽区，
  不参与攻击归属判定。
- 竖屏遮罩/失焦期间 `MobileInputBinder` 会把 `MobileUiRoot` 设为不激活并释放绑定；摇杆、观察区与
  按钮的 `OnDisable` 清除 held 状态，解除后必须重新按下。
- `ScreenAdapter.Update` 比较屏幕尺寸与 `Screen.safeArea` 的缓存值，地址栏伸缩或横竖屏切换
  只改安全区域时也会重算偏移。

## UI 倍率与布局

- **倍率解析**：`GameSettings.ResolveUIScale()` = 平台默认倍率（移动 / WebGL 用
  `MobilePlatformUIScale`，桌面 / 编辑器用 `DesktopPlatformUIScale`）× 全局 `UIScale`，钳在
  `[0.8, 1.5]`；`ResolveReferenceResolution()` = 设计分辨率 ÷ 该倍率。`MobileUiRoot` 挂
  `ScreenAdapter`（不启用安全区），按解析结果设置 `CanvasScaler` 的 reference resolution。
- **锚点**：`LookArea` 占右上象限（anchorMin `(0.5, 0.5)` / anchorMax `(1, 1)`）；技能与动作按钮
  统一右下锚点 `(1, 0)`；选人面板内容统一底部锚点 `(0.5, 0)`，相邻内容纵向净距至少 16。
- **观察区手势**：`TouchLookArea` 只接收真实触摸（鼠标事件忽略），按 `pointerId` 单指归属；
  失焦、暂停或 `OnDisable` 时复位手势。

## 测试

Editor：`UIManagerRegistrationTests`、`UIManagerLayerCanvasTests`、`UIPanelLifecycleTests`、`UIPanelContractTests`、
`UIPanelRegistryTests`、`UILayerTableTests`、`UISceneContentTests`、`ReadinessGateTests`。
PlayMode：`UIBootstrapTests`、`HudLifecycleTests`、`CharacterSelectPanelReopenTests`、`PlayerHudBindingTests`、`PlayerHudNetStatsTests`。

```bash
bash tools/unity-tests.sh EditMode UIManagerRegistrationTests
bash tools/unity-tests.sh PlayMode UIBootstrapTests
```

## 相关模块

## 移动技能 HUD

- `MobileActionButtons → MobileInputBinder → InputManager → Sys3CEntry` 是手机/WebGL 技能输入的唯一通路；UI 不直接调用技能协调器。
- 普攻为中心按钮；内弧逆时针排列防御、跳跃、Q、R，外弧逆时针排列火球（1）、炎爆（2）、陨石（3）。
- 五个技能按钮由 `MobileSkillButton` 显示顺时针径向冷却和向上取整的秒数；蓄力技能额外显示径向蓄力环。
- `MobileSkillBarPresenter` 从当前本地角色读取冷却、蓄力和剑气就绪真值。剑气可释放时普攻图标变暖金色并显示金框。
- UI 停用、WebGL 失焦、关闭触控模式或角色解绑时必须清除普攻及蓄力 held 状态，并取消攻击按住（不合成释放）、复位剑气金边，避免返回页面后自动施放。

## 火球瞄准摇杆与地面落点圈

- `MobileSkillAimJoystick` 挂在火球按钮上：**按下点为原点**，位移按 `_aimRadius`（90 Canvas 单位）
  归一；`_cancelEnterScale=1.4` / `_cancelExitScale=1.2` 构成取消回滞，`CancelArmed` 同时用于
  图标变红提示。组件只产出 `IsGestureActive` / `Aim` / `CancelArmed` 与 `AimReleaseKind`
  （Fire / Cancel），不直接调用技能协调器。
- `MobileInputBinder` 在 `LateUpdate` 帧末把瞄准状态与释放类型写进 `InputManager`；
  释放必须最后写，避免同帧 `pressed` 清掉释放锁存。
- `GroundAimIndicator` 是纯占位表现：无 `Collider`、不参与判定；由 `Sys3CEntry` 按落点与
  `ExplosionRadius` 定位缩放，颜色经 MaterialPropertyBlock 的 `_Color` 写入（普通 / 取消两色）。
- 瞄准期间**不显示蓄力环**：`MobileSkillBarPresenter` 对 `HoldToAim` 技能把蓄力值固定为 0，
  只保留径向冷却与倒计时。
- 失焦、关闭触控、角色解绑会复位手势（`ResetGesture`），不合成释放。

- [Sys3C](../Sys3C/README.md) —— HUD 数据源与角色装配
- [Bag](../Bag/README.md) —— 背包面板
- [Nameplate](../Nameplate/README.md) —— 世界空间名字与飘字
