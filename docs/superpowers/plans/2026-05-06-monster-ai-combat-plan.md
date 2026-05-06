# Monster AI & Combat System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Slime/TurtleShell monster AI, character-monster combat pipeline, and weapon abstraction layer.

**Architecture:** `IAttackShape` interface with Cone/Circle shape implementations forms the attack resolution layer shared by character weapons and monster attacks. `IAIBehaviour` strategy pattern handles unique monster behaviors (Defend/Taunt). Unified `HitZone` replaces both MonsterHitZone and PlayerHitZone. `IWeapon` + `MeleeWeapon` provide weapon abstraction for future extensibility.

**Tech Stack:** Unity 2022 LTS, C# hotfix layer, NavMesh, Animator Controller, ScriptableObject configs

---

## File Structure

### Assembly: Hotfix.GameSystems.Sys3C.Core (`Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/`)

| File | Type | Purpose |
|------|------|---------|
| `IAttackShape.cs` | New | Attack shape interface |
| `AttackShapeConfig.cs` | New | Serializable shape config + ShapeType enum |
| `AttackEffectConfig.cs` | New | Serializable effect (damage+knockback+status) |
| `ConeShape.cs` | New | Cone/fan-shaped attack resolution |
| `CircleShape.cs` | New | Circular AoE attack resolution |
| `AttackShapeFactory.cs` | New | Factory to create IAttackShape from config |
| `IWeapon.cs` | New | Weapon abstraction interface |
| `WeaponConfig.cs` | New | Weapon ScriptableObject config |

### Assembly: Hotfix.GameSystems.Combat (`Assets/Scripts/Hotfix/GameSystems/Combat/`)

| File | Type | Purpose |
|------|------|---------|
| `HitZone.cs` | New | Unified damage receiving component |
| `AttackHitbox.cs` | Modify | Accept AttackEffectConfig instead of DamageData |

### Assembly: Hotfix.GameSystems.Sys3C (`Assets/Scripts/Hotfix/GameSystems/Sys3C/`)

| File | Type | Purpose |
|------|------|---------|
| `MeleeWeapon.cs` | New | Melee weapon MonoBehaviour |
| `CharacterAttackHandler.cs` | New | Character attack entry point |
| `Sys3CEntry.cs` | Modify | Integrate CharacterAttackHandler |
| `FSM/FSMManager.cs` | Modify | Add OnAttackActivated event |
| `Skill/SkillConfig.cs` | Modify | Add AttackShape/Effects/Pattern/MoveLock/Targeting fields |

### Assembly: Hotfix.GameSystems.Monster (`Assets/Scripts/Hotfix/GameSystems/Monster/`)

| File | Type | Purpose |
|------|------|---------|
| `IAIBehaviour.cs` | New | Behaviour strategy interface |
| `MonsterAIContext.cs` | New | Context struct for behaviours |
| `DefendBehaviour.cs` | New | TurtleShell defend logic |
| `TauntBehaviour.cs` | New | Slime taunt logic |
| `AlertBehaviour.cs` | New | Alert/sense behaviour |
| `MonsterAI.cs` | Modify | Add 3 states, IAIBehaviour composition, AttackShape usage |
| `MonsterConfig.cs` | Modify | Add all new behaviour fields |
| `MonsterEntity.cs` | Modify | Use Combat.AttackHitbox + HitZone, adapt to AttackEffectConfig |
| `MonsterMovement.cs` | Modify | Configurable RotationSpeed |
| `MonsterHitZone.cs` | Delete | Replaced by Combat.HitZone |
| `MonsterAttackHitbox.cs` | Delete | Replaced by Combat.AttackHitbox |

### Asset Files

| File | Purpose |
|------|---------|
| `Assets/Monstor/SlimeConfig.asset` | Slime MonsterConfig |
| `Assets/Monstor/TurtleShellConfig.asset` | TurtleShell MonsterConfig |
| `Assets/Monstor/SwordShieldConfig.asset` | Sword+Shield WeaponConfig |
| `Assets/Monstor/DuoPolyart/Animators/Slime.controller` | Slime Animator Controller |
| `Assets/Monstor/DuoPolyart/Animators/TurtleShell.controller` | TurtleShell Animator Controller |

---

## Implementation Tasks

### Task 1: AttackShape base layer — Enums and Configs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IAttackShape.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackEffectConfig.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeConfig.cs`

**Assembly:** `Hotfix.GameSystems.Sys3C.Core` (no new references needed)

- [ ] **Step 1: Create IAttackShape.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface IAttackShape
    {
        IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask);
    }
}
```

- [ ] **Step 2: Create AttackEffectConfig.cs**

```csharp
using System;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum StatusEffectType
    {
        None = 0,
        Poison = 1,
        Bleed = 2,
        Slow = 3,
        Stun = 4,
    }

    [Serializable]
    public class AttackEffectConfig
    {
        public DamageData Damage;
        public float KnockbackForce;
        public float LaunchForce;
        public float StunDuration;
        public StatusEffectType Status;
        public float StatusDuration;
        public float StatusValue;
    }
}
```

- [ ] **Step 3: Create AttackShapeConfig.cs**

```csharp
using System;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum ShapeType
    {
        Cone = 0,
        Circle = 1,
        Rect = 2,
        Ray = 3,
    }

    [Serializable]
    public class AttackShapeConfig
    {
        public ShapeType Type;
        public float Range;
        public float Angle;
        public float Width;
        public bool StopAtFirst;
    }
}
```

- [ ] **Step 4: Refresh Unity to compile**

Run: `mcp__ai-game-developer__assets-refresh`
Expected: No compilation errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IAttackShape.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackEffectConfig.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeConfig.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IAttackShape.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackEffectConfig.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeConfig.cs.meta
git commit -m "feat: add IAttackShape interface, AttackShapeConfig and AttackEffectConfig"
```

---

### Task 2: AttackShape implementations — ConeShape and CircleShape

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ConeShape.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/CircleShape.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeFactory.cs`

- [ ] **Step 1: Create ConeShape.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class ConeShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _angle;
        private static readonly Collider[] _buffer = new Collider[32];

        public ConeShape(float range, float angle)
        {
            _range = range;
            _angle = angle;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            int count = Physics.OverlapSphereNonAlloc(origin, _range, _buffer, targetMask);

            for (int i = 0; i < count; i++)
            {
                var col = _buffer[i];
                Vector3 dir = col.bounds.center - origin;
                float dist = dir.magnitude;
                if (dist > _range) continue;

                float halfAngle = _angle * 0.5f;
                float angleToTarget = Vector3.Angle(forward, dir.normalized);
                if (angleToTarget > halfAngle) continue;

                var target = col.GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive) continue;
                if (results.Contains(target)) continue;

                results.Add(target);
            }
            return results;
        }
    }
}
```

