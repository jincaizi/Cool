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
