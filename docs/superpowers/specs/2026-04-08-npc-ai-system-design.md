# NPC 怪物 AI 系统设计

**版本**: 1.0
**日期**: 2026-04-08
**状态**: MVP

---

## 1. 概述

### 1.1 目标

为 MMO 游戏实现服务端 NPC 怪物 AI 系统，支持巡逻、追击、攻击行为，具备可扩展技能系统和仇恨管理。

### 1.2 核心原则

- **服务端权威**: AI 逻辑运行在服务器，客户端只负责表现
- **行为树驱动**: AI 决策由行为树主导，状态作为 Blackboard 变量
- **简化实现**: MVP 聚焦核心功能，避免过度设计
- **模块化扩展**: 各系统独立，可按需扩展

---

## 2. 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                        Server                                │
│                                                              │
│  ┌──────────┐    ┌──────────────┐    ┌─────────────────┐   │
│  │AiManager │───→│  AiComponent │───→│ PositionSync   │   │
│  │(全局管理) │    │              │    │ Notification   │   │
│  └──────────┘    │  ┌─────────┐│    └─────────────────┘   │
│                  │  │Blackboard││                          │
│                  │  │(运行时数据││                          │
│                  │  └────┬────┘│                          │
│                  │       │      │                          │
│                  │       ▼      │                          │
│                  │ ┌─────────┐ │    ┌─────────────────┐   │
│                  │ │Behavior │ │    │   SkillSystem   │   │
│                  │ │ Tree    │ │───→│   (MVP版)      │   │
│                  │ │         │ │    └─────────────────┘   │
│                  │ └─────────┘ │                           │
│                  └──────────────┘                          │
│                           │                               │
│                           ▼                               │
│                  ┌─────────────────┐                        │
│                  │   MoveSystem   │                        │
│                  │ (直接控制位置)  │                        │
│                  └─────────────────┘                        │
└─────────────────────────────────────────────────────────────┘
```

### 2.1 模块职责

| 模块 | 职责 |
|------|------|
| **AiManager** | 全局 AI 实例管理，spawn/despawn，分配到 Room |
| **AiComponent** | AI 实例核心，持有 Blackboard、行为树实例、技能系统 |
| **Blackboard** | AI 运行时数据容器（目标、位置、警觉等级等） |
| **BehaviorTree** | 行为树执行引擎，驱动 AI 决策 |
| **SkillSystem** | 技能配置和执行（瞬时施放+冷却+伤害） |
| **MoveSystem** | AI 移动控制，直接操作 Transform |

---

## 3. 行为树系统

### 3.1 节点类型（MVP）

| 节点类型 | 说明 | 执行结果 |
|---------|------|---------|
| **Sequence** | 顺序执行子节点，遇到失败则停止 | 全部成功=Success |
| **Selector** | 选择执行，遇到成功则停止 | 任意成功=Success |
| **Condition** | 条件检查，返回 Success/Failure | - |
| **Action** | 行为执行，支持多帧 Running | Success/Failure/Running |

### 3.2 行为树结构

```
        ┌─────────────┐
        │   Selector  │ ← 根节点
        └──────┬──────┘
               │
    ┌──────────┼──────────┐
    ▼          ▼          ▼
┌────────┐ ┌─────────┐ ┌────────┐
│Sequence│ │Condition│ │Sequence│
│(巡逻)  │ │(有目标?)│ │(返回)  │
└───┬────┘ └─────────┘ └───┬────┘
    │                     │        │
    ▼                     ▼        ▼
┌────────┐          ┌────────┐ ┌────────┐
│ Patrol │          │ Chase  │ │ Return │
│ Action │          │ Action │ │ Action │
└────────┘          └────────┘ └────────┘
```

### 3.3 行为树模板

```csharp
// 怪物 AI 行为树模板
Root: Selector
├── Sequence: 巡逻（警觉等级=PEACE）
│   ├── Condition: AlertLevel == PEACE
│   ├── Condition: !HasTarget
│   └── Action: Patrol
├── Sequence: 追击+攻击（警觉等级=HOSTILE）
│   ├── Condition: AlertLevel == HOSTILE
│   ├── Condition: HasTarget
│   ├── Action: Chase
│   └── Action: Attack (带冷却)
└── Sequence: 返回（目标丢失）
    ├── Condition: !HasTarget
    ├── Condition: AlertLevel != PEACE
    └── Action: Return
```

### 3.4 节点执行模型

| 模式 | 说明 |
|------|------|
| **Immediate** | 一帧内完成，返回 Success 或 Failure |
| **Running** | 需要多帧，每帧返回 Running，完成后返回 Success |

Action 节点典型执行：
1. **Chase**: Running，直到距离 < 攻击范围
2. **Attack**: Running，直到施放完成
3. **Patrol**: Running，直到到达下一个巡逻点
4. **Return**: Running，直到回到出生点

---

## 4. 警觉状态系统

### 4.1 三级警觉

| 等级 | 名称 | 触发条件 | AI 行为 |
|------|------|---------|--------|
| 0 | **PEACE** | 默认状态 | 区域巡逻 |
| 1 | **HOSTILE** | 检测到有效目标 | 追击 + 攻击 |

> **简化说明**: MVP 简化为两级（PEACE/HOSTILE），检测到目标直接进入战斗，不设置中间"警觉"状态。

### 4.2 警觉等级变量

警觉等级存储在 Blackboard 中：

```csharp
public class AiBlackboard
{
    public AlertLevel AlertLevel { get; set; } = AlertLevel.PEACE;
    public long? TargetId { get; set; }
    public Vector3 SpawnPosition { get; set; }
    public float PatrolRadius { get; set; } = 10f;
}
```

---

## 5. 检测系统

### 5.1 复合检测（MVP）

MVP 采用两种检测，暂不包含视线 Raycast（需要场景几何数据）：

| 检测类型 | 说明 | 参数 |
|---------|------|------|
| **距离检测** | 圆形范围内检测 | 感知半径 |
| **视野锥检测** | 前方锥形视野 | 角度 + 距离 |

### 5.2 检测实现

```csharp
public bool CanDetectTarget(Vector3 aiPosition, Vector3 aiForward, Vector3 targetPosition)
{
    // 1. 距离检测
    float distance = Vector3.Distance(aiPosition, targetPosition);
    if (distance > _detectionRadius) return false;

    // 2. 视野锥检测
    Vector3 directionToTarget = (targetPosition - aiPosition).normalized;
    float angle = Vector3.Angle(aiForward, directionToTarget);
    if (angle > _visionAngle / 2) return false;

    return true;
}
```

### 5.3 检测频率

- **和平状态**: 每 1 秒检测一次
- **战斗状态**: 每 0.5 秒检测一次

---

## 6. 技能系统

### 6.1 MVP 技能系统

MVP 技能系统支持：
- 瞬时施放
- 伤害计算
- 冷却管理
- 范围检测

### 6.2 技能配置（ScriptableObject）

```csharp
[CreateAssetMenu(fileName = "Skill_Slash", menuName = "Game/AI/Skill")]
public class SkillSO : ScriptableObject
{
    public string SkillName;
    public float Damage;
    public float Range;           // 技能范围（米）
    public float Cooldown;         // 冷却时间（秒）
    public float CastTime;        // 施放时间（秒），MVP=0
    public int Priority;          // 技能优先级
}
```

### 6.3 技能执行流程

```
1. 检测目标在技能范围内
2. 检查冷却是否完成
3. 施放技能（瞬时）
   - 计算伤害
   - 应用效果
4. 进入冷却
```

### 6.4 伤害计算

```csharp
public float CalculateDamage(SkillSO skill, float attackerLevel)
{
    // MVP: 固定伤害 = skill.Damage
    // 后续扩展: + 攻击力加成 + 暴击
    return skill.Damage;
}
```

---

## 7. 仇恨系统

### 7.1 仇恨表

```csharp
public class AggroTable
{
    private Dictionary<long, float> _aggroEntries = new();

    public void AddAggro(long targetId, float amount);
    public void RemoveAggro(long targetId);
    public void DecayAll(float deltaTime);
    public long? GetHighestAggroTarget();
}
```

### 7.2 仇恨规则（MVP）

| 事件 | 仇恨变化 |
|------|---------|
| 造成伤害 | +伤害值 |
| 目标脱离感知范围 | 每秒 -20% 仇恨 |
| 目标死亡 | 清除该目标仇恨 |

### 7.3 仇恨衰减

- **战斗状态**: 每 1 秒衰减 5% 仇恨
- **目标脱离**: 每 1 秒衰减 20% 仇恨
- 仇恨值不能低于 0

---

## 8. 移动系统

### 8.1 简化移动

MVP 中 AI 直接控制位置和朝向，不使用导航网格：

```csharp
public void MoveTo(Vector3 targetPosition, float speed)
{
    // 1. 朝向目标
    Vector3 direction = (targetPosition - _position).normalized;
    _rotation = Quaternion.LookRotation(direction);

    // 2. 移动到目标
    _position += direction * speed * Time.deltaTime;
}
```

### 8.2 移动参数

| 参数 | 说明 | 默认值 |
|------|------|--------|
| MoveSpeed | 移动速度（米/秒） | 3.0 |
| RotationSpeed | 转向速度（度/秒） | 180 |

---

## 9. AI 组件

### 9.1 AiComponent 结构

```csharp
public sealed class AiComponent
{
    public long InstanceId { get; }          // AI 实例 ID
    public long TemplateId { get; }          // AI 模板 ID

    public AiBlackboard Blackboard { get; } // 运行时数据
    public BehaviorTree BehaviorTree { get; }// 行为树实例
    public SkillSystem SkillSystem { get; } // 技能系统
    public AggroTable AggroTable { get; }   // 仇恨表

    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public float MoveSpeed { get; set; }
}
```

### 9.2 AI 生命周期

```
Spawn → Init → Update(循环) → OnDeath → Despawn
```

---

## 10. AiManager

### 10.1 职责

- 管理所有 AI 实例
- AI spawn/despawn
- AI 更新循环（事件驱动+定时轮询）
- 分配 AI 到 Room

### 10.2 更新频率

- **正常状态**: 每 1 秒更新一次
- **战斗状态**: 每 0.5 秒更新一次
- **事件触发**: 检测到目标时立即触发状态检查

### 10.3 位置同步

AI 位置通过现有的 `PositionSyncNotification` 消息同步给客户端。

---

## 11. 数据流

### 11.1 AI 更新循环

```
1. 检测目标（距离+视野）
   └─ 发现目标 → 更新 Blackboard.TargetId → AlertLevel = HOSTILE

2. 执行行为树
   └─ 根节点 Tick()
       └─ Selector: 遍历子节点
           └─ Sequence: 顺序执行
               └─ Action: 执行具体行为

3. 应用移动
   └─ MoveSystem 根据 Blackboard 数据移动 AI

4. 更新技能
   └─ SkillSystem 更新冷却

5. 更新仇恨
   └─ AggroTable 衰减仇恨

6. 同步状态
   └─ 发送 PositionSyncNotification 给客户端
```

### 11.2 消息通知

状态变化时发送简单通知：

```csharp
// 状态变化通知（客户端自行表现）
public void NotifyStateChange(long aiInstanceId, string newState)
{
    // 发送 AiStateChanged 消息
}
```

---

## 12. 目录结构

```
Server/
├── AI/
│   ├── Core/
│   │   ├── AiComponent.cs         # AI 实例核心
│   │   ├── AiBlackboard.cs        # 运行时数据
│   │   ├── AiManager.cs           # 全局管理器
│   │   └── AlertLevel.cs          # 警觉等级枚举
│   ├── BehaviorTree/
│   │   ├── BtNode.cs              # 节点基类
│   │   ├── BtSequence.cs          # 顺序节点
│   │   ├── BtSelector.cs          # 选择节点
│   │   ├── BtCondition.cs         # 条件节点
│   │   ├── BtAction.cs            # 行为节点
│   │   ├── BtContext.cs           # 执行上下文
│   │   └── BtTemplate.cs          # 行为树模板
│   ├── Skill/
│   │   ├── SkillSO.cs             # 技能配置
│   │   ├── SkillExecutor.cs       # 技能执行器
│   │   └── SkillEffect.cs         # 技能效果
│   ├── Combat/
│   │   ├── AggroTable.cs          # 仇恨表
│   │   └── DamageCalculator.cs    # 伤害计算
│   ├── Detection/
│   │   └── TargetDetector.cs      # 目标检测
│   └── Movement/
│       └── SimpleMoveSystem.cs    # 简化移动系统
└── Messages/
    └── AiMessages.cs              # AI 相关消息
```

---

## 13. 待扩展功能

以下功能不在 MVP 范围内，后续版本扩展：

| 功能 | 说明 |
|------|------|
| 视线检测 | 需要场景几何数据 |
| 导航网格 | 使用 NavMeshAgent 替代直接移动 |
| Buff/Debuff | 状态效果系统 |
| 技能连招 | 连续施放多个技能 |
| 仇恨衰减增益 | 更复杂的仇恨计算 |
| AI 协作 | 喊话增援、分工战斗 |
| 技能特效 | 视觉表现同步 |
| 位移技能 | 冲刺、闪烁等 |

---

## 14. 附录

### 14.1 常量参考

| 参数 | 默认值 |
|------|--------|
| 感知半径 | 30 米 |
| 视野角度 | 120 度 |
| 攻击范围 | 2 米 |
| 移动速度 | 3 米/秒 |
| 巡逻半径 | 10 米 |
| 仇恨衰减(战斗) | 5%/秒 |
| 仇恨衰减(脱离) | 20%/秒 |

### 14.2 行为树节点执行状态

```csharp
public enum BtStatus
{
    Success,   // 执行成功
    Failure,   // 执行失败
    Running    // 执行中（需要继续）
}
```
