using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class PhysicsRegistry : IEntityRegistry
    {
        private static PhysicsRegistry _instance;
        public static PhysicsRegistry Instance => _instance ??= new PhysicsRegistry();

        private readonly Dictionary<EntityType, HashSet<Transform>> _entities = new()
        {
            { EntityType.Player, new HashSet<Transform>() },
            { EntityType.Monster, new HashSet<Transform>() },
        };

        private static readonly Collider[] _buffer = new Collider[64];

        public void Register(Transform entity, EntityType type)
        {
            _entities[type].Add(entity);
        }

        public void Unregister(Transform entity)
        {
            foreach (var set in _entities.Values)
                set.Remove(entity);
        }

        public IReadOnlyList<Transform> FindNearby(Vector3 center, float radius, EntityType type)
        {
            var results = new List<Transform>();

            // Primary: check registered entities by distance (no Physics dependency)
            if (!_entities.TryGetValue(type, out var set)) return results;
            if (set != null && set.Count > 0)
            {
                foreach (var t in set)
                {
                    if (t == null) continue;
                    if (Vector3.Distance(center, t.position) <= radius)
                        results.Add(t);
                }
                return results;
            }

            // Fallback: Physics (for unregistered entities)
            int mask = type == EntityType.Player
                ? LayerMask.GetMask("Character")
                : LayerMask.GetMask("Monster");
            int count = Physics.OverlapSphereNonAlloc(center, radius, _buffer, mask);
            for (int i = 0; i < count; i++)
            {
                var t = _buffer[i].transform;
                if (!results.Contains(t))
                    results.Add(t);
            }
            return results;
        }
    }
}
