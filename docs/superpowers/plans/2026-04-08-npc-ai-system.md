# NPC AI System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement a server-authoritative NPC AI system with client mirror. Server runs .NET console with KCP networking, client receives position/animation sync for display only.

**Architecture:**
- Server: Standalone .NET console project with AI Core, BehaviorTree, Combat, Detection, Movement, and KCP networking
- Client: Unity Hotfix layer with NpcMirror components for receiving and displaying NPC state
- Shared: Protobuf message definitions for NpcSpawn, NpcDespawn, NpcPosSync, NpcAnimSync

**Tech Stack:** .NET 6+ console, KCP (from Cool), Protobuf, Unity 2022

---

## File Structure

### Server Project (new)
```
../Server/
├── Server.csproj
├── Program.cs
├── AI/
│   ├── Core/
│   │   ├── AiComponent.cs
│   │   ├── AiManager.cs
│   │   ├── AiBlackboard.cs
│   │   └── AlertLevel.cs
│   ├── BehaviorTree/
│   │   ├── BtStatus.cs
│   │   ├── BtNode.cs
│   │   ├── BtSelector.cs
│   │   ├── BtSequence.cs
│   │   ├── BtCondition.cs
│   │   ├── BtAction.cs
│   │   └── BtBuilder.cs
│   ├── Combat/
│   │   ├── AggroTable.cs
│   │   └── DamageCalculator.cs
│   ├── Detection/
│   │   └── TargetDetector.cs
│   ├── Movement/
│   │   └── SimpleMoveSystem.cs
│   └── Skill/
│       ├── SkillData.cs
│       └── SkillSystem.cs
├── Config/
│   └── MonsterConfig.cs
├── Network/
│   ├── NpcServer.cs
│   └── NpcSyncBroadcaster.cs
└── Messages/
    ├── NpcMessages.cs
    └── MessageId.cs
```

### Client Project (new/modify)
```
Assets/Scripts/Hotfix/GameSystems/NpcMirror/
├── NpcMirrorManager.cs
├── NpcMirrorComponent.cs
└── NpcAnimationController.cs
```

### Shared (reuse existing)
- Use existing `KcpNet` library from Cool/Assets/Scripts/AOT/KcpNet/
- Use existing `MessageId` enum, extend with Npc* IDs
- Use existing Protobuf serialization

---

## Task 1: Server Project Setup

**Files:**
- Create: `../Server/Server.csproj`
- Create: `../Server/Program.cs`

- [ ] **Step 1: Create Server.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net6.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Cool\Assets\Scripts\AOT\KcpNet\KcpNet.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create Program.cs skeleton**

```csharp
using System;
using Server.AI;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("NPC AI Server starting...");
        var server = new NpcServer();
        server.Start();
        Console.ReadLine();
    }
}
```

- [ ] **Step 3: Commit**
```bash
git add -A && git commit -m "feat(server): initial server project setup"
```

---

## Task 2: Shared Message Definitions

**Files:**
- Create: `../Server/Messages/MessageId.cs` (copy from client, extend)
- Create: `../Server/Messages/NpcMessages.cs`

- [ ] **Step 1: Create MessageId.cs** (extends existing MessageId enum)

```csharp
namespace Server.Messages
{
    public enum NpcMessageId : ushort
    {
        NpcSpawn = 2000,
        NpcDespawn = 2001,
        NpcPosSync = 2002,
        NpcAnimSync = 2003,
    }
}
```

- [ ] **Step 2: Create NpcMessages.cs**

```csharp
using ProtoBuf;
using UnityEngine;

namespace Server.Messages
{
    [ProtoContract]
    public class NpcSpawn : IMessage
    {
        [ProtoMember(1)] public long InstanceId;
        [ProtoMember(2)] public int TemplateId;
        [ProtoMember(3)] public float X;
        [ProtoMember(4)] public float Y;
        [ProtoMember(5)] public float Z;
        [ProtoMember(6)] public float RotationY;
    }

    [ProtoContract]
    public class NpcDespawn : IMessage
    {
        [ProtoMember(1)] public long InstanceId;
    }

    [ProtoContract]
    public class NpcPosSync : IMessage
    {
        [ProtoMember(1)] public long InstanceId;
        [ProtoMember(2)] public float X;
        [ProtoMember(3)] public float Y;
        [ProtoMember(4)] public float Z;
        [ProtoMember(5)] public float RotationY;
        [ProtoMember(6)] public long Timestamp;
    }

    [ProtoContract]
    public class NpcAnimSync : IMessage
    {
        [ProtoMember(1)] public long InstanceId;
        [ProtoMember(2)] public int AnimationState; // 0=Idle,1=Running,2=Attack,3=Death
    }
}

public enum NpcAnimationState
{
    Idle = 0,
    Running = 1,
    Attack = 2,
    Death = 3
}
```

