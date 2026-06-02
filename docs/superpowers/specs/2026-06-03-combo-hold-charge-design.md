# 连段蓄力系统设计

## 概述

修改长按蓄力逻辑：普攻1 不再被蓄力中途取消，而是等它自然播完后再进入蓄力阶段。

## 按键流程

```
按下攻击键
    │
    ├─ 立即播放 普攻1（横劈）
    │
    ├─ 普攻1 未完成 + 松手 → 普攻1 播完就结束，不进蓄力
    │
    ├─ 普攻1 完成时还在按 → 进入蓄力阶段
    │     ├─ 可移动、可转向
    │     ├─ 不自动释放（一直蓄到松手）
    │     └─ 松手 → 释放 普攻2（竖劈）
    │
    └─ 蓄力中松手 → 释放 普攻2
```

## 防连点

普攻1 执行期间，不在取消窗口内的新点击直接丢弃。`IsInCancelableWindow()` 已有此逻辑。

## 普攻1 vs 普攻2

| | 普攻1（横劈） | 普攻2（竖劈） |
|---|---|---|
| 技能资产 | BA_Combo1 (ComboSkillData) | BA_Heavy (ChargedSkillData) |
| 检测 | OverlapSphere 扇形 -15°~45° | OverlapSphere 扇形 -10°~10° |
| 范围 | 2 单位 | 2.5 单位 |
| 伤害 | 低 | 高 |

## 蓄力阶段

| 参数 | 值 |
|------|-----|
| 移动 | 允许 |
| 转向 | 允许 |
| 最小蓄力 | 0.3s |
| 最大蓄力 | 不自动释放，等松手 |
| 伤害公式 | BaseDamage × (1 + 蓄力进度 × 0.5) |

## 改动文件

| 文件 | 改动 |
|------|------|
| `Sys3CEntry.cs` | 监听普攻1完成事件 + 按键状态 → 决定进蓄力 |
| `InputManager.cs` | 保持现有按住/松开检测（已完成） |
| `SkillCoordinator.cs` | 加 `OnLightAttackCompleted` 回调 / 移除破坏性 ForceComplete |
| `SkillExecutor.cs` | 暴露技能完成事件（已有 `OnSkillCompleted`） |
| `BA_Heavy.asset` | `CanMoveWhileCharging = true`, `CanRotateWhileCharging = true`, `MaxChargeTime` 设大值或加不自动释放标记 |

## 不在范围

- 盾牌格挡
- 普攻3
- 跳跃攻击
