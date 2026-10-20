using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime.SwordQi
{
    /// <summary>
    /// 剑气弹体：直线飞行 + 每帧宽判定带检测 + 每目标一次命中。
    /// 生命周期与释放者解耦；飞满射程回池。
    /// </summary>
    public class SwordQiProjectile : MonoBehaviour
    {
        private SwordQiConfig _config;
        private DamageBlock _damage;
        private Vector3 _direction;
        private float _traveled;
        private Vector3 _spawnPosition;
        private float _stopDistance;
        private readonly HashSet<int> _hitIds = new HashSet<int>();
        private bool _released;
        private ComponentPool<SwordQiProjectile> _pool;

        public void OnSpawn(SwordQiConfig config, DamageBlock damage, Vector3 origin, Vector3 direction,
                            ComponentPool<SwordQiProjectile> pool)
        {
            _config = config;
            _damage = damage;
            _direction = direction;
            _pool = pool;
            _released = false;
            _traveled = 0f;
            _hitIds.Clear();
            _spawnPosition = origin;
            _stopDistance = config.Range;
            transform.position = origin;
            transform.rotation = Quaternion.LookRotation(direction);
            gameObject.SetActive(true);
        }

        public bool IsActive => !_released;
        public float StopDistance => _stopDistance;
        public float Traveled => _traveled;

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>每帧步进（公开供 EditMode 测试手动驱动）</summary>
        public void Tick(float deltaTime)
        {
            if (_config == null || _released) return;

            float step = _config.Speed * deltaTime;
            _traveled += step;
            transform.position += _direction * step;

            DetectAndDamage();

            if (_traveled >= _stopDistance)
                Release();
        }

        /// <summary>联机停点：让剑气飞到服务端给定的沿程距离后回池。
        /// 停点在身后时钳到当前已飞距离（下一帧立即释放）。</summary>
        public void StopAtPoint(Vector3 worldPoint)
        {
            if (_released || _config == null) return;
            Vector3 flat = worldPoint - _spawnPosition;
            flat.y = 0f;
            Vector3 dir = _direction;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            float along = Vector3.Dot(flat, dir.normalized);
            _stopDistance = Mathf.Clamp(along, _traveled, _config.Range);
        }

        internal float DistanceAlong(Vector3 worldPoint)
        {
            if (_config == null) return float.MaxValue;
            Vector3 flat = worldPoint - _spawnPosition;
            flat.y = 0f;
            Vector3 dir = _direction;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return float.MaxValue;
            return Vector3.Dot(flat, dir.normalized);
        }

        internal float LateralDistance(Vector3 worldPoint)
        {
            if (_config == null) return float.MaxValue;
            Vector3 flat = worldPoint - _spawnPosition;
            flat.y = 0f;
            Vector3 dir = _direction;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return float.MaxValue;
            return Vector3.Cross(dir.normalized, flat).magnitude;
        }

        private void DetectAndDamage()
        {
            if (NetworkModeService.Load() == NetworkMode.Online) return;   // 联网：伤害权在服务端

            // 固定目标类型：剑气为玩家攻击侧技能，只命中怪物
            var targets = PhysicsRegistry.Instance.FindNearby(transform.position,
                                                              _config.DetectRadius,
                                                              EntityType.Monster);
            foreach (var target in targets)
            {
                int id = target.Transform.GetInstanceID();
                if (!_hitIds.Add(id)) continue;

                Vector3 hitDir = (target.Transform.position - transform.position).normalized;
                target.TakeDamage(_damage, hitDir);
            }
        }

        /// <summary>飞满射程/手动回收；幂等</summary>
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
