# NPC AI 系统设计方案

**版本**: 2.0
**日期**: 2026-04-08
**状态**: Approved

---

## 1. 整体架构

### 1.1 目录结构

```
E:\CodeForJob\
├── Cool/                    # Unity 客户端
│   └── Assets/Scripts/
│       ├── AOT/            # 现有 KCP 网络
│       └── Hotfix/         # 客户端表现层
│           └── GameSystems/NpcMirror/  # NPC 镜像系统
│
└── Server/                  # 独立服务端 (.NET 控制台 + KCP)
    └── src/
        ├── AI/             # AI 核心
        │   ├── Core/       # AiComponent, AiManager, AiBlackboard
        │   ├── BehaviorTree/  # BtNode, BtSelector, BtSequence, BtCondition, BtAction
        │   ├── Combat/     # AggroTable, DamageCalculator
        │   ├── Detection/  # TargetDetector
        │   ├── Movement/   # SimpleMoveSystem
        │   └── Skill/      # SkillData, SkillSystem
        ├── Config/         # JSON 怪物配置
        ├── Network/        # KCP Server + 消息处理
        └── Messages/       # 协议定义
```

### 1.2 职责划分

| 层 | 职责 |
|----|------|
| **Server** | AI 决策、仇恨管理、技能施放、位置计算、动画状态计算、广播同步 |
| **Client** | 接收同步数据，更新 NPC Transform 和 Animator（仅表现） |

---

## 2. 服务端实现

### 2.1 AI 核心 (Core)

**AiBlackboard** - AI 运行时数据容器：
```csharp
public class AiBlackboard
{
    public AlertLevel AlertLevel { get; set; } = AlertLevel.PEACE;
    public long? TargetId { get; set; }
    public Vector3 SpawnPosition { get; set; }
    public Vector3 PatrolCenter { get; set; }
    public float PatrolRadius { get; set; } = 10f;
    public int CurrentPatrolIndex { get; set; }
}
```

**AiComponent** - AI 实例核心：
```csharp
public sealed class AiComponent
{
    public long InstanceId { get; }
    public int TemplateId { get; }
    public MonsterData Config { get; }

    public AiBlackboard Blackboard { get; }
    public BehaviorTree BehaviorTree { get; }
    public SkillSystem SkillSystem { get; }
    public AggroTable AggroTable { get; }
    public TargetDetector TargetDetector { get; }

    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public float MoveSpeed { get; set; }
    public float VisionRadius { get; set; }
    public float VisionAngle { get; set; }
    public float AttackRange { get; set; }

    public void Update(float deltaTime);
    public void SetTarget(long? targetId);
}
```

**AiManager** - 全局管理：
- 管理所有 AiComponent 实例
- spawn/despawn AI
- 定时更新循环（正常 1s，战斗 0.5s）
- 广播位置同步给客户端

### 2.2 行为树 (BehaviorTree)

**节点类型**：

| 节点 | 说明 | 结果 |
|------|------|------|
| BtNode | 基类 | - |
| BtSelector | 遇到成功停止 | 任意成功=Success |
| BtSequence | 遇到失败停止 | 全部成功=Success |
| BtCondition | 条件检查 | Success/Failure |
| BtAction | 行为执行 | Success/Failure/Running |

**预定义行为树模板**：
```csharp
// Root: Selector
// ├── Sequence: 巡逻（PEACE）
// │   ├── Condition: AlertLevel == PEACE
// │   ├── Condition: !HasTarget
// │   └── Action: Patrol
// ├── Sequence: 追击+攻击（HOSTILE）
// │   ├── Condition: AlertLevel == HOSTILE
// │   ├── Condition: HasTarget
// │   ├── Action: Chase
// │   └── Action: Attack
// └── Sequence: 返回
//     ├── Condition: !HasTarget
//     ├── Condition: AlertLevel != PEACE
//     └── Action: Return
```

**Action 节点**：
- `PatrolAction`: 沿巡逻点移动，到达后切换下一个
- `ChaseAction`: 向目标移动，直到距离 < 攻击范围
- `AttackAction`: 检测目标在攻击范围内则施放技能
- `ReturnAction`: 返回出生点

### 2.3 目标检测 (Detection)

**TargetDetector** - 复合检测：
```csharp
public bool CanDetectTarget(Vector3 aiPos, Vector3 aiForward, Vector3 targetPos)
{
    float distance = Vector3.Distance(aiPos, targetPos);
    if (distance > _detectionRadius) return false;

    Vector3 directionToTarget = (targetPos - aiPos).normalized;
    float angle = Vector3.Angle(aiForward, directionToTarget);
    if (angle > _visionAngle / 2) return false;

    return true;
}
```

- 和平状态：每 1 秒检测一次
- 战斗状态：每 0.5 秒检测一次

### 2.4 仇恨系统 (Combat)

**AggroTable**：
```csharp
public class AggroTable
{
    private Dictionary<long, float> _entries = new();

    public void AddAggro(long targetId, float amount);
    public void RemoveAggro(long targetId);
    public void DecayAll(float deltaTime);
    public long? GetHighestAggroTarget();
}
```

