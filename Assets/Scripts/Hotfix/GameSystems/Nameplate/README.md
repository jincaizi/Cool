# Nameplate —— 名字板、飘字与屏幕反馈

## 关键文件

| 文件 | 作用 |
|---|---|
| `EntityDisplayManager.cs` | 场景内统一管理名字板、血条、飘字与精力环 |
| `NameplateRenderer.cs` | 名字板渲染与对象池；含远程玩家专用**带血条模板**（`RegisterWithHealth` / `UpdateHealth`） |
| `FloatTextRenderer.cs` | 伤害 / 格挡飘字，对象池；`EnableVFX` 为总开关 |
| `FloatTextSettings.cs` / `FloatTextConfig.cs` | 飘字样式 |
| `NameplateSettings.cs` / `NameplateConfig.cs` / `NameplateData.cs` | 名字板配置与数据 |
| `DisplayEventBridge.cs` | 订阅伤害与 `BlockSucceededEvent`，转成飘字 |
| `DamageScreenEffect.cs` | 玩家受伤屏幕闪红 |

## 事件来源

- 伤害：`MonsterTakeDamageEvent` / `PlayerTakeDamageEvent` / `MonsterDotDamageEvent`
- 格挡：`BlockSucceededEvent`（本地由 `Sys3CEntry` 发布，远程由 `SyncLayer/CombatNetworkBridge` 发布）
- 精力环：`Stamina/StaminaRingRenderer` 由 `EntityDisplayManager` 持有，每帧读取 `StaminaSystem`

## 远程玩家血条（label sink）

- `NameplateRenderer.RegisterWithHealth(entityId, owner, config)` 走**独立模板 + 独立对象池**
  （纵向 Root：名字行 + `HPBar/Fill`），与单机 `Register` 互不影响；`UpdateHealth` 只写
  `fillAmount`（0..1），池对象复用时每次注册都会重设血条色。
- 生产接线：`SyncLayer/ClientNetworkHost.NameplateLabelSink` 实现 `INetworkLabelSink`，
  把 `RemoteEntityRegistry` 的 Register / UpdateName / UpdateHp / Unregister 转发到场景单例
  `EntityDisplayManager`；单例不存在（测试宿主、未进世界）时全部静默跳过。
- 血量来源是**服务端权威值**：快照 `Health` 掩码位或 `DamageResult.TargetRemainingHp`
  → `NetworkTargetable` → `INetworkLabelSink.UpdateHp`。bot 在 registry 侧就不注册名牌，
  所以不会出现「僵尸 bot 名牌」。

## UI 倍率

`EntityDisplayManager` 创建的 `EntityDisplayCanvas` 是 `ScreenSpaceOverlay` Canvas，其
`CanvasScaler` 使用 `GameSettings.ResolveReferenceResolution()`；名字板、飘字与精力环都挂在该
Canvas 下，跟随同一套解析后的 UI 倍率。

## 测试

Editor：`FloatTextRendererZeroDamageTests`。
PlayMode：`BlockFeedbackDisplayTests`、`DamageScreenFlashTests`、`PlayerHealTests`。

```bash
bash tools/unity-tests.sh PlayMode BlockFeedbackDisplayTests
```

## 相关模块

- [Skills](../Skills/README.md) —— 技能事件
- [Combat](../Combat/README.md) —— 格挡事件来源
- [VFX](../VFX/README.md) —— 受击闪光与火花
- [Stamina](../Stamina/README.md) —— 精力环数据
