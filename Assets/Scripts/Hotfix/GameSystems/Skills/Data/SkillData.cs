using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;
using Definition = Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "New Skill", menuName = "Game/Skills/Skill Data")]
    public class SkillData : ScriptableObject
    {
        [Header("=== Basic Info ===")]
        // 唯一数字ID，映射到SkillID枚举值 (10001=BasicAttack1, 20001=SkillQ, 20002=SkillR)
        [Tooltip("唯一数字ID，映射到SkillID枚举值 (10001=BasicAttack1, 20001=SkillQ, 20002=SkillR)")]
        [SerializeField] protected int _skillId;
        public int SkillId => _skillId;

        // UI和调试日志中显示的名称
        [Tooltip("UI和调试日志中显示的名称")]
        [SerializeField] protected string _skillName;
        public string SkillName => _skillName;

        // 风格文本/机制描述（可选）
        [Tooltip("风格文本/机制描述（可选）")]
        [SerializeField, TextArea(2, 5)] protected string _description;
        public string Description => _description;

        // 技能栏/HUD上显示的图标
        [Tooltip("技能栏/HUD上显示的图标")]
        [SerializeField] protected Sprite _icon;
        public Sprite Icon => _icon;

        [Header("=== Classification ===")]
        // 技能类别: BasicAttack使用连击逻辑; Special映射到Q/R键; Ultimate用于终极技能
        [Tooltip("技能类别: BasicAttack使用连击逻辑; Special映射到Q/R键; Ultimate用于终极技能")]
        [SerializeField] protected Definition.SkillType _skillType = Definition.SkillType.Special;
        public Definition.SkillType SkillType => _skillType;

        // 稀有度等级，目前用于 Cosmetic / 未来掉落系统
        [Tooltip("稀有度等级，目前用于 Cosmetic / 未来掉落系统")]
        [SerializeField] protected Definition.SkillQuality _quality = Definition.SkillQuality.Common;
        public Definition.SkillQuality Quality => _quality;

        [Header("=== Cost & Cooldown ===")]
        // 每次施放的法力/能量消耗 (0 = 无消耗)
        [Tooltip("每次施放的法力/能量消耗 (0 = 无消耗)")]
        [SerializeField] protected int _manaCost;
        public int ManaCost => _manaCost;

        // 激活后冷却时间(秒) (0 = 无冷却)
        [Tooltip("激活后冷却时间(秒) (0 = 无冷却)")]
        [SerializeField] protected float _cooldown;
        public float Cooldown => _cooldown;

        // 每次施放的体力消耗 (0 = 无消耗)
        [Tooltip("每次施放的体力消耗 (0 = 无消耗)")]
        [SerializeField] protected int _staminaCost;
        public int StaminaCost => _staminaCost;

        [Header("=== Release Behavior ===")]
        // 技能释放方式: Instant(瞬发), Timed(读条), Charged(蓄力), Channeled(引导)
        [Tooltip("技能释放方式: Instant(瞬发), Timed(读条), Charged(蓄力), Channeled(引导)")]
        [SerializeField] protected Definition.ReleaseType _releaseType = Definition.ReleaseType.Instant;
        public Definition.ReleaseType ReleaseType => _releaseType;

        // 技能释放前的引导时间(秒) (0 = 瞬发)。仅用于 Timed/Charged/Channeled。
        [Tooltip("技能释放前的引导时间(秒) (0 = 瞬发)。仅用于 Timed/Charged/Channeled。")]
        [SerializeField] protected float _castTime;
        public float CastTime => _castTime;

        // 引导持续时间(秒)。仅用于 Channeled 释放类型。
        [Tooltip("引导持续时间(秒)。仅用于 Channeled 释放类型。")]
        [SerializeField] protected float _channelDuration;
        public float ChannelDuration => _channelDuration;

        // 技能可以释放的最小蓄力时间。仅用于 Charged 释放类型。
        [Tooltip("技能可以释放的最小蓄力时间。仅用于 Charged 释放类型。")]
        [SerializeField] protected float _minChargeTime = 0.3f;
        public float MinChargeTime => _minChargeTime;

        // 技能自动释放的最大蓄力时间。仅用于 Charged 释放类型。
        [Tooltip("技能自动释放的最大蓄力时间。仅用于 Charged 释放类型。")]
        [SerializeField] protected float _maxChargeTime = 2f;
        public float MaxChargeTime => _maxChargeTime;

        [Header("=== Movement ===")]
        // 施法(读条)阶段是否可以移动?
        [Tooltip("施法(读条)阶段是否可以移动?")]
        [SerializeField] protected bool _canMoveWhileCasting = true;
        public bool CanMoveWhileCasting => _canMoveWhileCasting;

        // 引导阶段是否可以移动?
        [Tooltip("引导阶段是否可以移动?")]
        [SerializeField] protected bool _canMoveWhileChanneling = true;
        public bool CanMoveWhileChanneling => _canMoveWhileChanneling;

        // 施法阶段是否可以旋转?
        [Tooltip("施法阶段是否可以旋转?")]
        [SerializeField] protected bool _canRotateWhileCasting = true;
        public bool CanRotateWhileCasting => _canRotateWhileCasting;

        [Header("=== Animation ===")]
        // Animator Trigger参数名。SetTrigger(this)调用Animator Controller的Attack层中匹配的过渡。必须与控制器中的Trigger参数匹配(如 'Attack', 'SkillQ', 'SkillR')。
        [Tooltip("Animator Trigger参数名。SetTrigger(this)调用Animator Controller的Attack层中匹配的过渡。必须与控制器中的Trigger参数匹配(如 'Attack', 'SkillQ', 'SkillR')。")]
        [SerializeField] protected string _animatorTrigger;
        public string AnimatorTrigger => _animatorTrigger;

        // 施法动画片段。代码不直接播放 — 作为持续时间参考由SkillStateMachine使用(clip.length)。也可用于AnimatorOverrideController替换。
        [Tooltip("施法动画片段。代码不直接播放 — 作为持续时间参考由SkillStateMachine使用(clip.length)。也可用于AnimatorOverrideController替换。")]
        [SerializeField] protected AnimationClip _castClip;
        public AnimationClip CastClip => _castClip;

        // 释放/执行动画片段。代码不直接播放 — 作为持续时间参考。SkillStateMachine使用clip.length来计算Execution阶段的时间。
        [Tooltip("释放/执行动画片段。代码不直接播放 — 作为持续时间参考。SkillStateMachine使用clip.length来计算Execution阶段的时间。")]
        [SerializeField] protected AnimationClip _releaseClip;
        public AnimationClip ReleaseClip => _releaseClip;

        // 引导/循环动画片段。代码不直接播放 — 作为持续时间参考。SkillStateMachine使用clip.length来计算Channeling阶段的时间。
        [Tooltip("引导/循环动画片段。代码不直接播放 — 作为持续时间参考。SkillStateMachine使用clip.length来计算Channeling阶段的时间。")]
        [SerializeField] protected AnimationClip _channelClip;
        public AnimationClip ChannelClip => _channelClip;

        [Header("=== Combat ===")]
        // 锁定/命中目标的最大范围(世界单位)
        [Tooltip("锁定/命中目标的最大范围(世界单位)")]
        [SerializeField] protected float _range = 3f;
        public float Range => _range;

        // 方向性技能的角度(度) (360 = 全圆AOE)
        [Tooltip("方向性技能的角度(度) (360 = 全圆AOE)")]
        [SerializeField] protected float _angle = 360f;
        public float Angle => _angle;

        // AOE半径(世界单位) (0 = 单目标, > 0 = 球体/圆形AOE)
        [Tooltip("AOE半径(世界单位) (0 = 单目标, > 0 = 球体/圆形AOE)")]
        [SerializeField] protected float _areaRadius;
        public float AreaRadius => _areaRadius;

        // 目标检测的物理层遮罩
        [Tooltip("目标检测的物理层遮罩")]
        [SerializeField] protected LayerMask _targetMask = ~0;
        public LayerMask TargetMask => _targetMask;

        // 从技能开始算起的击球框/伤害帧时间(秒)。每个值是一次独立的伤害检测(如 [0.2, 0.5] = 在0.2秒和0.5秒有两次伤害)。
        [Tooltip("从技能开始算起的击球框/伤害帧时间(秒)。每个值是一次独立的伤害检测(如 [0.2, 0.5] = 在0.2秒和0.5秒有两次伤害)。")]
        [SerializeField] protected float[] _hitboxTimings = new float[] { 0.3f };
        public float[] HitboxTimings => _hitboxTimings;

        // 伤害配置: 基础伤害, 缩放, 暴击, 穿透, DOT设置
        [Tooltip("伤害配置: 基础伤害, 缩放, 暴击, 穿透, DOT设置")]
        [SerializeField] protected DamageBlock _damage;
        public DamageBlock Damage => _damage;

        [Header("=== Effects ===")]
        // 命中时施加的状态效果(buff, debuff, stun, knockback等)
        [Tooltip("命中时施加的状态效果(buff, debuff, stun, knockback等)")]
        [SerializeField] protected EffectDataList _applyEffects = new();
        public EffectDataList ApplyEffects => _applyEffects;

        // 施法阶段生成的VFX预制体
        [Tooltip("施法阶段生成的VFX预制体")]
        [SerializeField] protected GameObject _castVFX;
        public GameObject CastVFX => _castVFX;

        // 技能释放/命中时生成的VFX预制体
        [Tooltip("技能释放/命中时生成的VFX预制体")]
        [SerializeField] protected GameObject _releaseVFX;
        public GameObject ReleaseVFX => _releaseVFX;

        // 施法时播放的SFX
        [Tooltip("施法时播放的SFX")]
        [SerializeField] protected AudioClip _castSFX;
        public AudioClip CastSFX => _castSFX;

        [Header("=== Interruption ===")]
        // 受伤害是否可以中断此技能?
        [Tooltip("受伤害是否可以中断此技能?")]
        [SerializeField] protected bool _canBeInterruptedByDamage = true;
        public bool CanBeInterruptedByDamage => _canBeInterruptedByDamage;

        // 移动输入是否可以中断此技能?
        [Tooltip("移动输入是否可以中断此技能?")]
        [SerializeField] protected bool _canBeInterruptedByMovement = false;
        public bool CanBeInterruptedByMovement => _canBeInterruptedByMovement;

        // 与其他技能竞争时的优先级。越高 = 赢得中断争夺。
        [Tooltip("与其他技能竞争时的优先级。越高 = 赢得中断争夺。")]
        [SerializeField] protected int _interruptionPriority = 50;
        public int InterruptionPriority => _interruptionPriority;

        [Header("=== Dash ===")]
        // 冲刺距离(世界单位) (0 = 不冲刺)。进入Execution阶段时角色向前冲刺。
        [Tooltip("冲刺距离(世界单位) (0 = 不冲刺)。进入Execution阶段时角色向前冲刺。")]
        [SerializeField] protected float _dashDistance;
        public float DashDistance => _dashDistance;

        // 冲刺持续时间(秒)。越低 = 冲刺越快。
        [Tooltip("冲刺持续时间(秒)。越低 = 冲刺越快。")]
        [SerializeField] protected float _dashDuration;
        public float DashDuration => _dashDuration;

        [Header("=== Cancellation ===")]
        // 能否在恢复帧中取消为普通攻击?
        [Tooltip("能否在恢复帧中取消为普通攻击?")]
        [SerializeField] protected bool _canCancelIntoBasicAttack = true;
        public bool CanCancelIntoBasicAttack => _canCancelIntoBasicAttack;

        // 能否在恢复帧中取消为另一个技能?
        [Tooltip("能否在恢复帧中取消为另一个技能?")]
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
            float dashDuration = 0f,
            float maxChargeTime = 2f,
            float minChargeTime = 0.3f)
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
            data._maxChargeTime = maxChargeTime;
            data._minChargeTime = minChargeTime;
            data._canCancelIntoBasicAttack = true;
            data._canCancelIntoOtherSkill = false;
            data._canBeInterruptedByDamage = true;
            data._interruptionPriority = type == Definition.SkillType.BasicAttack ? 20 : 50;
            return data;
        }

        public bool CanMoveInState(Definition.SkillSubState subState)
        {
            return subState switch
            {
                Definition.SkillSubState.Casting => _canMoveWhileCasting,
                Definition.SkillSubState.Channeling => _canMoveWhileChanneling,
                _ => true
            };
        }

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

        public AnimationClip GetMainAnimationClip()
        {
            return _releaseClip ?? _castClip;
        }
    }

    [System.Serializable]
    public class EffectDataList
    {
        // 技能命中目标时施加的效果
        [Tooltip("技能命中目标时施加的效果")]
        [SerializeField] private EffectData[] _effects;
        public EffectData[] Effects => _effects;
    }
}