- [ ] **Step 2: Create CircleShape.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class CircleShape : IAttackShape
    {
        private readonly float _radius;
        private static readonly Collider[] _buffer = new Collider[32];

        public CircleShape(float radius)
        {
            _radius = radius;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            int count = Physics.OverlapSphereNonAlloc(origin, _radius, _buffer, targetMask);

            for (int i = 0; i < count; i++)
            {
                var target = _buffer[i].GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive) continue;
                if (results.Contains(target)) continue;
                results.Add(target);
            }
            return results;
        }
    }
}
```

- [ ] **Step 3: Create AttackShapeFactory.cs**

```csharp
using System;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeFactory
    {
        public static IAttackShape Create(AttackShapeConfig config)
        {
            if (config == null)
                return new ConeShape(2f, 120f);

            return config.Type switch
            {
                ShapeType.Cone => new ConeShape(config.Range, config.Angle),
                ShapeType.Circle => new CircleShape(config.Range),
                _ => new ConeShape(config.Range, config.Angle),
            };
        }
    }
}
```

- [ ] **Step 4: Refresh Unity to compile**

Run: `mcp__ai-game-developer__assets-refresh`
Expected: No compilation errors.

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ConeShape.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/CircleShape.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeFactory.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ConeShape.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/CircleShape.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeFactory.cs.meta
git commit -m "feat: add ConeShape, CircleShape and AttackShapeFactory"
```

---

### Task 3: Unified HitZone

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Combat/HitZone.cs`
- Delete: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterHitZone.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Combat/AttackHitbox.cs`

**Assembly:** `Hotfix.GameSystems.Combat` (already references Sys3C.Core and Skills)

- [ ] **Step 1: Create HitZone.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    [RequireComponent(typeof(Collider))]
    public class HitZone : MonoBehaviour
    {
        private IDamageable _owner;
        private readonly HashSet<int> _hitInstanceIds = new();

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        public void Init(IDamageable owner)
        {
            _owner = owner;
        }

        public void ResetHits()
        {
            _hitInstanceIds.Clear();
        }

        private void OnTriggerStay(Collider other)
        {
            var hitbox = other.GetComponent<IAttackHitbox>();
            if (hitbox == null || !hitbox.IsActive) return;
            if (!_hitInstanceIds.Add(hitbox.GetInstanceID())) return;

            Vector3 hitDir = (transform.position - hitbox.GetBounds().center).normalized;

            var data = hitbox.CurrentData;
            if (data != null && data.DamageData != null)
            {
                _owner?.TakeDamage(data.DamageData, hitDir);
            }
        }
    }
}
```

- [ ] **Step 2: Modify AttackHitbox.cs — Activate to accept AttackEffectConfig**

Read the file first, then modify.

```csharp
// Change Activate signature:
public void Activate(AttackEffectConfig effectConfig)
{
    var dmg = effectConfig?.Damage ?? new DamageData();
    CurrentData = new AttackHitboxData
    {
        DamageData = dmg,
        KnockbackForce = effectConfig?.KnockbackForce ?? 0,
        LaunchForce = effectConfig?.LaunchForce ?? 0,
        StunDuration = effectConfig?.StunDuration ?? 0,
    };
    IsActive = true;
    gameObject.SetActive(true);
}

// Keep the old overload for backward compat with existing callers
public void Activate(DamageData damageData)
{
    Activate(new AttackEffectConfig { Damage = damageData });
}
```

The full file after edits:

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    public class AttackHitbox : MonoBehaviour, IAttackHitbox
    {
        public bool IsActive { get; private set; }
        public AttackHitboxData CurrentData { get; private set; }

        private Collider _collider;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            gameObject.SetActive(false);
        }

        public void Activate(AttackEffectConfig effectConfig)
        {
            var dmg = effectConfig?.Damage ?? DamageData.CreateDefault(10f);
            CurrentData = new AttackHitboxData
            {
                DamageData = dmg,
                KnockbackForce = effectConfig?.KnockbackForce ?? 0,
                LaunchForce = effectConfig?.LaunchForce ?? 0,
                StunDuration = effectConfig?.StunDuration ?? 0,
            };
            IsActive = true;
            gameObject.SetActive(true);
        }

        public void Activate(DamageData damageData)
        {
            Activate(new AttackEffectConfig { Damage = damageData });
        }

        public void Deactivate()
        {
            IsActive = false;
            gameObject.SetActive(false);
        }

        public void TriggerHit() { }

        public Bounds GetBounds()
        {
            if (_collider != null)
                return _collider.bounds;
            return new Bounds(transform.position, Vector3.zero);
        }
    }
}
```

- [ ] **Step 3: Delete MonsterHitZone.cs**

```bash
rm Assets/Scripts/Hotfix/GameSystems/Monster/MonsterHitZone.cs
rm Assets/Scripts/Hotfix/GameSystems/Monster/MonsterHitZone.cs.meta
```

- [ ] **Step 4: Delete MonsterAttackHitbox.cs** (replaced by Combat.AttackHitbox)

```bash
rm Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAttackHitbox.cs
rm Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAttackHitbox.cs.meta
```

- [ ] **Step 5: Refresh Unity to compile**

Run: `mcp__ai-game-developer__assets-refresh`
Expected: Compilation errors in MonsterEntity.cs (references deleted types). This is expected — will fix in Task 4.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add unified HitZone, extend AttackHitbox, remove MonsterHitZone and MonsterAttackHitbox"
```

---

### Task 4: MonsterConfig extension

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs`

