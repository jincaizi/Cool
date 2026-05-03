# 技能系统技术文档

## 1. 系统概述

### 1.1 设计目标
- 解耦技能系统与Character依赖，通过接口实现
- 支持多种释放类型：瞬发、读条、蓄力、引导
- 完整的连段系统支持
- 技能打断矩阵与优先级控制
- 输入缓冲与预输入支持
- 网络同步支持

### 1.2 程序集结构
```
Assets/Scripts/Hotfix/GameSystems/
├── Skills/
│   ├── Skills.asmdef           # 独立程序集
│   ├── Definition/             # 枚举与常量
│   ├── Data/                   # ScriptableObject数据
│   ├── Effect/                 # 效果系统与接口
│   └── Runtime/                # 运行时逻辑
└── Sys3C/
    ├── Sys3C.asmdef            # 依赖Skills程序集
    ├── Character/              # 角色系统
    ├── Skill/                  # 集成层
    └── Network/                # 网络同步
```

## 2. 核心接口设计

### 2.1 IEffectTarget - 效果目标接口
```csharp
public interface IEffectTarget
{
    IEffectStats Stats { get; }
    IShieldSystem ShieldSystem { get; }
    IPhysicsSystem PhysicsSystem { get; }
    IStatusController StatusController { get; }
    Transform transform { get; }
    void Heal(float amount);
}
```
**设计说明**: 解耦技能系统与具体角色实现，任何实现此接口的类都可作为技能目标。

### 2.2 IEffectStats - 属性统计接口
```csharp
public interface IEffectStats
{
    float GetAttribute(AttributeType type);
    float GetMaxHealth();
    void AddModifier(AttributeType type, string id, float value, ModifierType modType);
    void RemoveModifier(AttributeType type, string id);
}
```

### 2.3 其他接口
| 接口名 | 用途 |
|--------|------|
| IShieldSystem | 护盾管理 |
| IPhysicsSystem | 物理效果（击退） |
| IStatusController | 状态控制（眩晕） |

## 3. 数据结构

### 3.1 枚举定义

#### SkillSubState - 技能子状态
```
None → Ready → Casting → Execution → Recovery → Completed
                ↓
            Channeling
                ↓
            Charging
```
| 状态 | 说明 |
|------|------|
| Ready | 就绪，可释放 |
| Casting | 读条中 |
| Channeling | 引导中 |
| Charging | 蓄力中 |
| Execution | 执行中（判定帧） |
| Recovery | 收招硬直 |
| Cancelled | 被中断 |
| Completed | 正常完成 |

#### ReleaseType - 释放类型
| 类型 | 说明 | 状态流转 |
|------|------|----------|
| Instant | 瞬发 | Ready → Execution |
| Timed | 读条 | Ready → Casting → Execution |
| Charged | 蓄力 | Ready → Casting → Charging → Execution |
| Channeled | 引导 | Ready → Casting → Channeling → Execution |

#### InterruptionSource - 打断来源
```
MovementInput, BasicAttack, AnotherSkill, DamageTaken,
Stun, RollDodge, Parry, TimeOut
```

### 3.2 SkillData - 技能数据基类
```csharp
[CreateAssetMenu(fileName = "New Skill", menuName = "Game/Skills/Skill Data")]
public class SkillData : ScriptableObject
{
    // 基础信息
    int SkillId;
    string SkillName;
    Definition.SkillType SkillType;    // BasicAttack/Special/Ultimate/Passive/Item
    Definition.SkillQuality Quality;   // Common/Uncommon/Rare/Epic/Legendary

    // 消耗与冷却
    int ManaCost;
    float Cooldown;
    int StaminaCost;

    // 释放行为
    Definition.ReleaseType ReleaseType;
    float CastTime;        // 读条时间
    float ChannelDuration; // 引导持续时间
    float MinChargeTime;   // 最小蓄力时间
    float MaxChargeTime;   // 最大蓄力时间

    // 移动限制
    bool CanMoveWhileCasting;
    bool CanMoveWhileChanneling;
    bool CanRotateWhileCasting;

    // 动画
    string AnimatorTrigger;
    AnimationClip CastClip;
    AnimationClip ReleaseClip;
    AnimationClip ChannelClip;

    // 战斗属性
    float Range;           // 范围
    float Angle;           // 扇形角度
    float AreaRadius;      // AOE半径
    LayerMask TargetMask;
    float[] HitboxTimings; // 判定帧时间点列表
    DamageData DamageData;

    // 打断配置
    bool CanBeInterruptedByDamage;
    bool CanBeInterruptedByMovement;
    int InterruptionPriority;
}
```

