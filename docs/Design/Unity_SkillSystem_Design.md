# Unity MMO 技能系统详细设计

## 概述

本文档是 `Unity_FSM_Design.md` 的技能系统补充，定义了 MMO 游戏中技能系统的完整设计方案。

---

## 目录结构

```
Assets/Scripts/[Layer]/Skills/
├── Definition/
│   ├── SkillState.cs          # 技能子状态枚举
│   ├── SkillType.cs           # 技能类型/品质枚举
│   └── SkillID.cs             # 技能ID定义
├── Data/
│   ├── SkillData.cs           # 技能数据基类 (ScriptableObject)
│   ├── BasicAttackData.cs     # 普攻技能数据
│   ├── SpecialSkillData.cs     # 特殊技能数据
│   └── DamageData.cs          # 伤害数据
├── Effect/
│   ├── SkillEffectHandler.cs  # 效果处理器
│   ├── BuffSystem/
│   │   ├── BuffData.cs       # Buff数据
│   │   ├── BuffHandler.cs    # Buff管理器
│   │   └── ActiveBuff.cs     # 活跃Buff实例
│   └── EffectData.cs         # 效果数据基类
├── Runtime/
│   ├── SkillExecutor.cs      # 技能执行器
│   ├── SkillStateMachine.cs   # 技能子状态机
│   ├── SkillCoordinator.cs    # 技能协调器
│   ├── SkillInputBuffer.cs    # 输入缓冲
│   ├── SkillInterruptionMatrix.cs  # 打断矩阵
│   └── CooldownManager.cs    # 冷却管理
└── Editor/
    └── SkillDataEditor.cs     # 技能数据编辑器
```

---

## 1. 核心枚举定义

### SkillState.cs

```csharp
namespace Game.Skills.Definition
{
    /// <summary>
    /// 技能执行子状态 - 用于攻击层内部状态机
    /// </summary>
    public enum SkillSubState
    {
        None = 0,
        
        // 前置阶段
        Cooldown,       // 冷却中（可触发，但不能释放）
        Ready,          // 就绪（可释放）
        InputBuffer,    // 输入缓冲等待
        
        // 施法阶段
        Casting,        // 读条中（不可移动）
        Channeling,     // 引导中（可移动）
        Charging,       // 蓄力中（按压蓄力，松发）
        
        // 执行阶段
        Execution,      // 释放/执行中（判定帧）
        HitConfirm,     // 命中确认
        
        // 收尾阶段
        Recovery,       // 收招硬直
        Cancelled,      // 被打断
        Completed       // 正常完成
    }
    
    /// <summary>
    /// 技能释放类型
    /// </summary>
    public enum ReleaseType
    {
        Instant,        // 瞬发
        Channeled,      // 引导型
        Charged,        // 蓄力型
        Timed           // 读条型
    }
    
    /// <summary>
    /// 打断来源
    /// </summary>
    public enum InterruptionSource
    {
        None = 0,
        MovementInput,      // 移动输入
        BasicAttack,        // 普攻输入
        AnotherSkill,       // 其他技能
        DamageTaken,        // 受到伤害
        Stun,               // 硬控（眩晕等）
        RollDodge,          // 翻滚
        Parry,              // 招架
        TimeOut             // 超时
    }
}
```

### SkillType.cs

```csharp
namespace Game.Skills.Definition
{
    public enum SkillType
    {
        BasicAttack,    // 普通攻击
        Special,        // 特殊技能（Q/R）
        Ultimate,       // 大招
        Passive,        // 被动
        Item           // 物品技能
    }
    
    public enum SkillQuality
    {
        Common = 1,     // 白色
        Uncommon = 2,   // 绿色
        Rare = 3,       // 蓝色
        Epic = 4,       // 紫色
        Legendary = 5   // 橙色
    }
}
```

---

## 2. 数据模型

### SkillData 基类

