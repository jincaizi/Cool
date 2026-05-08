using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;
using Definition = Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Skills.Data
{
    /// <summary>
    /// 技能数据基类 - ScriptableObject定义所有技能的基础配置
    /// </summary>
    [CreateAssetMenu(fileName = "New Skill", menuName = "Game/Skills/Skill Data")]
    public class SkillData : ScriptableObject
    {
        [Header("=== Basic Info ===")]
        [SerializeField] protected int _skillId;
        public int SkillId => _skillId;

        [SerializeField] protected string _skillName;
        public string SkillName => _skillName;

        [SerializeField, TextArea(2, 5)] protected string _description;
        public string Description => _description;

        [SerializeField] protected Sprite _icon;
        public Sprite Icon => _icon;

        [Header("=== Classification ===")]
        [SerializeField] protected Definition.SkillType _skillType = Definition.SkillType.Special;
        public Definition.SkillType SkillType => _skillType;

        [SerializeField] protected Definition.SkillQuality _quality = Definition.SkillQuality.Common;
        public Definition.SkillQuality Quality => _quality;

        [Header("=== Cost & Cooldown ===")]
        [SerializeField] protected int _manaCost;
        public int ManaCost => _manaCost;

        [SerializeField] protected float _cooldown;
        public float Cooldown => _cooldown;

        [SerializeField] protected int _staminaCost;
        public int StaminaCost => _staminaCost;

        [Header("=== Release Behavior ===")]
        [SerializeField] protected Definition.ReleaseType _releaseType = Definition.ReleaseType.Instant;
        public Definition.ReleaseType ReleaseType => _releaseType;

        [SerializeField] protected float _castTime;           // 读条时间
        public float CastTime => _castTime;

        [SerializeField] protected float _channelDuration;    // 引导持续时间
        public float ChannelDuration => _channelDuration;

        [SerializeField] protected float _minChargeTime = 0.3f;      // 最小蓄力时间
        public float MinChargeTime => _minChargeTime;

        [SerializeField] protected float _maxChargeTime = 2f;      // 最大蓄力时间
        public float MaxChargeTime => _maxChargeTime;

        [Header("=== Movement ===")]
        [SerializeField] protected bool _canMoveWhileCasting = true;
        public bool CanMoveWhileCasting => _canMoveWhileCasting;

        [SerializeField] protected bool _canMoveWhileChanneling = true;
        public bool CanMoveWhileChanneling => _canMoveWhileChanneling;

        [SerializeField] protected bool _canRotateWhileCasting = true;
        public bool CanRotateWhileCasting => _canRotateWhileCasting;

        [Header("=== Animation ===")]
        [SerializeField] protected string _animatorTrigger;
        public string AnimatorTrigger => _animatorTrigger;

        [SerializeField] protected AnimationClip _castClip;   // 读条动画
        public AnimationClip CastClip => _castClip;

        [SerializeField] protected AnimationClip _releaseClip; // 释放动画
        public AnimationClip ReleaseClip => _releaseClip;

        [SerializeField] protected AnimationClip _channelClip; // 引导动画
        public AnimationClip ChannelClip => _channelClip;

        [Header("=== Combat ===")]
        [SerializeField] protected float _range = 3f;
        public float Range => _range;

        [SerializeField] protected float _angle = 360f;       // 扇形角度
        public float Angle => _angle;

        [SerializeField] protected float _areaRadius;         // AOE半径
        public float AreaRadius => _areaRadius;

        [SerializeField] protected LayerMask _targetMask = ~0;
        public LayerMask TargetMask => _targetMask;

        [SerializeField] protected float[] _hitboxTimings = new float[] { 0.3f };     // 判定帧时间点列表
        public float[] HitboxTimings => _hitboxTimings;

        [SerializeField] protected DamageData _damageData;
        public DamageData DamageData => _damageData;

        [Header("=== Effects ===")]
        [SerializeField] protected EffectDataList _applyEffects = new();
        public EffectDataList ApplyEffects => _applyEffects;

        [SerializeField] protected GameObject _castVFX;
        public GameObject CastVFX => _castVFX;

        [SerializeField] protected GameObject _releaseVFX;
        public GameObject ReleaseVFX => _releaseVFX;

        [SerializeField] protected AudioClip _castSFX;
        public AudioClip CastSFX => _castSFX;

        [Header("=== Interruption ===")]
        [SerializeField] protected bool _canBeInterruptedByDamage = true;
        public bool CanBeInterruptedByDamage => _canBeInterruptedByDamage;

        [SerializeField] protected bool _canBeInterruptedByMovement = false;
        public bool CanBeInterruptedByMovement => _canBeInterruptedByMovement;

        [SerializeField] protected int _interruptionPriority = 50;
        public int InterruptionPriority => _interruptionPriority;

        [Header("=== Dash ===")]
        [SerializeField] protected float _dashDistance;
        public float DashDistance => _dashDistance;

        [SerializeField] protected float _dashDuration;
        public float DashDuration => _dashDuration;

        [Header("=== Cancellation ===")]
        [SerializeField] protected bool _canCancelIntoBasicAttack = true;
        public bool CanCancelIntoBasicAttack => _canCancelIntoBasicAttack;

        [SerializeField] protected bool _canCancelIntoOtherSkill = false;
        public bool CanCancelIntoOtherSkill => _canCancelIntoOtherSkill;

        public static SkillData CreateDefault(
            int skillId,
            string name,
            Definition.SkillType type,
            string animTrigger,
            float cooldown = 0f,
            float range = 3f,
            Definition.ReleaseType releaseType = Definition.ReleaseType.Instant,
            float dashDistance = 0f,
            float dashDuration = 0f)
        {
            var data = CreateInstance<SkillData>();
            data._skillId = skillId;
            data._skillName = name;
            data._skillType = type;
            data._animatorTrigger = animTrigger;
            data._cooldown = cooldown;
            data._range = range;
            data._releaseType = releaseType;
            data._castTime = 0f;
            data._dashDistance = dashDistance;
            data._dashDuration = dashDuration;
            data._canCancelIntoBasicAttack = true;
            data._canCancelIntoOtherSkill = false;
            data._canBeInterruptedByDamage = true;
            data._interruptionPriority = type == Definition.SkillType.BasicAttack ? 20 : 50;
            return data;
        }

        /// <summary>
        /// 获取在特定子状态下是否可以移动
        /// </summary>
        public bool CanMoveInState(Definition.SkillSubState subState)
        {
            return subState switch
            {
                Definition.SkillSubState.Casting => _canMoveWhileCasting,
                Definition.SkillSubState.Channeling => _canMoveWhileChanneling,
                _ => true
            };
        }

        /// <summary>
        /// 获取总执行时间（用于动画时长估算）
        /// </summary>
        public float GetTotalDuration()
        {
            return _releaseType switch
            {
                Definition.ReleaseType.Charged => _maxChargeTime,
                Definition.ReleaseType.Channeled => _castTime + _channelDuration,
                Definition.ReleaseType.Timed => _castTime,
                _ => 0f
            };
        }

        /// <summary>
        /// 获取主动画Clip
        /// </summary>
        public AnimationClip GetMainAnimationClip()
        {
            return _releaseClip ?? _castClip;
        }
    }

    /// <summary>
    /// 效果数据列表（Serializable包装器）
    /// </summary>
    [System.Serializable]
    public class EffectDataList
    {
        [SerializeField] private EffectData[] _effects;
        public EffectData[] Effects => _effects;
    }
}