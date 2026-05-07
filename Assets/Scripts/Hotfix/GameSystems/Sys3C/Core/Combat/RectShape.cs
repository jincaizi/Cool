using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 矩形 — 用于突刺、直线冲击波等前方矩形区域攻击
    /// </summary>
    public class RectShape : IAttackShape
    {
        private readonly float _range;
        private readonly float _halfWidth;
        private readonly bool _stopAtFirst;
        private readonly IEntityRegistry _registry;
        private readonly EntityType _targetType;

        public RectShape(float range, float width, bool stopAtFirst = false,
            IEntityRegistry registry = null, EntityType targetType = EntityType.Monster)
        {
            _range = range;
            _halfWidth = width * 0.5f;
            _stopAtFirst = stopAtFirst;
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

            float checkRadius = Mathf.Sqrt(_range * _range + _halfWidth * _halfWidth);
            var candidates = _registry?.FindNearby(origin, checkRadius, _targetType);

            if (candidates != null)
            {
                foreach (var target in candidates)
                {
                    Vector3 toTarget = target.Transform.position - origin;
                    float alongForward = Vector3.Dot(forward, toTarget);
                    if (alongForward < 0 || alongForward > _range) continue;

                    Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                    float lateralOffset = Mathf.Abs(Vector3.Dot(right, toTarget));
                    if (lateralOffset > _halfWidth) continue;

                    results.Add(target);
                    if (_stopAtFirst) return;
                }
                return;
            }

            // Fallback
            var buffer = PhysicsRegistry.SharedBuffer;
            int count = Physics.OverlapSphereNonAlloc(origin, checkRadius, buffer, targetMask);
            Vector3 rightDir = Vector3.Cross(Vector3.up, forward).normalized;
            for (int i = 0; i < count; i++)
            {
                var col = buffer[i];
                Vector3 toTarget = col.bounds.center - origin;
                float alongForward = Vector3.Dot(forward, toTarget);
                if (alongForward < 0 || alongForward > _range) continue;
                float lateralOffset = Mathf.Abs(Vector3.Dot(rightDir, toTarget));
                if (lateralOffset > _halfWidth) continue;
                var target = col.GetComponentInParent<IDamageable>();
                if (target != null && target.IsAlive)
                {
                    results.Add(target);
                    if (_stopAtFirst) return;
                }
            }
        }
    }
}
