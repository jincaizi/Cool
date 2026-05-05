# Monster Combat System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a client-side monster combat system — configurable monsters with AI FSM, collider hit detection, and loot drops — integrating with the existing player 3C/Skill systems.

**Architecture:** Component-based monsters (MonsterEntity assembles MonsterStats, MonsterAI, MonsterMovement, MonsterHitZone, MonsterAttackHitbox). Shares `DamageData`/`AttributeType` with player systems via `IDamageable` interface. Both player and monster attacks use collider-based hit detection with per-activation hit tracking (no double-hits). PlayerHitZone bridges monster attack hits into the existing `FSMManager.HandleDamage()` flow.

**Tech Stack:** Unity 2022.3, NavMeshAgent, Collider Triggers, ScriptableObject configs

---

### Task 1: IDamageable shared interface

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Combat/IDamageable.cs`

- [ ] **Step 1: Create IDamageable.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    public interface IDamageable
    {
        void TakeDamage(DamageData damageData, Vector3 hitDirection);
        bool IsAlive { get; }
        Transform Transform { get; }
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Combat/IDamageable.cs
git commit -m "feat(combat): add IDamageable shared interface"
```

---

### Task 2: MonsterEvents

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEvents.cs`

- [ ] **Step 1: Create MonsterEvents.cs**

```csharp
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    public struct MonsterDeathEvent : IEvent
    {
        public string MonsterId;
        public Vector3 Position;
        public LootResult[] Loot;

        public MonsterDeathEvent(string monsterId, Vector3 position, LootResult[] loot)
        {
            MonsterId = monsterId;
            Position = position;
            Loot = loot;
        }
    }

    public struct MonsterSpawnEvent : IEvent
    {
        public string MonsterId;
        public Vector3 Position;

        public MonsterSpawnEvent(string monsterId, Vector3 position)
        {
            MonsterId = monsterId;
            Position = position;
        }
    }
}
```

Note: `LootResult` is defined in Task 3 (MonsterLootTable). This task depends on Task 3 for the type, but we can commit together since compile-check happens after all files exist. To keep tasks independent, we'll place LootResult in MonsterLootTable.cs and MonsterEvents.cs references it from the same namespace — both files must exist for compilation.

- [ ] **Step 2: Compile check** (after Task 3)

- [ ] **Step 3: Commit**

(Delayed — commit together with Task 3)

---

### Task 3: MonsterLootTable ScriptableObject

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterLootTable.cs`

