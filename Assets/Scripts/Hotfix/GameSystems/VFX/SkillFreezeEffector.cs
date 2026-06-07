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

        // Reuse buffer to avoid per-hit array allocation in OverlapSphere
        private static readonly Collider[] _buffer = new Collider[16];

        private void OnEnable()
        {
            EventBus.Subscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<SkillHitTargetEvent>(OnHitTarget);
        }

        private bool WatchesSkill(int skillId) => SkillWatcher.Matches(_watchSkillIds, skillId);

        private void OnHitTarget(SkillHitTargetEvent e)
        {
            if (!e.IsFullCharge || !WatchesSkill(e.SkillId)) return;

            int count = Physics.OverlapSphereNonAlloc(e.HitPosition, 2f, _buffer);
            for (int i = 0; i < count; i++)
            {
                if (_buffer[i].TryGetComponent(out IEffectTarget target))
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
