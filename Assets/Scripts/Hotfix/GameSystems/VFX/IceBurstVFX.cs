using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class IceBurstVFX : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private GameObject _iceBurstPrefab;

        private void OnEnable()
        {
            EventBus.Subscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private bool WatchesSkill(int skillId)
        {
            if (_watchSkillIds == null || _watchSkillIds.Length == 0) return true;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnHitTarget(SkillHitTargetEvent e)
        {
            if (!WatchesSkill(e.SkillId)) return;

            if (_iceBurstPrefab != null)
            {
                var instance = Instantiate(_iceBurstPrefab, e.HitPosition, Quaternion.identity);
                var ps = instance.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    ps.Play();
                    Destroy(instance, ps.main.duration + ps.main.startLifetime.constantMax);
                }
                else
                {
                    Destroy(instance, 2f);
                }
            }
        }
    }
}
