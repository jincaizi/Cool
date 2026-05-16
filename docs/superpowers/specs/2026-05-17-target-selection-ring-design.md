# Target Selection Ring Design

## Overview

当玩家攻击命中敌人时，目标脚下出现选中光圈标识，用于清晰反馈当前攻击目标。切换目标时旧光圈消失、新光圈出现。目标死亡时光圈自动清除。

## Architecture

```
玩家角色 (Sys3CEntry/CharacterAttackHandler)
  └─ SelectionRing (光环子 GameObject, 默认隐藏)
       └─ SpriteRenderer + Circle 贴图 + Unlit/Transparent 材质
```

光环作为唯一实例挂在玩家角色身上。选中目标时，光环 re-parent 到目标 Transform 下（`localPosition = (0, yOffset, 0)`），利用父-子层级自动跟随，零运行时位置开销。取消选中时 re-parent 回玩家并隐藏。

## Components

### SelectionRing (新增)

- 挂在玩家角色上的 MonoBehaviour
- 子 GameObject 含 SpriteRenderer，贴图使用 `Assets/PreRes/Texture/Com/role_picSele.png`，默认隐藏
- `AttachTo(Transform parent, float yOffset)` — SetParent 到目标 + 设 localPos + 显示
- `Detach()` — SetParent 回玩家 + 隐藏 + 取消 OnDeath 订阅

### MonsterConfig (修改)

新增字段：
```csharp
[Tooltip("选中光环的脚底Y轴偏移量，用于调整光环在目标脚下的高度位置")]
[SerializeField] private float _ringYOffset = -0.9f;
```

### CharacterAttackHandler (修改)

新增：
- `ITargetable _currentTarget` — 当前选中目标
- `SelectionRing _selectionRing` — 光环引用
- `OnAttackHit()` 中：命中 ITargetable 目标时，切换光环到新目标
- 目标死亡回调：Detach + 清空 `_currentTarget`

## Data Flow

```
攻击命中 IDamageable target
  │
  ▼
CharacterAttackHandler.OnAttackHit(target)
  │
  ├─ target is ITargetable && target != _currentTarget ?
  │
  ├─ 旧目标: _selectionRing.Detach()
  │            → SetParent(player)
  │            → SetActive(false)
  │            → 取消 OnDeath 订阅
  │
  └─ 新目标: _selectionRing.AttachTo(target.transform, yOffset)
               → SetParent(target.transform)
               → localPosition = (0, yOffset, 0)
               → SetActive(true)
               → 订阅 target.OnDeath → Detach()

目标死亡:
  target.OnDeath → _selectionRing.Detach(), _currentTarget = null
```

## Files Changed

| File | Change |
|------|--------|
| `Sys3C/SelectionRing.cs` | **New** — 约 35 行 |
| `Sys3C/Monster/MonsterConfig.cs` | Add `_ringYOffset` field |
| `Sys3C/CharacterAttackHandler.cs` | Add `_currentTarget`, `_selectionRing`, modify hit flow |

## What We Don't Need

- Object pooling — 只有一个光环实例
- Network sync — 纯客户端视觉
- Animation — 后续可加，本次不做
- ITargetable interface changes
- MonsterEntity changes
