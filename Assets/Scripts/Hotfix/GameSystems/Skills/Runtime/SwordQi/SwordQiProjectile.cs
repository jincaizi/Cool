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
        private SwordQiElementTint _tint;

        /// <summary>本次飞行的元素（0 = 无元素/默认外观）。</summary>
        public int ElementId { get; private set; }

        public void OnSpawn(SwordQiConfig config, DamageBlock damage, Vector3 origin, Vector3 direction,
                            ComponentPool<SwordQiProjectile> pool, float startDistance = 0f,
                            int elementId = 0)
        {
            _config = config;
            _damage = damage;
            _direction = direction;
            _pool = pool;
            _released = false;
            _hitIds.Clear();
            _spawnPosition = origin;
            _stopDistance = config.Range;
            _traveled = Mathf.Clamp(startDistance, 0f, config.Range);
            ElementId = elementId;
            if (_tint == null)
            {
                // 池化实例只解析一次（MonoBehaviour 允许 GetComponent；本类是组件，不是普通类）
                _tint = new SwordQiElementTint(
                    GetComponentsInChildren<TrailRenderer>(true),
                    GetComponentsInChildren<ParticleSystem>(true),
                    GetComponentsInChildren<Renderer>(true));
            }
            _tint.Apply(elementId);
            Vector3 flatDir = direction;
            flatDir.y = 0f;
            flatDir = flatDir.sqrMagnitude > 0.0001f ? flatDir.normalized : direction;
            transform.position = origin + flatDir * _traveled;
            transform.rotation = Quaternion.LookRotation(flatDir);
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

    /// <summary>元素配色：0 = 白（默认），1 冰蓝 / 2 火橙 / 3 雷紫 / 4 风青。</summary>
    public static class ElementPalette
    {
        public static readonly Color Neutral = Color.white;
        public static readonly Color Ice = new Color(0.55f, 0.75f, 1f);
        public static readonly Color Fire = new Color(1f, 0.35f, 0.1f);
        public static readonly Color Thunder = new Color(0.7f, 0.4f, 1f);
        public static readonly Color Wind = new Color(0.3f, 1f, 0.9f);

        public static Color ColorOf(int elementId) => elementId switch
        {
            1 => Ice,
            2 => Fire,
            3 => Thunder,
            4 => Wind,
            _ => Neutral
        };
    }

    /// <summary>
    /// 剑气元素染色：拖尾/粒子的 startColor 与渲染器材质 `_Color`（MaterialPropertyBlock，
    /// 不污染共享材质）。元素 0 恢复预制体原色——池化实例上一发可能是别的元素。
    /// </summary>
    public sealed class SwordQiElementTint
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly TrailRenderer[] _trails;
        private readonly ParticleSystem[] _particles;
        private readonly Renderer[] _renderers;
        private readonly Color[] _trailColors;
        private readonly ParticleSystem.MinMaxGradient[] _particleColors;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        public SwordQiElementTint(TrailRenderer[] trails, ParticleSystem[] particles, Renderer[] renderers)
        {
            _trails = trails ?? System.Array.Empty<TrailRenderer>();
            _particles = particles ?? System.Array.Empty<ParticleSystem>();
            _renderers = renderers ?? System.Array.Empty<Renderer>();
            _trailColors = new Color[_trails.Length];
            for (int i = 0; i < _trails.Length; i++)
                _trailColors[i] = _trails[i].startColor;
            _particleColors = new ParticleSystem.MinMaxGradient[_particles.Length];
            for (int i = 0; i < _particles.Length; i++)
                _particleColors[i] = _particles[i].main.startColor;
        }

        public void Apply(int elementId)
        {
            bool tinted = elementId is >= 1 and <= 4;
            Color color = ElementPalette.ColorOf(elementId);

            for (int i = 0; i < _trails.Length; i++)
            {
                Color original = _trailColors[i];
                _trails[i].startColor = tinted
                    ? new Color(color.r, color.g, color.b, original.a)
                    : original;
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                var main = _particles[i].main;
                main.startColor = tinted
                    ? new ParticleSystem.MinMaxGradient(color)
                    : _particleColors[i];
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null || renderer.sharedMaterial == null
                    || !renderer.sharedMaterial.HasProperty(ColorId))
                    continue;
                if (!tinted)
                {
                    renderer.SetPropertyBlock(null);
                    continue;
                }
                Color original = renderer.sharedMaterial.GetColor(ColorId);
                _block.SetColor(ColorId, new Color(color.r, color.g, color.b, original.a));
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
