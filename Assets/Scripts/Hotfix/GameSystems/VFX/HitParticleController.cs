using System.Collections;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Core.Pool;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class HitParticleController : MonoBehaviour
    {
        public static bool EnableVFX = false;

        [SerializeField] private GameObject _normalHitParticles;
        [SerializeField] private GameObject _criticalHitParticles;
        [SerializeField] private GameObject _slashBloodTrailPrefab;
        [SerializeField] private GameObject _hitShockwavePrefab;
        [SerializeField] private GameObject _hitSparkBurstPrefab;
        [SerializeField] private HitFeedbackProfile _profile;

        private static ComponentPool<ParticleSystem> _normalPool;
        private static ComponentPool<ParticleSystem> _criticalPool;
        private static readonly Queue<GameObject> _sparkPool = new Queue<GameObject>();
        private static readonly Queue<GameObject> _shockwavePool = new Queue<GameObject>();
        private const int MaxPooledPerType = 8;

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
            while (_sparkPool.Count > 0) Destroy(_sparkPool.Dequeue());
            while (_shockwavePool.Count > 0) Destroy(_shockwavePool.Dequeue());
        }

        private void OnMonsterDamaged(MonsterTakeDamageEvent e)
        {
            if (!EnableVFX) return;

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
            var pool = prefab == _hitSparkBurstPrefab ? _sparkPool : _shockwavePool;

            GameObject go;
            if (pool.Count > 0)
            {
                go = pool.Dequeue();
                go.SetActive(true);
            }
            else
            {
                go = Instantiate(prefab);
            }

            go.transform.position = pos;
            if (dir != Vector3.zero)
                go.transform.forward = dir;

            StartCoroutine(ReturnToPool(go, pool, 1f));
        }

        private System.Collections.IEnumerator ReturnToPool(GameObject go, Queue<GameObject> pool, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (pool.Count < MaxPooledPerType)
            {
                go.SetActive(false);
                pool.Enqueue(go);
            }
            else
            {
                Destroy(go);
            }
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
