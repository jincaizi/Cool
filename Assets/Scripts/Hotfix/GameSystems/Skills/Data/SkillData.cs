using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;
using Definition = Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "New Skill", menuName = "Game/Skills/Skill Data")]
    public class SkillData : ScriptableObject
    {
        [Header("=== Basic Info ===")]
        [Tooltip("Unique numeric ID, maps to SkillID enum values (10001=BasicAttack1, 20001=SkillQ, 20002=SkillR)")]
        [SerializeField] protected int _skillId;
        public int SkillId => _skillId;

        [Tooltip("Display name shown in UI and debug logs")]
        [SerializeField] protected string _skillName;
        public string SkillName => _skillName;

        [Tooltip("Flavor text / mechanical description (optional)")]
        [SerializeField, TextArea(2, 5)] protected string _description;
        public string Description => _description;

        [Tooltip("Icon shown on skill bar / HUD")]
        [SerializeField] protected Sprite _icon;
        public Sprite Icon => _icon;

        [Header("=== Classification ===")]
        [Tooltip("Skill category: BasicAttack uses combo logic; Special maps to Q/R keys; Ultimate for super moves")]
        [SerializeField] protected Definition.SkillType _skillType = Definition.SkillType.Special;
        public Definition.SkillType SkillType => _skillType;

        [Tooltip("Rarity tier, currently cosmetic / future loot system")]
        [SerializeField] protected Definition.SkillQuality _quality = Definition.SkillQuality.Common;
        public Definition.SkillQuality Quality => _quality;

        [Header("=== Cost & Cooldown ===")]
        [Tooltip("Mana / energy cost per cast (0 = no cost)")]
        [SerializeField] protected int _manaCost;
        public int ManaCost => _manaCost;

        [Tooltip("Cooldown in seconds after activation begins (0 = no cooldown)")]
        [SerializeField] protected float _cooldown;
        public float Cooldown => _cooldown;

        [Tooltip("Stamina cost per cast (0 = no cost)")]
        [SerializeField] protected int _staminaCost;
        public int StaminaCost => _staminaCost;

        [Header("=== Release Behavior ===")]
        [Tooltip("How the skill is released: Instant (fire-and-forget), Timed (cast bar), Charged (hold to charge), Channeled (continuous guide)")]
        [SerializeField] protected Definition.ReleaseType _releaseType = Definition.ReleaseType.Instant;
        public Definition.ReleaseType ReleaseType => _releaseType;

        [Tooltip("Cast time in seconds before the skill fires (0 = instant). Only used for Timed/Charged/Channeled.")]
        [SerializeField] protected float _castTime;
        public float CastTime => _castTime;

        [Tooltip("Channel duration in seconds. Only used for Channeled release type.")]
        [SerializeField] protected float _channelDuration;
        public float ChannelDuration => _channelDuration;

        [Tooltip("Minimum hold time before the skill can be released. Only used for Charged release type.")]
        [SerializeField] protected float _minChargeTime = 0.3f;
        public float MinChargeTime => _minChargeTime;

        [Tooltip("Maximum hold time before the skill auto-fires. Only used for Charged release type.")]
        [SerializeField] protected float _maxChargeTime = 2f;
        public float MaxChargeTime => _maxChargeTime;

        [Header("=== Movement ===")]
        [Tooltip("Can the character move during the cast (read bar) phase?")]
        [SerializeField] protected bool _canMoveWhileCasting = true;
        public bool CanMoveWhileCasting => _canMoveWhileCasting;

        [Tooltip("Can the character move during the channel (guide) phase?")]
        [SerializeField] protected bool _canMoveWhileChanneling = true;
        public bool CanMoveWhileChanneling => _canMoveWhileChanneling;

        [Tooltip("Can the character rotate during the cast phase?")]
        [SerializeField] protected bool _canRotateWhileCasting = true;
        public bool CanRotateWhileCasting => _canRotateWhileCasting;

        [Header("=== Animation ===")]
        [Tooltip("Animator Trigger parameter name. SetTrigger(this) calls the matching transition in the Animator Controller's Attack layer. Must match a Trigger parameter in the controller (e.g. 'Attack', 'SkillQ', 'SkillR').")]
        [SerializeField] protected string _animatorTrigger;
        public string AnimatorTrigger => _animatorTrigger;

        [Tooltip("Cast animation clip. NOT played directly by code — used as duration reference by SkillStateMachine (clip.length). Can also be used for AnimatorOverrideController swapping.")]
        [SerializeField] protected AnimationClip _castClip;
        public AnimationClip CastClip => _castClip;

        [Tooltip("Release / execution animation clip. NOT played directly — used as duration reference. SkillStateMachine uses clip.length for the Execution phase timing.")]
        [SerializeField] protected AnimationClip _releaseClip;
        public AnimationClip ReleaseClip => _releaseClip;

        [Tooltip("Channel / looping animation clip. NOT played directly — used as duration reference. SkillStateMachine uses clip.length for the Channeling phase timing.")]
        [SerializeField] protected AnimationClip _channelClip;
        public AnimationClip ChannelClip => _channelClip;

        [Header("=== Combat ===")]
        [Tooltip("Maximum range to acquire / hit a target (world units)")]
        [SerializeField] protected float _range = 3f;
        public float Range => _range;

        [Tooltip("Cone angle in degrees for directional skills (360 = full circle AOE)")]
        [SerializeField] protected float _angle = 360f;
        public float Angle => _angle;

        [Tooltip("AOE radius in world units (0 = single-target, > 0 = sphere / circle AOE)")]
        [SerializeField] protected float _areaRadius;
        public float AreaRadius => _areaRadius;

        [Tooltip("Physics layer mask for target detection")]
        [SerializeField] protected LayerMask _targetMask = ~0;
        public LayerMask TargetMask => _targetMask;

        [Tooltip("Hitbox / damage frame timings in seconds from skill start. Each value is a separate damage tick (e.g. [0.2, 0.5] = two hits at 0.2s and 0.5s).")]
        [SerializeField] protected float[] _hitboxTimings = new float[] { 0.3f };
        public float[] HitboxTimings => _hitboxTimings;

        [Tooltip("Damage configuration: base damage, scaling, crit, penetration, DOT settings")]
        [SerializeField] protected DamageData _damageData;
        public DamageData DamageData => _damageData;

        [Header("=== Effects ===")]
        [Tooltip("Status effects applied on hit (buff, debuff, stun, knockback, etc.)")]
        [SerializeField] protected EffectDataList _applyEffects = new();
        public EffectDataList ApplyEffects => _applyEffects;

        [Tooltip("VFX prefab spawned during cast phase")]
        [SerializeField] protected GameObject _castVFX;
        public GameObject CastVFX => _castVFX;

        [Tooltip("VFX prefab spawned on skill release / hit")]
        [SerializeField] protected GameObject _releaseVFX;
        public GameObject ReleaseVFX => _releaseVFX;

        [Tooltip("SFX played during cast")]
        [SerializeField] protected AudioClip _castSFX;
        public AudioClip CastSFX => _castSFX;

        [Header("=== Interruption ===")]
        [Tooltip("Can taking damage interrupt this skill?")]
        [SerializeField] protected bool _canBeInterruptedByDamage = true;
        public bool CanBeInterruptedByDamage => _canBeInterruptedByDamage;

        [Tooltip("Can movement input interrupt this skill?")]
        [SerializeField] protected bool _canBeInterruptedByMovement = false;
        public bool CanBeInterruptedByMovement => _canBeInterruptedByMovement;

        [Tooltip("Priority when competing with other skills. Higher = wins interruption contest.")]
        [SerializeField] protected int _interruptionPriority = 50;
        public int InterruptionPriority => _interruptionPriority;

        [Header("=== Dash ===")]
        [Tooltip("Dash distance in world units (0 = no dash). Character dashes forward when entering Execution phase.")]
        [SerializeField] protected float _dashDistance;
        public float DashDistance => _dashDistance;

        [Tooltip("Dash duration in seconds. Lower = faster dash.")]
        [SerializeField] protected float _dashDuration;
        public float DashDuration => _dashDuration;

        [Header("=== Cancellation ===")]
        [Tooltip("Can this skill be cancelled into a basic attack during recovery?")]
        [SerializeField] protected bool _canCancelIntoBasicAttack = true;
        public bool CanCancelIntoBasicAttack => _canCancelIntoBasicAttack;

        [Tooltip("Can this skill be cancelled into another skill during recovery?")]
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
        [Tooltip("Effects applied when the skill hits a target")]
        [SerializeField] private EffectData[] _effects;
        public EffectData[] Effects => _effects;
    }
}