### 3.3 BasicAttackData - 普攻数据
```csharp
public class BasicAttackData : SkillData
{
    int ComboIndex;           // 第几段普攻
    float ComboWindow;        // 连段窗口时间 (0.5s)
    float ComboResetTime;     // 连段重置时间 (3s)
    BasicAttackData NextCombo;// 下一段普攻

    // 命中属性
    float HitStopDuration;   // 命中顿帧
    float ImpactForce;       // 冲击力度

    // 动画
    AnimationClip OverrideClip;

    // 移动
    bool EnableMovement;
    float MovementSpeed;

    // 收招取消
    bool AllowRecoveryCancel;
    float CancelableWindowStart;
    float CancelableWindowEnd;
}
```

### 3.4 SpecialSkillData - 特殊技能数据
```csharp
public class SpecialSkillData : SkillData
{
    // 蓄力属性
    AnimationCurve ChargeDamageCurve;
    AnimationCurve ChargeAreaCurve;
    bool HoldToCharge;
    bool ReleaseToFire;

    // 引导属性
    float TickInterval;
    float TickDamagePercent;
    bool ChannelFollowsTarget;
    bool BreakOnTargetMove;

    // AOE属性
    bool IsAOE;
    AOEDamageType AOEDamageType;  // Center/Origin/Direction
    bool DamageFalloff;
    AnimationCurve DamageFalloffCurve;

    // 弹道属性
    GameObject ProjectilePrefab;
    float ProjectileSpeed;
    bool ProjectilePierce;
    int MaxPierceTargets;
    bool Homing;

    // 多目标
    int MaxHitTargets;
    HitPriority HitPriority;  // Nearest/Furthest/LowestHP/HighestHP/HighestThreat
}
```

## 4. 技能状态机

### 4.1 SkillStateMachine
```csharp
public class SkillStateMachine
{
    SkillSubState CurrentState;
    float StateStartTime;
    float ElapsedTime;

    // 蓄力相关
    float ChargeStartTime;
    bool IsCharging;

    // 引导相关
    int CurrentTick;
    float LastTickTime;
    int TotalChannelTicks;

    // 事件
    event Action<SkillSubState> OnStateChanged;
    event Action<int> OnHitboxFrame;
    event Action OnHitConfirm;
    event Action OnSkillCompleted;
    event Action<InterruptionSource> OnSkillInterrupted;
}
```

### 4.2 状态转换流程
```
TryStart()
    ↓
[ReleaseType.Instant] → Execution → Recovery → Completed
    ↓
[ReleaseType.Timed] → Casting → (时间到) → Execution → Recovery → Completed
    ↓
[ReleaseType.Charged] → Casting → Charging → (松手/满蓄) → Execution → Recovery → Completed
    ↓
[ReleaseType.Channeled] → Casting → Channeling → (遍历HitboxTimings触发Tick) → Execution → Recovery → Completed
```

## 5. 技能协调器

### 5.1 SkillCoordinator
```csharp
public class SkillCoordinator
{
    IEffectTarget Owner;
    Dictionary<int, SkillData> SkillDatabase;
    Dictionary<int, SkillExecutor> ActiveExecutors;
    CooldownManager CooldownManager;
    SkillInputBuffer InputBuffer;
    SkillInterruptionMatrix InterruptionMatrix;

    SkillExecutor CurrentSkill;
    SkillExecutor QueuedSkill;

    // 连段追踪
    int CurrentComboIndex;
    float LastAttackTime;
    float ComboWindowEndTime;
}
```

### 5.2 技能链接规则
```csharp
bool CanChainSkill(int nextSkillId)
{
    switch (CurrentSubState)
    {
        case Execution:
        case HitConfirm:
            return false;  // 判定帧不允许取消

        case Cancelled:
        case Completed:
            return true;

        case Recovery:
            return NextData.CanCancelIntoBasicAttack || CanCancelIntoOtherSkill;

        case Casting:
            return NextData.ReleaseType == ReleaseType.Instant;

        case Channeling:
            return NextData.ReleaseType == ReleaseType.Instant && CanMoveWhileChanneling;

        case Charging:
            return false;  // 蓄力不允许取消

        default:
            return false;
    }
}
```

## 6. 输入缓冲系统

