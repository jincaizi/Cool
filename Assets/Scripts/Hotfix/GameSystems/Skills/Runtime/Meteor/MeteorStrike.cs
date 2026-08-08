using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Meteor
{
    /// <summary>
    /// 陨石：预警（红圈）→ 预警结束落地爆炸（落点 +1m 中心 AOE）→ 池化回收。
    /// Update 时间累加计时（池化对象禁用协程，防回池后泄漏）。
    /// </summary>
    public class MeteorStrike : MonoBehaviour
    {
        private MeteorConfig _config;
        private DamageBlock _damage;
        private Vector3 _impactPoint;
        private float _elapsed;
        private bool _exploded;
        private ComponentPool<MeteorStrike> _pool;
        private readonly System.Collections.Generic.HashSet<int> _hitIds = new();

        public void OnSpawn(MeteorConfig config, DamageBlock damage, Vector3 impactPoint,
                            ComponentPool<MeteorStrike> pool)
        {
            _config = config;
            _damage = damage;
            _impactPoint = impactPoint;
            _pool = pool;
            _exploded = false;
            _elapsed = 0f;
            _hitIds.Clear();
            transform.position = impactPoint;
            gameObject.SetActive(true);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>每帧步进（公开供 EditMode 测试手动驱动）</summary>
        public void Tick(float deltaTime)
        {
            if (_config == null || _exploded) return;

            _elapsed += deltaTime;
            if (_elapsed >= _config.WarnDuration)
                Explode();
        }

        private void Explode()
        {
            if (_exploded) return;

            // 爆炸检测中心：落点 +1m（脚踝高度），否则站地目标打不到
            Vector3 center = _impactPoint + Vector3.up * 1f;
            var targets = PhysicsRegistry.Instance.FindNearby(center, _config.ExplosionRadius, EntityType.Monster);
            foreach (var target in targets)
            {
                if (!target.IsAlive) continue;
                int id = target.Transform.GetInstanceID();
                if (!_hitIds.Add(id)) continue;

                Vector3 dir = (target.Transform.position - center).normalized;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;
                target.TakeDamage(_damage, dir);
            }

            if (_config.ImpactVFX != null)
            {
                var vfx = Object.Instantiate(_config.ImpactVFX, _impactPoint, Quaternion.identity);
                Destroy(vfx, 1.5f);
            }

            Release();
        }

        /// <summary>爆炸后回收；幂等</summary>
        public void Release()
        {
            if (_exploded) return;
            _exploded = true;
            gameObject.SetActive(false);
            _pool?.Return(this);
        }

        private void OnDestroy()
        {
            _exploded = true;
            _pool = null;
        }
    }
}
