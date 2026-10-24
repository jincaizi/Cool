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

        private static bool _warnedMissingManager;

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

            if (StatusEffectManager.Instance == null)
            {
                if (!_warnedMissingManager)
                {
                    _warnedMissingManager = true;
                    Debug.LogError("[SkillFreezeEffector] StatusEffectManager 未挂载，冰冻已跳过");
                }
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(e.HitPosition, 2f, _buffer);
            for (int i = 0; i < count; i++)
            {
                if (_buffer[i].TryGetComponent(out IEffectTarget target))
                {
                    var freezeEffect = new FreezeEffectData
                    {
                        SetDuration = _freezeDuration,
                        SetCanBeCleanse = false
                    };
                    StatusEffectManager.Instance.Apply(target, freezeEffect, null);
                    break;
                }
            }
        }
    }
}