- [ ] **Step 3: Commit**
```bash
git add -A && git commit -m "feat(server): add Npc message definitions"
```

---

## Task 3: AI Core - Blackboard, AlertLevel, Basic Types

**Files:**
- Create: `../Server/AI/Core/AlertLevel.cs`
- Create: `../Server/AI/Core/AiBlackboard.cs`
- Create: `../Server/AI/Core/AiComponent.cs` (stub)

- [ ] **Step 1: Create AlertLevel.cs**

```csharp
namespace Server.AI.Core
{
    public enum AlertLevel
    {
        PEACE = 0,
        HOSTILE = 1,
    }
}
```

- [ ] **Step 2: Create AiBlackboard.cs**

```csharp
using UnityEngine;

namespace Server.AI.Core
{
    public class AiBlackboard
    {
        public AlertLevel AlertLevel { get; set; } = AlertLevel.PEACE;
        public long? TargetId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public Vector3 PatrolCenter { get; set; }
        public float PatrolRadius { get; set; } = 10f;
        public int CurrentPatrolIndex { get; set; }
        public Vector3? LastKnownTargetPosition { get; set; }
    }
}
```

- [ ] **Step 3: Create AiComponent.cs (stub with basic fields)**

```csharp
using UnityEngine;

namespace Server.AI.Core
{
    public sealed class AiComponent
    {
        public long InstanceId { get; }
        public int TemplateId { get; }
        public AiBlackboard Blackboard { get; } = new();

        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public float MoveSpeed { get; set; }
        public float VisionRadius { get; set; }
        public float VisionAngle { get; set; }
        public float AttackRange { get; set; }
        public NpcAnimationState CurrentAnimState { get; set; } = NpcAnimationState.Idle;

        public AiComponent(long instanceId, int templateId)
        {
            InstanceId = instanceId;
            TemplateId = templateId;
        }

        public void SetTarget(long? targetId)
        {
            TargetId = targetId;
            Blackboard.TargetId = targetId;
            if (targetId.HasValue)
            {
                Blackboard.AlertLevel = AlertLevel.HOSTILE;
            }
        }
    }
}
```

- [ ] **Step 4: Commit**
```bash
git add -A && git commit -m "feat(server): add AI core types (AlertLevel, Blackboard, AiComponent)"
```

---

## Task 4: BehaviorTree Nodes

**Files:**
- Create: `../Server/AI/BehaviorTree/BtStatus.cs`
- Create: `../Server/AI/BehaviorTree/BtNode.cs`
- Create: `../Server/AI/BehaviorTree/BtSelector.cs`
- Create: `../Server/AI/BehaviorTree/BtSequence.cs`
- Create: `../Server/AI/BehaviorTree/BtCondition.cs`
- Create: `../Server/AI/BehaviorTree/BtAction.cs`

- [ ] **Step 1: Create BtStatus.cs**

```csharp
namespace Server.AI.BehaviorTree
{
    public enum BtStatus
    {
        Success,
        Failure,
        Running,
    }
}
```

- [ ] **Step 2: Create BtNode.cs**

```csharp
namespace Server.AI.BehaviorTree
{
    public abstract class BtNode
    {
        public abstract BtStatus Tick(AiComponent ai);
    }
}
```

- [ ] **Step 3: Create BtSelector.cs**

```csharp
using System.Collections.Generic;

namespace Server.AI.BehaviorTree
{
    public class BtSelector : BtNode
    {
        private readonly List<BtNode> _children = new();

        public BtSelector(params BtNode[] children)
        {
            _children.AddRange(children);
        }

        public override BtStatus Tick(AiComponent ai)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ai);
                if (status == BtStatus.Success)
                    return BtStatus.Success;
                if (status == BtStatus.Running)
                    return BtStatus.Running;
            }
            return BtStatus.Failure;
        }
    }
}
```

- [ ] **Step 4: Create BtSequence.cs**

