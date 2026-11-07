# Bag —— 背包系统

以数据和纯 C# 管理器为主，UI 面板可选。

## 目录

| 路径 | 内容 |
|---|---|
| `Core/Enums.cs` | 背包枚举；`Core/Events/` 放物品事件 |
| `Data/BagData.cs` / `Data/BagSlotData.cs` | 背包与槽位数据 |
| `Data/ItemData.cs` / `Data/ItemTemplate.cs` | 物品实例与模板 |
| `Runtime/BagManager.cs` | 单例管理器；增删、移动、堆叠、使用、锁定事件 |
| `Runtime/ItemFactory.cs` | 物品创建 |
| `UI/BagPanel.cs` | 背包面板 |

`BagManager` 通过 C# 事件（`OnItemAdded` 等）通知 UI，不依赖场景单例。

## 测试

本模块暂无专属测试。改动背包数据结构时至少跑一遍 EditMode 全量：

```bash
bash tools/unity-tests.sh EditMode
```

## 相关模块

- [UI](../UI/README.md) —— 面板框架
- [Nameplate](../Nameplate/README.md) —— 物品获得的飘字（如接入）
