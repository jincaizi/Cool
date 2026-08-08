using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Projectile
{
    /// <summary>
    /// 火球弹体：直线飞行 + 命中首个目标即爆（爆炸半径内全体结算一次）+ 池化回收。
    /// 生命周期与释放者解耦；飞满射程回池。目标检测走 PhysicsRegistry（同剑气）。
    /// </summary>
    public class FireballProjectile : MonoBehaviour
    {
        /// <summary>命中检测半径（米）</summary>
        private const float DetectRadius = 0.6f;

        private ProjectileSkillData _data;
        private DamageBlock _damage;
        private Vector3 _direction;
        private float _traveled;
        private bool _released;
        private ComponentPool<FireballProjectile> _pool;

        public void OnSpawn(ProjectileSkillData data, DamageBlock damage, Vector3 origin, Vector3 direction,
                            ComponentPool<FireballProjectile> pool)
        {
            _data = data;
            _damage = damage;
            _direction = direction;
            _pool = pool;
            _released = false;
            _traveled = 0f;
            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(direction);
            gameObject.SetActive(true);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>每帧步进（公开供 EditMode 测试手动驱动）</summary>
        public void Tick(float deltaTime)
        {
            if (_data == null || _released) return;

            float step = _data.ProjectileSpeed * deltaTime;
            _traveled += step;
            transform.position += _direction * step;

            DetectAndExplode();

            if (_traveled >= _data.ProjectileRange)
                Release();
        }

        private void DetectAndExplode()
        {
            // 固定目标类型：火球为玩家攻击侧技能，只命中怪物
            var targets = PhysicsRegistry.Instance.FindNearby(transform.position, DetectRadius, EntityType.Monster);
            if (targets.Count == 0) return;

            // 命中最近目标，其位置为爆炸中心
            IDamageable nearest = targets[0];
            float minSqr = (nearest.Transform.position - transform.position).sqrMagnitude;
            for (int i = 1; i < targets.Count; i++)
            {
                float sqr = (targets[i].Transform.position - transform.position).sqrMagnitude;
                if (sqr < minSqr)
                {
                    minSqr = sqr;
                    nearest = targets[i];
                }
            }
            Explode(nearest, nearest.Transform.position);
        }

        private void Explode(IDamageable directTarget, Vector3 hitPos)
        {
            // 直接命中目标必结算一次（爆炸半径 0 = 纯单体）
            ApplyDamage(directTarget);

            // 爆炸半径 > 0 时，波及爆炸中心附近的其他目标（同一伤害块）
            if (_data.ExplosionRadius > 0f)
            {
                var splash = PhysicsRegistry.Instance.FindNearby(hitPos, _data.ExplosionRadius, EntityType.Monster);
                foreach (var target in splash)
                {
                    if (target == directTarget) continue; // 直接目标已结算，避免重复
                    if (!target.IsAlive) continue;
                    ApplyDamage(target);
                }
            }
            Release();
        }

        private void ApplyDamage(IDamageable target)
        {
            Vector3 dir = (target.Transform.position - transform.position).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = _direction;
            target.TakeDamage(_damage, dir);
        }

        /// <summary>飞满射程/命中后回收；幂等</summary>
        public void Release()
        {
            if (_released) return;
            _released = true;
            gameObject.SetActive(false);
            _pool?.Return(this);
        }

        private void OnDestroy()
        {
            // 场景销毁时置位，防止后续 Release 回池已销毁实例
            _released = true;
            _pool = null;
        }
    }
}
