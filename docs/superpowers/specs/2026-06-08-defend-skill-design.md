# 角色持盾防御系统设计

> **状态:** 已批准
> **日期:** 2026-06-08
> **类型:** 新功能
> **关联系统:** 3C FSM、HitFSM、Damage Pipeline

---

## 1. 需求摘要

角色持盾，按住按键举盾防御。防御时移动减速、不能跳跃。正面受击减伤并播放格挡动画，背面受击正常受伤。盾有耐久上限，累计吸收伤害超过耐久后盾破眩晕。

**动画资源：**
- 持盾防御：`Assets/RpgDuo/Animation/SwordAndShield/Defend_SwordAndShield.fbx`
- 持盾受击：`Assets/RpgDuo/Animation/SwordAndShield/DefendHit_SwordAndShield.fbx`
- 盾破眩晕：`Assets/RpgDuo/Animation/SwordAndShield/Dizzy_SwordAndShield.fbx`

---

## 2. 架构决策

### ADR: 防御合并到现有 FSM 层

**选择：** 防御不创建独立 FSM 类，将 Defend 状态合并到 BaseFSM（移动层），将 DefendHit 状态合并到 HitFSM（受击层）。

**理由：**
- Defend（举盾姿态）本质是移动姿态变体，和 Idle/Walk 同层
- DefendHit（格挡动作）本质是受击反应变体，和 Hit/Knockback 同层
- Dizzy（盾破眩晕）HitFSM 已有，直接复用
- 不新增 FSM 类、Animator 层、LayerType 枚举
- 共享 `CharacterData.IsDefending` 字段作为各组件之间的协调信号

**风险：** BaseFSM 和 HitFSM 各加一个状态，改动范围小但需要对原来的转换规则做精确修改。

---

## 3. 状态机设计

### 3.1 BaseFSM 新增 Defend 状态

```
现有：
  Idle ↔ Move ↔ Sprint
    ↕      ↕       ↕
  JumpStart → JumpAir → JumpEnd
    ↓
  Death

新增：
  [按住按键] → LockState(Defend)
  Defend → [松键] → Unlock(Idle 或 Move)
  Defend → [IsDead] → Death
  Defend → [盾破/背面受击/眩晕] → Unlock → 正常评估
```

**转换规则（StateTransitionTable）：**

```
Defend:
  → Idle    [always, priority=1]       // 松键默认
  → Move    [MoveDir.sqrMagnitude > 0.01f, priority=2]  // 松键且移动中
  → Death   [IsDead, priority=100]
```

**进入条件（外部强制）：** 只有 `IsGrounded && !IsDead && HitFSM.CurrentState == HitState.None` 时才接受 Defend 请求。

### 3.2 HitFSM 新增 DefendHit 状态

```
现有流程：
  None → Hit → Recover
       → Knockback → Recover
       → Dizzy → Recover → None
       → Death

防御中正面受击新流程：
  None → DefendHit → 动画播完 → Recover → None
                    → 耐久耗尽 → Dizzy → Recover → None

防御中背面受击流程（不变）：
  None → Hit → Recover → None
```

**DefendHit 行为：**
- 动画时长 ~0.4s，由动画事件触发 `OnAnimationEnd("DefendHit")`
- 播放期间同一优先级受击不再触发新动画
- 减伤和耐久扣除由 DefendModifier 处理
- 优先级：50（和 Hit 同级，低于 Dizzy 60+）

### 3.3 优先级（新增 DefendHit）

```
Death:    100
Down:      90
Launched:  80
Knockback: 70
Dizzy:     60
Hit:       50
DefendHit: 50    ← 新增
GetUp:     40
None:       0
```

---

## 4. 数据结构

### 4.1 CharacterData 新增字段

```csharp
struct CharacterData
{
    // ... 现有字段 ...
    bool IsDefending;   // 是否处于防御姿态
}
```

### 4.2 CharacterController 新增成员

```csharp
class CharacterController
{
    // 盾耐久
    private float _shieldDurability;
    private const float MaxShieldDurability = 50f;
    private const float DefendSpeedMultiplier = 0.4f;
    
    // 防御状态
    private bool _isDefending;
    public bool IsDefending => _isDefending;
    
    // 耐久剩余比例（供 UI 显示）
    public float ShieldDurabilityPercent => 
        _shieldDurability / MaxShieldDurability;
}
```

### 4.3 HitFSM HitState 枚举新增

```csharp
enum HitState
{
    None,
    Hit,
    Knockback,
    Launched,
    Dizzy,
    Down,
    GetUp,
    Death,
    DefendHit   // 新增
}
```

### 4.4 BaseFSM BaseState 枚举新增

```csharp
enum BaseState
{
    Idle,
    Move,
    Sprint,
    JumpStart,
    JumpAir,
    JumpEnd,
    Death,
    Locomotion,
    Defend   // 新增
}
```

### 4.5 Animator 参数新增

| 参数 | 类型 | 用途 |
|------|------|------|
| `IsDefending` | Bool | Base 层内 Defend → Idle/DefendHit 转换 |

`HitState` Int 参数已有，DefendHit 通过值 8 传递。

---

## 5. 组件交互流程

### 5.1 进入防御

```
Input (按住右键)
  → CharacterController.TryEnterDefend()
    → 条件检查: IsGrounded && !IsDead && HitFSM == None && !_isDefending
    → _isDefending = true
    → _shieldDurability = MaxShieldDurability
    → _data.IsDefending = true
    → FSMManager.EnterDefend()
      → BaseFSM.LockState(Defend)
      → Animator.SetBool("IsDefending", true)
```

