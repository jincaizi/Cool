using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    /// <summary>
    /// 普攻技能数据 - 包含连段系统配置
    /// </summary>
    [CreateAssetMenu(fileName = "BasicAttack", menuName = "Game/Skills/Basic Attack")]
    public class BasicAttackData : SkillData
    {
        [Header("=== Combo System ===")]
        [SerializeField] private int _comboIndex;            // 第几段普攻
        public int ComboIndex => _comboIndex;

        [SerializeField] private float _comboWindow = 0.5f;         // 连段窗口时间
        public float ComboWindow => _comboWindow;

        [SerializeField] private float _comboResetTime = 3f;        // 连段重置时间
        public float ComboResetTime => _comboResetTime;

        [SerializeField] private BasicAttackData _nextCombo;        // 下一段（null表示最后一击）
        public BasicAttackData NextCombo => _nextCombo;

        [Header("=== Hit Properties ===")]
        [SerializeField] private float _hitStopDuration;             // 命中顿帧
        public float HitStopDuration => _hitStopDuration;

        [SerializeField] private float _impactForce;                // 冲击力度
        public float ImpactForce => _impactForce;

        [SerializeField] private Vector3 _impactDirection;         // 冲击方向偏移
        public Vector3 ImpactDirection => _impactDirection;

        [Header("=== Animation Override ===")]
        [SerializeField] private AnimationClip _overrideClip;      // 覆盖默认动画
        public AnimationClip OverrideClip => _overrideClip;

        [Header("=== Movement ===")]
        [SerializeField] private bool _enableMovement = true;      // 普攻期间允许移动
        public bool EnableMovement => _enableMovement;

        [SerializeField] private float _movementSpeed;              // 攻击时移动速度
        public float MovementSpeed => _movementSpeed;

        [Header("=== Attack Properties ===")]
        [SerializeField] private AttackHitType _hitType = AttackHitType.Slash;
        public AttackHitType HitType => _hitType;

        [SerializeField] private bool _canBeParried = true;        // 可被招架
        public bool CanBeParried => _canBeParried;

        [Header("=== Recovery Cancel ===")]
        [SerializeField] private bool _allowRecoveryCancel = true;  // 允许收招取消
        public bool AllowRecoveryCancel => _allowRecoveryCancel;

        [SerializeField] private float _cancelableWindowStart;      // 可取消窗口开始
        public float CancelableWindowStart => _cancelableWindowStart;

        [SerializeField] private float _cancelableWindowEnd;       // 可取消窗口结束
        public float CancelableWindowEnd => _cancelableWindowEnd;

        private void ValidateComboIndex()
        {
            // 强制设置为普攻类型
            _skillType = Definition.SkillType.BasicAttack;
        }

        /// <summary>
        /// 获取当前攻击段位的动画
        /// </summary>
        public AnimationClip GetAnimationClip()
        {
            return _overrideClip ?? base.GetMainAnimationClip();
        }

        /// <summary>
        /// 检查是否在可取消窗口内
        /// </summary>
        public bool IsInCancelableWindow(float elapsedTime, float totalDuration)
        {
            if (!_allowRecoveryCancel) return false;

            float normalizedTime = elapsedTime / totalDuration;
            return normalizedTime >= _cancelableWindowStart && normalizedTime <= _cancelableWindowEnd;
        }

        /// <summary>
        /// 获取下一段普攻的ID（如果没有返回0）
        /// </summary>
        public int GetNextComboId()
        {
            return _nextCombo?.SkillId ?? 0;
        }
    }

    /// <summary>
    /// 普攻打击类型
    /// </summary>
    public enum AttackHitType
    {
        Slash,     // 劈砍
        Pierce,    // 穿刺
        Blunt,     // 钝击
        Kick       // 踢击
    }
}