| 字段 | 类型 | 说明 |
|------|------|------|
| SkillId | int | 技能唯一ID |
| SkillName | string | 技能名称 |
| SkillType | SkillType | 技能类型 |
| Quality | SkillQuality | 品质 |
| ManaCost | int | 魔法消耗 |
| Cooldown | float | 冷却时间 |
| ReleaseType | ReleaseType | 释放类型 |
| CastTime | float | 读条时间 |
| ChannelDuration | float | 引导持续时间 |
| MinChargeTime | float | 最小蓄力时间 |
| MaxChargeTime | float | 最大蓄力时间 |
| CanMoveWhileCasting | bool | 读条时能否移动 |
| Range | float | 技能范围 |
| HitboxTimings | float[] | 判定帧时间点列表 |
| DamageData | DamageData | 伤害数据 |
| ApplyEffects | List<EffectData> | 施加的效果列表 |

### DamageData

| 字段 | 类型 | 说明 |
|------|------|------|
| BaseDamage | float | 基础伤害 |
| AttackRatio | float | 攻击力缩放系数 |
| ScalingAttribute | AttributeType | 缩放属性 |
| DamageType | DamageType | 伤害类型 |
| CriticalRateBonus | float | 暴击率加成 |
| CriticalDamageBonus | float | 暴击伤害加成 |
| IsTrueDamage | bool | 是否为真实伤害 |
| IsDOT | bool | 是否为持续伤害 |
| TickInterval | float | 持续伤害间隔 |

---

## 3. 状态机设计

### 技能状态转换图

```
                    ┌─────────┐
                    │ Cooldown│←────────────────────────────┐
                    └────┬────┘                              │
                         │ TryStart()                        │
                         ▼                                   │
┌─────────┐   ┌─────────┴─────────┐   ┌─────────┐            │
│Cancelled│◄──│     Ready         │   │ InputBuf│            │
└─────────┘   └─────────┬─────────┘   └─────────┘            │
                         │                                   │
                         ▼                                   │
              ReleaseType? ───────────────────────────────►  │
              │                                              │
    ┌─────────┼─────────┬─────────┐                           │
    ▼         ▼         ▼         ▼                           │
 Instant   Timed    Charged   Channeled                        │
    │         │         │         │                           │
    ▼         ▼         ▼         ▼                           │
 Execution ┌───────────┼───────────────────┐                 │
    │       │           │                   │                 │
    │    CastTime    CastTime           CastTime             │
    │       │           │                   │                 │
    │       └─────┬─────┴────┐              │                 │
    │             ▼          ▼              ▼                 │
    │         ┌─────────┐  ┌──────────┐  ┌────────────────┐  │
    │         │Charging │  │Execution │  │   Channeling   │  │
    │         └───┬─────┘  └────┬─────┘  └───────┬────────┘  │
    │             │             │                │             │
    │     Release? │      Hitbox Frames    TickInterval       │
    │             │             │                │             │
    │             └──────┬──────┘                │             │
    │                    ▼                       │             │
    │              ┌──────────┐                  │             │
    │              │ Recovery │                  │             │
    │              └────┬─────┘                  │             │
    │                   │                       │             │
    │                   └───────────────────────┘             │
    │                           │                              │
    ▼                           ▼                              │
┌─────────┐               ┌──────────┐                        │
│Complete │               │ Complete │───────────────────────┘
└─────────┘               └──────────┘
```

### 状态详细说明

| 状态 | 说明 | 可中断 | 可取消 |
|------|------|--------|--------|
| Cooldown | 冷却中 | - | - |
| Ready | 就绪等待释放 | 任意来源 | 任意技能 |
| Casting | 读条中 | 伤害/眩晕 | 瞬发技能 |
| Charging | 蓄力中 | 伤害/眩晕 | 瞬发技能 |
| Channeling | 引导中 | 伤害/眩晕 | 瞬发技能 |
| Execution | 执行中 | 否 | 否 |
| Recovery | 收招硬直 | 任意 | 任意技能 |
| Cancelled | 被打断 | - | - |
| Completed | 正常完成 | - | - |

---

## 4. 打断矩阵

### 默认打断规则