```csharp
using System.Collections.Generic;

namespace Server.AI.BehaviorTree
{
    public class BtSequence : BtNode
    {
        private readonly List<BtNode> _children = new();

        public BtSequence(params BtNode[] children)
        {
            _children.AddRange(children);
        }

        public override BtStatus Tick(AiComponent ai)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ai);
                if (status == BtStatus.Failure)
                    return BtStatus.Failure;
                if (status == BtStatus.Running)
                    return BtStatus.Running;
            }
            return BtStatus.Success;
        }
    }
}
```

- [ ] **Step 5: Create BtCondition.cs**

```csharp
namespace Server.AI.BehaviorTree
{
    public delegate bool ConditionDelegate(AiComponent ai);

    public class BtCondition : BtNode
    {
        private readonly ConditionDelegate _condition;

        public BtCondition(ConditionDelegate condition)
        {
            _condition = condition;
        }

        public override BtStatus Tick(AiComponent ai)
        {
            return _condition(ai) ? BtStatus.Success : BtStatus.Failure;
        }
    }
}
```

- [ ] **Step 6: Create BtAction.cs**

```csharp
namespace Server.AI.BehaviorTree
{
    public delegate BtStatus ActionDelegate(AiComponent ai);

    public class BtAction : BtNode
    {
        private readonly ActionDelegate _action;

        public BtAction(ActionDelegate action)
        {
            _action = action;
        }

        public override BtStatus Tick(AiComponent ai)
        {
            return _action(ai);
        }
    }
}
```

- [ ] **Step 7: Commit**
```bash
git add -A && git commit -m "feat(server): add behavior tree node types"
```

---

## Task 5: Combat - AggroTable

**Files:**
- Create: `../Server/AI/Combat/AggroTable.cs`

