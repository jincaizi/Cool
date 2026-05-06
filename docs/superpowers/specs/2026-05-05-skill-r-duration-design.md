# 技能R持续性技能设计方案

## 概述

技能R是一个持续性技能，包含起手动画和持续循环动画，支持最大时长限制和按键松开取消。

## 动画状态机设计

```
Idle ──(按下R键)──→ SkillR_Start ──(动画完成)──→ SkillR_Loop
                                                         │
                                                    (松开/超时)
                                                         │
                                                         ↓
                                                       Idle
```

| 状态 | Loop Time | 说明 |
|------|-----------|------|
| SkillR_Start | false | 起手动画，播放一次后自动进入 Loop |
| SkillR_Loop | true | 持续动画，循环播放直到结束 |

## Animator Controller 配置

### SkillR_Start 状态
- Motion: 技能起手动画 Clip
- Loop Time: **false**
- 从 Idle 到 SkillR_Start 的转换条件: `SkillR` trigger == true

### SkillR_Loop 状态
- Motion: 技能持续动画 Clip (Loop)
- Loop Time: **true**
- 从 SkillR_Start 到 SkillR_Loop 的转换: HasExitTime = true, Exit Time = 1.0

### 结束转换
- SkillR_Loop → Idle: 无需条件，当代码设置 AttackState = Idle 时自然过渡

## FSM 层状态定义

在 `CharacterData.cs` 的 `AttackState` 枚举中增加：

```csharp
public enum AttackState
{
    Idle = 0,
    Attack1 = 1,
    Attack2 = 2,
    SkillQ = 3,
    SkillR_Start = 4,  // 新增
    SkillR_Loop = 5    // 新增
}
```

## 核心逻辑

| 阶段 | 触发条件 | 行为 |
|------|----------|------|
| 开始 | 按下 R 键（地面） | 进入 SkillR_Start |
| 持续 | Start 动画完成 | 自动进入 SkillR_Loop |
| 结束 | 松开 R 键 OR 达到最大时长 | 返回 Idle |

## 配置项

在 `SkillConfig.cs` 中增加：

```csharp
[Header("Duration Skill")]
public float MaxDuration = 3f;  // 最大持续时长（秒），0表示无限制
```

## 代码改动点

### 1. CharacterData.cs
- AttackState 枚举新增 `SkillR_Start = 4` 和 `SkillR_Loop = 5`

### 2. AttackFSM.cs
- 新增字段: `_skillRDuration` 计时器
- 新增方法: `UpdateSkillRDuration(float deltaTime)` - 更新持续时间检测
- 修改 `RequestSkillR()`: 进入 SkillR_Start 状态
- 修改 `OnAnimationCompleted()`: 当 SkillR_Start 完成时自动进入 SkillR_Loop
- 新增方法: `CancelSkillR()`: 取消技能R，返回Idle

### 3. SkillConfig.cs
- 增加 `MaxDuration` 配置字段

### 4. AnimationDriver.cs
- 新增 `TriggerSkillRStart()` 方法（如果需要分开触发）

### 5. AttackStateBehaviour.cs
- 扩展哈希值支持: `HASH_SkillR_Start` 和 `HASH_SkillR_Loop`
- 修改 `GetStateName()` 返回对应字符串
- 在 OnStateExit 中触发 SkillR_Start → SkillR_Loop 的自动转换逻辑

### 6. FSMManager.cs
- 修改 `HandleAnimationCompleted()`: 处理 `SkillR_Start` 完成回调
- 在 Update 中调用技能R持续时间检测

### 7. Sys3CEntry.cs
- 在 `HandleInput()` 中检测 R 键松开事件
- 当处于 SkillR_Loop 状态时，松开 R 键调用 `CancelSkillR()`

### 8. InputManager.cs
- 新增 `IsSkill3Released()` 方法检测 R 键释放

## 状态流转时序

```
用户按下R键
    ↓
RequestSkillR() 被调用
    ↓
AttackState = SkillR_Start
AnimationDriver.TriggerSkillR() 被调用
    ↓
Animator 播放 SkillR_Start 动画
    ↓
SkillR_Start 动画完成 (OnStateExit)
    ↓
AttackFSM.OnAnimationCompleted("SkillR_Start")
    ↓
AttackState = SkillR_Loop
    ↓
Animator 播放 SkillR_Loop 动画 (循环)
    ↓
用户松开R键 或 达到MaxDuration
    ↓
AttackFSM.CancelSkillR()
    ↓
AttackState = Idle
    ↓
Animator 过渡到 Idle
```

## 取消检测优先级

1. 最大时长超时 → 自动取消
2. 玩家松开按键 → 主动取消

## 注意事项

- SkillR_Start 和 SkillR_Loop 在 Animator Controller 中的状态名称必须与代码中 HASH 值匹配
- MaxDuration = 0 表示无最大时长限制（由松开按键控制结束）