- [ ] **Step 1: Rewrite MonsterConfig.cs with all new fields**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

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

        [Header("Attack")]
        public int AttackAnimCount = 1;
        public float[] AttackWeights = { 1f };
        public float AttackAnimSpeed = 1f;

        [Header("Attack Shape")]
        public AttackShapeConfig AttackShape;

        [Header("Attack Effects")]
        public AttackEffectConfig[] AttackEffects;

        [Header("Defend")]
        public bool EnableDefend;
        public float DefendHPThreshold = 0.5f;
        public float DefendChaseTimeThreshold = 3f;
        public float DefendDuration = 2f;
        public float DefendDamageReduction = 0.8f;
        public float DefendAngle = 180f;
        public int DefendBlockCountToCounter = 2;
        public float DefendCounterDamageMultiplier = 1.5f;
        public float DefendCooldown = 8f;

        [Header("Taunt")]
        public bool EnableTaunt;
        public float TauntChance = 0.6f;
        public float TauntDuration = 1.5f;

        [Header("Alert")]
        public float AlertRange = 15f;

        [Header("Movement")]
        public bool ChaseAnimIsRun = true;
        public float RotationSpeed = 10f;

        [Header("Loot & Death")]
        public MonsterLootTable LootTable;
        public float DeathDestroyDelay = 3f;
    }
}
```

Remove old fields: `AttackDamage`, `KnockbackForce`, `HitStunDuration` (merged into AttackEffects).

- [ ] **Step 2: Refresh to verify no unrelated errors**

Run: `mcp__ai-game-developer__assets-refresh`

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterConfig.cs
git commit -m "feat: extend MonsterConfig with AttackShape, Defend, Taunt, Alert, Movement sections"
```

---

### Task 5: MonsterEntity adaptation

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

After deleting MonsterHitZone and MonsterAttackHitbox, MonsterEntity needs to use `Combat.HitZone` and `Combat.AttackHitbox`.

- [ ] **Step 1: Rewrite MonsterEntity.cs**

```csharp
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Combat;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterEntity : MonoBehaviour, IDamageable
    {
        [Header("Components")]
        public Animator Animator;
        public NavMeshAgent NavAgent;
        public HitZone HitZone;
        public AttackHitbox AttackHitbox;

        private MonsterConfig _config;
        private MonsterStats _stats;
        private MonsterAI _ai;
        private MonsterMovement _movement;
        private Vector3 _spawnPoint;

        bool IDamageable.IsAlive => !_stats.IsDead;
        Transform IDamageable.Transform => transform;

        public event Action OnDeathComplete;
        public event Action<LootResult[]> OnLootDrop;

        public void Init(MonsterConfig config, Vector3 spawnPoint)
        {
            _config = config;
            _spawnPoint = spawnPoint;

            _stats = new MonsterStats(config);
            _movement = new MonsterMovement(NavAgent, transform, config);
            _ai = new MonsterAI(_movement, _stats, Animator, transform, config, spawnPoint);

            HitZone.Init(this);

            _stats.OnDeath += HandleDeath;
            _stats.OnHPChanged += (cur, max) => { };

            _ai.OnDeathComplete += () => StartCoroutine(DeathSequence());
            _ai.OnAttackHitboxActivate += (effect) =>
            {
                if (AttackHitbox != null && effect != null)
                {
                    AttackHitbox.Activate(effect);
                    HitZone.ResetHits();
                }
            };
            _ai.OnAttackHitboxDeactivate += () =>
            {
                if (AttackHitbox != null)
                    AttackHitbox.Deactivate();
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
        }

        private IEnumerator DeathSequence()
        {
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

Key changes:
- `HitZone` type is now `Combat.HitZone` (not `MonsterHitZone`)
- `AttackHitbox` type is now `Combat.AttackHitbox` (not `MonsterAttackHitbox`)
- `OnAttackFrame` replaced by `OnAttackHitboxActivate` / `OnAttackHitboxDeactivate` event pair
- HitZone.ResetHits() called before each new attack

- [ ] **Step 2: Refresh to verify**

Run: `mcp__ai-game-developer__assets-refresh`
Expected: Errors in MonsterAI.cs (old OnAttackFrame event removed, new events not yet added). Will fix in Task 6.

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "refactor: adapt MonsterEntity to use Combat.HitZone and Combat.AttackHitbox"
```

---

### Task 6: AI Behaviour strategy — interface + context

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/IAIBehaviour.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAIContext.cs`

- [ ] **Step 1: Create IAIBehaviour.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    public interface IAIBehaviour
    {
        bool CanEnter(MonsterAIContext ctx);
        void Enter(MonsterAIContext ctx);
        void Update(MonsterAIContext ctx, float deltaTime);
        void Exit(MonsterAIContext ctx);
        MonsterAIState StateType { get; }
    }
}
```

