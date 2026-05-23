using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class HitParticleController : MonoBehaviour
    {
        [SerializeField] private GameObject _normalHitParticles;
        [SerializeField] private GameObject _criticalHitParticles;

        private static ComponentPool<ParticleSystem> _normalPool;
        private static ComponentPool<ParticleSystem> _criticalPool;
        private bool _warnedMissingPrefab;

        private void OnEnable()
        {
            EventBus.Subscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MonsterTakeDamageEvent>(OnMonsterDamaged);
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            var prefab = e.IsCritical && _criticalHitParticles != null
                ? _criticalHitParticles : _normalHitParticles;

            if (prefab == null)
            {
                if (!_warnedMissingPrefab)
                {
                    Debug.LogWarning($"[HitParticleController] No particle prefab assigned on {name}", this);
                    _warnedMissingPrefab = true;
                }
                return;
            }

            var pool = GetOrCreatePool(prefab, e.IsCritical);
            var ps = pool.Get();
            ps.transform.position = e.HitPosition;
            if (e.HitDirection != Vector3.zero)
                ps.transform.forward = e.HitDirection;
            var pooled = ps.GetComponent<PooledParticle>();
            if (pooled != null) pooled.SetPool(pool);
            ps.Play();
        }

        private ComponentPool<ParticleSystem> GetOrCreatePool(GameObject prefab, bool isCritical)
        {
            if (isCritical)
            {
                if (_criticalPool == null)
                    _criticalPool = new ComponentPool<ParticleSystem>(
                        prefab.GetComponent<ParticleSystem>(), null);
                return _criticalPool;
            }
            if (_normalPool == null)
                _normalPool = new ComponentPool<ParticleSystem>(
                    prefab.GetComponent<ParticleSystem>(), null);
            return _normalPool;
        }
    }
}