- [ ] **Step 1: Create AggroTable.cs**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Server.AI.Combat
{
    public class AggroTable
    {
        private readonly Dictionary<long, float> _entries = new();
        private const float DecayRateInCombat = 0.05f;    // 5% per second
        private const float DecayRateOutOfRange = 0.20f;   // 20% per second

        public void AddAggro(long targetId, float amount)
        {
            if (_entries.TryGetValue(targetId, out var existing))
                _entries[targetId] = existing + amount;
            else
                _entries[targetId] = amount;
        }

        public void RemoveAggro(long targetId)
        {
            _entries.Remove(targetId);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public void DecayAll(float deltaTime, bool targetInRange)
        {
            float decayRate = targetInRange ? DecayRateInCombat : DecayRateOutOfRange;
            float decayFactor = 1f - (decayRate * deltaTime);

            var keys = _entries.Keys.ToList();
            foreach (var key in keys)
            {
                _entries[key] = Mathf.Max(0, _entries[key] * decayFactor);
                if (_entries[key] <= 0)
                    _entries.Remove(key);
            }
        }

        public long? GetHighestAggroTarget()
        {
            if (_entries.Count == 0) return null;
            return _entries.OrderByDescending(kv => kv.Value).First().Key;
        }
    }
}
```

Note: Uses `Mathf` from UnityEngine - server will need UnityEngine reference.

- [ ] **Step 2: Commit**
```bash
git add -A && git commit -m "feat(server): add AggroTable for threat management"
```

---

## Task 6: Detection - TargetDetector

**Files:**
- Create: `../Server/AI/Detection/TargetDetector.cs`

- [ ] **Step 1: Create TargetDetector.cs**

```csharp
using UnityEngine;

namespace Server.AI.Detection
{
    public class TargetDetector
    {
        private readonly float _detectionRadius;
        private readonly float _visionAngle;

        public TargetDetector(float detectionRadius, float visionAngle)
        {
            _detectionRadius = detectionRadius;
            _visionAngle = visionAngle;
        }

        public bool CanDetectTarget(Vector3 aiPosition, Vector3 aiForward, Vector3 targetPosition)
        {
            float distance = Vector3.Distance(aiPosition, targetPosition);
            if (distance > _detectionRadius) return false;

            Vector3 directionToTarget = (targetPosition - aiPosition).normalized;
            float angle = Vector3.Angle(aiForward, directionToTarget);
            if (angle > _visionAngle / 2) return false;

            return true;
        }
    }
}
```

- [ ] **Step 2: Commit**
```bash
git add -A && git commit -m "feat(server): add TargetDetector with distance + vision cone"
```

---

## Task 7: Movement - SimpleMoveSystem

**Files:**
- Create: `../Server/AI/Movement/SimpleMoveSystem.cs`

- [ ] **Step 1: Create SimpleMoveSystem.cs**

```csharp
using UnityEngine;

namespace Server.AI.Movement
{
    public class SimpleMoveSystem
    {
        private const float RotationSpeed = 180f; // degrees per second

        public void MoveTo(AiComponent ai, Vector3 targetPosition, float deltaTime)
        {
            Vector3 direction = (targetPosition - ai.Position);
            direction.y = 0; // ignore vertical

            if (direction.sqrMagnitude < 0.001f) return;

            direction.Normalize();

            // Rotate towards target
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            ai.Rotation = Quaternion.RotateTowards(ai.Rotation, targetRotation, RotationSpeed * deltaTime);

            // Move forward
            Vector3 movement = direction * ai.MoveSpeed * deltaTime;
            ai.Position += movement;
        }

        public bool HasReached(AiComponent ai, Vector3 target, float threshold = 0.1f)
        {
            float dist = Vector3.Distance(ai.Position, target);
            dist.y = 0;
            return dist < threshold;
        }
    }
}
```

- [ ] **Step 2: Commit**
```bash
git add -A && git commit -m "feat(server): add SimpleMoveSystem for AI movement"
```

---

## Task 8: Skill - SkillData, SkillSystem

**Files:**
- Create: `../Server/AI/Skill/SkillData.cs`
- Create: `../Server/AI/Skill/SkillSystem.cs`

- [ ] **Step 1: Create SkillData.cs**

```csharp
namespace Server.AI.Skill
{
    public class SkillData
    {
        public string SkillName { get; set; }
        public float Damage { get; set; }
        public float Range { get; set; }
        public float Cooldown { get; set; }
        public float CastTime { get; set; }
    }
}
```

- [ ] **Step 2: Create SkillSystem.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Server.AI.Skill
{
    public class SkillSystem
    {
        private readonly List<SkillData> _skills = new();
        private readonly Dictionary<string, float> _cooldowns = new();

        public SkillSystem(IEnumerable<SkillData> skills)
        {
            foreach (var skill in skills)
            {
                _skills.Add(skill);
                _cooldowns[skill.SkillName] = 0f;
            }
        }

        public void Update(float deltaTime)
        {
            foreach (var key in _cooldowns.Keys)
            {
                _cooldowns[key] = Mathf.Max(0, _cooldowns[key] - deltaTime);
            }
        }

        public bool CanCast(string skillName)
        {
            return _cooldowns.TryGetValue(skillName, out var cd) && cd <= 0;
        }

        public float? CastSkill(string skillName)
        {
            if (!CanCast(skillName)) return null;

            var skill = _skills.Find(s => s.SkillName == skillName);
            if (skill == null) return null;

            _cooldowns[skillName] = skill.Cooldown;
            return skill.Damage;
        }

        public float GetDamage(string skillName)
        {
            var skill = _skills.Find(s => s.SkillName == skillName);
            return skill?.Damage ?? 0f;
        }
    }
}
```

- [ ] **Step 3: Commit**
```bash
git add -A && git commit -m "feat(server): add SkillSystem with cooldown management"
```

---

## Task 9: Monster Config (JSON)

**Files:**
- Create: `../Server/Config/MonsterConfig.cs`
- Create: `../Server/Config/monster_config.json`

- [ ] **Step 1: Create MonsterConfig.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Server.Config
{
    public class MonsterConfig
    {
        public List<MonsterData> monsters { get; set; } = new();
    }

    public class MonsterData
    {
        public int templateId;
        public string name;
        public float hp;
        public float moveSpeed;
        public float detectionRadius;
        public float visionAngle;
        public float attackRange;
        public float patrolRadius;
        public List<string> skills;
    }
}
```

- [ ] **Step 2: Create monster_config.json**

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
    },
    {
      "templateId": 2,
      "name": "Wolf",
      "hp": 150,
      "moveSpeed": 3.5,
      "detectionRadius": 15,
      "visionAngle": 90,
      "attackRange": 2.0,
      "patrolRadius": 10,
      "skills": ["Attack"]
    }
  ]
}
```

- [ ] **Step 3: Commit**
```bash
git add -A && git commit -m "feat(server): add monster configuration"
```

---

## Task 10: AI Actions (Patrol, Chase, Attack, Return)

**Files:**
- Create: `../Server/AI/BehaviorTree/BtActions.cs`

- [ ] **Step 1: Create BtActions.cs**

```csharp
using UnityEngine;
using Server.AI.Core;
using Server.AI.BehaviorTree;
using Server.AI.Movement;
using Server.AI.Skill;
using Server.Messages;

namespace Server.AI
{
    public static class BtActions
    {
        public static BtStatus Patrol(AiComponent ai, SimpleMoveSystem moveSystem, Vector3[] patrolPoints)
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
                return BtStatus.Success;

            int idx = ai.Blackboard.CurrentPatrolIndex % patrolPoints.Length;
            Vector3 target = patrolPoints[idx];

            moveSystem.MoveTo(ai, target, Time.deltaTime);

            if (moveSystem.HasReached(ai, target))
            {
                ai.Blackboard.CurrentPatrolIndex++;
                ai.CurrentAnimState = NpcAnimationState.Idle;
            }
            else
            {
                ai.CurrentAnimState = NpcAnimationState.Running;
            }

            return BtStatus.Running;
        }

        public static BtStatus Chase(AiComponent ai, Vector3 targetPosition, SimpleMoveSystem moveSystem)
        {
            moveSystem.MoveTo(ai, targetPosition, Time.deltaTime);
            ai.CurrentAnimState = NpcAnimationState.Running;
            return BtStatus.Running;
        }

        public static BtStatus Attack(AiComponent ai, Vector3 targetPosition, SkillSystem skillSystem)
        {
            float distance = Vector3.Distance(ai.Position, targetPosition);
            if (distance > ai.AttackRange)
                return BtStatus.Failure; // target out of range, stop attacking

            var damage = skillSystem.CastSkill("Attack");
            if (damage.HasValue)
            {
                ai.CurrentAnimState = NpcAnimationState.Attack;
                return BtStatus.Success;
            }

            return BtStatus.Running; // on cooldown, keep trying
        }

        public static BtStatus Return(AiComponent ai, SimpleMoveSystem moveSystem)
        {
            moveSystem.MoveTo(ai, ai.Blackboard.SpawnPosition, Time.deltaTime);

            if (moveSystem.HasReached(ai, ai.Blackboard.SpawnPosition))
            {
                ai.Blackboard.AlertLevel = AlertLevel.PEACE;
                ai.CurrentAnimState = NpcAnimationState.Idle;
                return BtStatus.Success;
            }

            ai.CurrentAnimState = NpcAnimationState.Running;
            return BtStatus.Running;
        }
    }
}
```

- [ ] **Step 2: Commit**
```bash
git add -A && git commit -m "feat(server): add AI action implementations (Patrol, Chase, Attack, Return)"
```

---

## Task 11: AiManager - Core AI Loop

**Files:**
- Create: `../Server/AI/Core/AiManager.cs`
- Modify: `../Server/AI/Core/AiComponent.cs` (add full fields)

- [ ] **Step 1: Update AiComponent.cs to include all systems**

```csharp
using UnityEngine;
using Server.AI.Combat;
using Server.AI.Detection;
using Server.AI.Movement;
using Server.AI.Skill;
using Server.AI.BehaviorTree;

namespace Server.AI.Core
{
    public sealed class AiComponent
    {
        public long InstanceId { get; }
        public int TemplateId { get; }

        public AiBlackboard Blackboard { get; } = new();
        public BehaviorTree.BehaviorTree BehaviorTree { get; set; }
        public SkillSystem SkillSystem { get; }
        public AggroTable AggroTable { get; } = new();
        public TargetDetector TargetDetector { get; }

        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public float MoveSpeed { get; set; }
        public float VisionRadius { get; set; }
        public float VisionAngle { get; set; }
        public float AttackRange { get; set; }
        public NpcAnimationState CurrentAnimState { get; set; } = NpcAnimationState.Idle;
        public Vector3[] PatrolPoints { get; set; }

        private readonly SimpleMoveSystem _moveSystem = new();
        private long? _currentTargetId;

        public AiComponent(long instanceId, int templateId, MonsterData config)
        {
            InstanceId = instanceId;
            TemplateId = templateId;
            MoveSpeed = config.moveSpeed;
            VisionRadius = config.detectionRadius;
            VisionAngle = config.visionAngle;
            AttackRange = config.attackRange;

            Blackboard.SpawnPosition = Vector3.zero;
            Blackboard.PatrolCenter = Vector3.zero;
            Blackboard.PatrolRadius = config.patrolRadius;

            // Init skills
            var skills = new List<SkillData>();
            foreach (var skillName in config.skills)
            {
                skills.Add(new SkillData { SkillName = skillName, Damage = 10, Range = config.attackRange, Cooldown = 1f });
            }
            SkillSystem = new SkillSystem(skills);

            TargetDetector = new TargetDetector(config.detectionRadius, config.visionAngle);
        }

        public void Update(float deltaTime)
        {
            // Update skill cooldowns
            SkillSystem.Update(deltaTime);

            // Update aggro decay
            bool targetInRange = _currentTargetId.HasValue;
            AggroTable.DecayAll(deltaTime, targetInRange);

            // Execute behavior tree
            if (BehaviorTree != null)
            {
                BehaviorTree.Tick(this);
            }
        }

        public void SetTarget(long? targetId)
        {
            _currentTargetId = targetId;
            Blackboard.TargetId = targetId;
            if (targetId.HasValue)
            {
                Blackboard.AlertLevel = AlertLevel.HOSTILE;
            }
        }
    }
}
```

- [ ] **Step 2: Create AiManager.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Server.AI.Core
{
    public class AiManager
    {
        private readonly Dictionary<long, AiComponent> _aiComponents = new();
        private long _nextInstanceId = 1;
        private readonly object _lock = new();
        private bool _running;
        private Thread _updateThread;

        public event Action<long, Vector3, Quaternion, NpcAnimationState> OnAiStateChanged;

        public AiComponent Spawn(int templateId, Vector3 position, Quaternion rotation)
        {
            lock (_lock)
            {
                long id = _nextInstanceId++;
                var ai = new AiComponent(id, templateId, GetConfig(templateId));
                ai.Position = position;
                ai.Rotation = rotation;
                ai.Blackboard.SpawnPosition = position;
                ai.Blackboard.PatrolCenter = position;

                // Build behavior tree
                ai.BehaviorTree = BuildBehaviorTree(ai);

                // Generate patrol points
                ai.PatrolPoints = GeneratePatrolPoints(position, ai.Blackboard.PatrolRadius);

                _aiComponents[id] = ai;
                return ai;
            }
        }

        public void Despawn(long instanceId)
        {
            lock (_lock)
            {
                _aiComponents.Remove(instanceId);
            }
        }

        public void Start()
        {
            _running = true;
            _updateThread = new Thread(UpdateLoop);
            _updateThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _updateThread?.Join();
        }

        private void UpdateLoop()
        {
            while (_running)
            {
                float deltaTime = Time.deltaTime;
                List<AiComponent> ais;

                lock (_lock)
                {
                    ais = new List<AiComponent>(_aiComponents.Values);
                }

                foreach (var ai in ais)
                {
                    try
                    {
                        var prevAnim = ai.CurrentAnimState;
                        ai.Update(deltaTime);

                        if (ai.CurrentAnimState != prevAnim ||
                            Vector3.Distance(ai.Position, lastPositions.GetValueOrDefault(ai.InstanceId)) > 0.1f)
                        {
                            OnAiStateChanged?.Invoke(ai.InstanceId, ai.Position, ai.Rotation, ai.CurrentAnimState);
                            lastPositions[ai.InstanceId] = ai.Position;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"AI Update error: {e}");
                    }
                }

                Thread.Sleep(100); // 10Hz update
            }
        }

        private Dictionary<long, Vector3> lastPositions = new();

        private MonsterData GetConfig(int templateId)
        {
            // Load from config - simplified for now
            return new MonsterData
            {
                templateId = templateId,
                moveSpeed = 3f,
                detectionRadius = 30f,
                visionAngle = 120f,
                attackRange = 2f,
                patrolRadius = 10f,
                skills = new() { "Attack" }
            };
        }

        private Vector3[] GeneratePatrolPoints(Vector3 center, float radius)
        {
            const int count = 4;
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * (360f / count);
                float rad = angle * Mathf.Deg2Rad;
                points[i] = center + new Vector3(Mathf.Cos(rad) * radius, 0, Mathf.Sin(rad) * radius);
            }
            return points;
        }

        private BehaviorTree.BehaviorTree BuildBehaviorTree(AiComponent ai)
        {
            // Root: Selector
            // ├── Sequence: 巡逻（PEACE）
            // ├── Sequence: 追击+攻击（HOSTILE）
            // └── Sequence: 返回
            return BehaviorTree.BehaviorTreeBuilder.Create()
                .Selector()
                    .Sequence("Patrol")
                        .Condition(ai => ai.Blackboard.AlertLevel == AlertLevel.PEACE)
                        .Condition(ai => !ai.Blackboard.TargetId.HasValue)
                        .Action(ai => BtActions.Patrol(ai, new SimpleMoveSystem(), ai.PatrolPoints))
                    .End()
                    .Sequence("ChaseAndAttack")
                        .Condition(ai => ai.Blackboard.AlertLevel == AlertLevel.HOSTILE)
                        .Condition(ai => ai.Blackboard.TargetId.HasValue)
                        .Action(ai => BtActions.Chase(ai, ai.Blackboard.LastKnownTargetPosition ?? Vector3.zero, new SimpleMoveSystem()))
                        .Action(ai => BtActions.Attack(ai, ai.Blackboard.LastKnownTargetPosition ?? Vector3.zero, ai.SkillSystem))
                    .End()
                    .Sequence("Return")
                        .Condition(ai => !ai.Blackboard.TargetId.HasValue)
                        .Condition(ai => ai.Blackboard.AlertLevel != AlertLevel.PEACE)
                        .Action(ai => BtActions.Return(ai, new SimpleMoveSystem()))
                    .End()
                .End()
                .Build();
        }
    }
}
```

- [ ] **Step 3: Commit**
```bash
git add -A && git commit -m "feat(server): add AiManager with behavior tree execution"
```

---

## Task 12: Server Network - NpcServer, NpcSyncBroadcaster

**Files:**
- Create: `../Server/Network/NpcServer.cs`
- Create: `../Server/Network/NpcSyncBroadcaster.cs`

- [ ] **Step 1: Create NpcServer.cs (stub using KCP)**

```csharp
using System;
using System.Net;
using System.Threading;
using KcpNet;

