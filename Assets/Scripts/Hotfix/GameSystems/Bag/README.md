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

## 属性剑气武器背包（2026-11-14）

状态：Plan 01 的权威换装已完成；2026-11-16 跟进补齐简易列表、八把模型与交互边界。
视觉/交互/出包验收待人工。

- 登录基线 `WeaponInventoryBaseline.Weapons` 经 `BagManager.ApplyServerWeapons` 重建背包：
  物品使用服务器实例 ID/模板 ID，武器模板按需注册为 `ItemType.Equipment`。
- 背包行点击装备调用 `BagManager.RequestEquip(item)` 触发 `OnEquipRequested`；
  联网宿主接 `CombatNetworkBridge.RequestEquip` 发权威换装请求，服务器确认后才更新装备。
  拒绝时宿主恢复到确认携带的服务器摘要。

## 测试

本模块暂无专属测试。改动背包数据结构时至少跑一遍 EditMode 全量：

```bash
bash tools/unity-tests.sh EditMode
```

## 相关模块

- [UI](../UI/README.md) —— 面板框架
- [Nameplate](../Nameplate/README.md) —— 物品获得的飘字（如接入）

## 简易武器背包

DemoDay 联网进入后，右上角「背包」显示服务器发放的剑。点一行发起换装，服务器确认后刷新「已装备」和本地角色右手模型。角色死亡或技能执行期间暂不接受点选。

- `UI/WeaponBagController.cs` 是连接级交互会话：同一时刻只允许一个在途请求；`ClientEquipSeq`
  匹配才结束等待；等待超过 5 秒提示「换装确认超时，可重试」并恢复点击。过期回包可推进权威摘要，
  但不结束当前请求、不覆盖其提示。
- 请求发出后保留原「已装备」标记和模型；确认/拒绝后列表统一读取 `ClientEquipmentState`
  的权威摘要。拒绝显示「换装失败：原因」（空原因退化为「换装失败」）。
- 确认在技能/死亡期间到达时列表当帧更新，持剑外观延迟到动作结束，且只应用最新摘要。
  缺失元素剑外观时回退原配剑并显示「武器外观暂不可用」，有效资源或默认剑恢复后清除。
- 角色重建保留同一连接的装备摘要与在途序号并重放外观；断线由宿主 `Reset` 清空请求、提示与面板。
- `UI/WeaponBagView.cs` 的 `BuildRows` 是列表唯一渲染口径：登录顺序、「已装备」/「换装中」标记，
  在途期间所有行不可点。回归测试：`Assets/Scripts/Tests/Editor/Bag/WeaponBagViewTests.cs`。