仇恨规则：
| 事件 | 仇恨变化 |
|------|---------|
| 造成伤害 | +伤害值 |
| 目标脱离感知范围 | 每秒 -20% 仇恨 |
| 战斗状态 | 每秒 -5% 仇恨 |

### 2.5 技能系统 (Skill)

**SkillData**（对应客户端 ScriptableObject）：
```csharp
public class SkillData
{
    public string SkillName;
    public float Damage;
    public float Range;       // 技能范围（米）
    public float Cooldown;    // 冷却时间（秒）
    public float CastTime;    // 施放时间（秒），MVP=0
}
```

**SkillSystem**：
- 维护技能列表和冷却状态
- 瞬时施放
- 伤害计算（返回伤害值，由调用方应用）

### 2.6 移动系统 (Movement)

**SimpleMoveSystem**：
```csharp
public void MoveTo(Vector3 target, float speed, float dt)
{
    Vector3 direction = (target - _position).normalized;
    _rotation = Quaternion.LookRotation(direction);
    _position += direction * speed * dt;
}
```

参数：
| 参数 | 默认值 |
|------|--------|
| MoveSpeed | 3 米/秒 |
| RotationSpeed | 180 度/秒 |

### 2.7 怪物配置 (Config)

**monster_config.json**：
```json
{
  "monsters": [
    {
      "templateId": 1,
      "name": "Slime",
      "hp": 100,
      "moveSpeed": 2.0,
      "detectionRadius": 8,
      "visionAngle": 120,
      "attackRange": 1.5,
      "patrolRadius": 5,
      "skills": ["Attack"]
    }
  ]
}
```

---

## 3. 网络同步

### 3.1 消息定义

| 消息 | 方向 | 内容 |
|------|------|------|
| NpcSpawn | Server→Client | InstanceId, TemplateId, Position, Rotation |
| NpcDespawn | Server→Client | InstanceId |
| NpcPosSync | Server→Client | InstanceId, Position, Rotation (10Hz) |
| NpcAnimSync | Server→Client | InstanceId, AnimationState (变化时) |

**AnimationState 枚举**：
```csharp
public enum NpcAnimationState
{
    Idle,
    Running,
    Attack,
    Death
}
```

### 3.2 同步频率

| 状态 | 位置同步 | AI 更新 |
|------|---------|---------|
| PEACE | 10Hz | 1Hz |
| HOSTILE | 10Hz | 0.5Hz |

---

## 4. 客户端实现 (NpcMirror)

### 4.1 目录结构

```
Assets/Scripts/Hotfix/GameSystems/NpcMirror/
├── NpcMirrorManager.cs      # 管理所有镜像 NPC
├── NpcMirrorComponent.cs   # 单个 NPC 镜像
└── NpcAnimationController.cs  # 动画状态驱动
```

### 4.2 NpcMirrorComponent

```csharp
public class NpcMirrorComponent
{
    public long InstanceId { get; }
    public void SetPosition(Vector3 pos);
    public void SetRotation(Quaternion rot);
    public void SetAnimationState(NpcAnimationState state);
}
```

职责：
- 接收服务端同步数据
- 更新 Transform（使用 Lerp 平滑过渡）
- 更新 Animator 参数

### 4.3 数据接收

通过现有 KCP 客户端接收消息：
- `OnNpcSpawn` → 创建 NpcMirrorComponent
- `OnNpcDespawn` → 销毁 NpcMirrorComponent
- `OnNpcPosSync` → 更新位置
- `OnNpcAnimSync` → 更新动画

---

## 5. AI 更新循环

```
1. 定时触发（正常 1s，战斗 0.5s）
   └─ 遍历所有 AiComponent

2. 检测目标
   └─ TargetDetector.CanDetectTarget()
   └─ 发现目标 → SetTarget() → Blackboard.TargetId → AlertLevel = HOSTILE

3. 执行行为树
   └─ Root.Tick()
       └─ Selector: 遍历子节点
           └─ Sequence: 顺序执行
               └─ Action: 返回 Success/Failure/Running

4. 应用移动
   └─ SimpleMoveSystem 根据 Blackboard 数据移动

5. 更新技能
   └─ SkillSystem.Update() 冷却递减

6. 更新仇恨
   └─ AggroTable.DecayAll()

7. 广播同步
   └─ 发送 NpcPosSync (位置变化时)
   └─ 发送 NpcAnimSync (动画状态变化时)
```

---

## 6. 常量参考

| 参数 | 默认值 |
|------|--------|
| 感知半径 | 30 米 |
| 视野角度 | 120 度 |
| 攻击范围 | 2 米 |
| 移动速度 | 3 米/秒 |
| 巡逻半径 | 10 米 |
| 仇恨衰减(战斗) | 5%/秒 |
| 仇恨衰减(脱离) | 20%/秒 |
| 位置同步频率 | 10Hz |

---

## 7. 待扩展功能

| 功能 | 说明 |
|------|------|
| 视线检测 | 需要场景几何数据 |
| 导航网格 | NavMeshAgent 替代直接移动 |
| Buff/Debuff | 状态效果系统 |
| 技能连招 | 连续施放多个技能 |
| AI 协作 | 喊话增援 |
| 位移技能 | 冲刺、闪烁等 |
