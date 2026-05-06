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

            // Registry path
            if (_registry != null)
            {
                var entityType = ResolveEntityType(targetMask);
                var entities = _registry.FindNearby(origin, _radius, entityType);
                foreach (var entity in entities)
                {
                    var target = entity.GetComponentInParent<IDamageable>();
                    if (target == null || !target.IsAlive) continue;
                    if (results.Contains(target)) continue;
                    results.Add(target);
                }
                return results;
            }

            // Fallback: Physics
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
