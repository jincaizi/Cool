using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ChargedSkill", menuName = "Game/Skills/Charged Skill")]
    public class ChargedSkillData : SkillData
    {
        [Header("=== Charge ===")]
        [Tooltip("按住按钮继续蓄力?")]
        [SerializeField] private bool _holdToCharge = true;
        public bool HoldToCharge => _holdToCharge;

        [Tooltip("松开按钮发射?")]
        [SerializeField] private bool _releaseToFire = true;
        public bool ReleaseToFire => _releaseToFire;

        [Tooltip("技能可以释放的最小蓄力时间")]
        [SerializeField] private float _minChargeTime = 0.3f;
        public float MinChargeTime => _minChargeTime;

        [Tooltip("技能自动释放的最大蓄力时间")]
        [SerializeField] private float _maxChargeTime = 2f;
        public float MaxChargeTime => _maxChargeTime;

        [Tooltip("蓄力时间上的伤害倍率曲线")]
        [SerializeField] private AnimationCurve _chargeDamageCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
        public AnimationCurve ChargeDamageCurve => _chargeDamageCurve;

        [Tooltip("蓄力时间上的AOE半径倍率曲线")]
        [SerializeField] private AnimationCurve _chargeAreaCurve = AnimationCurve.Linear(0f, 1f, 1f, 1.5f);
        public AnimationCurve ChargeAreaCurve => _chargeAreaCurve;

        [Header("=== Movement ===")]
        [Tooltip("蓄力阶段是否可以移动?")]
        [SerializeField] private bool _canMoveWhileCharging = true;
        public bool CanMoveWhileCharging => _canMoveWhileCharging;

        [Tooltip("蓄力阶段是否可以旋转?")]
        [SerializeField] private bool _canRotateWhileCharging = true;
        public bool CanRotateWhileCharging => _canRotateWhileCharging;

        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;

        public float GetDamageScaleForCharge(float chargeProgress)
        {
            if (chargeProgress <= 0) return 1f;
            return _chargeDamageCurve?.Evaluate(chargeProgress) ?? (1f + chargeProgress);
        }

        public float GetAreaScaleForCharge(float chargeProgress)
        {
            if (chargeProgress <= 0) return 1f;
            return _chargeAreaCurve?.Evaluate(chargeProgress) ?? 1f;
        }

        private void OnValidate()
        {
            if (_skillType == Definition.SkillType.BasicAttack)
                _skillType = Definition.SkillType.Special;
        }
    }
}
