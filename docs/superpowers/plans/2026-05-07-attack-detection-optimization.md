# Attack Detection Optimization & Shape Extension — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Optimize attack detection from O(n) linear scan to Physics-first spatial query, add Sector/Rect shapes for all 4 skills, add debug Gizmo visualization, and upgrade interfaces to eliminate per-call GetComponent overhead.

**Architecture:** IEntityRegistry stores IDamageable directly. PhysicsRegistry uses Physics.OverlapSphereNonAlloc (PhysX acceleration) as primary query, registered set as supplement. Shapes share a static Collider buffer, use HashSet dedup, Dot/sqrMagnitude for math, and support ResolveNonAlloc. New SectorShape handles asymmetric arcs. RectShape implements thrust with StopAtFirst. AttackShapeGizmos provides unified Gizmo drawing.

**Tech Stack:** Unity 2022.3 LTS, C#

---

### Task 1: Upgrade IEntityRegistry + IAttackShape interfaces

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IEntityRegistry.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IAttackShape.cs`

- [ ] **Step 1: Replace IEntityRegistry — Transform → IDamageable**

Replace the entire file:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum EntityType
    {
        Player = 0,
        Monster = 1,
    }

    public interface IEntityRegistry
    {
        void Register(IDamageable entity, EntityType type);
        void Unregister(IDamageable entity);
        IReadOnlyList<IDamageable> FindNearby(Vector3 center, float radius, EntityType type);
    }
}
```

- [ ] **Step 2: Add ResolveNonAlloc to IAttackShape**

Replace the entire file:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface IAttackShape
    {
        IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask);

        void ResolveNonAlloc(
            Vector3 origin, Vector3 forward, LayerMask targetMask,
            List<IDamageable> results);
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IEntityRegistry.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/IAttackShape.cs
git commit -m "refactor: upgrade interfaces — IEntityRegistry uses IDamageable, IAttackShape adds ResolveNonAlloc"
```

---

### Task 2: PhysicsRegistry — Physics-first + IDamageable storage

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/PhysicsRegistry.cs`

