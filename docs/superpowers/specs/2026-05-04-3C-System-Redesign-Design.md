# 3C 系统重构设计方案

> **状态:** 已批准
> **日期:** 2026-05-04
> **版本:** 1.0

---

## 1. 设计目标

1. 实现三层分层 FSM（Base > Attack > Hit）
2. 支持完整技能系统（瞬发/读条/引导/蓄力）
3. 内置简化霸体/打断机制
4. 预留网络同步接口
5. 提供完整调试工具
6. 混合动画方案（Animator + Playable API）

---

## 2. 整体架构

### 2.1 核心架构

```
┌─────────────────────────────────────────────────────────────────┐
│                       StateCoordinator                           │
│              优先级仲裁: Hit > Attack > Base                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │                    BaseLayer FSM                        │   │
│   │  Idle ←→ Move ←→ Sprint ←→ Jump(Start/Air/End)        │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │                   AttackLayer FSM                        │   │
│   │  Idle ←→ Attack1 ↔ Attack2                             │   │
│   │         ↕                                                │   │
│   │  SkillQ / SkillR (SkillStateMachine 子状态机)           │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │                    HitLayer FSM                          │   │
│   │  [None] → Hit → Knockback → Down → [Death]               │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
│                         EventBus                                 │
│  (层间通信、状态变化通知、技能事件、伤害事件、动画事件)         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   ┌────────────┐  ┌────────────┐  ┌────────────┐              │
│   │ Animation  │  │  Network    │  │    UI      │              │
│   │   Driver   │  │   Bridge   │  │  Layer     │              │
│   └────────────┘  └────────────┘  └────────────┘              │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 优先级规则

| 优先级 | 层 | 说明 |
|--------|-----|------|
| P1 | Hit | 最高，受击/死亡可打断一切 |
| P2 | Attack | 攻击/技能，可锁定 Base 层 |
| P3 | Base | 基础移动，最低优先级 |

### 2.3 打断条件

- **Hit 层激活** → Attack 和 Base 立即退出
- **Attack 层激活** → Base 层被锁定（但动画可通过 Avatar Mask 保留下半身）
- **高优先级请求激活时** → 检查当前状态霸体标记

---

## 3. BaseLayer FSM

### 3.1 状态定义

| 状态 | 说明 | 进入条件 | 退出条件 |
|------|------|----------|----------|
| Idle | 站立待机 | 无移动输入 + 着地 | 移动/冲刺/跳跃/被更高层打断 |
| Move | 行走 | 有移动输入 + 未冲刺 | 无移动输入/冲刺/跳跃/被更高层打断 |
| Sprint | 冲刺 | 移动中按住冲刺键 | 松开冲刺键/停止移动/跳跃 |
| JumpStart | 起跳 | 跳跃请求 + 着地 | 动画完成（约 3-5 帧） |
| JumpAir | 空中 | JumpStart 完成 | 着地 |
| JumpEnd | 落地缓冲 | JumpAir 着地 | 动画完成（normalizedTime ≥ 0.9） |

### 3.2 状态转换图

```
                         ┌──────────────────────────────────────────┐
                         │              被更高优先级打断             │
                         │         (Hit层激活 / Attack层激活)        │
                         └──────────────────────────────────────────┘
                                            │
                                            ▼
┌────────┐  MoveDir>0   ┌────────┐  IsSprint   ┌────────┐  RequestJump  ┌───────────┐
│  Idle  │ ────────────▶│  Move  │ ───────────▶│ Sprint │ ─────────────▶│ JumpStart │
│  (0)   │ ◀──────────── │  (1)   │ ◀───────────│  (2)   │               │    (3)    │
└────────┘  MoveDir=0   └────────┘  !IsSprint   └────────┘               └─────┬─────┘
    ▲                              ▲                     ▲                      │
    │                              │                     │                      │ 动画完成
    │                              │                     │                      ▼
    │                              │                     │                ┌───────────┐
    │                              │                     │                │  JumpAir  │
    │                              │                     │                │    (4)    │
    │                              │                     │                └─────┬─────┘
    │                              │                     │                      │ IsGrounded
    │                              │                     │                      ▼
    │                              │                     │                ┌───────────┐
    └──────────────────────────────┴─────────────────────┴────────────────▶│  JumpEnd  │
                                                                    IsGrounded│    (5)    │
                                                                         ◀───┴───────────┘
                                                                              ExitTime≥0.9
