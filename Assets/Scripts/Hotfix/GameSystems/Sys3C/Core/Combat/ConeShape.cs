using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class ConeShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _angle;
        private readonly IEntityRegistry _registry;
        private readonly EntityType _targetType;

        public ConeShape(float range, float angle, IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            _range = range;
            _angle = angle;
            _registry = registry;
            _targetType = targetType;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            float halfAngle = _angle * 0.5f;

            if (_registry != null)
            {
                var entities = _registry.FindNearby(origin, _range, _targetType);
                foreach (var entity in entities)
                {
                    Vector3 dir = entity.position - origin;
                    float dist = dir.magnitude;
                    if (dist > _range) continue;

                    float angleToTarget = Vector3.Angle(forward, dir.normalized);
                    if (angleToTarget > halfAngle) continue;

                    var target = entity.GetComponentInParent<IDamageable>();
                    if (target == null || !target.IsAlive) continue;
                    if (results.Contains(target)) continue;
                    results.Add(target);
                }
                return results;
            }

            // Fallback: Physics
            var buffer = new Collider[32];
            int count = Physics.OverlapSphereNonAlloc(origin, _range, buffer, targetMask);
            for (int i = 0; i < count; i++)
            {
                var col = buffer[i];
                Vector3 dir = col.bounds.center - origin;
                float dist = dir.magnitude;
                if (dist > _range) continue;

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
