using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.Projectile
{
    /// <summary>
    /// 火球弹体：抛物线弹道（初速反解使落点精确落在 射程 × 朝向 处地面）+
    /// 命中首个目标即爆 / 落地即爆（爆炸半径内全体结算一次）+ 池化回收。
    /// 生命周期与释放者解耦；目标检测走 PhysicsRegistry（同剑气）。
    /// </summary>
    public class FireballProjectile : MonoBehaviour
    {
        /// <summary>命中检测半径（米）</summary>
        private const float DetectRadius = 0.6f;

        /// <summary>最大飞行时间（秒），超时强制落地爆炸（防弹道异常无限飞行）</summary>
        private const float MaxFlightTime = 6f;

        private ProjectileSkillData _data;
        private DamageBlock _damage;
        private Vector3 _direction;
        private Vector3 _velocity;
        private float _groundY;
        private float _flightTime;
        private bool _released;
        private bool _hasTarget;
        private Vector3 _targetPoint;
        private ComponentPool<FireballProjectile> _pool;

        public bool IsActive => !_released;

        public void OnSpawn(ProjectileSkillData data, DamageBlock damage, Vector3 origin, Vector3 direction,
                            ComponentPool<FireballProjectile> pool, Vector3? targetPoint = null)
        {
            _data = data;
            _damage = damage;
            _direction = direction;
            _pool = pool;
            _released = false;
            _flightTime = 0f;
            _hasTarget = targetPoint.HasValue;
            _targetPoint = targetPoint ?? default;
            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(direction);

            float t;
            if (_hasTarget)
            {
                // 目标点弹道：水平朝向落点，飞行时间 = 水平距离 / 投射物速度；
                // 垂直初速按 "t 秒内到达落点高度" 反解（dy = vy0·t − ½·g·t² → vy0 = dy/t + ½·g·t）
                Vector3 flat = _targetPoint - origin;
                flat.y = 0f;
                float distance = flat.magnitude;
                _direction = distance > 0.0001f ? flat / distance : direction;
                transform.rotation = Quaternion.LookRotation(_direction);
                _groundY = _targetPoint.y;
                t = Mathf.Max(0.1f, distance / Mathf.Max(0.01f, data.ProjectileSpeed));
                float dy = _targetPoint.y - origin.y;
                float vy0 = dy / t + 0.5f * data.ProjectileGravity * t;
                _velocity = _direction * data.ProjectileSpeed + Vector3.up * vy0;
            }
            else
            {
                // 固定射程弹道：水平 = 投射物速度沿（压平）方向；垂直初速按
                // "飞行时间 t 内从发射高度下落到地面"反解
                t = data.ProjectileSpeed > 0.01f
                    ? data.ProjectileRange / data.ProjectileSpeed
                    : 1f;
                _groundY = origin.y - data.SpawnOffset.y;
                float dy = _groundY - origin.y;
                float vy0 = dy / t + 0.5f * data.ProjectileGravity * t;
                _velocity = direction * data.ProjectileSpeed + Vector3.up * vy0;
            }

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

            // 半隐式欧拉积分：先施加重力再积分位移
            _velocity.y -= _data.ProjectileGravity * deltaTime;
            transform.position += _velocity * deltaTime;
            _flightTime += deltaTime;

            // 目标点弹道不撞怪提前爆：落点由玩家指定，途中目标不触发爆炸
            if (!_hasTarget) DetectAndExplode();
            if (_released) return;

            // 落地：到达地面高度即爆。
            // 目标点弹道以"越过落点平面"为到达判据：上坡落点可能在仍在上升时恰好到达，
            // 只靠"下降段 + y ≤ 地面"会多飞约 1m；下降段/短暂飞行保护用于避免出生帧误判落地。
            bool reachedTarget = _hasTarget
                && Vector3.Dot(transform.position - _targetPoint, _direction) >= 0f;
            bool landed = _hasTarget
                ? reachedTarget
                  || (_velocity.y < 0f && _flightTime >= 0.05f && transform.position.y <= _groundY)
                : transform.position.y <= _groundY;

            if (landed)
            {
                // 目标点弹道：爆炸中心取落点本身；越过平面那一帧的当前位置最多超前
                // 一帧位移（30fps 约 0.6m），会把爆炸圆整体推过落点。
                Explode(null, _hasTarget ? _targetPoint : transform.position);
                return;
            }

            // 飞行超时安全网（正常弹道 ~1s 内落地，兜底防无限飞行）
            if (_flightTime > MaxFlightTime)
                Explode(null, transform.position);
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

        /// <summary>远程表现对齐：把视觉移到服务端命中点后引爆；幂等。</summary>
        public void ExplodeAt(Vector3 point)
        {
            if (_released) return;
            transform.position = point;
            if (_data == null) { Release(); return; } // EditMode 测试与降级视觉没有数据源
            Explode(null, point);
        }

        /// <summary>爆炸：直接目标（可为空 = 落地爆炸）必结算一次；爆炸半径 > 0 时波及周围目标</summary>
        private void Explode(IDamageable directTarget, Vector3 hitPos)
        {
            if (_released) return;
            if (directTarget != null)
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

            // 命中/落地爆炸特效：一次性实例化 + 自动销毁（不受 EnableVFX 门控——gameplay 表现同陨石）
            if (_data.Presentation != null && _data.Presentation.ReleaseVFX != null)
            {
                var vfx = Object.Instantiate(_data.Presentation.ReleaseVFX, hitPos, Quaternion.identity);
                Destroy(vfx, 1.5f);
            }
            Release();
        }

        private void ApplyDamage(IDamageable target)
        {
            if (NetworkModeService.Load() == NetworkMode.Online) return;   // 联网：伤害权在服务端

            Vector3 dir = (target.Transform.position - transform.position).normalized;
            if (dir.sqrMagnitude < 0.0001f) dir = _direction;
            target.TakeDamage(_damage, dir);
        }

        /// <summary>落地/命中后回收；幂等</summary>
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