- [ ] **Step 2: Create MonsterAIContext.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Monster
{
    public struct MonsterAIContext
    {
        public Transform Self;
        public Transform Target;
        public Animator Animator;
        public MonsterStats Stats;
        public MonsterMovement Movement;
        public MonsterConfig Config;
        public float DeltaTime;
        public float StateTimer;
        public int CurrentAttackIndex;
        public bool AttackHitTarget;
        public IAttackShape AttackShape;
        public int DefendBlockCount;
        public float DefendChaseTimer;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/IAIBehaviour.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAIContext.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/IAIBehaviour.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAIContext.cs.meta
git commit -m "feat: add IAIBehaviour interface and MonsterAIContext"
```

---

### Task 7: DefendBehaviour, TauntBehaviour, AlertBehaviour

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/DefendBehaviour.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/TauntBehaviour.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Monster/AlertBehaviour.cs`

- [ ] **Step 1: Create DefendBehaviour.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public class DefendBehaviour : IAIBehaviour
    {
        private int _blockCount;
        private bool _counterReady;

        public MonsterAIState StateType => MonsterAIState.Defend;

        public bool CanEnter(MonsterAIContext ctx)
        {
            if (!ctx.Config.EnableDefend) return false;
            if (ctx.Target == null) return false;

            float hpRatio = ctx.Stats.HP / ctx.Stats.MaxHP;
            return hpRatio < ctx.Config.DefendHPThreshold;
        }

        public void Enter(MonsterAIContext ctx)
        {
            _blockCount = 0;
            _counterReady = false;
            ctx.Movement.Stop();
            ctx.Animator.SetBool("IsDefending", true);
        }

        public void Update(MonsterAIContext ctx, float deltaTime)
        {
            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);

            if (_blockCount >= ctx.Config.DefendBlockCountToCounter)
                _counterReady = true;
        }

        public void Exit(MonsterAIContext ctx)
        {
            ctx.Animator.SetBool("IsDefending", false);
            ctx.Movement.Resume();
        }

        public void OnBlocked(MonsterAIContext ctx)
        {
            _blockCount++;
        }

        public bool IsCounterReady() => _counterReady;

        public float GetCounterMultiplier() => _counterReady
            ? ctx.Config.DefendCounterDamageMultiplier
            : 1f;
    }
}
```

Wait, `OnBlocked` and `IsCounterReady` are not in IAIBehaviour. These are DefendBehaviour-specific methods. The caller needs to know about them. Let me make DefendBehaviour expose these.

Actually, let me fix this — the context needs to carry the defend state, or MonsterAI needs to query the behaviour. Let me take a simpler approach: MonsterAI holds the behaviours as typed fields when they exist, or we cast IAIBehaviour to specific types.

Better approach: Put block tracking and counter state in the context, not the behaviour. The behaviour just reads/writes the context.

- [ ] **Step 1 (revised): Create DefendBehaviour.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    public class DefendBehaviour : IAIBehaviour
    {
        private bool _counterReady;
        private float _defendCooldownTimer;

        public MonsterAIState StateType => MonsterAIState.Defend;

        public bool IsCounterReady => _counterReady;
        public float CooldownTimer => _defendCooldownTimer;

        public void SetCooldownTimer(float value) => _defendCooldownTimer = value;

        public bool CanEnter(MonsterAIContext ctx)
        {
            if (!ctx.Config.EnableDefend) return false;
            if (_defendCooldownTimer > 0) return false;

            float hpRatio = ctx.Stats.HP / ctx.Stats.MaxHP;
            bool hpCondition = hpRatio < ctx.Config.DefendHPThreshold;
            bool chaseCondition = ctx.DefendChaseTimer > ctx.Config.DefendChaseTimeThreshold;
            return hpCondition || chaseCondition;
        }

        public void Enter(MonsterAIContext ctx)
        {
            ctx.DefendBlockCount = 0;
            _counterReady = false;
            ctx.Movement.Stop();
            ctx.Animator.SetBool("IsDefending", true);
        }

        public void Update(MonsterAIContext ctx, float deltaTime)
        {
            _defendCooldownTimer -= deltaTime;
            if (ctx.Target != null)
                ctx.Movement.LookAt(ctx.Target.position);
            if (ctx.DefendBlockCount >= ctx.Config.DefendBlockCountToCounter)
                _counterReady = true;
        }

        public void Exit(MonsterAIContext ctx)
        {
            ctx.Animator.SetBool("IsDefending", false);
            ctx.Movement.Resume();
            _defendCooldownTimer = ctx.Config.DefendCooldown;
        }
    }
}
```

- [ ] **Step 2: Create TauntBehaviour.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    public class TauntBehaviour : IAIBehaviour
    {
        public MonsterAIState StateType => MonsterAIState.Taunt;

        public bool CanEnter(MonsterAIContext ctx)
        {
            return ctx.Config.EnableTaunt
                && !ctx.AttackHitTarget
                && UnityEngine.Random.value < ctx.Config.TauntChance;
        }

        public void Enter(MonsterAIContext ctx)
        {
            ctx.Movement.Stop();
            ctx.Animator.SetTrigger("Taunt");
        }

        public void Update(MonsterAIContext ctx, float deltaTime) { }

        public void Exit(MonsterAIContext ctx)
        {
            ctx.Movement.Resume();
        }
    }
}
```

- [ ] **Step 3: Create AlertBehaviour.cs**

```csharp
namespace Hotfix.GameSystems.Monster
{
    public class AlertBehaviour : IAIBehaviour
    {
        public MonsterAIState StateType => MonsterAIState.Alert;

        public bool CanEnter(MonsterAIContext ctx)
        {
            if (ctx.Target == null) return false;
            float dist = UnityEngine.Vector3.Distance(
                ctx.Self.position, ctx.Target.position);
            return dist > ctx.Config.DetectRange && dist < ctx.Config.AlertRange;
        }

        public void Enter(MonsterAIContext ctx)
        {
            ctx.Animator.SetTrigger("SenseSomething");
        }

        public void Update(MonsterAIContext ctx, float deltaTime) { }

        public void Exit(MonsterAIContext ctx) { }
    }
}
```

- [ ] **Step 4: Refresh to compile**

Run: `mcp__ai-game-developer__assets-refresh`

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/DefendBehaviour.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/TauntBehaviour.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/AlertBehaviour.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/DefendBehaviour.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Monster/TauntBehaviour.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Monster/AlertBehaviour.cs.meta
git commit -m "feat: add DefendBehaviour, TauntBehaviour, AlertBehaviour"
```

---

