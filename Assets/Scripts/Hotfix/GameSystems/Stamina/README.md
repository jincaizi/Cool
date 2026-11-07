# Stamina —— 精力模型

## 组成

- `StaminaSystem.cs`：纯 C# 资源模型。调用方每帧先 `TryConsume`，最后 `Tick` 一次；
  配置见 `StaminaConfig`（最大精力、冲刺 / 格挡消耗、回复速率、力竭延迟）。
- `StaminaRingRenderer.cs`：角色右上方精力环，运行时生成贴图，`Image.Filled` 径向填充；
  由 `Nameplate/EntityDisplayManager` 持有。

## 接入

- 冲刺：`Sys3C` 的输入 / 移动策略调用 `TryConsume`。
- 格挡：格挡消耗与 `BlockSucceededEvent` 链路；相关测试 `BlockStaminaTests`、`BlockMitigationE2ETests`。
- UI：`StaminaRingRenderer` 每帧读取 `StaminaSystem`。

## 测试

Editor：`StaminaSystemTests`、`StaminaSkillAssetTests`、`SkillCoordinatorStaminaTests`。
PlayMode：`StaminaIntegrationTests`、`StaminaRingTests`、`BlockStaminaTests`。

```bash
bash tools/unity-tests.sh EditMode StaminaSystemTests
bash tools/unity-tests.sh PlayMode StaminaIntegrationTests
```

## 相关模块

- [Sys3C](../Sys3C/README.md) —— 冲刺输入
- [Skills](../Skills/README.md) —— 技能消耗
- [Nameplate](../Nameplate/README.md) —— 精力环
