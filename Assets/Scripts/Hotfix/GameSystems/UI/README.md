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

## 测试

Editor：`UIManagerRegistrationTests`、`UIManagerLayerCanvasTests`、`UIPanelLifecycleTests`、`UIPanelContractTests`、
`UIPanelRegistryTests`、`UILayerTableTests`、`UISceneContentTests`、`ReadinessGateTests`。
PlayMode：`UIBootstrapTests`、`HudLifecycleTests`、`CharacterSelectPanelReopenTests`、`PlayerHudBindingTests`。

```bash
bash tools/unity-tests.sh EditMode UIManagerRegistrationTests
bash tools/unity-tests.sh PlayMode UIBootstrapTests
```

## 相关模块

- [Sys3C](../Sys3C/README.md) —— HUD 数据源与角色装配
- [Bag](../Bag/README.md) —— 背包面板
- [Nameplate](../Nameplate/README.md) —— 世界空间名字与飘字