```

### 3.3 关键设计决策

1. **JumpStart 不可打断** — 起跳瞬间锁定，除非被 Hit 层打断
2. **JumpEnd 允许攻击取消** — 可以在落地动画期间输入攻击
3. **Sprint 期间可跳跃** — 但冲刺速度在跳跃时可能降低

---

## 4. AttackLayer FSM

### 4.1 状态定义

| 状态 | 说明 | 进入条件 | 退出条件 |
|------|------|----------|----------|
| Idle | 待命 | 无技能执行 | 收到技能请求 |
| Attack1 | 普通攻击第一击 | 普攻请求 + Idle | 动画完成/连击窗口结束 |
| Attack2 | 普通攻击第二击 | Attack1 期间连击输入 | 动画完成/连击窗口结束 |
| SkillQ | 技能Q | Q 键请求 | 技能状态机完成 |
| SkillR | 技能R | R 键请求 | 技能状态机完成 |

### 4.2 技能子状态机 (SkillStateMachine)

作为 Attack 层的子状态机，管理复杂技能的内部状态：

```
┌─────────────────────────────────────────────────────────────────┐
│                      SkillStateMachine                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────┐   TryStart()    ┌──────────┐                       │
│  │ Cooldown │◀────────────────│  Ready   │                       │
│  └──────────┘                 └────┬─────┘                       │
│       ▲                           │                             │
│       │ Cooldown                  │ ReleaseType?                 │
│       │                           ▼                             │
│       │              ┌─────────────────────────────┐            │
│       │              │                             │            │
│       │       ┌──────┼──────┬────────┬────────┐     │            │
│       │       ▼      ▼      ▼        ▼        ▼     │            │
│       │   Instant  Cast   Channel  Charge  Timed   │            │
│       │       │      │       │        │        │              │
│       │       └──────┴───┬───┴────────┴────────┘              │
│       │                  ▼                                    │
│       │            ┌──────────┐                              │
│       │            │Execution │ ← 判定帧/效果触发            │
│       │            └────┬─────┘                              │
│       │                 │                                    │
│       │                 ▼                                    │
│       │           ┌──────────┐                                │
│       │           │Recovery │ ← 收招硬直                      │
│       │           └────┬─────┘                                │
│       │                │                                     │
│       └────────────────┼──────────────────────────────────────┘
│                        ▼
│                  ┌──────────┐
│                  │Complete  │
│                  └──────────┘
│                        ▲
│                        │ Interrupted
│                        │
│                  ┌──────────┐
│                  │ Cancelled│
│                  └──────────┘
└─────────────────────────────────────────────────────────────────┘
```

### 4.3 技能子状态 (SkillSubState)

| 状态 | 说明 | 可移动 | 可被打断 |
|------|------|--------|----------|
| Cooldown | 冷却中 | ✓ | - |
| Ready | 就绪 | ✓ | ✓ |
| Casting | 读条中 | ✗ | ✓ |
| Channeling | 引导中 | ✓ | ✓ |
| Charging | 蓄力中 | ✗ | ✓ |
| Execution | 执行中 | 技能定义 | ✗ |
| Recovery | 收招 | 技能定义 | ✓ |
| Cancelled | 被打断 | - | - |
| Completed | 正常完成 | - | - |

### 4.4 打断矩阵 (InterruptionMatrix)

| 当前技能状态 | 移动输入 | 普攻 | 另一技能 | 伤害 | 眩晕 |
|-------------|---------|------|---------|------|------|
| Casting | ✗ | ✓ | ✓ | ✓ | ✗ |
| Channeling | ✓ | ✓ | ✓ | ✓ | ✗ |
| Charging | ✗ | ✗ | ✓ | ✓ | ✗ |
| Recovery | ✓ | ✓ | ✓ | ✓ | ✓ |
| Execution | ✗ | ✗ | ✗ | ✗ | ✗ |

---

## 5. HitLayer FSM

### 5.1 状态定义

| 状态 | 说明 | 进入条件 | 退出条件 |
|------|------|----------|----------|
| None | 无受击 | - | 受到伤害 |
| Hit | 普通受击 | 受到非致命伤害 | 动画完成 |
| Knockback | 击退 | 强力攻击/特定技能 | 动画完成 |
| Down | 倒地 | 累计击倒值满/击倒攻击 | 倒地时间结束 |
| Death | 死亡 | 生命值归零 | 复活/永久 |

### 5.2 状态转换图

```
┌─────────────────────────────────────────────────────────────────┐
│                         HitLayer FSM                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  [任意层] ──(受到伤害)──▶ ┌────────┐                           │
│                            │  Hit   │                           │
│                            └───┬────┘                           │
│                                │ ExitTime≥0.9                   │
│                                ▼                                 │
│                         [返回上层] 或                             │
│                                │                                │
│                           KnockbackForce?                        │
│                                │                                │
│                                ▼                                 │
│                           ┌──────────┐                          │
│                           │Knockback │                          │
│                           └────┬─────┘                          │
│                                │ ExitTime≥1.0                   │
│                                ▼                                 │
│                           [倒地判定?] ──(是)──▶ ┌────────┐      │
│                                                  │  Down  │      │
│                                                  └────┬───┘      │
│                                                       │ GetUpTime │
│                                                       ▼           │
│                                                  [起身动画]       │
│                                                       │           │
│                                                  Health≤0        │
│                                                       │           │
│                                                       ▼           │
│                                                  ┌────────┐       │
│                                                  │ Death  │       │
│                                                  └────────┘       │
└─────────────────────────────────────────────────────────────────┘
```

### 5.3 霸体检查 (简化版)

```csharp
bool CanBeInterrupted(InterruptionSource source)
{
    // 有霸体标记的状态不可被打断
    if (CurrentState.HasSuperArmor) return false;
    
    // 检查打断矩阵
    return InterruptionMatrix.CanInterrupt(CurrentState, source);
}
```

---

## 6. EventBus 系统

### 6.1 事件类型定义

```csharp
// 状态变化事件
public class StateChangedEvent : IEvent
{
    public LayerType Layer;
    public string PreviousState;
    public string CurrentState;
}

