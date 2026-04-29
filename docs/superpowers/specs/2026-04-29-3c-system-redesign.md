# 3C 系统重构设计文档

**Date:** 2026-04-29
**Status:** Approved

---

## 一、架构总览

```
┌─────────────────────────────────────────────────────┐
│                    Animator                          │
├─────────────────────────────────────────────────────┤
│  Layer 0 (Base):   Idle | Move | Sprint | Jump      │
│  Layer 1 (Attack): Idle | Attack1 | Attack2 | Q | R │
│  Layer 2 (Hit):    [叠加任何Base/Attack动画]          │
└─────────────────────────────────────────────────────┘
                           ▲
                           │ State/Trigger 参数
                           │
┌─────────────────────────────────────────────────────┐
│               CharacterController                    │
│  - 物理移动、跳跃、重力、地面检测                      │
│  - 状态变化 → 事件通知                                │
└─────────────────────────────────────────────────────┘
                           ▲ 事件
                           │
┌─────────────────────────────────────────────────────┐
│                  FSM Manager                         │
│  - 分层状态机：BaseFSM + AttackFSM                   │
│  - 监听事件 + 轮询移动状态                            │
│  - 驱动 Animator 参数                                │
└─────────────────────────────────────────────────────┘
                           ▲ SkillConfig + 技能注册
                           │
┌─────────────────────────────────────────────────────┐
│                  Skill System                        │
│  - SkillConfig (SO): 动画名、CD、空中可用标记         │
│  - SkillRegistry: 运行时技能注册表                    │
└─────────────────────────────────────────────────────┘
```

---

## 二、FSM 分层设计

### Base Layer FSM（底层）

负责物理移动、跳跃、重力、地面检测驱动的状态。

| 状态 | 说明 | 可转换到 |
|------|------|----------|
| Idle | 站立 | Move, Sprint, JumpStart, Death |
| Move | 行走 | Idle, Sprint, JumpStart, Death |
| Sprint | 冲刺 | Idle, Move, JumpStart, Death |
| JumpStart | 起跳 | JumpAir, Death |
| JumpAir | 空中 | JumpEnd, Death |
| JumpEnd | 落地 | Idle, Move, Sprint, Death |
| Death | 死亡 | （终止状态） |

### Attack Layer FSM（顶层）

负责普攻连击、技能释放。

| 状态 | 说明 | 可转换到 |
|------|------|----------|
| AttackIdle | 无攻击 | Attack1, AttackQ, AttackR |
| Attack1 | 第一击 | Attack2, AttackIdle |
| Attack2 | 第二击 | Attack1, AttackIdle |
| AttackQ | 突刺 | AttackIdle |
| AttackR | 技能R | AttackIdle |

---

## 三、事件通知机制

### 混合方案

- **事件委托**：跳跃请求、落地检测、死亡通知（即时响应）
- **轮询检测**：移动/站立状态切换（简化逻辑）

### 事件定义

```csharp
public event Action OnJumpRequested;
public event Action OnLanded;
public event Action OnDeath;
```

### StateMachineBehaviour 职责

| Behaviour | 职责 |
|-----------|------|
| `BaseStateBehaviour` | 监听 JumpStart/JumpAir 动画完成 |
| `AttackStateBehaviour` | 监听 Attack1/Attack2 动画完成，处理连击窗口 |
| `HitStateBehaviour` | 监听 Hit 动画完成，触发返回 |

### 连击窗口机制

- 进入 Attack1 后 **5帧** 解锁连击输入
- 在 normalizedTime **0.3~0.8** 区间内输入可触发 Attack2

### JumpEnd 落地返回

- FSM Manager 轮询检测 JumpPhase 变化
- JumpEnd 动画完成由 `BaseStateBehaviour` 通知

---

## 四、Skill 系统设计

### SkillConfig（ScriptableObject）

