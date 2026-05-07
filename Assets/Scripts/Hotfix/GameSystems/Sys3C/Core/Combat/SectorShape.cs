using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 非对称扇形 — 用于横切（0°→90°）、斜劈（-30°→0°）等自定义角度弧线攻击
    /// </summary>
    public class SectorShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _angleStart;
        private readonly float _angleEnd;
        private readonly IEntityRegistry _registry;
        private readonly EntityType _targetType;

        public SectorShape(float range, float angleStart, float angleEnd,
            IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            _range = range;
            _angleStart = angleStart;
            _angleEnd = angleEnd;
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
            var dedup = new HashSet<IDamageable>();

            if (_registry != null)
            {
                var candidates = _registry.FindNearby(origin, _range, _targetType);
                foreach (var target in candidates)
                {
                    if (!dedup.Add(target)) continue;
                    Vector3 dir = target.Transform.position - origin;
                    float localAngle = Vector3.SignedAngle(forward, dir, Vector3.up);
                    if (localAngle >= _angleStart && localAngle <= _angleEnd)
                        results.Add(target);
                }
                return;
            }

            // Fallback
            var buffer = PhysicsRegistry.SharedBuffer;
            int count = Physics.OverlapSphereNonAlloc(origin, _range, buffer, targetMask);
            for (int i = 0; i < count; i++)
            {
                var col = buffer[i];
                Vector3 dir = col.bounds.center - origin;
                float localAngle = Vector3.SignedAngle(forward, dir, Vector3.up);
                if (localAngle < _angleStart || localAngle > _angleEnd) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive && dedup.Add(target))
                    results.Add(target);
            }
        }
    }
}
