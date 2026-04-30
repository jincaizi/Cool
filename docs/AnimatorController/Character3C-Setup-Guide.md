# Character3C Controller 配置指南

**基于:** 2026-04-29-3c-system-redesign.md
**创建时间:** 2026-04-29

---

## 一、Animator Parameters 配置

在 Animator 面板的 Parameters 标签页中创建以下参数：

| 参数名 | 类型 | 用途 |
|--------|------|------|
| `BaseState` | Int | Base Layer 状态标识 (0-6) |
| `AttackState` | Int | Attack Layer 状态标识 (0-4) |
| `IsJumping` | Bool | 是否处于跳跃状态 |
| `IsHit` | Bool | 是否正在播放受击动画 |
| `Attack` | Trigger | 触发普攻/连击 |
| `SkillQ` | Trigger | 触发技能Q |
| `SkillR` | Trigger | 触发技能R |
| `Hit` | Trigger | 触发受击动画 |

### BaseState 枚举值

| 值 | 状态名 | 说明 |
|----|--------|------|
| 0 | Idle | 站立 |
| 1 | Move | 行走 |
| 2 | Sprint | 冲刺 |
| 3 | JumpStart | 起跳 |
| 4 | JumpAir | 空中 |
| 5 | JumpEnd | 落地 |
| 6 | Death | 死亡 |

### AttackState 枚举值

| 值 | 状态名 | 说明 |
|----|--------|------|
| 0 | AttackIdle | 无攻击 |
| 1 | Attack1 | 第一击 |
| 2 | Attack2 | 第二击 |
| 3 | AttackQ | 突刺技能 |
| 4 | AttackR | 技能R |

---

## 二、Layer 配置

### Layer 0: Base Layer (基础移动层)

- **Weight:** 1
- **Blend Mode:** Override
- **IK Pass:** 根据需求启用

**包含状态:**
```
Idle ←→ Move ←→ Sprint
                ↓
           JumpStart → JumpAir → JumpEnd → (返回上述任意状态)
                                        ↓
                                      Death
```

### Layer 1: Attack Layer (攻击层)

- **Weight:** 1
- **Blend Mode:** Override
- **Mask:** 可选择性添加 UpperBody 或 FullBody

**包含状态:**
```
AttackIdle ←→ Attack1 ←→ Attack2
      ↑
      └──────── AttackQ
      ↑
      └──────── AttackR
```

### Layer 2: Hit Layer (受击叠加层)

- **Weight:** 0 (默认)，被触发时设为 1
- **Blend Mode:** Additive (叠加)
- **IK Pass:** 禁用

---

## 三、State 详细配置

### 3.1 Base Layer States

#### Idle State
```
- Name: Idle
- Motion: (拖入 Idle 动画)
- Speed: 1
- Cycle Offset: 0
- Transitions (退出条件):
  → Move:    BaseState == 1
  → Sprint:  BaseState == 2
  → Death:   BaseState == 6
```

#### Move State
```
- Name: Move
- Motion: (拖入 Walk/Run 动画)
- Speed: 1
- Cycle Offset: 0
- Transitions (退出条件):
  → Idle:    BaseState == 0
  → Sprint:   BaseState == 2
  → Death:    BaseState == 6
```

#### Sprint State
```
- Name: Sprint
- Motion: (拖入 Sprint 动画)
- Speed: 1
- Cycle Offset: 0
- Transitions (退出条件):
  → Idle:    BaseState == 0
  → Move:    BaseState == 1
  → Death:   BaseState == 6
```

#### JumpStart State
```
- Name: JumpStart
- Motion: (拖入 JumpStart 动画)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: OFF
- Transitions:
  → JumpAir: (自动转换，1帧后)
```

#### JumpAir State
```
- Name: JumpAir
- Motion: (拖入 JumpAir/JumpLoop 动画)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: OFF
- Transitions:
  → JumpEnd: BaseState == 5 (IsGrounded && falling)
```

#### JumpEnd State
```
- Name: JumpEnd
- Motion: (拖入 JumpEnd/Landing 动画)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: OFF
- StateMachineBehaviour: BaseStateBehaviour
  (用于检测动画完成并调用 FinishJump)
- Transitions:
  → Idle:   BaseState == 0
  → Move:   BaseState == 1
  → Sprint: BaseState == 2
```