// 技能事件
public class SkillActivatedEvent : IEvent { public int SkillId; }
public class SkillCompletedEvent : IEvent { public int SkillId; }
public class SkillInterruptedEvent : IEvent { public int SkillId; InterruptionSource Source; }

// 伤害事件
public class DamageEvent : IEvent
{
    public EntityId Source;
    public EntityId Target;
    public float Damage;
    public DamageType Type;
    public bool IsCritical;
}

// 动画事件
public class AnimationEvent : IEvent
{
    public string EventName;
    public float NormalizedTime;
}

// 移动事件
public class JumpEvent : IEvent { public JumpPhase Phase; }
public class LandEvent : IEvent { public float FallDistance; }
```

### 6.2 使用示例

```csharp
// 角色控制器
public void TakeDamage(DamageData damage)
{
    // 通知 HitLayer
    EventBus.Emit(new DamageEvent { Target = this.Id, Damage = damage.Total });
    
    // 触发霸体检查
    if (!HasSuperArmor)
    {
        HitLayer.Interrupt(InterruptionSource.DamageTaken);
    }
}

// 网络同步 (第三阶段)
public class NetworkBridge : IEventListener
{
    public void OnSkillActivated(SkillActivatedEvent e)
    {
        if (IsLocalPlayer)
            SendToServer(new CS_SkillActivate { SkillId = e.SkillId });
    }
}
```

---

## 7. 协调器设计

### 7.1 StateCoordinator

```csharp
public class StateCoordinator
{
    private BaseLayerFSM _baseLayer;
    private AttackLayerFSM _attackLayer;
    private HitLayerFSM _hitLayer;
    
    // 当前活跃层
    public LayerType ActiveLayer { get; private set; }
    
    // 处理输入请求
    public bool TryActivateSkill(int skillId, SkillData data)
    {
        // Hit 层最高优先级
        if (_hitLayer.CurrentState != HitState.None)
            return false;
        
        // 检查 Attack 层状态
        if (_attackLayer.CanActivateSkill(data))
        {
            _attackLayer.ActivateSkill(skillId);
            UpdateActiveLayer(LayerType.Attack);
            return true;
        }
        
        return false;
    }
    
    // 处理伤害
    public void HandleDamage(EntityId source, DamageData damage)
    {
        var interruptSource = DetermineInterruptionSource(damage);
        
        // 霸体检查
        if (_attackLayer.HasSuperArmor)
            return;
        
        // Hit 层打断一切
        _hitLayer.EnterHit(damage);
        UpdateActiveLayer(LayerType.Hit);
    }
    