- [ ] **Step 1: Rewrite the entire file**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class PhysicsRegistry : IEntityRegistry
    {
        private static PhysicsRegistry _instance;
        public static PhysicsRegistry Instance => _instance ??= new PhysicsRegistry();

        private readonly Dictionary<EntityType, HashSet<IDamageable>> _entities = new()
        {
            { EntityType.Player, new HashSet<IDamageable>() },
            { EntityType.Monster, new HashSet<IDamageable>() },
        };

        private static readonly Collider[] _buffer = new Collider[64];

        public static Collider[] SharedBuffer => _buffer;

        public void Register(IDamageable entity, EntityType type)
        {
            _entities[type].Add(entity);
        }

        public void Unregister(IDamageable entity)
        {
            foreach (var set in _entities.Values)
                set.Remove(entity);
        }

        public IReadOnlyList<IDamageable> FindNearby(Vector3 center, float radius, EntityType type)
        {
            var results = new List<IDamageable>();
            var dedup = new HashSet<IDamageable>();

            int mask = type == EntityType.Player
                ? LayerMask.GetMask("Character")
                : LayerMask.GetMask("Monster");

            // Primary: Physics (PhysX spatial acceleration)
            int count = Physics.OverlapSphereNonAlloc(center, radius, _buffer, mask);
            for (int i = 0; i < count; i++)
            {
                var target = _buffer[i].GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive && dedup.Add(target))
                    results.Add(target);
            }

            // Supplement: registered entities without colliders
            if (_entities.TryGetValue(type, out var set))
            {
                float r2 = radius * radius;
                foreach (var entity in set)
                {
                    if (entity == null || !entity.IsAlive || dedup.Contains(entity)) continue;
                    if ((center - entity.Transform.position).sqrMagnitude <= r2)
                    {
                        dedup.Add(entity);
                        results.Add(entity);
                    }
                }
            }

            return results;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/PhysicsRegistry.cs
git commit -m "perf: Physics-first query + IDamageable storage + sqrMagnitude + shared buffer"
```

---

### Task 3: AttackShapeConfig — add Sector to enum, AngleStart/AngleEnd fields

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeConfig.cs`

- [ ] **Step 1: Replace the file**

```csharp
using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum ShapeType
    {
        Cone = 0,
        Circle = 1,
        Rect = 2,
        Sector = 3,
    }

    [Serializable]
    public class AttackShapeConfig
    {
        [Header("Shape")]
        [Tooltip("攻击形状类型")]
        public ShapeType Type;

        [Header("Dimensions")]
        [Tooltip("攻击范围（距离/半径）")]
        public float Range = 2f;

        [Tooltip("锥形角度（仅 Cone 有效）")]
        public float Angle = 120f;

        [Tooltip("扇形起始角度，forward=0°，正值=右，负值=左（仅 Sector 有效）")]
        public float AngleStart;

        [Tooltip("扇形终止角度（仅 Sector 有效）")]
        public float AngleEnd = 90f;

        [Tooltip("矩形宽度（仅 Rect 有效）")]
        public float Width = 1f;

        [Header("Collision")]
        [Tooltip("碰到第一个目标后是否停止")]
        public bool StopAtFirst;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeConfig.cs
git commit -m "feat: add Sector shape type + AngleStart/AngleEnd fields to config"
```

---

### Task 4: Optimize ConeShape + CircleShape

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ConeShape.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/CircleShape.cs`

- [ ] **Step 1: Rewrite ConeShape.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class ConeShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _halfAngleCos;
        private readonly IEntityRegistry _registry;
        private readonly EntityType _targetType;

        public ConeShape(float range, float angle, IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            _range = range;
            _halfAngleCos = Mathf.Cos(angle * 0.5f * Mathf.Deg2Rad);
            _registry = registry;
            _targetType = targetType;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            ResolveNonAlloc(origin, forward, targetMask, results);
            return results;
        }

        public void ResolveNonAlloc(
            Vector3 origin, Vector3 forward, LayerMask targetMask,
            List<IDamageable> results)
        {
            results.Clear();
            var dedup = new HashSet<IDamageable>();

            if (_registry != null)
            {
                var candidates = _registry.FindNearby(origin, _range, _targetType);
                foreach (var target in candidates)
                {
                    if (!dedup.Add(target)) continue;
                    Vector3 dir = target.Transform.position - origin;
                    if (Vector3.Dot(forward, dir.normalized) < _halfAngleCos) continue;
                    results.Add(target);
                }
                return;
            }

            // Fallback: direct Physics (no registry)
            var buffer = PhysicsRegistry.SharedBuffer;
            int count = Physics.OverlapSphereNonAlloc(origin, _range, buffer, targetMask);
            for (int i = 0; i < count; i++)
            {
                var col = buffer[i];
                Vector3 dir = col.bounds.center - origin;
                if (Vector3.Dot(forward, dir.normalized) < _halfAngleCos) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive && dedup.Add(target))
                    results.Add(target);
            }
        }
    }
}
```

- [ ] **Step 2: Rewrite CircleShape.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class CircleShape : IAttackShape
    {
        private readonly float _radius;
        private readonly IEntityRegistry _registry;
        private readonly EntityType _targetType;

        public CircleShape(float radius, IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            _radius = radius;
            _registry = registry;
            _targetType = targetType;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            ResolveNonAlloc(origin, forward, targetMask, results);
            return results;
        }

        public void ResolveNonAlloc(
            Vector3 origin, Vector3 forward, LayerMask targetMask,
            List<IDamageable> results)
        {
            results.Clear();

            if (_registry != null)
            {
                var candidates = _registry.FindNearby(origin, _radius, _targetType);
                foreach (var target in candidates)
                    results.Add(target);
                return;
            }

            // Fallback: direct Physics
            var buffer = PhysicsRegistry.SharedBuffer;
            int count = Physics.OverlapSphereNonAlloc(origin, _radius, buffer, targetMask);
            for (int i = 0; i < count; i++)
            {
                var target = buffer[i].GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive)
                    results.Add(target);
            }
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/ConeShape.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/CircleShape.cs
git commit -m "perf: HashSet dedup + Dot angle + sqrMagnitude + shared buffer in shapes"
```

---

### Task 5: Create SectorShape + RectShape

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/SectorShape.cs`
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/RectShape.cs`

- [ ] **Step 1: Create SectorShape.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 非对称扇形 — 用于横切（0°→90°）、斜劈（-30°→0°）等自定义角度弧线攻击
    /// </summary>
    public class SectorShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _angleStart;   // degrees
        private readonly float _angleEnd;     // degrees
        private readonly float _rangeSq;
        private readonly IEntityRegistry _registry;
        private readonly EntityType _targetType;

        public SectorShape(float range, float angleStart, float angleEnd,
            IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            _range = range;
            _rangeSq = range * range;
            _angleStart = angleStart;
            _angleEnd = angleEnd;
            _registry = registry;
            _targetType = targetType;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            ResolveNonAlloc(origin, forward, targetMask, results);
            return results;
        }

        public void ResolveNonAlloc(
            Vector3 origin, Vector3 forward, LayerMask targetMask,
            List<IDamageable> results)
        {
            results.Clear();
            var dedup = new HashSet<IDamageable>();

            if (_registry != null)
            {
                var candidates = _registry.FindNearby(origin, _range, _targetType);
                foreach (var target in candidates)
                {
                    if (!dedup.Add(target)) continue;
                    Vector3 dir = target.Transform.position - origin;
                    float localAngle = Vector3.SignedAngle(forward, dir, Vector3.up);
                    if (localAngle >= _angleStart && localAngle <= _angleEnd)
                        results.Add(target);
                }
                return;
            }

            // Fallback
            var buffer = PhysicsRegistry.SharedBuffer;
            int count = Physics.OverlapSphereNonAlloc(origin, _range, buffer, targetMask);
            for (int i = 0; i < count; i++)
            {
                var col = buffer[i];
                Vector3 dir = col.bounds.center - origin;
                float localAngle = Vector3.SignedAngle(forward, dir, Vector3.up);
                if (localAngle < _angleStart || localAngle > _angleEnd) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive && dedup.Add(target))
                    results.Add(target);
            }
        }
    }
}
```

- [ ] **Step 2: Create RectShape.cs**

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 矩形 — 用于突刺、直线冲击波等前方矩形区域攻击
    /// </summary>
    public class RectShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _halfWidth;
        private readonly bool _stopAtFirst;
        private readonly IEntityRegistry _registry;
        private readonly EntityType _targetType;

        public RectShape(float range, float width, bool stopAtFirst = false,
            IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            _range = range;
            _halfWidth = width * 0.5f;
            _stopAtFirst = stopAtFirst;
            _registry = registry;
            _targetType = targetType;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            ResolveNonAlloc(origin, forward, targetMask, results);
            return results;
        }

        public void ResolveNonAlloc(
            Vector3 origin, Vector3 forward, LayerMask targetMask,
            List<IDamageable> results)
        {
            results.Clear();

            float checkRadius = Mathf.Sqrt(_range * _range + _halfWidth * _halfWidth);
            var candidates = _registry?.FindNearby(origin, checkRadius, _targetType);

            if (candidates != null)
            {
                foreach (var target in candidates)
                {
                    Vector3 toTarget = target.Transform.position - origin;
                    float alongForward = Vector3.Dot(forward, toTarget);
                    if (alongForward < 0 || alongForward > _range) continue;

                    Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                    float lateralOffset = Mathf.Abs(Vector3.Dot(right, toTarget));
                    if (lateralOffset > _halfWidth) continue;

                    results.Add(target);
                    if (_stopAtFirst) return;
                }
                return;
            }

            // Fallback
            var buffer = PhysicsRegistry.SharedBuffer;
            int count = Physics.OverlapSphereNonAlloc(origin, checkRadius, buffer, targetMask);
            Vector3 rightDir = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < count; i++)
            {
                var col = buffer[i];
                Vector3 toTarget = col.bounds.center - origin;
                float alongForward = Vector3.Dot(forward, toTarget);
                if (alongForward < 0 || alongForward > _range) continue;
                float lateralOffset = Mathf.Abs(Vector3.Dot(rightDir, toTarget));
                if (lateralOffset > _halfWidth) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive)
                {
                    results.Add(target);
                    if (_stopAtFirst) return;
                }
            }
        }
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/SectorShape.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/RectShape.cs
git commit -m "feat: add SectorShape (asymmetric arc) + RectShape (thrust with StopAtFirst)"
```

---

### Task 6: Update AttackShapeFactory

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeFactory.cs`

