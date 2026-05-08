using UnityEngine;
using Definition = Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Skills.Data
{
    public abstract class SkillData : ScriptableObject
    {
        [Header("=== Identity ===")]
        [Tooltip("唯一数字ID，映射到SkillID枚举值")]
        [SerializeField] protected int _skillId;
        public int SkillId => _skillId;

        [Tooltip("UI和调试日志中显示的名称")]
        [SerializeField] protected string _skillName;
        public string SkillName => _skillName;

        [Tooltip("风格文本/机制描述（可选）")]
        [SerializeField, TextArea(2, 5)] protected string _description;
        public string Description => _description;

        [Tooltip("技能栏/HUD上显示的图标")]
        [SerializeField] protected Sprite _icon;
        public Sprite Icon => _icon;

        [Tooltip("技能类别")]
        [SerializeField] protected Definition.SkillType _skillType = Definition.SkillType.Special;
        public Definition.SkillType SkillType => _skillType;

        [Tooltip("稀有度等级")]
        [SerializeField] protected Definition.SkillQuality _quality = Definition.SkillQuality.Common;
        public Definition.SkillQuality Quality => _quality;

        [Header("=== Cost ===")]
        [Tooltip("每次施放的法力/能量消耗")]
        [SerializeField] protected int _manaCost;
        public int ManaCost => _manaCost;

        [Tooltip("激活后冷却时间(秒)")]
        [SerializeField] protected float _cooldown;
        public float Cooldown => _cooldown;

        [Tooltip("每次施放的体力消耗")]
        [SerializeField] protected int _staminaCost;
        public int StaminaCost => _staminaCost;

        [Header("=== Animation ===")]
        [Tooltip("Animator Trigger参数名")]
        [SerializeField] protected string _animatorTrigger;
        public string AnimatorTrigger => _animatorTrigger;

        [Tooltip("施法动画片段（持续时间参考）")]
        [SerializeField] protected AnimationClip _castClip;
        public AnimationClip CastClip => _castClip;

        [Tooltip("释放/执行动画片段（持续时间参考）")]
        [SerializeField] protected AnimationClip _releaseClip;
        public AnimationClip ReleaseClip => _releaseClip;

        [Header("=== Dash (Cross-cutting) ===")]
        [Tooltip("冲刺距离(世界单位) (0 = 不冲刺)")]
        [SerializeField] protected float _dashDistance;
        public float DashDistance => _dashDistance;

        [Tooltip("冲刺持续时间(秒)")]
        [SerializeField] protected float _dashDuration;
        public float DashDuration => _dashDuration;

        [Header("=== Interruption ===")]
        [Tooltip("受伤害是否可以中断此技能?")]
        [SerializeField] protected bool _canBeInterruptedByDamage = true;
        public bool CanBeInterruptedByDamage => _canBeInterruptedByDamage;

        [Tooltip("移动输入是否可以中断此技能?")]
        [SerializeField] protected bool _canBeInterruptedByMovement;
        public bool CanBeInterruptedByMovement => _canBeInterruptedByMovement;

        [Tooltip("与其他技能竞争时的优先级")]
        [SerializeField] protected int _interruptionPriority = 50;
        public int InterruptionPriority => _interruptionPriority;

        [Header("=== Cancellation ===")]
        [Tooltip("能否在恢复帧中取消为普通攻击?")]
        [SerializeField] protected bool _canCancelIntoBasicAttack = true;
        public bool CanCancelIntoBasicAttack => _canCancelIntoBasicAttack;

        [Tooltip("能否在恢复帧中取消为另一个技能?")]
        [SerializeField] protected bool _canCancelIntoOtherSkill;
        public bool CanCancelIntoOtherSkill => _canCancelIntoOtherSkill;

        [Header("=== Damage ===")]
        [Tooltip("伤害配置（纯Buff技能可留空）")]
        [SerializeField] protected DamageBlock _damage;
        public DamageBlock Damage => _damage;

        public AnimationClip GetMainAnimationClip()
        {
            return _releaseClip ?? _castClip;
        }
    }
}
