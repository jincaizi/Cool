using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class ConeShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _angle;
        private readonly IEntityRegistry _registry;

        public ConeShape(float range, float angle, IEntityRegistry registry = null)
        {
            _range = range;
            _angle = angle;
            _registry = registry;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            float halfAngle = _angle * 0.5f;

            // Registry path
            if (_registry != null)
            {
                var entityType = ResolveEntityType(targetMask);
                var entities = _registry.FindNearby(origin, _range, entityType);
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

        private static EntityType ResolveEntityType(LayerMask mask)
        {
            int charLayer = LayerMask.NameToLayer("Character");
            int monLayer = LayerMask.NameToLayer("Monster");
            if (charLayer >= 0 && (mask.value & (1 << charLayer)) != 0)
                return EntityType.Player;
            if (monLayer >= 0 && (mask.value & (1 << monLayer)) != 0)
                return EntityType.Monster;
            return EntityType.Monster;
        }
    }
}