### 6.1 SkillInputBuffer
```csharp
public class SkillInputBuffer
{
    Queue<BufferedCommand> CommandQueue;
    float BufferWindow = 0.15f;    // 150ms缓冲窗口
    float MaxBufferTime = 0.5f;  // 最大缓冲保留时间
    int MaxQueueSize = 3;        // 最大缓冲数量
}

public struct SkillInput
{
    int SkillId;
    Vector3 TargetPosition;
    int TargetEntityId;
    Vector3 InputDirection;
    bool IsRangedSkill;
    bool IsCharging;
}
```

### 6.2 缓冲处理流程
1. 技能输入 → 检查冷却/资源
2. 如果冷却中 → 存入InputBuffer
3. 每帧ProcessInputBuffer()检查缓冲命令
4. 冷却结束且无活动技能 → 执行缓冲命令

## 7. 打断矩阵

### 7.1 SkillInterruptionMatrix
```csharp
public class SkillInterruptionMatrix
{
    // 默认规则表
    static readonly Dictionary<SkillType, Dictionary<InterruptionSource, bool>> DefaultRules;

    // 技能特定规则
    Dictionary<int, Dictionary<InterruptionSource, bool>> CustomRules;

    bool CanBeInterrupted(SkillData skillData, InterruptionSource source);
    bool CanBeInterruptedInState(SkillData skillData, SkillSubState subState, InterruptionSource source);
    int GetInterruptionPriority(InterruptionSource source);
}
```

### 7.2 默认打断规则表
| 技能类型 | DamageTaken | Stun | RollDodge | Parry | MovementInput |
|----------|-------------|------|-----------|-------|---------------|
| BasicAttack | false | true | true | true | true |
| Special | true | true | false | true | false |
| Ultimate | **false** | **false** | false | **false** | false |

**注意**: 大招具有霸体保护，不受伤害和控制效果打断。

## 8. 冷却管理

### 8.1 CooldownManager
```csharp
public class CooldownManager
{
    Dictionary<int, CooldownEntry> Cooldowns;

    void StartCooldown(int skillId, float duration);
    bool IsOnCooldown(int skillId);
    float GetRemainingCooldown(int skillId);
    float GetNormalizedCooldown(int skillId);  // [0,1]
    void ReduceCooldown(int skillId, float amount);
    void ReduceCooldownPercent(int skillId, float percent);
    void ResetCooldown(int skillId);
    void ClearAll();
}
```

## 9. 效果系统

### 9.1 EffectData基类
```csharp
public class EffectData
{
    EffectType Type;
    string EffectId;
    float Duration;
    int MaxStacks;
    StackingRule StackingRule;  // Refresh/Stack/Ignore
    bool IsTickEffect;
    float TickInterval;

    void Apply(IEffectTarget caster, IEffectTarget target);
    void Remove(IEffectTarget caster, IEffectTarget target);
    void OnTick(IEffectTarget caster, IEffectTarget target);
}
```

### 9.2 效果类型
| 类型 | 说明 | 特殊字段 |
|------|------|----------|
| BuffEffectData | 属性加成 | AttributeType, Value, ModifierType |
| HealEffectData | 治疗 | BaseHeal, SpellPowerRatio, PercentOfMaxHealth |
| ShieldEffectData | 护盾 | ShieldAmount, AbsorbedDamageType |
| KnockbackEffectData | 击退 | Force, UpwardForce, Radius |
| StunEffectData | 眩晕 | CanBeCleanse |

## 10. 3C系统集成

### 10.1 适配器模式
```
┌─────────────────────────────────────┐
│     SkillCoordinatorBridge           │ ← 实现IEffectTarget
│  (继承自IEffectTarget)               │
└──────────────┬──────────────────────┘
               │
    ┌──────────┼──────────┬──────────┐
    ↓          ↓          ↓          ↓
┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
│Stats   │ │Shield  │ │Physics │ │Status  │
│Adapter │ │Adapter │ │Adapter │ │Adapter │
└────────┘ └────────┘ └────────┘ └────────┘
    ↓          ↓          ↓          ↓
┌─────────────────────────────────────┐
│      CharacterController              │
│   (现有角色控制器实现)               │
└─────────────────────────────────────┘
```

### 10.2 CharacterStatsAdapter
```csharp
public class CharacterStatsAdapter : IEffectStats
{
    Dictionary<AttributeType, float> BaseAttributes;
    Dictionary<AttributeType, Dictionary<string, Modifier>> Modifiers;

    float GetAttribute(AttributeType type);
    float GetMaxHealth();
    void AddModifier(AttributeType type, string id, float value, ModifierType modType);
    void RemoveModifier(AttributeType type, string id);
}
```