| 技能类型 | 移动输入 | 普攻 | 技能 | 伤害 | 翻滚 |
|---------|---------|------|------|------|------|
| BasicAttack | ✓ | - | - | ✗ | ✓ |
| Special | ✗ | - | - | ✓ | ✗ |
| Ultimate | ✗ | - | - | ✗ | ✗ |
| Passive | - | - | - | - | - |

- ✓ = 可打断
- ✗ = 不可打断
- - = 不适用

---

## 5. 协调器流程

```
┌─────────────────────────────────────────────────────────────┐
│                      SkillCoordinator                        │
├─────────────────────────────────────────────────────────────┤
│  1. HandleInput(skillId)                                    │
│     ├─ 检查冷却 ──► 缓冲输入                                │
│     ├─ 检查资源 ──► 扣血/魔                                 │
│     └─ 检查当前状态                                         │
│                                                             │
│  2. 当前无技能执行?                                         │
│     └─► TryActivateSkill()                                 │
│                                                             │
│  3. 当前有技能执行?                                         │
│     ├─ 检查可取消窗口                                       │
│     ├─ CanChainSkill()? ──► 尝试取消当前                   │
│     └─ 否 ──► 缓冲输入                                      │
│                                                             │
│  4. Update() 每帧                                          │
│     ├─ 更新当前技能状态机                                   │
│     ├─ 处理连段窗口超时                                     │
│     ├─ 处理缓冲输入                                        │
│     └─ 更新冷却                                            │
└─────────────────────────────────────────────────────────────┘
```

---

## 6. 与FSM协调器的接口

```csharp
public interface ISkillOwner
{
    // 属性
    CharacterStats Stats { get; }
    
    // 事件
    event Action<SkillData> OnSkillActivated;
    event Action<int, SkillSubState> OnSkillStateChanged;
    
    // 方法
    void ConsumeResources(SkillData skill);
    bool HasEnoughResources(SkillData skill);
    void ApplyDamage(Character target, DamageData damage);
    void ApplyEffect(Character target, EffectData effect);
}
```

### 协调器调用时序

```
1. 玩家输入技能
       ↓
2. SkillCoordinator.HandleInput()
       ↓
3. 检查冷却/资源/优先级
       ↓
4. 创建 SkillExecutor
       ↓
5. 通知 ISkillOwner.OnSkillActivated()
       ↓
6. AttackLayer FSM 切换到技能对应状态
       ↓
7. StateCoordinator 协调动画层（上身覆盖）
       ↓
8. SkillExecutor.Update() 逐帧更新
       ↓
9. 判定帧 → DamageCalculator → ApplyDamage
       ↓
10. 技能完成/取消 → OnSkillStateChanged()
       ↓
11. AttackLayer FSM 返回 AttackIdle
```

---

## 7. 实现优先级

### P0 (核心)
- SkillState.cs, SkillType.cs - 枚举定义
- SkillData.cs - 技能数据基类
- DamageData.cs - 伤害数据
- SkillStateMachine.cs - 技能子状态机
- CooldownManager.cs - 冷却管理

### P1 (扩展)
- BasicAttackData.cs - 普攻数据
- SpecialSkillData.cs - 特殊技能数据
- SkillExecutor.cs - 技能执行器
- SkillCoordinator.cs - 技能协调器
- SkillInputBuffer.cs - 输入缓冲
- SkillInterruptionMatrix.cs - 打断矩阵

### P2 (效果系统)
- EffectData.cs - 效果基类
- BuffData.cs, BuffHandler.cs - Buff系统
- HealEffectData.cs, ShieldEffectData.cs - 治疗/护盾效果
- SkillEffectHandler.cs - 效果处理器

### P3 (编辑器工具)
- SkillDataEditor.cs - 自定义编辑器
- SkillDataEditorWindow.cs - 技能配置窗口

---

## 8. 后续扩展

- [ ] 技能树系统
- [ ] 符文/铭文系统
- [ ] 技能天赋联动
- [ ] 技能特效编辑器
- [ ] 技能配置导入导出