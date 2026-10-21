using DataDefinition;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    /// <summary>本地玩家实际回血时，在身体位置播放绿色粒子。</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHealVFX : MonoBehaviour
    {
        private GameSettings _settings;

        private void Awake()
        {
            _settings = GameSettings.Instance;
        }

        private void OnEnable()
        {
            EventBus.Subscribe<PlayerHealedEvent>(OnHealed);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<PlayerHealedEvent>(OnHealed);
        }

        private void OnHealed(PlayerHealedEvent e)
        {
            if (_settings == null || _settings.HealVfxPrefab == null) return;
            if (e.EntityId != gameObject.GetInstanceID()) return;

            var go = Instantiate(_settings.HealVfxPrefab,
                transform.position + Vector3.up * 1.0f,
                Quaternion.identity, transform);

            float lifetime = 2f;
            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                lifetime = main.duration + main.startLifetime.constantMax + 0.5f;
            }
            Destroy(go, lifetime);
        }
    }
}