```csharp
[CreateAssetMenu(fileName = "SkillConfig", menuName = "Game/Skill")]
public class SkillConfig : ScriptableObject
{
    public string SkillName;
    public string AnimationName;      // 动画名（如 "AttackQ"）
    public float Cooldown;            // CD时间（秒）
    public bool CanUseInAir;          // 是否可空中使用
    public float ComboWindowStart;     // 连击窗口开始（normalizedTime）
    public float ComboWindowEnd;       // 连击窗口结束（normalizedTime）
    public int ComboFrameLock;         // 固定帧解锁
}
```

### 技能列表

| 技能 | 动画 | CD | 可空中 | 连击窗口 |
|------|------|-----|--------|----------|
| Attack1 | Attack01 | 0 | 是 | 5帧后 + 0.3~0.8 |
| Attack2 | Attack02 | 0 | 是 | 无 |
| SkillQ | Attack03 | 5s | 是 | 无 |
| SkillR | Attack04 | 10s | 否 | 无 |

### SkillRegistry

```csharp
public class SkillRegistry
{
    private Dictionary<string, SkillConfig> _skills;
    private Dictionary<string, float> _cooldowns;

    public void Register(SkillConfig config);
    public bool CanUse(string skillName);  // 检查CD + 状态
    public void Use(string skillName);
    public void Update(float deltaTime);   // 更新CD
}
```

---

## 五、Hit 叠加层设计

### Hit Layer 规则

- 独立权重（Additive），不影响 Base/Attack 动画
- 任何状态可触发（Idle/Move/空中/攻击中）
- Hit 播放完自动返回原状态

### 触发流程

1. 受到伤害 → HitStateBehaviour 激活 Layer 2
2. 播放 Hit 动画（优先级最高）
3. Hit 动画结束 → 返回原状态继续

---

## 六、网络同步

### 本地预测 + 服务端权威

- 客户端先行预测，发送输入到服务器
- 服务端计算权威位置，广播给客户端
- 客户端平滑插值到权威位置

### 预测内容

| 操作 | 客户端 | 服务端 |
|------|--------|--------|
| 移动/冲刺 | 预测 | 校正 |
| 跳跃 | 预测 | 校正 |
| 攻击/技能 | 触发 | 验证 |

---

## 七、文件结构

```
Assets/Scripts/Hotfix/GameSystems/Sys3C/
├── Character/
│   ├── CharacterController.cs    // 物理控制
│   ├── CharacterData.cs          // 数据结构
│   └── GroundDetector.cs          // 地面检测
├── FSM/
│   ├── FSMManager.cs             // FSM统一管理
│   ├── BaseFSM.cs                 // 底层状态机
│   ├── AttackFSM.cs              // 攻击状态机
│   └── States/
│       ├── IState.cs             // 状态接口
│       ├── IdleState.cs
│       ├── MoveState.cs
│       ├── SprintState.cs
│       ├── JumpStartState.cs
│       ├── JumpAirState.cs
│       ├── JumpEndState.cs
│       └── DeathState.cs
├── Animation/
│   ├── StateBehaviours/
│   │   ├── BaseStateBehaviour.cs     // Base层动画监听
│   │   ├── AttackStateBehaviour.cs    // Attack层动画监听
│   │   └── HitStateBehaviour.cs       // Hit层动画监听
│   └── AnimationDriver.cs         // Animator参数驱动
├── Skill/
│   ├── SkillConfig.cs             // 技能配置SO
│   ├── SkillRegistry.cs          // 技能注册表
│   └── SkillDefs.cs              // 技能枚举/常量
├── Network/
│   ├── NetworkPrediction.cs      // 客户端预测
│   ├── NetworkBridge.cs          // 网络桥接
│   └── ServerAuthority.cs        // 服务端权威
└── Sys3CEntry.cs                 // 入口
```

---

## 八、实现顺序

1. **Character 层**：CharacterController、CharacterData、GroundDetector
2. **FSM 基础**：FSMManager、IState、基础状态实现
3. **Animation 层**：StateBehaviours、AnimationDriver
4. **Skill 系统**：SkillConfig SO、SkillRegistry
5. **Hit 系统**：HitStateBehaviour、叠加层逻辑
6. **网络层**：NetworkPrediction、ServerAuthority（可选）
