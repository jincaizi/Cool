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