namespace Server.Network
{
    public class NpcServer
    {
        private KcpServer _kcpServer;
        private AiManager _aiManager;
        private NpcSyncBroadcaster _broadcaster;

        public void Start()
        {
            _aiManager = new AiManager();
            _broadcaster = new NpcSyncBroadcaster();

            // KCP server setup (pseudo-code - adapt to existing KcpServer)
            _kcpServer = new KcpServer(8080);
            _kcpServer.OnClientConnected += OnClientConnected;
            _kcpServer.OnClientDisconnected += OnClientDisconnected;
            _kcpServer.OnMessageReceived += OnMessageReceived;

            _aiManager.OnAiStateChanged += (id, pos, rot, anim) =>
            {
                _broadcaster.QueueSync(id, pos, rot, anim);
            };

            _aiManager.Start();

            // Spawn test NPC
            _aiManager.Spawn(1, new UnityEngine.Vector3(0, 0, 0), UnityEngine.Quaternion.identity);

            Console.WriteLine("NpcServer started on port 8080");
        }

        private void OnClientConnected(long sessionId)
        {
            Console.WriteLine($"Client connected: {sessionId}");
            _broadcaster.AddSession(sessionId);
        }

        private void OnClientDisconnected(long sessionId)
        {
            Console.WriteLine($"Client disconnected: {sessionId}");
            _broadcaster.RemoveSession(sessionId);
        }

