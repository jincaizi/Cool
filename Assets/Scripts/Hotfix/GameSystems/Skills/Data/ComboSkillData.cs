using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "ComboAttack", menuName = "Game/Skills/Combo Attack")]
    public class ComboSkillData : SkillData
    {
        [Header("=== Combo ===")]
        [Tooltip("连击链中第几次打击 (1 = 第一击)")]
        [SerializeField] private int _comboIndex;
        public int ComboIndex => _comboIndex;

        [Tooltip("接受下一个连击输入的时间窗口(秒)")]
        [SerializeField] private float _comboWindow = 0.5f;
        public float ComboWindow => _comboWindow;

        [Tooltip("无输入时连击链重置时间")]
        [SerializeField] private float _comboResetTime = 3f;
        public float ComboResetTime => _comboResetTime;

        [Tooltip("连击链中下一个ComboSkillData的引用")]
        [SerializeField] private ComboSkillData _nextCombo;
        public ComboSkillData NextCombo => _nextCombo;

        [Header("=== Movement ===")]
        [Tooltip("此攻击期间角色是否可以移动?")]
        [SerializeField] private bool _enableMovement = true;
        public bool EnableMovement => _enableMovement;

        [Tooltip("此攻击期间的速度倍率 (1 = 正常速度)")]
        [SerializeField] private float _movementSpeed;
        public float MovementSpeed => _movementSpeed;

        [Header("=== Hit FX ===")]
        [Tooltip("打击类型")]
        [SerializeField] private AttackHitType _hitType = AttackHitType.Slash;
        public AttackHitType HitType => _hitType;

        [Tooltip("命中时施加在目标上的击退力")]
        [SerializeField] private float _impactForce;
        public float ImpactForce => _impactForce;

        [Tooltip("相对于攻击者朝向的击退方向偏移")]
        [SerializeField] private Vector3 _impactDirection;
        public Vector3 ImpactDirection => _impactDirection;

        [Tooltip("此攻击是否可以被目标格挡?")]
        [SerializeField] private bool _canBeParried = true;
        public bool CanBeParried => _canBeParried;

        [Header("=== Recovery Cancel ===")]
        [Tooltip("此攻击的恢复帧是否可以取消到下一个动作?")]
        [SerializeField] private bool _allowRecoveryCancel = true;
        public bool AllowRecoveryCancel => _allowRecoveryCancel;

        [Tooltip("取消窗口开始的归一化时间 (0-1)")]
        [SerializeField] private float _cancelableWindowStart;
        public float CancelableWindowStart => _cancelableWindowStart;

        [Tooltip("取消窗口结束的归一化时间 (0-1)")]
        [SerializeField] private float _cancelableWindowEnd;
        public float CancelableWindowEnd => _cancelableWindowEnd;

        [Header("=== Animation Override ===")]
        [Tooltip("可选动画片段，用于覆盖基础技能的动画")]
        [SerializeField] private AnimationClip _overrideClip;
        public AnimationClip OverrideClip => _overrideClip;

        [Header("=== Config Blocks ===")]
        [SerializeField] private ShapeBlock _shape;
        public ShapeBlock Shape => _shape;

        [SerializeField] private PresentationBlock _presentation;
        public PresentationBlock Presentation => _presentation;

        public AnimationClip GetAnimationClip()
        {
            return _overrideClip ?? base.GetMainAnimationClip();
        }

        public bool IsInCancelableWindow(float elapsedTime, float totalDuration)
        {
            if (!_allowRecoveryCancel) return false;
            float normalizedTime = elapsedTime / totalDuration;
            return normalizedTime >= _cancelableWindowStart && normalizedTime <= _cancelableWindowEnd;
        }

        public int GetNextComboId()
        {
            return _nextCombo?.SkillId ?? 0;
        }

        private void OnValidate()
        {
            _skillType = Definition.SkillType.BasicAttack;
        }
    }

    public enum AttackHitType
    {
        [Tooltip("斩击/切割伤害")]
        Slash,
        [Tooltip("刺穿/穿刺伤害")]
        Pierce,
        [Tooltip("钝器/撞击伤害")]
        Blunt,
        [Tooltip("空手踢伤害")]
        Kick
    }
}
