# Animator Controller 配置指南

## Character3C.controller 参数配置

### 必需参数

| 参数名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `BaseState` | Int | 0 | 基础层状态 (0=Idle, 1=Move, 2=Sprint, 3=JumpStart, 4=JumpAir, 5=JumpEnd, 6=Death) |
| `AttackState` | Int | 0 | 攻击层状态 (0=Idle, 1=Attack1, 2=Attack2, 3=SkillQ, 4=SkillR) |
| `HitState` | Int | 0 | **新增** 受击层状态 (0=None, 1=Hit, 2=Knockback, 3=Launched, 4=Dizzy, 5=Down, 6=GetUp, 7=Death) |
| `IsJumping` | Bool | false | 是否跳跃中 |
| `IsHit` | Bool | false | **新增** 是否受击 |
| `IsDead` | Bool | false | **新增** 是否死亡 |
| `Attack` | Trigger | - | 攻击触发器 |
| `SkillQ` | Trigger | - | 技能Q触发器 |
| `SkillR` | Trigger | - | 技能R触发器 |
| `Hit` | Trigger | - | **新增** 受击触发器 |
| `Death` | Trigger | - | **新增** 死亡触发器 |

### Hit 层状态映射

在 Animator Controller 的 Hit 层中，每个动画状态需要设置对应的 `HitState` 值：

| 动画状态名 | HitState 值 | 说明 |
|------------|-------------|------|
| `Empty` | 0 | 无受击 |
| `Hit` | 1 | 普通受击 |
| `Knockback` | 2 | **新增** 击退 |
| `Launched` | 3 | **新增** 浮空 |
| `Dizzy` | 4 | **新增** 眩晕 |
| `Down` | 5 | 倒地 |
| `GetUp` | 6 | 起身 |
| `Death` | 7 | 死亡 |

### Hit 层配置

1. **Layer Settings**:
   - Name: `Hit`
   - Weight: `1`
   - Blending Mode: `Override`
   - IK Pass: `Off`

2. **Avatar Mask**: 如果需要只影响部分身体，可以创建 Avatar Mask

3. **动画过渡配置**:

   ```
   Empty -> Hit: Trigger == Hit (立即过渡, Has Exit Time = false)
   Hit -> Empty: Has Exit Time = true, Exit Time = 0.9 (动画播放完毕后自动返回)
   ```

4. **HitState 参数驱动**:
   - 每个受击动画状态需要在动画结束前设置 `HitState` 参数
   - 可以使用 Animation 窗口中的 Animator Parameters 部分添加

### 新增动画状态

需要为以下状态添加动画或占位符：

| 状态 | 建议使用现有动画 | 说明 |
|------|------------------|------|
| `Knockback` | 可复用 Hit 动画 | 击退效果可通过位移实现 |
| `Launched` | 需要浮空动画 | 上升后落下 |
| `Dizzy` | 需要眩晕动画 | 原地转圈或星星效果 |

### 测试验证

1. 在 Unity 中打开 `Character3C.controller`
2. 添加缺少的参数
3. 配置 Hit 层的状态和过渡
4. 运行游戏，按 T 键触发受击，验证状态转换