### 10.3 修饰符类型
```csharp
public enum ModifierType
{
    Flat,        // 加法: Final = Base + Flat
    PercentAdd,  // 百分比加法: Final = Base * (1 + PercentAdd)
    PercentMult  // 百分比乘法: Final = Base * PercentMult
}
```

### 10.4 伤害计算流程
```csharp
float CalculateFinalDamage(IEffectStats attackerStats)
{
    float damage = BaseDamage;
    damage += ScalingAttribute * AttackRatio;
    // 暴击计算
    if (Random.value < CriticalChance + CriticalRateBonus)
        damage *= (1.5f + CriticalDamageBonus);
    return damage;
}
```

## 11. Buff系统

### 11.1 BuffHandler
```csharp
public class BuffHandler
{
    IEffectTarget Owner;
    Dictionary<string, ActiveBuff> ActiveBuffs;

    void ApplyBuff(BuffData data, IEffectTarget caster);
    void RemoveBuff(string buffId);
    bool HasBuff(string buffId);
    int GetStackCount(string buffId);
    void Update(float deltaTime);
    void ClearAll();
    bool HasControlEffect();
}
```

### 11.2 堆叠规则
| 规则 | 行为 |
|------|------|
| Refresh | 刷新持续时间，保持层数 |
| Stack | 增加层数，上限MaxStacks |
| Ignore | 忽略新效果，保持现有 |

### 11.3 ActiveBuff
```csharp
public class ActiveBuff
{
    BuffData Data;
    IEffectTarget Caster;
    IEffectTarget Owner;
    int CurrentStacks;
    float RemainingTime;
    float TickTimer;

    void Refresh();
    void AddStack();
    void Update(float deltaTime);
    void Remove();
}
```

## 12. 网络同步

### 12.1 SkillSyncData
```csharp
[Serializable]
public struct SkillSyncData
{
    int SkillId;
    SkillSubState SubState;
    float StateElapsedTime;
    float ChargeProgress;
    long ServerTimestamp;
}
```

### 12.2 同步策略
- **同步间隔**: 100ms
- **同步内容**: 技能ID、状态、状态时间、蓄力进度
- **预测执行**: 客户端立即执行，服务端校验
- **状态回滚**: 服务端状态与客户端不符时回滚

## 13. 使用示例

### 13.1 初始化
```csharp
// 在CharacterController中
_skillRegistry = new SkillRegistry();
_skillRegistry.RegisterSkillDataRange(skillDataList);

_skillBridge = new SkillCoordinatorBridge(this, _skillRegistry);
```

### 13.2 输入处理
```csharp
// 普攻
_skillBridge.HandleAttackInput(direction);

// 技能Q
_skillBridge.HandleSkillQInput(targetPosition);

// 技能R
_skillBridge.HandleSkillRInput(targetPosition);
```

### 13.3 更新
```csharp
void Update()
{
    _skillBridge.Update(Time.deltaTime);

    // 查询技能状态
    if (_skillBridge.IsCasting)
    {
        // 施法中，禁止移动等
    }
}
```

### 13.4 伤害处理
```csharp
// 受到伤害时
_skillBridge.HandleDamageTaken(damage, damageType);

// 应用Buff
var buffData = Resources.Load<BuffData>("Buffs/SpeedBoost");
_skillBridge.BuffHandler.ApplyBuff(buffData, this);
```

## 14. 扩展点

### 14.1 自定义打断规则
```csharp
_interruptionMatrix.SetCustomRule(skillId, InterruptionSource.DamageTaken, true);
```

### 14.2 自定义效果
```csharp
public class CustomEffectData : EffectData
{
    public override void Apply(IEffectTarget caster, IEffectTarget target)
    {
        // 自定义效果逻辑
    }
}
```

### 14.3 技能优先级
```csharp
// 数字越大优先级越高
skillData.InterruptionPriority = 100;
```

## 15. 性能优化建议

1. **对象池**: SkillExecutor建议使用对象池管理
2. **批量检测**: AOE检测使用SpatialHash优化
3. **状态缓存**: 避免每帧创建新对象
4. **事件合并**: 多个BuffTick可合并处理

## 16. 注意事项

1. **命名空间隔离**: Skills程序集与Sys3C程序集分离
2. **接口实现**: 所有适配器需实现完整接口方法
3. **空值检查**: IEffectTarget的各属性可能返回null
4. **线程安全**: 网络同步需考虑多线程访问