### 5.2 防御中移动

```
CharacterController.Update():
  if (_isDefending)
  {
      // 限制跳跃
      _data.RequestJump = false;
      
      // 移动减速
      moveCommand.Speed = MoveSpeed * DefendSpeedMultiplier;
  }
```

### 5.3 防御中正面受击

```
Damage 管线:
  IDamageable.TakeDamage(data, hitDirection)
    → FSMManager.HandleDamage(...)
      → DefendModifier.Modify(ctx)
          isDefending=true → 计算角度
          → 正面: 减伤, ShouldKnockback=false, ReactLevel=None
          → 扣除盾耐久
          → 背面: 不介入, ShouldKnockback=true
      
      → 正面 case:
          HitFSM.EnterDefendHit(hitData)
          // 盾耐久扣除量为格挡减免的部分：
          // _shieldDurability -= (originalDamage - reducedDmg)
          → CharacterController.AbsorbDamage(originalDamage - reducedDmg)
  
      → 背面 case:
          BaseFSM.Unlock(Idle)
          HitFSM.EnterHit(hitData)  // 正常受击
```

### 5.4 盾破

```
DefendModifier.Modify() 减耐久:
  _shieldDurability <= 0
    → CharacterController.OnShieldBreak()
      → _isDefending = false
      → _data.IsDefending = false
    → FSMManager.HandleShieldBreak()
      → BaseFSM.Unlock(Idle)
      → HitFSM.EnterDizzy(hitData)
```

### 5.5 退出防御

```
Input (松右键)
  → CharacterController.TryExitDefend()
    → _isDefending = false
    → _data.IsDefending = false
    → FSMManager.ExitDefend()
      → BaseFSM.Unlock(Idle)  // 转换表自动评估 Move
      → Animator.SetBool("IsDefending", false)

外部强制退出（死亡/眩晕/背面受击）走同样路径：
    TryExitDefend() 被对应处理方法调用
```

---

## 6. Animator 配置

### 6.1 Base Layer（层0）

Defend 状态接入 Blend Tree 旁边：

```
Any State → Defend    [condition: IsDefending=true]
Defend → Idle         [condition: IsDefending=false]
```

DefendHit 在 Hit Layer（层2）处理：

```
Hit Layer:
  Any State → DefendHit  [condition: HitState=8]
  DefendHit → Empty      [exit time: normalizedTime >= 0.95]
```

### 6.2 层权重

防御中 Hit 层权重提升到 1 播放 DefendHit，动画播完回调 `HandleHitAnimationCompleted("DefendHit")` 后权重回到 0。

此行为与现有 Hit 状态（HitState=1）完全一致，复用已有的 HitStateBehaviour。

---

## 7. 文件变更清单

| 文件 | 变更 |
|------|------|
| `Sys3C/Core/Enums.cs` | LayerType 不变，无需修改 |
| `Sys3C/Character/CharacterData.cs` | 加 `IsDefending` 字段 |
| `Sys3C/FSM/BaseFSM.cs` | BaseState 枚举加 `Defend`；加防御期间速度限制逻辑 |
| `Sys3C/FSM/StateTransitionTable.cs` | 加 Defend 的转换规则 |
| `Sys3C/FSM/HitFSM.cs` | HitState 枚举加 `DefendHit`；OnAnimationEnd 加 case；加 `EnterDefendHit` 方法 |
| `Sys3C/FSM/FSMManager.cs` | 加 `EnterDefend/ExitDefend/HandleShieldBreak`；修改 HandleDamage 伤害分流 |
| `Sys3C/Character/CharacterController.cs` | 加 `TryEnterDefend/TryExitDefend/OnShieldBreak/AbsorbDamage`；移动速度判断 |
| `Sys3C/Animation/AnimHashes.cs` | 加 `IsDefending` hash |
| `Sys3C/Core/StateCoordinator.cs` | 加 `CanDefend` 判断（不新增 LayerType） |
| `Sys3C/Sys3CEntry.cs` | HandleInput 加防御按键处理；接入 CharacterController 的 DefendModifier 谓词 |
| `Monster/Damage/Modifiers/DefendModifier.cs` | 接入角色 `_isDefending`（当前仅用于怪物，需改造为通用谓词） |
| `Sys3C/Animation/StateBehaviours/HitStateBehaviour.cs` | 加 DefendHit 动画名 hash 映射 |

---

## 8. 边界情况处理

| 场景 | 处理 |
|------|------|
| 防御中松键 | Unlock → 转换表评估，自动切 Idle/Move |
| DefendHit 期间松键 | Hit 层遮罩挡住 Base 层 Idle，动画播完露出来 |
| DefendHit 期间第二次受击 | 减伤但不重新触发动画 |
| DefendHit 期间耐久归零 | 立刻切 Dizzy，中断 DefendHit 动画 |
| 背面受击 | DefendModifier 不介入 → 正常受击 → Unlock 退出防御 |
| 死亡 | LockState(Death) 覆盖一切 |
| 空中按防御 | 忽略，接地后才能举盾 |
| 攻击中按防御 | 忽略（CanDefend 检查） |
| 防御中按攻击 | 忽略（CanAttack 检查） |
| 外部眩晕 | StatusController → TryExitDefend |
| UI 显示 | CharacterController.ShieldDurabilityPercent 提供数据 |

---

## 9. 未覆盖项（v2 考虑）

- 防御 + 攻击键 = 盾击反击
- 多方向精确格挡角度
- 不同盾牌类型的不同耐久/减伤
- 格挡成功后的子弹时间/完美格挡
- 网络同步防御状态
