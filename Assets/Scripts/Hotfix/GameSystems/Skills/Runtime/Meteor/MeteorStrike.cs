using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
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
        [SerializeField] private Transform _warningVisual;
        [SerializeField] private Transform _fallingVisual;
        private static readonly Vector3 FallOffset = new Vector3(3.1f, 6.4f, -2.7f);
        private static readonly RaycastHit[] GroundHits = new RaycastHit[16];
        private const int GroundMask = Physics.DefaultRaycastLayers & ~((1 << 9) | (1 << 10));
        private const float SurfaceOffset = 0.06f;
        // 岩体是 1.7m 的屏幕朝向粒子，按半径预留高度。
        private const float BodyClearance = 0.9f;

        private MeteorConfig _config;
        private DamageBlock _damage;
        private Vector3 _impactPoint;
        private float _elapsed;
        private bool _exploded;
        private ComponentPool<MeteorStrike> _pool;
        private readonly System.Collections.Generic.HashSet<int> _hitIds = new();

        /// <summary>爆炸检测中心相对落点的垂直偏移（脚踝高度）</summary>
        private const float AnkleHeightOffset = 1f;

        public void OnSpawn(MeteorConfig config, DamageBlock damage, Vector3 impactPoint,
                            ComponentPool<MeteorStrike> pool)
        {
            _config = config;
            _damage = damage;
            _impactPoint = GroundPoint(impactPoint, out var groundNormal);
            _pool = pool;
            _exploded = false;
            _elapsed = 0f;
            _hitIds.Clear();
            transform.position = _impactPoint;
            if (_warningVisual != null)
            {
                _warningVisual.SetPositionAndRotation(_impactPoint + groundNormal * SurfaceOffset,
                    Quaternion.FromToRotation(Vector3.up, groundNormal) * Quaternion.Euler(90f, 0f, 0f));
                _warningVisual.localScale = Vector3.one * (config.ExplosionRadius * 2.2f);
            }
            if (_fallingVisual != null)
                _fallingVisual.gameObject.SetActive(false);
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
            else
                UpdateFallingVisual();
        }

        private void UpdateFallingVisual()
        {
            if (_fallingVisual == null) return;
            float progress = Mathf.Clamp01((_elapsed / _config.WarnDuration - 0.44f) / 0.56f);
            if (progress <= 0f) return;

            Vector3 end = _impactPoint + Vector3.up * BodyClearance;
            Vector3 position = end + FallOffset * (1f - progress * progress);
            if (TryGetGround(position, out var surface))
                position.y = Mathf.Max(position.y, surface.point.y + BodyClearance / surface.normal.y);
            _fallingVisual.SetPositionAndRotation(position, Quaternion.LookRotation(-FallOffset));
            if (!_fallingVisual.gameObject.activeSelf)
                _fallingVisual.gameObject.SetActive(true);
        }

        internal static Vector3 ResolveAimPoint(Vector3 targetPosition, Transform caster)
        {
            Vector3 requested = targetPosition.sqrMagnitude > 0.0001f
                ? targetPosition
                : caster.position + caster.forward * 12f;
            return GroundPoint(requested, out _);
        }

        private static Vector3 GroundPoint(Vector3 requested, out Vector3 normal)
        {
            if (TryGetGround(requested, out var ground))
            {
                normal = ground.normal;
                return ground.point;
            }
            normal = Vector3.up;
            return requested;
        }

        private static bool TryGetGround(Vector3 point, out RaycastHit ground)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 4f, Vector3.down, GroundHits,
                24f, GroundMask, QueryTriggerInteraction.Ignore);
            ground = default;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = GroundHits[i];
                if (hit.normal.y < 0.45f || hit.collider is CharacterController
                    || hit.collider.GetComponentInParent<ITargetable>() != null)
                    continue;
                float distance = Mathf.Abs(hit.point.y - point.y);
                if (distance >= closest) continue;
                closest = distance;
                ground = hit;
            }
            return ground.collider != null;
        }

        private void Explode()
        {
            if (_exploded) return;

            // 联网：伤害权在服务端——只跳过伤害循环，ImpactVFX 与回池照旧
            bool online = NetworkModeService.Load() == NetworkMode.Online;
            if (!online)
            {
                // 爆炸检测中心：落点 +1m（脚踝高度），否则站地目标打不到
                Vector3 center = _impactPoint + Vector3.up * AnkleHeightOffset;
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
            }

            if (_config.ImpactVFX != null)
            {
                var vfx = Object.Instantiate(_config.ImpactVFX,
                    _impactPoint + Vector3.up * SurfaceOffset, Quaternion.identity);
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