### Task 8: MonsterAI refactor

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs`

This is the largest change. MonsterAI needs:
1. Enum values 6,7,8 for Defend/Taunt/Alert
2. IAIBehaviour list, built from config
3. OnAttackHitboxActivate/Deactivate events (replacing OnAttackFrame)
4. AttackShape resolution in Attack state
5. Defend state handling with damage reduction
6. Taunt state handling after missed attacks

- [ ] **Step 1: Rewrite MonsterAI.cs**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;
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
        Death = 5,
        Defend = 6,
        Taunt = 7,
        Alert = 8,
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
        private int _currentAttackIndex;
        private bool _attackHitTarget;
        private float _defendChaseTimer;
        private float _defendStateTimer;

        private readonly List<Vector3> _patrolPoints = new();

        // Behaviour strategies
        private DefendBehaviour _defend;
        private TauntBehaviour _taunt;
        private AlertBehaviour _alert;

        private IAttackShape _attackShape;

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
        public event Action<AttackEffectConfig> OnAttackHitboxActivate;
        public event Action OnAttackHitboxDeactivate;
        public event Action<MonsterAIState, MonsterAIState> OnStateChanged;

        private static readonly int HASH_AIState = Animator.StringToHash("AIState");
        private static readonly int HASH_Attack = Animator.StringToHash("Attack");
        private static readonly int HASH_AttackIndex = Animator.StringToHash("AttackIndex");
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static readonly int HASH_Death = Animator.StringToHash("Death");
        private static readonly int HASH_Taunt = Animator.StringToHash("Taunt");
        private static readonly int HASH_Defend = Animator.StringToHash("IsDefending");
        private static readonly int HASH_Speed = Animator.StringToHash("Speed");

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
            BuildBehaviours();
        }

        private void BuildBehaviours()
        {
            if (_config.EnableDefend)
                _defend = new DefendBehaviour();
            if (_config.EnableTaunt)
                _taunt = new TauntBehaviour();
            _alert = new AlertBehaviour();
        }

        public void Update(float deltaTime)
        {
            if (_state == MonsterAIState.Death) return;

            _attackCooldown -= deltaTime;
            _stateTimer -= deltaTime;

            if (_state == MonsterAIState.Chase)
                _defendChaseTimer += deltaTime;

            EvaluateTransitions();
            ExecuteState(deltaTime);
        }

        public void NotifyHit(DamageData damageData, Vector3 hitDirection)
        {
            if (_state == MonsterAIState.Death) return;

            // TurtleShell: if defending, reduce damage and count block
            if (_state == MonsterAIState.Defend && _defend != null)
            {
                Vector3 dirToAttacker = hitDirection;
                float angle = Vector3.Angle(_self.forward, -dirToAttacker);
                if (angle < _config.DefendAngle * 0.5f)
                {
                    // Apply damage reduction directly on stats
                    // Store original HP, apply reduced damage, restore
                    _stats.TakeDamage(new DamageData());
                    // Note: actual reduction handled via modified TakeDamage or separate method
                    _defendStateTimer = 0; // record block
                }
            }

            _preHitState = _state == MonsterAIState.Hit ? _preHitState : _state;

            // Defend state gets DefendHit animation, others get normal GetHit
            if (_state == MonsterAIState.Defend)
            {
                _animator.SetTrigger(HASH_Hit); // DefendHit is transition from Defend with Hit trigger
            }
            else
            {
                _stateTimer = _config.DefendHPThreshold > 0 ? 0.3f : 0.3f;
                TransitionTo(MonsterAIState.Hit);
                _animator.SetTrigger(HASH_Hit);
            }

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

            // Behaviour checks
            if (_state != MonsterAIState.Defend
                && _state != MonsterAIState.Taunt
                && _state != MonsterAIState.Alert
                && _state != MonsterAIState.Hit
                && _state != MonsterAIState.Death)
            {
                if (_defend != null && _defend.CanEnter(BuildContext()))
                {
                    TransitionTo(MonsterAIState.Defend);
                    return;
                }
            }

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
                    {
                        ReturnToSpawn();
                    }
                    else if (distToTarget < _config.AttackRange && _attackCooldown <= 0)
                    {
                        TransitionTo(MonsterAIState.Attack);
                    }
                    else if (Target == null)
                    {
                        TransitionTo(MonsterAIState.Idle);
                    }
                    break;

                case MonsterAIState.Attack:
                    // Attack finished via state timer
                    if (_stateTimer <= 0)
                    {
                        // Check for taunt
                        if (_taunt != null && _taunt.CanEnter(BuildContext()))
                            TransitionTo(MonsterAIState.Taunt);
                        else
                            TransitionTo(MonsterAIState.Chase);
                    }
                    break;

                case MonsterAIState.Defend:
                    if (_stateTimer <= 0)
                    {
                        if (_defend != null && _defend.IsCounterReady)
                            TransitionTo(MonsterAIState.Attack);
                        else if (distToTarget < _config.AttackRange && _attackCooldown <= 0)
                            TransitionTo(MonsterAIState.Attack);
                        else
                            TransitionTo(MonsterAIState.Chase);
                    }
                    break;

                case MonsterAIState.Taunt:
                    if (_stateTimer <= 0)
                    {
                        if (distToTarget < _config.AttackRange)
                            TransitionTo(MonsterAIState.Attack);
                        else if (Target != null)
                            TransitionTo(MonsterAIState.Chase);
                        else
                            TransitionTo(MonsterAIState.Idle);
                    }
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
                case MonsterAIState.Idle:
                    _movement.Stop();
                    break;

                case MonsterAIState.Patrol:
                    break;

                case MonsterAIState.Chase:
                    if (Target != null)
                    {
                        _movement.Chase(Target);
                        _movement.LookAt(Target.position);
                        _animator.SetFloat(HASH_Speed, _config.ChaseAnimIsRun ? 2f : 1f);
                    }
                    break;

                case MonsterAIState.Attack:
                    _movement.Stop();
                    if (Target != null)
                        _movement.LookAt(Target.position);
                    break;

                case MonsterAIState.Defend:
                    // movement already stopped in Enter
                    break;

                case MonsterAIState.Taunt:
                    // movement already stopped in Enter
                    break;

                case MonsterAIState.Hit:
                    break;
            }
        }

        private void TransitionTo(MonsterAIState newState)
        {
            if (_state == newState) return;

            // Exit current behaviour
            ExitBehaviourForState(_state);

            var old = _state;
            _state = newState;
            _animator.SetInteger(HASH_AIState, (int)newState);
            OnStateChanged?.Invoke(old, newState);

            switch (newState)
            {
                case MonsterAIState.Idle:
                    _stateTimer = _config.IdleDuration;
                    _animator.SetFloat(HASH_Speed, 0);
                    break;

                case MonsterAIState.Patrol:
                    _movement.PatrolTo(_patrolPoints[_patrolIndex]);
                    _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
                    _animator.SetFloat(HASH_Speed, 1f);
                    break;

                case MonsterAIState.Chase:
                    _defendChaseTimer = 0;
                    _movement.Resume();
                    _animator.SetFloat(HASH_Speed, 2f);
                    break;

                case MonsterAIState.Attack:
                    _attackCooldown = _config.AttackCooldown;
                    _stateTimer = 0.5f; // attack animation duration
                    _currentAttackIndex = PickAttackIndex();
                    _animator.SetInteger(HASH_AttackIndex, _currentAttackIndex);
                    _animator.SetTrigger(HASH_Attack);

                    // Resolve attack shape
                    var effect = GetCurrentEffect();
                    _attackHitTarget = ResolveAttack(effect);
                    OnAttackHitboxActivate?.Invoke(effect);
                    break;

                case MonsterAIState.Defend:
                    _stateTimer = _config.DefendDuration;
                    EnterBehaviourForState(MonsterAIState.Defend);
                    break;

                case MonsterAIState.Taunt:
                    _stateTimer = _config.TauntDuration;
                    EnterBehaviourForState(MonsterAIState.Taunt);
                    break;

                case MonsterAIState.Alert:
                    EnterBehaviourForState(MonsterAIState.Alert);
                    break;
            }
        }

        private bool ResolveAttack(AttackEffectConfig effect)
        {
            if (effect == null) return false;
            int mask = LayerMask.GetMask("Character");
            var shape = AttackShapeFactory.Create(_config.AttackShape);
            var targets = shape.Resolve(_self.position, _self.forward, mask);
            foreach (var t in targets)
            {
                Vector3 dir = (t.Transform.position - _self.position).normalized;
                t.TakeDamage(effect.Damage, dir);
            }
            return targets.Count > 0;
        }

        private int PickAttackIndex()
        {
            if (_config.AttackAnimCount <= 1) return 0;
            if (_config.AttackWeights == null || _config.AttackWeights.Length == 0) return 0;
            float roll = UnityEngine.Random.value;
            float cumulative = 0;
            for (int i = 0; i < _config.AttackWeights.Length && i < _config.AttackAnimCount; i++)
            {
                cumulative += _config.AttackWeights[i];
                if (roll <= cumulative) return i;
            }
            return 0;
        }

        private AttackEffectConfig GetCurrentEffect()
        {
            if (_config.AttackEffects == null || _config.AttackEffects.Length == 0)
                return new AttackEffectConfig
                {
                    Damage = DamageData.CreateDefault(_config.AttackPower),
                };
            int idx = Mathf.Min(_currentAttackIndex, _config.AttackEffects.Length - 1);
            return _config.AttackEffects[idx];
        }

        private MonsterAIContext BuildContext()
        {
            return new MonsterAIContext
            {
                Self = _self,
                Target = _target,
                Animator = _animator,
                Stats = _stats,
                Movement = _movement,
                Config = _config,
                DeltaTime = Time.deltaTime,
                StateTimer = _stateTimer,
                CurrentAttackIndex = _currentAttackIndex,
                AttackHitTarget = _attackHitTarget,
                AttackShape = _attackShape,
                DefendBlockCount = 0,
            };
        }

        private void EnterBehaviourForState(MonsterAIState state)
        {
            var ctx = BuildContext();
            if (state == MonsterAIState.Defend) _defend?.Enter(ctx);
            if (state == MonsterAIState.Taunt) _taunt?.Enter(ctx);
            if (state == MonsterAIState.Alert) _alert?.Enter(ctx);
        }

        private void ExitBehaviourForState(MonsterAIState state)
        {
            var ctx = BuildContext();
            if (state == MonsterAIState.Defend) _defend?.Exit(ctx);
            if (state == MonsterAIState.Taunt) _taunt?.Exit(ctx);
            if (state == MonsterAIState.Alert) _alert?.Exit(ctx);
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
            _target = null;
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

- [ ] **Step 2: Refresh and fix compilation errors**

Run: `mcp__ai-game-developer__assets-refresh`
Expected: May have errors in MonsterStats.TakeDamage or related. Fix as needed.

- [ ] **Step 3: Modify MonsterMovement.cs — add configurable RotationSpeed**

```csharp
// Change LookAt method to use config.RotationSpeed
public void LookAt(Vector3 target)
{
    Vector3 dir = target - _self.position;
    dir.y = 0;
    if (dir.sqrMagnitude > 0.01f)
    {
        _self.rotation = Quaternion.Slerp(
            _self.rotation,
            Quaternion.LookRotation(dir),
            _config.RotationSpeed * Time.deltaTime);
    }
}
```

The MonsterMovement constructor now needs to store `_config`:

```csharp
// Add field:
private readonly MonsterConfig _config;

