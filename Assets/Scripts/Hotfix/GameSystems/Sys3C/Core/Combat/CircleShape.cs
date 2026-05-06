using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class CircleShape : IAttackShape
    {
        private readonly float _radius;
        private readonly IEntityRegistry _registry;

        public CircleShape(float radius, IEntityRegistry registry = null)
        {
            _radius = radius;
            _registry = registry;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();

            if (_registry != null)
            {
                foreach (var et in new[] { EntityType.Player, EntityType.Monster })
                {
                    var entities = _registry.FindNearby(origin, _radius, et);
                    foreach (var entity in entities)
                    {
                        var target = entity.GetComponentInParent<IDamageable>();
                        if (target == null || !target.IsAlive) continue;
                        if (results.Contains(target)) continue;
                        results.Add(target);
                    }
                }
                return results;
            }

            var buffer = new Collider[32];
            int count = Physics.OverlapSphereNonAlloc(origin, _radius, buffer, targetMask);
            for (int i = 0; i < count; i++)
            {
                var target = buffer[i].GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive) continue;
                if (results.Contains(target)) continue;
                results.Add(target);
            }
            return results;
        }
    }
}
