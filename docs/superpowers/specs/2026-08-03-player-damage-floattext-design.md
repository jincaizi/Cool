# 玩家掉血飘字差异化设计

日期:2026-08-03
状态:已批准

## 背景与问题

当前玩家掉血飘字与怪物掉血飘字**共用同一个配置**(`FloatText_Damage.asset`,红字 52px 无描边),战斗中难以区分"自己掉血"与"打怪掉血"。`DisplayEventBridge.cs` 中注释已注明"玩家飘字复用怪物同款 settings(后续需要差异化时再新增配置)"。

## 目标

- 玩家掉血飘字与怪物掉血飘字一眼可辨
- 怪物掉血/暴击飘字视觉**完全不变**
- 扣除血量 ≤ 0 时不显示飘字

## 设计决策

| 决策 | 选择 | 理由 |
|---|---|---|
| 区分手段 | 颜色 + 黑描边 + 字号略大 | 用户确认;不引入动画/符号 |
| 玩家配色 | 亮灰 (0.85, 0.85, 0.85) + 黑描边 | 用户确认;"损失感"与怪物红字强对比 |
| 怪物侧 | 不动(红字 52px,暴击橙字) | 用户确认;怪物飘字高频,不宜喧宾夺主 |
| 玩家暴击 | 不区分 | 用户确认;需要改事件数据链(PlayerTakeDamageEvent 缺 IsCritical),超出本次范围 |
| 实现方式 | 数据驱动:FloatTextSettings 加描边字段 | 与现有"颜色/字号在资产中"模式一致,调色不动代码 |
| 0 伤害过滤 | 渲染器入口单点过滤 | 一处生效,覆盖玩家/怪物/heal 所有调用方 |

## 改动清单

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextSettings.cs` | 加 2 字段:`OutlineWidth`(float,默认 0)、`OutlineColor`(Color,默认黑) |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/FloatTextRenderer.cs` | ① `ShowDamageText` 入口加 `if (value <= 0) return;` ② `Spawn()` 设置完 color/fontSize 后追加 `tmp.outlineWidth = settings.OutlineWidth; tmp.outlineColor = settings.OutlineColor;` |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/DisplayEventBridge.cs` | 构造函数加 `FloatTextSettings playerDamageSettings` 参数;`OnPlayerDamaged` 改用新参数;删除"后续需要差异化"注释 |
| `Assets/Scripts/Hotfix/GameSystems/Nameplate/EntityDisplayManager.cs` | 加 `[SerializeField] FloatTextSettings _playerDamageSettings`,传入 bridge,场景接线 |
| 新资产 `Assets/PreRes/Settings/Display/FloatText_PlayerDamage.asset` | Type=Normal;Color=(0.85,0.85,0.85);OutlineWidth≈0.3(编辑器内微调);OutlineColor=黑;FontSize=58;Duration=0.8;MoveUpDistance=40;FadeStartRatio=0.5;StartScale=1 |

## 数据流

```
怪物打玩家 → PlayerTakeDamageEvent → EventBus
  → DisplayEventBridge.OnPlayerDamaged → ShowDamageText(玩家id, 灰字资产, damage)
  → [value <= 0 return] → Spawn(): 灰字 + 黑描边 + 58px 上飘
```

## 边界与正确性

- **池化复用**:outline 每次 Spawn 重新赋值,怪物字(OutlineWidth=0)不会残留玩家灰字样式
- **合并隔离**:merge key = (entityId, type),玩家挨打与怪物被打永不合并;玩家连续挨打仍正常合并数字
- **现有资产零影响**:怪物相关资产 OutlineWidth=0,渲染与现在完全一致
- **描边实现**:TMP 运行时 `outlineWidth/outlineColor` 属性,无需新材质资产
- **0 伤害**:玩家/怪物/heal 的 0 值数字均不显示飘字

## 验证方式

1. Play Mode 中让玩家挨打 + 打怪,同屏截图对比:玩家=灰字黑描边 58px,怪物=红字 52px 无描边,一眼可辨
2. 池化复用验证:先打怪再挨打,灰色描边不残留到怪物字上
3. 0 伤害验证:构造 0 值伤害事件,确认无飘字

## 范围外(未来可能)

- 玩家吃暴击的差异化(需要 PlayerTakeDamageEvent 增加 IsCritical 及数据链)
- 玩家掉血专属动画/符号