    // 更新活跃层
    private void UpdateActiveLayer(LayerType newLayer)
    {
        // 设置层权重，控制动画混合
        AnimationDriver.SetLayerWeight(LayerType.Hit, newLayer == LayerType.Hit ? 1f : 0f);
        // ...
    }
}
```

---

## 8. 动画系统设计

### 8.1 混合方案架构

```
┌─────────────────────────────────────────────────────────────────┐
│                    Animator Controller                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Layer 0: Base Layer (Weight=1, Mask=None)                      │
│  ├── States: Idle, Move, Sprint, JumpStart, JumpAir, JumpEnd    │
│  └── Default: Idle                                              │
│                                                                  │
│  Layer 1: Attack Layer (Weight=0/1, Mask=UpperBody)             │
│  ├── States: Idle, Attack1, Attack2, SkillQ, SkillR             │
│  └── Default: Idle                                              │
│                                                                  │
│  Layer 2: Hit Layer (Weight=0/1, Mask=None)                     │
│  ├── States: None, Hit, Knockback, Down, Death                  │
│  └── Default: None                                              │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Playable API 控制
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    AnimationDriver                               │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  // 层权重控制                                                   │
│  SetLayerWeight(LayerType.Attack, 1f);  // 攻击时开启攻击层     │
│  SetLayerWeight(LayerType.Hit, 1f);     // 受击时完全覆盖       │
│                                                                  │
│  // 状态同步                                                    │
│  SetBaseState(BaseState.Move);                                  │
│  SetAttackState(AttackState.Attack1);                           │
│                                                                  │
│  // 触发器                                                       │
│  TriggerAttack();                                               │
│  TriggerHit();                                                  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 8.2 Playable API 扩展

```csharp
public class AnimationMixer
{
    private AnimationLayerMixerPlayable _layerMixer;
    
    // 动态调整层权重（用于受击覆盖）
    public void SetLayerWeight(int layerIndex, float weight)
    {
        var inputPort = (uint)layerIndex;
        _layerMixer.SetInputWeight(inputPort, weight);
    }
    
    // 过渡动画
    public void CrossFade(string stateName, float duration)
    {
        // 实现平滑过渡
    }
}
```

---

## 9. 技能系统设计

### 9.1 技能数据结构

```csharp
[CreateAssetMenu(fileName = "SkillData", menuName = "Game/Skill")]
public class SkillData : ScriptableObject
{
    public int SkillId;
    public string SkillName;
    public SkillType Type;
    public float ManaCost;
    public float Cooldown;
    
    // 释放类型
    public ReleaseType ReleaseType;
    public float CastTime;          // 读条时间
    public float ChannelDuration;   // 引导时间
    public float MinChargeTime;     // 最小蓄力
    public float MaxChargeTime;     // 最大蓄力
    public bool CanMoveWhileCasting;
    
    // 效果
    public float Range;
    public float[] HitboxTimings;
    public DamageData DamageData;
    public List<EffectData> ApplyEffects;
    
    // 动画
    public string AnimationState;
    public int AnimatorLayer;
}

public enum ReleaseType
{
    Instant,     // 瞬发
    Channeled,   // 引导型
    Charged,     // 蓄力型
    Timed        // 读条型
}
```

### 9.2 CooldownManager

```csharp
public class CooldownManager
{
    private Dictionary<int, float> _cooldowns = new();
    
    public bool IsReady(int skillId)
    {
        return !_cooldowns.TryGetValue(skillId, out var remaining) || remaining <= 0;
    }
    
    public float GetRemaining(int skillId)
    {
        return _cooldowns.TryGetValue(skillId, out var remaining) ? remaining : 0;
    }
    
    public void StartCooldown(int skillId, float duration)
    {
        _cooldowns[skillId] = duration;
    }
    
    public void Update(float deltaTime)
    {
        foreach (var key in _cooldowns.Keys.ToList())
        {
            _cooldowns[key] -= deltaTime;
        }
    }
}
```

### 9.3 DamageCalculator

```csharp
public class DamageCalculator
{
    public DamageResult Calculate(EntityId attacker, EntityId target, DamageData data)
    {
        var stats = _statsSystem.GetStats(attacker);
        var result = new DamageResult();
        
        // 计算基础伤害
        float baseDamage = data.BaseDamage;
        
        // 属性缩放
        float scalingAttr = stats.GetAttribute(data.ScalingAttribute);
        float scaledDamage = baseDamage + scalingAttr * data.AttackRatio;
        
        // 暴击计算
        float critRate = stats.GetCritRate() + data.CriticalRateBonus;
        result.IsCritical = Random.value < critRate;
        float critMultiplier = result.IsCritical 
            ? stats.GetCritDamage() + data.CriticalDamageBonus 
            : 1f;
        
        result.Total = scaledDamage * critMultiplier;
        
        // 真实伤害
        if (data.IsTrueDamage)
            result.Total = baseDamage;
        
        return result;
    }
}
```

