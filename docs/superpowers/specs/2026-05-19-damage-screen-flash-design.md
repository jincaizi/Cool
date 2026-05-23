# 受击闪屏增强设计

## 1. 概述

为 `DamageScreenEffect` 添加快速闪烁序列，提升受击反馈的视觉冲击力。

## 2. 效果规格

- **闪烁模式**：快速闪烁（N 次亮灭）后淡出
- **闪烁次数**：3 次
- **闪烁时序**：
  - 亮（alpha=0.15）：0.08s
  - 灭（alpha=0）：0.05s
  - 循环 3 次后进入淡出
- **淡出**：从最后一次闪烁顶点开始，1.5s 线性淡出至 0
- **CD**：从 `GameSettings.FlashCooldown` 读取（秒），3 秒默认值

## 3. 架构

```
DamageScreenEffect
├── Flash()              # 触发闪烁逻辑
├── _lastFlashTime       # 上次闪烁时间戳
└── _flashSequence       # DOTween.Sequence（用于中断）
```

**关键行为：**
- CD 未到则忽略本次调用
- 新的 Flash 调用中断正在进行的 Sequence，重新开始
- `Cleanup()` 负责 Kill Sequence 并销毁对象

## 4. 代码变更

### DamageScreenEffect.Flash()

```
t = 0.0s  → alpha=0.15（亮）
t = 0.08s → alpha=0（灭）
t = 0.13s → alpha=0.15（亮）
t = 0.21s → alpha=0（灭）
t = 0.26s → alpha=0.15（亮）
t = 0.34s → alpha=0（灭）
t = 0.39s → alpha=0.15（亮）
t = 0.47s → alpha=0（灭）
t = 0.52s → alpha=0.15 开始淡出
t = 2.02s → alpha=0
```

## 5. GameSettings 扩展

新增字段（建议放在 `HitFlash` 相关配置附近）：

| 字段 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FlashCooldown` | float | 3f | 闪屏 CD（秒） |

## 6. 测试要点

- [ ] 短时间内多次受击只会触发一次闪烁（CD 生效）
- [ ] 闪烁过程中再次受击会打断当前动画并重新开始
- [ ] CD 到期后受击正常触发闪烁
- [ ] 切换场景后残留 Sequence 被正确清理