// Update constructor:
public MonsterMovement(NavMeshAgent agent, Transform self, MonsterConfig config)
{
    _agent = agent;
    _self = self;
    _config = config;
    _agent.speed = config.MoveSpeed;
    _agent.stoppingDistance = config.AttackRange * 0.8f;
}
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Monster/MonsterAI.cs \
        Assets/Scripts/Hotfix/GameSystems/Monster/MonsterMovement.cs
git commit -m "refactor: MonsterAI with IAIBehaviour composition, AttackShape integration"
```

---

### Task 9: Weapon system — interfaces and configs

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IWeapon.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/WeaponConfig.cs`

- [ ] **Step 1: Create IWeapon.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum WeaponType
    {
        Melee = 0,
        Ranged = 1,
    }

    public interface IWeapon
    {
        WeaponType WeaponType { get; }
        bool CanAttack();
        void Attack(Vector3 forward, LayerMask targetMask);
        WeaponConfig Config { get; }
    }
}
```

- [ ] **Step 2: Create WeaponConfig.cs**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    [CreateAssetMenu(menuName = "Game/Weapon/Config")]
    public class WeaponConfig : ScriptableObject
    {
        public string WeaponId;
        public WeaponType WeaponType;
        public AttackShapeConfig AttackShape;
        public AttackEffectConfig[] Effects;
        public float AttackSpeed = 1f;
        public string[] SkillIds;
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IWeapon.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/WeaponConfig.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IWeapon.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/WeaponConfig.cs.meta
git commit -m "feat: add IWeapon interface and WeaponConfig"
```

---

### Task 10: MeleeWeapon and CharacterAttackHandler

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/MeleeWeapon.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs`

- [ ] **Step 1: Create MeleeWeapon.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C
{
    public class MeleeWeapon : MonoBehaviour, IWeapon
    {
        [SerializeField] private WeaponConfig _config;
        [SerializeField] private float _attackCooldownTimer;

        public WeaponConfig Config => _config;
        public WeaponType WeaponType => WeaponType.Melee;

        public bool CanAttack() => _attackCooldownTimer <= 0;

        public void Attack(Vector3 forward, LayerMask targetMask)
        {
            if (_config == null) return;

            var shape = AttackShapeFactory.Create(_config.AttackShape);
            var targets = shape.Resolve(transform.position, forward, targetMask);

            if (_config.Effects == null || _config.Effects.Length == 0) return;

            foreach (var t in targets)
            {
                foreach (var e in _config.Effects)
                {
                    Vector3 dir = (t.Transform.position - transform.position).normalized;
                    t.TakeDamage(e.Damage, dir);
                }
            }

            _attackCooldownTimer = 1f / _config.AttackSpeed;
        }

        private void Update()
        {
            if (_attackCooldownTimer > 0)
                _attackCooldownTimer -= Time.deltaTime;
        }
    }
}
```

- [ ] **Step 2: Create CharacterAttackHandler.cs**

