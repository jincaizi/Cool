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
        [SerializeField] private GameObject _slashBloodTrailPrefab;
        [SerializeField] private GameObject _hitShockwavePrefab;
        [SerializeField] private GameObject _hitSparkBurstPrefab;
        [SerializeField] private HitFeedbackProfile _profile;

        private static ComponentPool<ParticleSystem> _normalPool;
        private static ComponentPool<ParticleSystem> _criticalPool;
        private bool _warnedMissingPrefab;

        private static ComponentPool<SlashBloodTrail> _trailPool;
        private bool _warnedMissingTrail;

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

            if (prefab != null)
            {
                var pool = GetOrCreatePool(prefab, e.IsCritical);
                var ps = pool.Get();
                ps.transform.position = e.HitPosition;
                if (e.HitDirection != Vector3.zero)
                    ps.transform.forward = e.HitDirection;

                float scale = _profile != null
                    ? (e.IsCritical ? _profile.CritParticleScale
                        : e.SkillId > 0 ? _profile.SkillParticleScale
                        : _profile.NormalParticleScale)
                    : 1f;
                ps.transform.localScale = Vector3.one * scale;

                var pooled = ps.GetComponent<PooledParticle>();
                if (pooled != null) pooled.SetPool(pool);
                ps.Play();
            }
            else if (!_warnedMissingPrefab)
            {
                Debug.LogWarning("[HitParticleController] No particle prefab assigned on " + name, this);
                _warnedMissingPrefab = true;
            }

            if (_hitSparkBurstPrefab != null)
                SpawnAtHit(_hitSparkBurstPrefab, e.HitPosition, e.HitDirection);

            if ((e.IsCritical || e.SkillId > 0) && _hitShockwavePrefab != null)
                SpawnAtHit(_hitShockwavePrefab, e.HitPosition, e.HitDirection);

            if (e.IsCritical && _slashBloodTrailPrefab != null)
            {
                var trailPool = GetOrCreateTrailPool(_slashBloodTrailPrefab);
                var trail = trailPool.Get();
                trail.SetPool(trailPool);
                trail.Activate(e.HitPosition, e.HitDirection);
            }
            else if (e.IsCritical && !_warnedMissingTrail)
            {
                Debug.LogWarning("[HitParticleController] No slash blood trail prefab assigned", this);
                _warnedMissingTrail = true;
            }
        }

        private void SpawnAtHit(GameObject prefab, Vector3 pos, Vector3 dir)
        {
            var go = Instantiate(prefab, pos, Quaternion.identity);
            if (dir != Vector3.zero)
                go.transform.forward = dir;
            Destroy(go, 1f);
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

        private ComponentPool<SlashBloodTrail> GetOrCreateTrailPool(GameObject prefab)
        {
            if (_trailPool == null)
                _trailPool = new ComponentPool<SlashBloodTrail>(
                    prefab.GetComponent<SlashBloodTrail>(), null);
            return _trailPool;
        }
    }
}
