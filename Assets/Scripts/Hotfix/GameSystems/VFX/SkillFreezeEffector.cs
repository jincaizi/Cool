using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.VFX
{
    public class SkillFreezeEffector : MonoBehaviour
    {
        [SerializeField] private int[] _watchSkillIds;
        [SerializeField] private float _freezeDuration = 2f;

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
            if (_watchSkillIds == null || _watchSkillIds.Length == 0)
                return skillId == (int)Skills.Definition.SkillID.SkillR;
            foreach (var id in _watchSkillIds)
                if (id == skillId) return true;
            return false;
        }

        private void OnHitTarget(SkillHitTargetEvent e)
        {
            if (!e.IsFullCharge || !WatchesSkill(e.SkillId)) return;

            var colliders = Physics.OverlapSphere(e.HitPosition, 2f);
            foreach (var col in colliders)
            {
                if (col.TryGetComponent(out IEffectTarget target))
                {
                    var freezeEffect = new StunEffectData
                    {
                        SetDuration = _freezeDuration,
                        SetCanBeCleanse = false
                    };
                    freezeEffect.Apply(null, target);
                    break;
                }
            }
        }
    }
}