#### Death State
```
- Name: Death
- Motion: (拖入 Death 动画)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: ON (或 OFF，取决于需求)
- Transitions: (无，通常是终止状态)
```

---

### 3.2 Attack Layer States

#### AttackIdle State
```
- Name: AttackIdle
- Motion: Empty (或待机动画)
- Speed: 1
- Has Exit Time: ON
- Transitions:
  → Attack1: Attack == true (当 AttackState == 1)
```

#### Attack1 State
```
- Name: Attack1
- Motion: (拖入 Attack01 动画，攻击动画)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: OFF
- StateMachineBehaviour: AttackStateBehaviour
  (处理连击窗口：5帧后解锁，0.3~0.8 normalizedTime 区间可触发 Attack2)
- Transitions:
  → Attack2: Attack == true (连击条件满足)
  → AttackIdle: 动画完成 (AttackState == 0)
```

#### Attack2 State
```
- Name: Attack2
- Motion: (拖入 Attack02 动画，第二击)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: OFF
- StateMachineBehaviour: AttackStateBehaviour
- Transitions:
  → AttackIdle: 动画完成 (AttackState == 0)
```

#### AttackQ State (SkillQ)
```
- Name: AttackQ
- Motion: (拖入 Attack03 动画，突刺技能)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: OFF
- Transitions:
  → AttackIdle: 动画完成 (AttackState == 0)
```

#### AttackR State (SkillR)
```
- Name: AttackR
- Motion: (拖入 Attack04 动画，技能R)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: OFF
- Transitions:
  → AttackIdle: 动画完成 (AttackState == 0)
```

---

### 3.3 Hit Layer States

#### Hit State
```
- Name: Hit
- Motion: (拖入 Hit 动画)
- Speed: 1
- Cycle Offset: 0
- Has Exit Time: OFF
- StateMachineBehaviour: HitStateBehaviour
  (Hit 动画完成时调用 OnHitCompleted)
- Transitions:
  → (无，动画完成后 Layer Weight 归零自动返回)
```

---

## 四、Transitions 条件汇总

### Base Layer Transitions

| 从 | 到 | 条件 |
|----|----|------|
| Idle | Move | BaseState == 1 |
| Idle | Sprint | BaseState == 2 |
| Idle | Death | BaseState == 6 |
| Move | Idle | BaseState == 0 |
| Move | Sprint | BaseState == 2 |
| Move | Death | BaseState == 6 |
| Sprint | Idle | BaseState == 0 |
| Sprint | Move | BaseState == 1 |
| Sprint | Death | BaseState == 6 |
| JumpStart | JumpAir | (自动) |
| JumpAir | JumpEnd | BaseState == 5 |

### Attack Layer Transitions

| 从 | 到 | 条件 |
|----|----|------|
| AttackIdle | Attack1 | Attack Trigger |
| Attack1 | Attack2 | Attack Trigger (连击窗口) |
| Attack1 | AttackIdle | AttackState == 0 |
| Attack2 | AttackIdle | AttackState == 0 |
| AttackIdle | AttackQ | SkillQ Trigger |
| AttackQ | AttackIdle | AttackState == 0 |
| AttackIdle | AttackR | SkillR Trigger |
| AttackR | AttackIdle | AttackState == 0 |

### Hit Layer Transitions

| 从 | 到 | 条件 |
|----|----|------|
| (任意) | Hit | Hit Trigger |

---

## 五、StateMachineBehaviour 脚本挂载

| Layer | State | Behaviour 脚本 | 作用 |
|-------|-------|----------------|------|
| Base | JumpEnd | `BaseStateBehaviour` | 监听 JumpEnd 动画完成 |
| Attack | Attack1 | `AttackStateBehaviour` | 连击窗口 + 动画完成 |
| Attack | Attack2 | `AttackStateBehaviour` | 动画完成 |
| Hit | Hit | `HitStateBehaviour` | 受击动画完成 |

---

## 六、代码集成要点

### FSMManager 初始化时设置回调

```csharp
// 在 FSMManager 构造函数或初始化方法中添加
BaseStateBehaviour.SetCallback(OnAnimationCompleted);
AttackStateBehaviour.SetCallback(OnAnimationCompleted);
HitStateBehaviour.SetCallback(OnAnimationCompleted);
```