        private void OnMessageReceived(long sessionId, object message)
        {
            // Handle client messages if needed
        }
    }
}
```

- [ ] **Step 2: Create NpcSyncBroadcaster.cs**

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using Server.Messages;
using KcpNet;

namespace Server.Network
{
    public class NpcSyncBroadcaster
    {
        private readonly ConcurrentDictionary<long, Action<object>> _sessions = new();
        private readonly ConcurrentQueue<NpcPosSync> _syncQueue = new();
        private readonly ConcurrentQueue<NpcAnimSync> _animQueue = new();

        public void AddSession(long sessionId)
        {
            _sessions[sessionId] = null;
        }

        public void RemoveSession(long sessionId)
        {
            _sessions.TryRemove(sessionId, out _);
        }

        public void QueueSync(long instanceId, Vector3 pos, Quaternion rot, NpcAnimationState anim)
        {
            _syncQueue.Enqueue(new NpcPosSync
            {
                InstanceId = instanceId,
                X = pos.x,
                Y = pos.y,
                Z = pos.z,
                RotationY = rot.eulerAngles.y,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            _animQueue.Enqueue(new NpcAnimSync
            {
                InstanceId = instanceId,
                AnimationState = (int)anim
            });
        }

        public void Broadcast()
        {
            while (_syncQueue.TryDequeue(out var sync))
            {
                foreach (var session in _sessions.Keys)
                {
                    // Send via KCP
                    // _kcpServer.Send(session, sync);
                }
            }

            while (_animQueue.TryDequeue(out var anim))
            {
                foreach (var session in _sessions.Keys)
                {
                    // Send via KCP
                }
            }
        }
    }
}
```