```csharp
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C
{
    public class CharacterAttackHandler : MonoBehaviour
    {
        [SerializeField] private LayerMask _targetMask = -1;
        private IWeapon _currentWeapon;

        private void Start()
        {
            _currentWeapon = GetComponent<IWeapon>();
            if (_currentWeapon == null)
                _currentWeapon = GetComponentInChildren<IWeapon>();
        }

        public void EquipWeapon(IWeapon weapon) => _currentWeapon = weapon;

        public void OnAttackActivated()
        {
            if (_currentWeapon == null || !_currentWeapon.CanAttack()) return;
            _currentWeapon.Attack(transform.forward, _targetMask);
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/MeleeWeapon.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/MeleeWeapon.cs.meta \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/CharacterAttackHandler.cs.meta
git commit -m "feat: add MeleeWeapon and CharacterAttackHandler"
```

---

### Task 11: FSMManager and Sys3CEntry integration

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillConfig.cs`

- [ ] **Step 1: Add OnAttackActivated event to FSMManager**

In `FSMManager.cs`, add the event alongside existing events:

```csharp
// Insert after existing events (around line 29):
public event Action OnAttackActivated;
```

Then in the `RequestNormalAttack()` method (or wherever AttackFSM transitions to attack), fire the event:

```csharp
// In the method that triggers attack, add after state transition:
OnAttackActivated?.Invoke();
```

To find the exact injection point, search for where OnAttackCompleted is fired — `OnAttackActivated` should fire when entering an attack state, not when completing one. Add it in `AttackFSM.TransitionTo(Attack1/Attack2)` or in the FSMManager's attack request methods.

For simplicity, fire it in `FSMManager.RequestNormalAttack()`:

```csharp
public void RequestNormalAttack()
{
    if (_attackFSM.CanRequestAttack())
    {
        _attackFSM.RequestAttack();
        OnAttackActivated?.Invoke();
    }
}
```

- [ ] **Step 2: Modify Sys3CEntry.cs — integrate CharacterAttackHandler**

```csharp
// Add field:
private CharacterAttackHandler _attackHandler;

// In Start(), after existing initialization:
_attackHandler = GetComponent<CharacterAttackHandler>();
if (_attackHandler == null)
    _attackHandler = gameObject.AddComponent<CharacterAttackHandler>();

_fsmManager.OnAttackActivated += () => _attackHandler?.OnAttackActivated();
```

- [ ] **Step 3: Modify SkillConfig.cs — add AttackShape fields**

```csharp
// Add these fields after existing fields:
[Header("Attack")]
public AttackShapeConfig AttackShape;
public AttackEffectConfig[] Effects;

[Header("Execution")]
public ExecutePattern Pattern;
public MoveBehaviour MoveLock;
public TargetingMode Targeting;

[Header("Dash (MoveLock==Dash)")]
public float DashDistance;
public float DashDuration;

[Header("Pulse (Pattern==Pulse)")]
public float PulseInterval;
public float PulseDuration;

// Add enums at bottom of file (outside class, inside namespace):
public enum ExecutePattern { Instant, Pulse, Channel, Combo }
public enum MoveBehaviour { Root, Free, Dash }
public enum TargetingMode { Forward, Self, Target, Ground }
```

- [ ] **Step 4: Refresh Unity and fix any compilation errors**

Run: `mcp__ai-game-developer__assets-refresh`

- [ ] **Step 5: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/FSM/FSMManager.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs \
        Assets/Scripts/Hotfix/GameSystems/Sys3C/Skill/SkillConfig.cs
git commit -m "feat: integrate CharacterAttackHandler into Sys3CEntry, extend SkillConfig"
```

---

### Task 12: Create Animator Controllers

Use Unity MCP tools to create two Animator Controllers.

- [ ] **Step 1: Create Slime.controller**

```bash
# Create controller asset
```

Use `mcp__ai-game-developer__animator-create`:

```
sourcePaths: ["Assets/Monstor/DuoPolyart/Animators/Slime.controller"]
```

Then use `mcp__ai-game-developer__animator-modify` to add parameters and states:

**Parameters to add:**
1. AIState (Int) — default 0
2. Attack (Trigger)
3. AttackIndex (Int) — default 0
4. Hit (Trigger)
5. Death (Trigger)
6. Taunt (Trigger)
7. Speed (Float) — default 0

**Layer to add: "Base"**

**States to add:**
- IdleNormal (motion: IdleNormal_Slime_Anim)
- Walk (BlendTree: Walk_Slime_Anim)
- Run (motion: Run_Slime_Anim)
- IdleBattle (motion: IdleBattle_Slime_Anim)
- Attack01 (motion: Attack01_Slime_Anim)
- Attack02 (motion: Attack02_Slime_Anim)
- GetHit (motion: GetHit_Slime_Anim)
- Die (motion: Die_Slime_Anim)
- Taunt (motion: Taunt_Slime_Anim)

**Transitions:**
- AnyState → Die: Death trigger
- AnyState → GetHit: Hit trigger
- AnyState → Taunt: Taunt trigger
- IdleNormal → IdleBattle: AIState >= 2
- IdleBattle → Attack01/Attack02: Attack trigger, AttackIndex condition
- Attack → IdleBattle: Exit Time

- [ ] **Step 2: Create TurtleShell.controller**

Same as Slime but:
- No Taunt parameter
- Add IsDefending (Bool)
- Add Defend state (motion: Defend_TurtleShell_Anim)
- Defend → DefendHit transition on Hit trigger

- [ ] **Step 3: Commit**

```bash
git add Assets/Monstor/DuoPolyart/Animators/Slime.controller \
        Assets/Monstor/DuoPolyart/Animators/TurtleShell.controller \
        Assets/Monstor/DuoPolyart/Animators/Slime.controller.meta \
        Assets/Monstor/DuoPolyart/Animators/TurtleShell.controller.meta
git commit -m "feat: create Slime and TurtleShell Animator Controllers"
```

---

### Task 13: Prefab assembly

- [ ] **Step 1: Open SlimePBR prefab and add components**

```bash
# Use Unity MCP to add components
```

Add to SlimePBR prefab:
1. **NavMeshAgent** — leave default settings (MonsterMovement sets speed at runtime)
2. **Collider** (CapsuleCollider, isTrigger=true) — for HitZone
3. **HitZone** script (from Combat assembly)
4. **MonsterEntity** script
5. **AttackHitbox** child GameObject:
   - Position at front of Slime (x=0, y=0.5, z=0.8)
   - Add Collider (SphereCollider, radius=0.8, isTrigger=true)
   - Add AttackHitbox script
   - Set Layer to "MonsterAttack"
