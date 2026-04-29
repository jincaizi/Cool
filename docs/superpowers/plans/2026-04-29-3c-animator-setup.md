# Animator Controller 配置要求

**Date:** 2026-04-29
**Status:** Required for Implementation

---

## Layer 配置

| Layer | Index | Weight | Blending | 说明 |
|-------|-------|--------|----------|------|
| Base | 0 | 1 | Override | Idle, Move, Sprint, JumpStart, JumpAir, JumpEnd |
| Attack | 1 | 1 | Override | AttackIdle, Attack1, Attack2, SkillQ, SkillR |
| Hit | 2 | 1 | Additive | Hit（叠加在任意层之上） |

---

## 参数配置

| 参数名 | 类型 | 说明 |
|--------|------|------|
| BaseState | Int | 0=Idle, 1=Move, 2=Sprint, 3=JumpStart, 4=JumpAir, 5=JumpEnd, 6=Death |
| AttackState | Int | 0=AttackIdle, 1=Attack1, 2=Attack2, 3=SkillQ, 4=SkillR |
| IsJumping | Bool | 是否在跳跃中（BaseState 3/4/5） |
| IsHit | Bool | 是否受击中 |
| Attack | Trigger | 普攻触发 |
| SkillQ | Trigger | 技能Q触发 |
| SkillR | Trigger | 技能R触发 |
| Hit | Trigger | 受击触发 |

---

## Base Layer 状态

```
Base Layer (Override)
├── Idle (Idle_Normal_SwordAndShield)
│   ├── [IsMoving] → Move
│   ├── [IsSprinting] → Sprint
│   ├── [IsJumping] → JumpStart
│   └── [IsDead] → Death
│
├── Move (MoveFWD_Normal_InPlace_SwordAndShield)
│   ├── [!IsMoving] → Idle
│   ├── [IsSprinting] → Sprint
│   ├── [IsJumping] → JumpStart
│   └── [IsDead] → Death
│
├── Sprint (SprintFWD_Battle_InPlace_SwordAndShield)
│   ├── [!IsSprinting] → Idle
│   ├── [IsJumping] → JumpStart
│   └── [IsDead] → Death
│
├── JumpStart (JumpStart_Normal_InPlace_SwordAndShield)
│   ├── [动画完成] → JumpAir
│   └── [IsDead] → Death
│
├── JumpAir (JumpAir_Normal_InPlace_SwordAndShield)
│   ├── [IsGrounded + velocity.y <= 0] → JumpEnd
│   └── [IsDead] → Death
│
├── JumpEnd (JumpEnd_Normal_InPlace_SwordAndShield)
│   └── [动画完成/normaltime >= 0.9] → Idle
│
└── Death (Die01_SwordAndShield)
    └── (终止状态)
```

---

## Attack Layer 状态

```
Attack Layer (Override)
├── AttackIdle
│   ├── [Attack trigger] → Attack1
│   ├── [SkillQ trigger] → SkillQ
│   ├── [SkillR trigger] → SkillR
│   └── [Hit trigger] → Hit (最高优先级)
│
├── Attack1 (Attack01_SwordAndShield)
│   ├── [Attack trigger + 连击条件] → Attack2
│   ├── [动画完成/normaltime >= 0.9] → AttackIdle
│   └── [Hit trigger] → Hit
│
├── Attack2 (Attack02_SwordAndShield)
│   ├── [动画完成] → AttackIdle
│   └── [Hit trigger] → Hit
│
├── SkillQ (Attack03_SwordAndShiled) — 突刺
│   ├── [动画完成] → AttackIdle
│   └── [Hit trigger] → Hit
│
└── SkillR (Attack04_SwordAndShiled)
    ├── [动画完成] → AttackIdle
    └── [Hit trigger] → Hit
```

**连击条件：**
- 5帧后解锁（`ComboFrameLock = 5`）
- normalizedTime 在 0.3~0.8 区间内

---

## Hit Layer 状态

```
Hit Layer (Additive)
└── Hit (Hit01 or similar)
    └── [动画完成/normaltime >= 0.9] → (自动返回原状态)
```

**注意：** Hit 层使用 Additive 混合，不影响 Base/Attack 层动画。

---

## StateMachineBehaviour 挂载

在 Animator Controller 的各状态上挂载对应的 StateMachineBehaviour：

| Layer | 状态 | Behaviour |
|-------|------|-----------|
| Base | JumpEnd | `BaseStateBehaviour` |
| Attack | Attack1 | `AttackStateBehaviour` |
| Attack | Attack2 | `AttackStateBehaviour` |
| Hit | Hit | `HitStateBehaviour` |

---

## 转换规则汇总

### Base Layer

| From | To | Condition |
|------|-----|-----------|
| Idle | Move | `IsMoving == true` |
| Idle | Sprint | `IsSprinting == true` |
| Idle | JumpStart | `IsJumping == true` |
| Move | Idle | `IsMoving == false` |
| Move | Sprint | `IsSprinting == true` |
| Move | JumpStart | `IsJumping == true` |
| Sprint | Idle | `IsSprinting == false` |
| Sprint | JumpStart | `IsJumping == true` |
| JumpStart | JumpAir | HasExitTime / 自动 |
| JumpAir | JumpEnd | OnLanded event |
| JumpEnd | Idle | Animation Complete |
| Any | Death | `IsDead == true` |

### Attack Layer

| From | To | Condition |
|------|-----|-----------|
| AttackIdle | Attack1 | `Attack` trigger |
| AttackIdle | SkillQ | `SkillQ` trigger |
| AttackIdle | SkillR | `SkillR` trigger |
| Attack1 | Attack2 | `Attack` + ComboWindow |
| Attack1 | AttackIdle | Animation Complete |
| Attack2 | AttackIdle | Animation Complete |
| SkillQ | AttackIdle | Animation Complete |
| SkillR | AttackIdle | Animation Complete |
| Any | Hit | `Hit` trigger (最高优先级) |

---

## 实现检查清单

- [ ] 创建/修改 Animator Controller
- [ ] 配置 3 个 Layer（Base, Attack, Hit）
- [ ] Hit Layer 设置为 Additive 混合
- [ ] 添加所有参数（BaseState, AttackState, IsJumping, IsHit, Attack, SkillQ, SkillR, Hit）
- [ ] 创建所有状态并绑定动画
- [ ] 配置状态转换规则
- [ ] 在目标状态上挂载 StateMachineBehaviour
- [ ] 测试状态转换和动画播放