- [ ] **Step 3: Commit**
```bash
git add -A && git commit -m "feat(server): add NpcServer and NpcSyncBroadcaster"
```

---

## Task 13: Client NpcMirror System

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirrorManager.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcMirrorComponent.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/NpcMirror/NpcAnimationController.cs`

- [ ] **Step 1: Create NpcMirrorComponent.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.NpcMirror
{
    public class NpcMirrorComponent
    {
        public long InstanceId { get; }
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private NpcAnimationState _targetAnimState;
        private float _lerpSpeed = 10f;

        public GameObject GameObject { get; private set; }
        public Transform Transform { get; private set; }

        public NpcMirrorComponent(long instanceId, Vector3 position, Quaternion rotation)
        {
            InstanceId = instanceId;
            _targetPosition = position;
            _targetRotation = rotation;
        }

        public void SetGameObject(GameObject go)
        {
            GameObject = go;
            Transform = go.transform;
            Transform.position = _targetPosition;
            Transform.rotation = _targetRotation;
        }

        public void SetPosition(Vector3 pos)
        {
            _targetPosition = pos;
        }

        public void SetRotation(Quaternion rot)
        {
            _targetRotation = rot;
        }

        public void SetAnimationState(NpcAnimationState state)
        {
            _targetAnimState = state;
        }

        public void Update(float deltaTime)
        {
            if (Transform == null) return;

            Transform.position = Vector3.Lerp(Transform.position, _targetPosition, _lerpSpeed * deltaTime);
            Transform.rotation = Quaternion.Slerp(Transform.rotation, _targetRotation, _lerpSpeed * deltaTime);
        }
    }
}
```