- [ ] **Step 1: Add SectorShape and RectShape branches**

Replace the entire file:

```csharp
namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeFactory
    {
        public static IAttackShape Create(AttackShapeConfig config,
            IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            if (config == null)
                return new ConeShape(2f, 120f, registry, targetType);

            return config.Type switch
            {
                ShapeType.Cone => new ConeShape(config.Range, config.Angle, registry, targetType),
                ShapeType.Circle => new CircleShape(config.Range, registry, targetType),
                ShapeType.Sector => new SectorShape(config.Range, config.AngleStart, config.AngleEnd, registry, targetType),
                ShapeType.Rect => new RectShape(config.Range, config.Width, config.StopAtFirst, registry, targetType),
                _ => new ConeShape(config.Range, config.Angle, registry, targetType),
            };
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeFactory.cs
git commit -m "feat: wire SectorShape + RectShape into AttackShapeFactory"
```

---

### Task 7: Create AttackShapeGizmos — debug visualization

**Files:**
- Create: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeGizmos.cs`

- [ ] **Step 1: Create the file**

```csharp
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public static class AttackShapeGizmos
    {
        public static bool Enabled = true;
        public static Color HitColor = Color.red;
        public static Color MissColor = Color.green;
        private const int ARC_SEGMENTS = 24;

        public static void DrawCone(Vector3 origin, Vector3 forward, float range, float angle)
        {
            if (!Enabled) return;
            float halfAngle = angle * 0.5f;
            Gizmos.color = MissColor;
            DrawArc(origin, forward, range, -halfAngle, halfAngle);
            Vector3 left = Quaternion.Euler(0, -halfAngle, 0) * forward * range;
            Vector3 right = Quaternion.Euler(0, halfAngle, 0) * forward * range;
            Gizmos.DrawLine(origin, origin + left);
            Gizmos.DrawLine(origin, origin + right);
        }

        public static void DrawSector(Vector3 origin, Vector3 forward, float range,
            float angleStart, float angleEnd)
        {
            if (!Enabled) return;
            Gizmos.color = MissColor;
            DrawArc(origin, forward, range, angleStart, angleEnd);
            Vector3 startDir = Quaternion.Euler(0, angleStart, 0) * forward * range;
            Vector3 endDir = Quaternion.Euler(0, angleEnd, 0) * forward * range;
            Gizmos.DrawLine(origin, origin + startDir);
            Gizmos.DrawLine(origin, origin + endDir);
        }

        public static void DrawCircle(Vector3 origin, float radius)
        {
            if (!Enabled) return;
            Gizmos.color = MissColor;
            Vector3 prev = origin + Vector3.forward * radius;
            for (int i = 1; i <= ARC_SEGMENTS; i++)
            {
                float angle = i * 360f / ARC_SEGMENTS * Mathf.Deg2Rad;
                Vector3 next = origin + new Vector3(
                    Mathf.Sin(angle) * radius, 0, Mathf.Cos(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        public static void DrawRect(Vector3 origin, Vector3 forward, float range, float width)
        {
            if (!Enabled) return;
            Gizmos.color = MissColor;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 farCenter = origin + forward * range;
            Vector3 halfW = right * (width * 0.5f);
            Gizmos.DrawLine(origin - halfW, origin + halfW);              // near edge
            Gizmos.DrawLine(farCenter - halfW, farCenter + halfW);       // far edge
            Gizmos.DrawLine(origin - halfW, farCenter - halfW);          // left side
            Gizmos.DrawLine(origin + halfW, farCenter + halfW);          // right side
        }

        public static void DrawHit(Vector3 position)
        {
            if (!Enabled) return;
            Gizmos.color = HitColor;
            Gizmos.DrawWireSphere(position, 0.3f);
        }

        private static void DrawArc(Vector3 origin, Vector3 forward, float radius,
            float startAngle, float endAngle)
        {
            float span = endAngle - startAngle;
            int steps = Mathf.Max(2, Mathf.RoundToInt(ARC_SEGMENTS * (span / 360f)));
            Vector3 prev = origin + Quaternion.Euler(0, startAngle, 0) * forward * radius;
            for (int i = 1; i <= steps; i++)
            {
                float angle = startAngle + (i / (float)steps) * span;
                Vector3 next = origin + Quaternion.Euler(0, angle, 0) * forward * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/Core/Combat/AttackShapeGizmos.cs
git commit -m "feat: add AttackShapeGizmos — debug visualization for all 4 shapes"
```

---

### Task 8: Adapt callers — MeleeWeapon, Sys3CEntry, MonsterEntity

**Files:**
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/MeleeWeapon.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs`
- Modify: `Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs`

- [ ] **Step 1: MeleeWeapon — use ResolveNonAlloc, adapt shapes**

Replace the entire file:

```csharp
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C
{
    public class MeleeWeapon : MonoBehaviour, IWeapon
    {
        [SerializeField] private WeaponConfig _config;
        private float _attackCooldownTimer;
        private readonly List<IDamageable> _hitBuffer = new List<IDamageable>(16);

        public WeaponConfig Config => _config;
        public WeaponType WeaponType => WeaponType.Melee;

        public bool CanAttack() => _attackCooldownTimer <= 0;

        public void Attack(Vector3 forward, LayerMask targetMask)
        {
            if (_config == null) return;

            var shape = AttackShapeFactory.Create(_config.AttackShape, PhysicsRegistry.Instance, EntityType.Monster);
            _hitBuffer.Clear();
            shape.ResolveNonAlloc(transform.position, forward, targetMask, _hitBuffer);

            if (_hitBuffer.Count == 0)
            {
                Debug.Log("[Attack] Miss - no target in range");
                return;
            }

            if (_config.Effects == null || _config.Effects.Length == 0) return;

            foreach (var t in _hitBuffer)
            {
                foreach (var e in _config.Effects)
                {
                    Vector3 dir = (t.Transform.position - transform.position).normalized;
                    t.TakeDamage(e.Damage, dir);
                    Debug.Log($"[Attack] Hit {t.Transform.name} for {e.Damage.BaseDamage} damage");
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

- [ ] **Step 2: Sys3CEntry — register as IDamageable**

Change the registration line (line 39 — `PhysicsRegistry.Instance.Register(transform, EntityType.Player)`):

```csharp
PhysicsRegistry.Instance.Register(this, EntityType.Player);
```

Change the unregistration in OnDestroy (find and replace all `PhysicsRegistry.Instance.Unregister(transform)` with):

```csharp
PhysicsRegistry.Instance.Unregister(this);
```

- [ ] **Step 3: MonsterEntity — register as IDamageable**

Change the registration line (line 45 — `PhysicsRegistry.Instance.Register(transform, EntityType.Monster)`):

```csharp
PhysicsRegistry.Instance.Register(this, EntityType.Monster);
```

Change the unregistration in OnDestroy (find and replace all `PhysicsRegistry.Instance.Unregister(transform)` with):

```csharp
PhysicsRegistry.Instance.Unregister(this);
```

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/Hotfix/GameSystems/Sys3C/MeleeWeapon.cs Assets/Scripts/Hotfix/GameSystems/Sys3C/Sys3CEntry.cs Assets/Scripts/Hotfix/GameSystems/Monster/MonsterEntity.cs
git commit -m "refactor: adapt callers to IDamageable registration + ResolveNonAlloc"
```

---

### Task 9: Verification

- [ ] **Step 1: Refresh Unity assets and check compilation**

```
Use MCP assets-refresh to trigger recompilation
Then use MCP console-get-logs (Error filter, last 2 minutes) to verify zero errors
```

- [ ] **Step 2: Git status — confirm all files committed**

```bash
git log --oneline -10
git status --short
```

Expected: 9 commits total, clean working tree (aside from pre-existing dirty files).