6. Assign Animator controller to Slime.controller

- [ ] **Step 2: Open TurtleShellPBR prefab and add same components**

Add same components as Slime. AttackHitbox position forward, layer "MonsterAttack".

- [ ] **Step 3: Save and close prefabs**

- [ ] **Step 4: Commit**

```bash
git add Assets/Monstor/DuoPolyart/Prefabs/
git commit -m "feat: assemble monster prefabs with all required components"
```

---

### Task 14: DemoDay scene setup

- [ ] **Step 1: Open DemoDay scene and bake NavMesh**

```bash
# Open scene and bake NavMesh on LowpolyTerrain
```

- [ ] **Step 2: Create MonsterSpawner GameObjects**

Place two MonsterSpawners in scene:
1. **TurtleShellSpawner** — near camp (fixed point), Groups: TurtleShell config, Count=1, SpawnRadius=2
2. **SlimeSpawner** — open area, Groups: Slime config, Count=3, SpawnRadius=8, RespawnDelay=30s

- [ ] **Step 3: Create necessary Layers**

Create layers in Project Settings:
- Monster (layer 8)
- Character (layer 9)
- MonsterAttack (layer 10)
- CharacterAttack (layer 11)

Configure Physics collision matrix so MonsterAttack only collides with Character, and CharacterAttack only collides with Monster.

- [ ] **Step 4: Assign layers to prefabs**

Slime/TurtleShell: root = Monster, AttackHitbox child = MonsterAttack
Character: root = Character

- [ ] **Step 5: Add MeleeWeapon and CharacterAttackHandler to MaleCharacterPBR**

Add `MeleeWeapon` and `CharacterAttackHandler` components to MaleCharacterPBR in the DemoDay scene.

- [ ] **Step 6: Commit**

```bash
git add Assets/SimpleLowPolyNature/Scenes/DemoDay.unity
git commit -m "feat: setup DemoDay scene with NavMesh, MonsterSpawners and character combat"
```

---

### Task 15: Config assets creation

- [ ] **Step 1: Create SlimeConfig.asset**

Create MonsterConfig ScriptableObject at `Assets/Monstor/SlimeConfig.asset`:

```
MonsterId: "slime"
DisplayName: "Slime"
MaxHP: 60
AttackPower: 15
Defense: 5
MoveSpeed: 5
DetectRange: 10
LeaveRange: 15
AttackRange: 2
AttackCooldown: 1.0
PatrolRadius: 5
IdleDuration: 2
AttackAnimCount: 2
AttackWeights: [0.7, 0.3]
AttackAnimSpeed: 1.2
AttackShape: { Type: Cone, Range: 2, Angle: 120 }
AttackEffects: [
  { Damage.BaseDamage: 12, KnockbackForce: 2 },
  { Damage.BaseDamage: 20, KnockbackForce: 5 }
]
EnableTaunt: true
TauntChance: 0.6
TauntDuration: 1.5
EnableDefend: false
AlertRange: 15
ChaseAnimIsRun: true
RotationSpeed: 15
DeathDestroyDelay: 3
```

- [ ] **Step 2: Create TurtleShellConfig.asset**

Create MonsterConfig at `Assets/Monstor/TurtleShellConfig.asset`:

```
MonsterId: "turtleshell"
DisplayName: "TurtleShell"
MaxHP: 150
AttackPower: 25
Defense: 20
MoveSpeed: 2
DetectRange: 8
LeaveRange: 12
AttackRange: 2
AttackCooldown: 2.0
PatrolRadius: 3
IdleDuration: 3
AttackAnimCount: 2
AttackWeights: [0.5, 0.5]
AttackAnimSpeed: 1.0
AttackShape: { Type: Circle, Range: 2 }
AttackEffects: [
  { Damage.BaseDamage: 20, KnockbackForce: 3 },
  { Damage.BaseDamage: 35, KnockbackForce: 8 }
]
EnableTaunt: false
EnableDefend: true
DefendHPThreshold: 0.5
DefendChaseTimeThreshold: 3
DefendDuration: 2
DefendDamageReduction: 0.8
DefendAngle: 180
DefendBlockCountToCounter: 2
DefendCounterDamageMultiplier: 1.5
DefendCooldown: 8
AlertRange: 12
ChaseAnimIsRun: false
RotationSpeed: 5
DeathDestroyDelay: 3
```

- [ ] **Step 3: Create SwordShieldConfig.asset**

Create WeaponConfig at `Assets/Monstor/SwordShieldConfig.asset`:

```
WeaponId: "sword_shield"
WeaponType: Melee
AttackShape: { Type: Cone, Range: 2, Angle: 120 }
Effects: [
  { Damage.BaseDamage: 15, Damage.AttackRatio: 1.2, KnockbackForce: 2 }
]
AttackSpeed: 1.0
SkillIds: ["skill_q", "skill_r"]
```

- [ ] **Step 4: Update DemoDay spawners to reference correct configs**

Assign SlimeConfig to SlimeSpawner, TurtleShellConfig to TurtleShellSpawner, SwordShieldConfig to MeleeWeapon on character.

- [ ] **Step 5: Commit**

```bash
git add Assets/Monstor/SlimeConfig.asset \
        Assets/Monstor/TurtleShellConfig.asset \
        Assets/Monstor/SwordShieldConfig.asset \
        Assets/Monstor/SlimeConfig.asset.meta \
        Assets/Monstor/TurtleShellConfig.asset.meta \
        Assets/Monstor/SwordShieldConfig.asset.meta
git commit -m "feat: create SlimeConfig, TurtleShellConfig and SwordShieldConfig assets"
```

---

### Task 16: End-to-end verification

- [ ] **Step 1: Enter Play Mode and verify**

1. Slime spawns, patrols, chases player when in range
2. TurtleShell spawns, defends at low HP
3. Press J — character attacks hit Slime (HP decreases)
4. Slime attacks hit character (HP decreases)
5. TurtleShell kills drop loot, respawn after delay
6. Check console for no null reference errors

- [ ] **Step 2: Fix any issues found**

- [ ] **Step 3: Commit any fixes**

```bash
git add -A
git commit -m "fix: end-to-end verification fixes"
```
