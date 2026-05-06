using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public class ConeShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _angle;
        private static readonly Collider[] _buffer = new Collider[32];

        public ConeShape(float range, float angle)
        {
            _range = range;
            _angle = angle;
        }

        public IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask)
        {
            var results = new List<IDamageable>();
            int count = Physics.OverlapSphereNonAlloc(origin, _range, _buffer, targetMask);

            for (int i = 0; i < count; i++)
            {
                var col = _buffer[i];
                Vector3 dir = col.bounds.center - origin;
                float dist = dir.magnitude;
                if (dist > _range) continue;

                float halfAngle = _angle * 0.5f;
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
