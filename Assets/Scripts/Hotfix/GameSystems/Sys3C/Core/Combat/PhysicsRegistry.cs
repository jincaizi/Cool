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
        private static readonly int _maskCharacter = LayerMask.GetMask("Character");
        private static readonly int _maskMonster = LayerMask.GetMask("Monster");

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

            int mask = type == EntityType.Player ? _maskCharacter : _maskMonster;

            // Primary: Physics (PhysX spatial acceleration)
            int count = Physics.OverlapSphereNonAlloc(center, radius, _buffer, mask);
            if (count >= _buffer.Length)
                Debug.LogWarning($"[PhysicsRegistry] OverlapSphere buffer overflow ({count} hits, buffer size {_buffer.Length})");
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
