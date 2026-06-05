using System.Collections.Generic;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class IceBurstVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private GameObject _iceBurstPrefab;

        private static readonly Queue<GameObject> _pool = new Queue<GameObject>();
        private const int MaxPooled = 4;

        private void OnEnable()
        {
            EventBus.Subscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillHitTargetEvent>(OnHitTarget);
            // Clean up pool
            while (_pool.Count > 0) Destroy(_pool.Dequeue());
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0)
                return skillId == (int)Skills.Definition.SkillID.SkillR;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnHitTarget(SkillHitTargetEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;
            if (_iceBurstPrefab == null) return;

            GameObject instance;
            if (_pool.Count > 0)
            {
                instance = _pool.Dequeue();
                instance.SetActive(true);
            }
            else
            {
                instance = Instantiate(_iceBurstPrefab);
            }

            instance.transform.position = e.HitPosition;
            instance.transform.rotation = Quaternion.identity;

            var ps = instance.GetComponent<ParticleSystem>();
            float lifetime = ps != null
                ? ps.main.duration + ps.main.startLifetime.constantMax
                : 2f;

            StartCoroutine(ReturnToPool(instance, lifetime));
        }

        private System.Collections.IEnumerator ReturnToPool(GameObject instance, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (_pool.Count < MaxPooled)
            {
                instance.SetActive(false);
                _pool.Enqueue(instance);
            }
            else
            {
                Destroy(instance);
            }
        }
    }
}