### 9.4 BuffSystem

```csharp
public class BuffHandler
{
    private List<ActiveBuff> _activeBuffs = new();
    
    public void ApplyBuff(EntityId target, BuffData data)
    {
        var buff = new ActiveBuff
        {
            Id = data.BuffId,
            Duration = data.Duration,
            Effects = data.Effects
        };
        
        _activeBuffs.Add(buff);
        
        // 应用效果
        foreach (var effect in data.Effects)
        {
            effect.Apply(target);
        }
        
        EventBus.Emit(new BuffAppliedEvent { Target = target, BuffId = data.BuffId });
    }
    
    public void Update(float deltaTime)
    {
        for (int i = _activeBuffs.Count - 1; i >= 0; i--)
        {
            var buff = _activeBuffs[i];
            buff.Duration -= deltaTime;
            
            if (buff.Duration <= 0)
            {
                RemoveBuff(buff);
            }
        }
    }
    
    private void RemoveBuff(ActiveBuff buff)
    {
        // 移除效果
        foreach (var effect in buff.Effects)
        {
            effect.Remove(buff.Target);
        }
        
        _activeBuffs.Remove(buff);
        EventBus.Emit(new BuffRemovedEvent { Target = buff.Target, BuffId = buff.Id });
    }
}
```

---

## 10. 网络预留接口

### 10.1 网络桥接接口

```csharp
public interface INetworkBridge
{
    // 发送
    void SendInput(InputCommand command);
    void SendSkillActivation(int skillId);
    void SendPositionSync(Vector3 position, Quaternion rotation);
    
    // 接收
    event Action<ServerStateUpdate> OnServerStateUpdate;
    event Action<ServerDamageEvent> OnDamageReceived;
}

public class NetworkBridgeStub : INetworkBridge
{
    // 本地模式实现，不发送网络包
    public void SendInput(InputCommand command) { }
    public void SendSkillActivation(int skillId) { }
    public void SendPositionSync(Vector3 position, Quaternion rotation) { }
}

public class NetworkBridgeImpl : INetworkBridge
{
    private KcpClient _kcpClient;
    
    // 第三阶段实现，集成 KCP
    public void SendInput(InputCommand command) 
    {
        var packet = new CP_Input { Command = command };
        _kcpClient.Send(packet);
    }
}
```

---

## 11. 调试工具

### 11.1 Runtime 状态窗口

```
┌─────────────────────────────────────────────────────────────────┐
│                    3C System Debug                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─ BaseLayer ─────────────────────────────────────────────┐   │
│  │ State: [Move]  │ Speed: 5.2  │ Grounded: true          │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─ AttackLayer ──────────────────────────────────────────┐   │
│  │ State: [Attack1] │ Combo: 1 │ SkillState: Execution   │   │
│  │ Cooldown: 0.3s   │ SuperArmor: false                    │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─ HitLayer ──────────────────────────────────────────────┐   │
│  │ State: [None]   │ Invincible: false                    │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                  │
│  [Lock Base] [Lock Attack] [Force Hit] [Clear All]             │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 11.2 状态日志

```csharp
public static class StateLogger
{
    private static List<StateLogEntry> _log = new();
    private static StreamWriter _fileWriter;
    
    public static void LogStateChange(EntityId entity, LayerType layer, 
        string from, string to)
    {
        var entry = new StateLogEntry
        {
            Timestamp = Time.time,
            Entity = entity,
            Layer = layer,
            FromState = from,
            ToState = to
        };
        
        _log.Add(entry);
        Debug.Log($"[{entity}] {layer}: {from} → {to}");
        _fileWriter?.WriteLine($"{entry.Timestamp},{entity},{layer},{from},{to}");
    }
    