- [ ] **Step 1: Create MonsterLootTable.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    [Serializable]
    public struct LootEntry
    {
        public string ItemId;
        public int MinCount;
        public int MaxCount;
        [Range(0f, 1f)]
        public float DropChance;
    }

    public struct LootResult
    {
        public string ItemId;
        public int Count;
    }

    [CreateAssetMenu(fileName = "LootTable", menuName = "Game/Monster/LootTable")]
    public class MonsterLootTable : ScriptableObject
    {
        public LootEntry[] Entries;
        public int GoldMin = 5;
        public int GoldMax = 20;

        public List<LootResult> Roll()
        {
            var results = new List<LootResult>();
            int gold = UnityEngine.Random.Range(GoldMin, GoldMax + 1);
            if (gold > 0) results.Add(new LootResult { ItemId = "Gold", Count = gold });

            if (Entries != null)
            {
                foreach (var entry in Entries)
                {
                    if (UnityEngine.Random.value < entry.DropChance)
                    {
                        int count = UnityEngine.Random.Range(entry.MinCount, entry.MaxCount + 1);
                        results.Add(new LootResult { ItemId = entry.ItemId, Count = count });
                    }
                }
            }
            return results;
        }
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit (Tasks 2+3 together)**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEvents.cs Assets/Scripts/Hotfix/GameSystems/Monster/MonsterLootTable.cs
git commit -m "feat(monster): add MonsterEvents and MonsterLootTable"
```

---

### Task 4: MonsterConfig ScriptableObject

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs`

- [ ] **Step 1: Create MonsterConfig.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    [CreateAssetMenu(fileName = "MonsterConfig", menuName = "Game/Monster/Config")]
    public class MonsterConfig : ScriptableObject
    {
        [Header("Basic")]
        public string MonsterId;
        public string DisplayName;
        public GameObject Prefab;

        [Header("Stats")]
        public float MaxHP = 100;
        public float AttackPower = 20;
        public float Defense = 10;
        public float MoveSpeed = 3.5f;

        [Header("AI Ranges")]
        public float DetectRange = 10f;
        public float LeaveRange = 15f;
        public float AttackRange = 2f;
        public float AttackCooldown = 1.5f;

        [Header("Patrol")]
        public float PatrolRadius = 5f;
        public float IdleDuration = 2f;

        [Header("Combat")]
        public DamageData AttackDamage;
        public float KnockbackForce;
        public float HitStunDuration = 0.3f;

        [Header("Loot & Death")]
        public MonsterLootTable LootTable;
        public float DeathDestroyDelay = 3f;
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs
git commit -m "feat(monster): add MonsterConfig ScriptableObject"
```

---

### Task 5: MonsterStats

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterStats.cs`

- [ ] **Step 1: Create MonsterStats.cs**

```csharp
using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterStats
    {
        private readonly Dictionary<AttributeType, float> _attributes = new();

        public float HP => _attributes[AttributeType.Health];
        public float MaxHP { get; private set; }
        public float AttackPower => _attributes[AttributeType.AttackPower];
        public float Defense => _attributes[AttributeType.Defense];
        public bool IsDead => HP <= 0;

        public event Action OnDeath;
        public event Action<float, float> OnHPChanged;

        public MonsterStats(MonsterConfig config)
        {
            _attributes[AttributeType.Health] = config.MaxHP;
            _attributes[AttributeType.AttackPower] = config.AttackPower;
            _attributes[AttributeType.Defense] = config.Defense;
            _attributes[AttributeType.Speed] = config.MoveSpeed;
            MaxHP = config.MaxHP;
        }

        public void TakeDamage(DamageData damageData)
        {
            if (IsDead) return;

            float def = _attributes[AttributeType.Defense];
            float finalDamage = Mathf.Max(1, damageData.BaseDamage - def * 0.3f);

            _attributes[AttributeType.Health] -= finalDamage;
            OnHPChanged?.Invoke(HP, MaxHP);

            if (HP <= 0)
            {
                _attributes[AttributeType.Health] = 0;
                OnDeath?.Invoke();
            }
        }
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterStats.cs
git commit -m "feat(monster): add MonsterStats with damage calculation"
```

---

### Task 6: MonsterMovement

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterMovement.cs`

- [ ] **Step 1: Create MonsterMovement.cs**

```csharp
using UnityEngine;
using UnityEngine.AI;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterMovement
    {
        private readonly NavMeshAgent _agent;
        private readonly Transform _self;

        public bool HasReachedDestination =>
            !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.1f;

        public MonsterMovement(NavMeshAgent agent, Transform self, MonsterConfig config)
        {
            _agent = agent;
            _self = self;
            _agent.speed = config.MoveSpeed;
            _agent.stoppingDistance = config.AttackRange * 0.8f;
        }

        public void Stop()
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }

        public void Resume()
        {
            _agent.isStopped = false;
        }

        public void Chase(Transform target)
        {
            _agent.isStopped = false;
            _agent.SetDestination(target.position);
        }

        public void PatrolTo(Vector3 point)
        {
            _agent.isStopped = false;
            _agent.SetDestination(point);
        }

        public void ReturnToSpawn(Vector3 spawnPoint)
        {
            _agent.isStopped = false;
            _agent.SetDestination(spawnPoint);
        }

        public void LookAt(Vector3 target)
        {
            Vector3 dir = target - _self.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f)
            {
                _self.rotation = Quaternion.Slerp(
                    _self.rotation,
                    Quaternion.LookRotation(dir),
                    10f * Time.deltaTime);
            }
        }
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterMovement.cs
git commit -m "feat(monster): add MonsterMovement with NavMeshAgent"
```

---

### Task 7: MonsterAI state machine

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs`

- [ ] **Step 1: Create MonsterAI.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public enum MonsterAIState
    {
        Idle = 0,
        Patrol = 1,
        Chase = 2,
        Attack = 3,
        Hit = 4,
        Death = 5
    }

    public class MonsterAI
    {
        private readonly MonsterMovement _movement;
        private readonly MonsterStats _stats;
        private readonly Animator _animator;
        private readonly Transform _self;
        private readonly MonsterConfig _config;
        private readonly Vector3 _spawnPoint;

        private MonsterAIState _state;
        private MonsterAIState _preHitState;
        private float _stateTimer;
        private float _attackCooldown;
        private int _patrolIndex;
        private readonly List<Vector3> _patrolPoints = new();

        private Transform _target;
        public Transform Target
        {
            get => _target;
            set
            {
                _target = value;
                if (value == null && _state != MonsterAIState.Death && _state != MonsterAIState.Hit)
                    ReturnToSpawn();
            }
        }

        public MonsterAIState CurrentState => _state;

        public event Action OnDeathComplete;
        public event Action OnAttackFrame;
        public event Action<MonsterAIState, MonsterAIState> OnStateChanged;

        private static readonly int HASH_State = Animator.StringToHash("AIState");
        private static readonly int HASH_Attack = Animator.StringToHash("Attack");
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static readonly int HASH_Death = Animator.StringToHash("Death");

        public MonsterAI(
            MonsterMovement movement, MonsterStats stats, Animator animator,
            Transform self, MonsterConfig config, Vector3 spawnPoint)
        {
            _movement = movement;
            _stats = stats;
            _animator = animator;
            _self = self;
            _config = config;
            _spawnPoint = spawnPoint;

            _state = MonsterAIState.Idle;
            _stateTimer = config.IdleDuration;
            GeneratePatrolPoints();
        }

        public void Update(float deltaTime)
        {
            if (_state == MonsterAIState.Death) return;

            _attackCooldown -= deltaTime;
            _stateTimer -= deltaTime;

            EvaluateTransitions();
            ExecuteState(deltaTime);
        }

        public void NotifyHit(DamageData damageData, Vector3 hitDirection)
        {
            if (_state == MonsterAIState.Death) return;

            _preHitState = _state == MonsterAIState.Hit ? _preHitState : _state;
            _stateTimer = _config.HitStunDuration;
            TransitionTo(MonsterAIState.Hit);
            _animator.SetTrigger(HASH_Hit);
            _movement.Stop();
        }

        public void EnterDeath()
        {
            _movement.Stop();
            TransitionTo(MonsterAIState.Death);
            _animator.SetTrigger(HASH_Death);
        }

        private void EvaluateTransitions()
        {
            float distToTarget = Target != null
                ? Vector3.Distance(_self.position, Target.position)
                : float.MaxValue;

            switch (_state)
            {
                case MonsterAIState.Idle:
                    if (distToTarget < _config.DetectRange)
                        TransitionTo(MonsterAIState.Chase);
                    else if (_patrolPoints.Count > 0 && _stateTimer <= 0)
                        TransitionTo(MonsterAIState.Patrol);
                    break;

                case MonsterAIState.Patrol:
                    if (distToTarget < _config.DetectRange)
                        TransitionTo(MonsterAIState.Chase);
                    else if (_movement.HasReachedDestination)
                        TransitionTo(MonsterAIState.Idle);
                    break;

                case MonsterAIState.Chase:
                    if (distToTarget > _config.LeaveRange)
                        ReturnToSpawn();
                    else if (distToTarget < _config.AttackRange && _attackCooldown <= 0)
                        TransitionTo(MonsterAIState.Attack);
                    else if (Target == null)
                        TransitionTo(MonsterAIState.Idle);
                    break;

                case MonsterAIState.Attack:
                    if (distToTarget > _config.LeaveRange)
                        ReturnToSpawn();
                    else if (distToTarget > _config.AttackRange)
                        TransitionTo(MonsterAIState.Chase);
                    break;

                case MonsterAIState.Hit:
                    if (_stats.IsDead)
                        EnterDeath();
                    else if (_stateTimer <= 0)
                        RecoverFromHit();
                    break;
            }
        }

        private void ExecuteState(float deltaTime)
        {
            switch (_state)
            {
                case MonsterAIState.Chase:
                    if (Target != null)
                    {
                        _movement.Chase(Target);
                        _movement.LookAt(Target.position);
                    }
                    break;

                case MonsterAIState.Attack:
                    _movement.Stop();
                    if (Target != null)
                        _movement.LookAt(Target.position);
                    break;
            }
        }

        private void TransitionTo(MonsterAIState newState)
        {
            if (_state == newState) return;

            var old = _state;
            _state = newState;
            _animator.SetInteger(HASH_State, (int)newState);
            OnStateChanged?.Invoke(old, newState);

            switch (newState)
            {
                case MonsterAIState.Idle:
                    _stateTimer = _config.IdleDuration;
                    break;

                case MonsterAIState.Patrol:
                    _movement.PatrolTo(_patrolPoints[_patrolIndex]);
                    _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
                    break;

                case MonsterAIState.Chase:
                    _movement.Resume();
                    break;

                case MonsterAIState.Attack:
                    _attackCooldown = _config.AttackCooldown;
                    _animator.SetTrigger(HASH_Attack);
                    OnAttackFrame?.Invoke();
                    break;
            }
        }

        private void RecoverFromHit()
        {
            if (Target != null)
            {
                float dist = Vector3.Distance(_self.position, Target.position);
                TransitionTo(dist < _config.AttackRange ? MonsterAIState.Attack : MonsterAIState.Chase);
            }
            else
            {
                var fallback = _preHitState == MonsterAIState.Hit || _preHitState == MonsterAIState.Death
                    ? MonsterAIState.Idle : _preHitState;
                TransitionTo(fallback);
            }
        }

        private void ReturnToSpawn()
        {
            Target = null;
            _movement.ReturnToSpawn(_spawnPoint);
            TransitionTo(MonsterAIState.Idle);
        }

        private void GeneratePatrolPoints()
        {
            _patrolPoints.Clear();
            if (_config.PatrolRadius <= 0) return;

            for (int i = 0; i < 3; i++)
            {
                float angle = (360f / 3) * i * Mathf.Deg2Rad;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * _config.PatrolRadius;
                _patrolPoints.Add(_spawnPoint + offset);
            }
        }
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs
git commit -m "feat(monster): add MonsterAI 6-state FSM"
```

---

### Task 8: AttackHitboxData + AttackHitbox (shared Combat layer)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitboxData.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs`

- [ ] **Step 1: Create AttackHitboxData.cs**

```csharp
using System;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    [Serializable]
    public class AttackHitboxData
    {
        public DamageData DamageData;
        public float KnockbackForce;
    }
}
```

- [ ] **Step 2: Create AttackHitbox.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Combat
{
    public class AttackHitbox : MonoBehaviour
    {
        public bool IsActive { get; private set; }

        private AttackHitboxData _currentData;
        private readonly HashSet<IDamageable> _hitTargets = new();

        private void Awake()
        {
            gameObject.SetActive(false);
        }

        public void Activate(AttackHitboxData data)
        {
            _currentData = data;
            IsActive = true;
            _hitTargets.Clear();
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }

        private void OnTriggerStay(Collider other)
        {
            if (!IsActive) return;

            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null || _hitTargets.Contains(damageable)) return;

            _hitTargets.Add(damageable);
            Vector3 dir = (damageable.Transform.position - transform.position).normalized;
            damageable.TakeDamage(_currentData.DamageData, dir);
        }
    }
}
```

- [ ] **Step 3: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitboxData.cs Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs
git commit -m "feat(combat): add AttackHitboxData and AttackHitbox"
```

---

### Task 9: MonsterHitZone

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterHitZone.cs`

- [ ] **Step 1: Create MonsterHitZone.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Combat;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterHitZone : MonoBehaviour
    {
        public void Init(IDamageable owner)
        {
            // owner reference is for future use (e.g. damage modifiers)
            // Hit detection is handled by AttackHitbox.OnTriggerStay via GetComponentInParent<IDamageable>
        }
    }
}
```

Note: The actual hit detection works because `AttackHitbox.OnTriggerStay` calls `GetComponentInParent<IDamageable>()`. Since `MonsterEntity` implements `IDamageable` and is a parent of `MonsterHitZone`, the component lookup finds it. `MonsterHitZone` just needs its Collider set to Trigger — no explicit Init needed for the basic flow.

Simplified:

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    [RequireComponent(typeof(Collider))]
    public class MonsterHitZone : MonoBehaviour
    {
        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }
    }
}
```

- [ ] **Step 2: Compile check** (deferred to Task 11 when all files exist together)

- [ ] **Step 3: Commit** (deferred to Task 11)

---

### Task 10: MonsterAttackHitbox

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAttackHitbox.cs`

