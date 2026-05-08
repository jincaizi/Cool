using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "BasicAttack", menuName = "Game/Skills/Basic Attack")]
    public class BasicAttackData : SkillData
    {
        [Header("=== Combo System ===")]
        [Tooltip("Which hit in the combo chain this represents (1 = first hit, 2 = second, etc.)")]
        [SerializeField] private int _comboIndex;
        public int ComboIndex => _comboIndex;

        [Tooltip("Time window in seconds after this hit lands during which the next combo input is accepted")]
        [SerializeField] private float _comboWindow = 0.5f;
        public float ComboWindow => _comboWindow;

        [Tooltip("Time without further input before the combo chain resets to the first hit")]
        [SerializeField] private float _comboResetTime = 3f;
        public float ComboResetTime => _comboResetTime;

        [Tooltip("Reference to the next BasicAttackData in the combo chain (null = this is the finisher)")]
        [SerializeField] private BasicAttackData _nextCombo;
        public BasicAttackData NextCombo => _nextCombo;

        [Header("=== Hit Properties ===")]
        [Tooltip("Hit-stop duration in seconds (brief freeze-frame on impact for game feel)")]
        [SerializeField] private float _hitStopDuration;
        public float HitStopDuration => _hitStopDuration;

        [Tooltip("Knockback force applied to the target on hit")]
        [SerializeField] private float _impactForce;
        public float ImpactForce => _impactForce;

        [Tooltip("Knockback direction offset relative to attacker's forward")]
        [SerializeField] private Vector3 _impactDirection;
        public Vector3 ImpactDirection => _impactDirection;

        [Header("=== Animation Override ===")]
        [Tooltip("Optional animation clip to override the base skill's animation. Used by AnimatorOverrideController.")]
        [SerializeField] private AnimationClip _overrideClip;
        public AnimationClip OverrideClip => _overrideClip;

        [Header("=== Movement ===")]
        [Tooltip("Can the character move during this attack?")]
        [SerializeField] private bool _enableMovement = true;
        public bool EnableMovement => _enableMovement;

        [Tooltip("Movement speed multiplier during this attack (1 = normal speed)")]
        [SerializeField] private float _movementSpeed;
        public float MovementSpeed => _movementSpeed;

        [Header("=== Attack Properties ===")]
        [Tooltip("Hit type for damage calculation and visual feedback (slash/pierce/blunt/kick)")]
        [SerializeField] private AttackHitType _hitType = AttackHitType.Slash;
        public AttackHitType HitType => _hitType;

        [Tooltip("Can this attack be parried by the target?")]
        [SerializeField] private bool _canBeParried = true;
        public bool CanBeParried => _canBeParried;

        [Header("=== Recovery Cancel ===")]
        [Tooltip("Can this attack's recovery frames be cancelled into the next action?")]
        [SerializeField] private bool _allowRecoveryCancel = true;
        public bool AllowRecoveryCancel => _allowRecoveryCancel;

        [Tooltip("Normalized time (0-1) when the cancel window opens during recovery")]
        [SerializeField] private float _cancelableWindowStart;
        public float CancelableWindowStart => _cancelableWindowStart;

        [Tooltip("Normalized time (0-1) when the cancel window closes during recovery")]
        [SerializeField] private float _cancelableWindowEnd;
        public float CancelableWindowEnd => _cancelableWindowEnd;

        private void ValidateComboIndex()
        {
            _skillType = Definition.SkillType.BasicAttack;
        }

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
    }

    public enum AttackHitType
    {
        [Tooltip("Slashing / cutting damage")]
        Slash,
        [Tooltip("Piercing / thrusting damage")]
        Pierce,
        [Tooltip("Blunt / crushing damage")]
        Blunt,
        [Tooltip("Unarmed kick damage")]
        Kick
    }
}