    public static void DumpToFile(string path)
    {
        // 输出到文件，便于复现 BUG
    }
}
```

---

## 12. 文件结构

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Sys3C.asmdef
│
├── Core/
│   ├── StateCoordinator.cs        # 状态协调器
│   ├── EventBus.cs                # 事件总线
│   └── StatePriority.cs            # 优先级定义
│
├── Layers/
│   ├── Base/
│   │   ├── BaseLayerFSM.cs
│   │   ├── BaseStates.cs
│   │   └── BaseTransitionTable.cs
│   │
│   ├── Attack/
│   │   ├── AttackLayerFSM.cs
│   │   ├── AttackStates.cs
│   │   ├── SkillStateMachine/
│   │   │   ├── SkillStateMachine.cs
│   │   │   ├── SkillSubStates.cs
│   │   │   └── SkillTransitionTable.cs
│   │   └── InterruptionMatrix.cs
│   │
│   └── Hit/
│       ├── HitLayerFSM.cs
│       ├── HitStates.cs
│       └── HitTransitionTable.cs
│
├── Animation/
│   ├── AnimationDriver.cs
│   ├── AnimationMixer.cs           # Playable API 扩展
│   └── StateBehaviours/
│       ├── BaseStateBehaviour.cs
│       ├── AttackStateBehaviour.cs
│       └── HitStateBehaviour.cs
│
├── Character/
│   ├── CharacterController.cs
│   ├── CharacterData.cs
│   ├── GroundDetector.cs
│   └── Adapters/
│       ├── StatsAdapter.cs
│       ├── ShieldAdapter.cs
│       └── StatusAdapter.cs
│
├── Skill/
│   ├── Definition/
│   │   ├── SkillData.cs
│   │   ├── SkillType.cs
│   │   ├── DamageData.cs
│   │   └── SkillID.cs
│   │
│   ├── Runtime/
│   │   ├── SkillExecutor.cs
│   │   ├── SkillCoordinator.cs
│   │   ├── SkillInputBuffer.cs
│   │   ├── CooldownManager.cs
│   │   └── DamageCalculator.cs
│   │
│   └── Effect/
│       ├── EffectHandler.cs
│       ├── BuffData.cs
│       └── BuffHandler.cs
│
├── Network/
│   ├── INetworkBridge.cs           # 网络接口
│   ├── NetworkBridgeStub.cs        # 本地实现
│   └── NetworkBridgeImpl.cs        # KCP 实现 (第三阶段)
│
├── Camera/
│   └── ThirdPersonCameraController.cs
│
├── Input/
│   ├── InputManager.cs
│   ├── KeyboardAdapter.cs
│   └── JoystickAdapter.cs
│
├── Debug/
│   ├── DebugWindow.cs              # Runtime 窗口
│   ├── StateLogger.cs              # 状态日志
│   └── DebugCommands.cs            # 控制台命令
│
└── Entry/
    └── Sys3CEntry.cs
```

---

## 13. 与原设计对比

| 方面 | 原设计 | 新设计 |
|------|--------|--------|
| 层数 | 2层 | **3层** (新增 Hit) |
| 协调器 | FSMManager | **StateCoordinator** |
| 技能 | AttackFSM | **SkillStateMachine** |
| 打断 | 无 | **InterruptionMatrix** |
| 冷却 | 无 | **CooldownManager** |
| 伤害 | 无 | **DamageCalculator** |
| Buff | 框架 | **完整 BuffSystem** |
| 事件通信 | 直接引用 | **EventBus** |
| 动画 | 仅 Animator | **Playable API 扩展** |
| 调试 | Debug.Log | **DebugWindow + StateLogger** |
| 网络 | 框架 | **接口预留** |

---

## 14. 实现阶段

### Phase 1: FSM 重构 (当前)
1. Core: EventBus, StateCoordinator, StatePriority
2. BaseLayer: FSM + States + Transitions
3. HitLayer: FSM + States + Transitions
4. AnimationDriver + AnimationMixer
5. DebugWindow + StateLogger

### Phase 2: 技能系统
1. SkillData + DamageData ScriptableObject
2. SkillStateMachine
3. CooldownManager
4. DamageCalculator
5. BuffSystem
6. SkillCoordinator

### Phase 3: 网络集成
1. INetworkBridge 实现
2. KCP 集成
3. 客户端预测
4. 服务器同步

---

## 15. 附录：Animator Controller 参数

| 参数 | 类型 | 驱动层 | 说明 |
|------|------|--------|------|
| BaseState | Int | Base Layer | 基础状态 (0-5) |
| AttackState | Int | Attack Layer | 攻击状态 (0-4) |
| HitState | Int | Hit Layer | 受击状态 (0-4) |
| IsJumping | Bool | Base Layer | 跳跃中标记 |
| IsHit | Bool | Hit Layer | 受击中标记 |
| Attack | Trigger | Attack Layer | 普攻触发 |
| SkillQ | Trigger | Attack Layer | 技能Q触发 |
| SkillR | Trigger | Attack Layer | 技能R触发 |
| Hit | Trigger | Hit Layer | 受击触发 |

---

**文档版本:** 1.0
**创建日期:** 2026-05-04