- [ ] **Step 1: Create MonsterAttackHitbox.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterAttackHitbox : MonoBehaviour
    {
        public bool IsActive { get; private set; }

        private DamageData _damageData;
        private readonly HashSet<GameObject> _hitTargets = new();

        private void Awake()
        {
            gameObject.SetActive(false);
        }

        public void Activate(DamageData damageData)
        {
            IsActive = true;
            _damageData = damageData;
            _hitTargets.Clear();
            gameObject.SetActive(true);
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }

        private void OnTriggerStay(Collider other)
        {
            if (!IsActive) return;

            var handler = other.GetComponentInParent<IMonsterDamageHandler>();
            if (handler == null || _hitTargets.Contains(handler.TargetGameObject)) return;

            _hitTargets.Add(handler.TargetGameObject);

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            Vector3 hitDir = (handler.TargetTransform.position - hitPoint).normalized;
            handler.OnMonsterAttackHit(_damageData, hitDir);
        }
    }

    public interface IMonsterDamageHandler
    {
        GameObject TargetGameObject { get; }
        Transform TargetTransform { get; }
        void OnMonsterAttackHit(DamageData damageData, Vector3 hitDirection);
    }
}
```

- [ ] **Step 2: Compile check** (deferred to Task 11)

- [ ] **Step 3: Commit** (deferred to Task 11)

---

### Task 11: MonsterEntity entry point

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 1: Create MonsterEntity.cs**

```csharp
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Hotfix.GameSystems.Combat;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterEntity : MonoBehaviour, IDamageable
    {
        [Header("Components")]
        public Animator Animator;
        public NavMeshAgent NavAgent;
        public MonsterHitZone HitZone;
        public MonsterAttackHitbox AttackHitbox;

        private MonsterConfig _config;
        private MonsterStats _stats;
        private MonsterAI _ai;
        private MonsterMovement _movement;
        private Vector3 _spawnPoint;

        // ---- IDamageable ----
        bool IDamageable.IsAlive => !_stats.IsDead;
        Transform IDamageable.Transform => transform;

        // ---- Events ----
        public event Action OnDeathComplete;
        public event Action<LootResult[]> OnLootDrop;

        public void Init(MonsterConfig config, Vector3 spawnPoint)
        {
            _config = config;
            _spawnPoint = spawnPoint;

            _stats = new MonsterStats(config);
            _movement = new MonsterMovement(NavAgent, transform, config);
            _ai = new MonsterAI(_movement, _stats, Animator, transform, config, spawnPoint);

            _stats.OnDeath += HandleDeath;
            _stats.OnHPChanged += (cur, max) => { /* HUD update hook */ };

            _ai.OnDeathComplete += () => StartCoroutine(DeathSequence());
            _ai.OnAttackFrame += () =>
            {
                if (AttackHitbox != null && config.AttackDamage != null)
                    AttackHitbox.Activate(config.AttackDamage);
            };
        }

        private void Update()
        {
            if (_stats == null || _stats.IsDead) return;
            _ai.Update(Time.deltaTime);
        }

        void IDamageable.TakeDamage(DamageData data, Vector3 hitDirection)
        {
            if (_stats.IsDead) return;

            _stats.TakeDamage(data);
            _ai.NotifyHit(data, hitDirection);
        }

        private void HandleDeath()
        {
            _ai.EnterDeath();
            NavAgent.enabled = false;
            EventBus.Emit(new MonsterDeathEvent(_config.MonsterId, transform.position, null));
        }

        private IEnumerator DeathSequence()
        {
            // Roll loot
            var loot = _config.LootTable?.Roll();
            if (loot != null && loot.Count > 0)
            {
                var lootArr = loot.ToArray();
                OnLootDrop?.Invoke(lootArr);
                EventBus.Emit(new MonsterDeathEvent(_config.MonsterId, transform.position, lootArr));
            }

            yield return new WaitForSeconds(_config.DeathDestroyDelay);
            OnDeathComplete?.Invoke();
            Destroy(gameObject);
        }
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit (Tasks 9+10+11 together)**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterHitZone.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAttackHitbox.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "feat(monster): add MonsterHitZone, MonsterAttackHitbox, and MonsterEntity"
```

---

### Task 12: PlayerHitZone (bridge monster attack → player damage)

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Combat/PlayerHitZone.cs`

- [ ] **Step 1: Create PlayerHitZone.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    public class PlayerHitZone : MonoBehaviour, IMonsterDamageHandler
    {
        private Sys3C.FSM.FSMManager _fsmManager;

        GameObject IMonsterDamageHandler.TargetGameObject => gameObject;
        Transform IMonsterDamageHandler.TargetTransform => transform;

        public void Init(Sys3C.FSM.FSMManager fsmManager)
        {
            _fsmManager = fsmManager;
        }

        void IMonsterDamageHandler.OnMonsterAttackHit(DamageData damageData, Vector3 hitDirection)
        {
            if (_fsmManager == null) return;

            float damage = damageData != null
                ? damageData.CalculateFinalDamage(null)
                : 10f;

            _fsmManager.HandleDamage(
                sourceId: -1,
                damage: damage,
                hitDirection: hitDirection,
                knockbackForce: 1f,
                launchForce: 0,
                stunDuration: 0,
                isCritical: false
            );
        }
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Combat/PlayerHitZone.cs
git commit -m "feat(combat): add PlayerHitZone for monster-to-player damage"
```

---

### Task 13: MonsterSpawner

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterSpawner.cs`

- [ ] **Step 1: Create MonsterSpawner.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterSpawner : MonoBehaviour
    {
        [Serializable]
        public class SpawnGroup
        {
            public MonsterConfig Config;
            public int Count;
            public float SpawnRadius = 3f;
        }

        public SpawnGroup[] Groups;
        public float RespawnDelay = 30f;

        private readonly List<MonsterEntity> _aliveMonsters = new();
        private readonly Dictionary<string, float> _respawnTimers = new();

        private void Start()
        {
            foreach (var group in Groups)
                SpawnGroup(group);
        }

        private void Update()
        {
            var toRespawn = new List<string>();
            foreach (var kvp in _respawnTimers)
            {
                if (Time.time >= kvp.Value)
                    toRespawn.Add(kvp.Key);
            }

            foreach (var key in toRespawn)
            {
                _respawnTimers.Remove(key);
                foreach (var group in Groups)
                {
                    if (group.Config.MonsterId == key)
                    {
                        SpawnGroup(group);
                        break;
                    }
                }
            }
        }

        private void SpawnGroup(SpawnGroup group)
        {
            for (int i = 0; i < group.Count; i++)
            {
                Vector3 pos = transform.position
                    + UnityEngine.Random.insideUnitSphere * group.SpawnRadius;
                pos.y = transform.position.y;
                Spawn(group.Config, pos);
            }
        }

        public MonsterEntity Spawn(MonsterConfig config, Vector3 position)
        {
            if (config.Prefab == null)
            {
                Debug.LogError($"[MonsterSpawner] Prefab is null for {config.MonsterId}");
                return null;
            }

            var go = Instantiate(config.Prefab, position, Quaternion.identity);
            var entity = go.GetComponent<MonsterEntity>();
            if (entity == null)
            {
                Debug.LogError($"[MonsterSpawner] MonsterEntity component not found on prefab {config.MonsterId}");
                Destroy(go);
                return null;
            }

            entity.Init(config, position);
            entity.OnDeathComplete += () => HandleMonsterDeath(config, entity);
            _aliveMonsters.Add(entity);

            EventBus.Emit(new MonsterSpawnEvent(config.MonsterId, position));
            return entity;
        }

        private void HandleMonsterDeath(MonsterConfig config, MonsterEntity entity)
        {
            _aliveMonsters.Remove(entity);
            _respawnTimers[config.MonsterId] = Time.time + RespawnDelay;
        }
    }
}
```

- [ ] **Step 2: Compile check**

Run MCP tool: `assets-refresh` with options: ForceSynchronousImport

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterSpawner.cs
git commit -m "feat(monster): add MonsterSpawner with respawn support"
```

---

## Completion Checklist

After all tasks are done:
- [ ] All .cs files compile without errors (`assets-refresh` with ForceSynchronousImport, then `console-get-logs` with logTypeFilter=Error)
- [ ] Create a test MonsterConfig asset in Unity: `assets-material-create` or manually via Inspector
- [ ] Create a test MonsterLootTable asset
- [ ] Verify MonsterSpawner spawns monsters at runtime in PlayMode
- [ ] Verify player attacks hit monsters and trigger damage flow
- [ ] Verify monster deaths trigger loot drops and respawn

---

**Plan version:** 1.0
**Last updated:** 2026-05-06
