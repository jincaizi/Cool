using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class CircleShape : IAttackShape
    {
        private readonly float _radius;
        private static readonly Collider[] _buffer = new Collider[32];

        public CircleShape(float radius)
        {
            _radius = radius;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            int count = Physics.OverlapSphereNonAlloc(origin, _radius, _buffer, targetMask);

            for (int i = 0; i < count; i++)
            {
                var target = _buffer[i].GetComponentInParent<IDamageable>();
                if (target == null || !target.IsAlive) continue;
                if (results.Contains(target)) continue;
                results.Add(target);
            }
            return results;
        }
    }
}