### AnimationDriver (需要创建)

```csharp
public class AnimationDriver
{
    private readonly Animator _animator;

    // 参数哈希缓存
    private static readonly int HASH_BaseState = Animator.StringToHash("BaseState");
    private static readonly int HASH_AttackState = Animator.StringToHash("AttackState");
    private static readonly int HASH_IsJumping = Animator.StringToHash("IsJumping");
    private static readonly int HASH_IsHit = Animator.StringToHash("IsHit");
    private static readonly int HASH_Attack = Animator.StringToHash("Attack");
    private static readonly int HASH_SkillQ = Animator.StringToHash("SkillQ");
    private static readonly int HASH_SkillR = Animator.StringToHash("SkillR");
    private static readonly int HASH_Hit = Animator.StringToHash("Hit");

    public void SetBaseState(BaseState state)
    {
        _animator.SetInteger(HASH_BaseState, (int)state);
    }

    public void SetAttackState(AttackState state)
    {
        _animator.SetInteger(HASH_AttackState, (int)state);
    }

    public void SetIsJumping(bool isJumping)
    {
        _animator.SetBool(HASH_IsJumping, isJumping);
    }

    public void SetIsHit(bool isHit)
    {
        _animator.SetBool(HASH_IsHit, isHit);
    }

    public void TriggerAttack()
    {
        _animator.SetTrigger(HASH_Attack);
    }

    public void TriggerSkillQ()
    {
        _animator.SetTrigger(HASH_SkillQ);
    }

    public void TriggerSkillR()
    {
        _animator.SetTrigger(HASH_SkillR);
    }

    public void TriggerHit()
    {
        _animator.SetTrigger(HASH_Hit);
    }
}
```

---

## 七、创建步骤清单

1. [ ] 在 Unity 中创建 Animator Controller，命名为 `Character3C`
2. [ ] 创建 Parameters (Int: BaseState, AttackState; Bool: IsJumping, IsHit; Trigger: Attack, SkillQ, SkillR, Hit)
3. [ ] 添加 Layer 0 "Base"，配置 Base Layer 状态
4. [ ] 添加 Layer 1 "Attack"，配置 Attack Layer 状态
5. [ ] 添加 Layer 2 "Hit"，配置 Hit Layer 状态
6. [ ] 为各状态指定动画片段 (Animation Clip)
7. [ ] 配置状态转换条件和参数
8. [ ] 为 JumpEnd 状态挂载 `BaseStateBehaviour`
9. [ ] 为 Attack1、Attack2 状态挂载 `AttackStateBehaviour`
10. [ ] 为 Hit 状态挂载 `HitStateBehaviour`
11. [ ] 调整 Layer 权重和混合模式
12. [ ] 在代码中初始化 StateMachineBehaviour 回调
13. [ ] 测试状态转换和动画播放

---

## 八、测试检查清单

- [ ] Idle ↔ Move ↔ Sprint 切换正常
- [ ] 跳跃触发 JumpStart → JumpAir → JumpEnd → 返回原状态
- [ ] 普攻触发 Attack1 → Attack2 → AttackIdle
- [ ] 连击在 5 帧后、normalizedTime 0.3~0.8 区间可触发
- [ ] SkillQ/SkillR 动画正常播放并返回
- [ ] 受击动画叠加在任意状态上
- [ ] 死亡状态锁定，停止所有其他动画
- [ ] 状态转换日志输出正常

---

## 九、示例动画命名约定

| 动画类型 | 推荐命名 |
|----------|----------|
| 站立待机 | `Idle` |
| 行走 | `Walk` / `Move` |
| 冲刺 | `Sprint` / `Run` |
| 起跳 | `JumpStart` / `Jump_Start` |
| 空中 | `JumpAir` / `Jump_Loop` |
| 落地 | `JumpEnd` / `Land` |
| 死亡 | `Death` / `Die` |
| 普攻1 | `Attack01` / `Attack1` |
| 普攻2 | `Attack02` / `Attack2` |
| 技能Q | `Attack03` / `SkillQ` |
| 技能R | `Attack04` / `SkillR` |
| 受击 | `Hit` / `Damage` / `Stagger` |

---

**文档版本:** 1.0
**维护者:** Sys3C Team
**最后更新:** 2026-04-29