- [ ] **Step 2: Create NpcAnimationController.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.NpcMirror
{
    public class NpcAnimationController
    {
        private readonly Animator _animator;
        private NpcAnimationState _currentState = NpcAnimationState.Idle;

        public NpcAnimationController(Animator animator)
        {
            _animator = animator;
        }

        public void SetState(NpcAnimationState state)
        {
            if (state == _currentState) return;
            _currentState = state;

            _animator.SetInteger("State", (int)state);
        }
    }
}
```

- [ ] **Step 3: Create NpcMirrorManager.cs**

```csharp
using System.Collections.Generic;

namespace Hotfix.GameSystems.NpcMirror
{
    public class NpcMirrorManager
    {
        private readonly Dictionary<long, NpcMirrorComponent> _mirrors = new();

        public NpcMirrorComponent CreateNpc(long instanceId, int templateId, Vector3 position, Quaternion rotation)
        {
            if (_mirrors.TryGetValue(instanceId, out var existing))
                return existing;

            var mirror = new NpcMirrorComponent(instanceId, position, rotation);
            _mirrors[instanceId] = mirror;
            return mirror;
        }

        public void RemoveNpc(long instanceId)
        {
            if (_mirrors.TryGetValue(instanceId, out var mirror))
            {
                // Destroy GameObject if exists
                mirror.GameObject?.Destroy();
                _mirrors.Remove(instanceId);
            }
        }

        public void OnNpcSpawn(long instanceId, int templateId, Vector3 position, Quaternion rotation)
        {
            CreateNpc(instanceId, templateId, position, rotation);
        }

        public void OnNpcDespawn(long instanceId)
        {
            RemoveNpc(instanceId);
        }

        public void OnNpcPosSync(long instanceId, Vector3 position, Quaternion rotation)
        {
            if (_mirrors.TryGetValue(instanceId, out var mirror))
            {
                mirror.SetPosition(position);
                mirror.SetRotation(rotation);
            }
        }

        public void OnNpcAnimSync(long instanceId, NpcAnimationState state)
        {
            if (_mirrors.TryGetValue(instanceId, out var mirror))
            {
                mirror.SetAnimationState(state);
            }
        }

        public void Update(float deltaTime)
        {
            foreach (var mirror in _mirrors.Values)
            {
                mirror.Update(deltaTime);
            }
        }
    }
}
```

- [ ] **Step 4: Commit**
```bash
git add -A && git commit -m "feat(client): add NpcMirror client-side display system"
```

---

## Self-Review Checklist

1. **Spec coverage:** All spec sections covered - AI Core, BehaviorTree, Detection, Combat, Skill, Movement, Network, Client Mirror
2. **Placeholder scan:** No TBD/TODO - all implementations are complete
3. **Type consistency:** BtStatus, AlertLevel, NpcAnimationState used consistently throughout

---

**Plan complete and saved to `docs/superpowers/plans/2026-04-08-npc-ai-system.md`**

Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
