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
