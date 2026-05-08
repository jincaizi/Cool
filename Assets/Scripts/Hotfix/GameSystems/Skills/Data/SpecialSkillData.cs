using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "SpecialSkill", menuName = "Game/Skills/Special Skill")]
    public class SpecialSkillData : SkillData
    {
        [Header("=== Charge Skill Properties ===")]
        [Tooltip("Damage multiplier curve over charge time (x=charge progress 0-1, y=damage multiplier)")]
        [SerializeField] private AnimationCurve _chargeDamageCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
        public AnimationCurve ChargeDamageCurve => _chargeDamageCurve;

        [Tooltip("AOE radius multiplier curve over charge time (x=charge progress 0-1, y=radius multiplier)")]
        [SerializeField] private AnimationCurve _chargeAreaCurve = AnimationCurve.Linear(0f, 1f, 1f, 1.5f);
        public AnimationCurve ChargeAreaCurve => _chargeAreaCurve;

        [Tooltip("Hold button to continue charging? (false = press once, auto-charge to max)")]
        [SerializeField] private bool _holdToCharge = true;
        public bool HoldToCharge => _holdToCharge;

        [Tooltip("Release button to fire? (false = auto-fire at max charge)")]
        [SerializeField] private bool _releaseToFire = true;
        public bool ReleaseToFire => _releaseToFire;

        [Header("=== Channel Properties ===")]
        [Tooltip("Interval in seconds between damage ticks during channel")]
        [SerializeField] private float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        [Tooltip("Percentage of base damage dealt per tick (0-1)")]
        [SerializeField] [Range(0f, 1f)] private float _tickDamagePercent = 0.2f;
        public float TickDamagePercent => _tickDamagePercent;

        [Tooltip("Should the channeling effect follow the target as it moves?")]
        [SerializeField] private bool _channelFollowsTarget;
        public bool ChannelFollowsTarget => _channelFollowsTarget;

        [Tooltip("Break the channel if the target moves out of range?")]
        [SerializeField] private bool _breakOnTargetMove;
        public bool BreakOnTargetMove => _breakOnTargetMove;

        [Header("=== Casting Bar ===")]
        [Tooltip("Show a casting bar on the HUD for this skill?")]
        [SerializeField] private bool _showCastingBar = true;
        public bool ShowCastingBar => _showCastingBar;

        [Tooltip("Can the character move while the casting bar is visible?")]
        [SerializeField] private bool _canMoveWhileCastingBar = true;
        public bool CanMoveWhileCastingBar => _canMoveWhileCastingBar;

        [Tooltip("Color of the casting bar on the HUD")]
        [SerializeField] private Color _castingBarColor = Color.blue;
        public Color CastingBarColor => _castingBarColor;

        [Header("=== AOE Properties ===")]
        [Tooltip("Is this an area-of-effect skill?")]
        [SerializeField] private bool _isAOE;
        public bool IsAOE => _isAOE;

        [Tooltip("AOE origin: Center (around target), Origin (around caster), Direction (forward cone)")]
        [SerializeField] private AOEDamageType _aoeDamageType = AOEDamageType.Center;
        public AOEDamageType AOEDamageType => _aoeDamageType;

        [Tooltip("Does damage decrease with distance from center?")]
        [SerializeField] private bool _damageFalloff = true;
        public bool DamageFalloff => _damageFalloff;

        [Tooltip("Damage falloff curve (x=distance from center ratio, y=damage multiplier)")]
        [SerializeField] private AnimationCurve _damageFalloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.5f);
        public AnimationCurve DamageFalloffCurve => _damageFalloffCurve;

        [Header("=== Projectile ===")]
        [Tooltip("Prefab for the projectile spawned on cast (null = no projectile, melee/hitscan)")]
        [SerializeField] private GameObject _projectilePrefab;
        public GameObject ProjectilePrefab => _projectilePrefab;

        [Tooltip("Projectile travel speed in world units per second")]
        [SerializeField] private float _projectileSpeed = 20f;
        public float ProjectileSpeed => _projectileSpeed;

        [Tooltip("Does the projectile pierce through targets?")]
        [SerializeField] private bool _projectilePierce;
        public bool ProjectilePierce => _projectilePierce;

        [Tooltip("Maximum number of targets the projectile can pierce through")]
        [SerializeField] private int _maxPierceTargets = 3;
        public int MaxPierceTargets => _maxPierceTargets;

        [Tooltip("Does the projectile home in on the target?")]
        [SerializeField] private bool _homing;
        public bool Homing => _homing;

        [Header("=== Multi-Hit ===")]
        [Tooltip("Maximum number of targets this skill can hit in one cast")]
        [SerializeField] private int _maxHitTargets = 1;
        public int MaxHitTargets => _maxHitTargets;

        [Tooltip("Target priority when multiple targets are in range")]
        [SerializeField] private HitPriority _hitPriority = HitPriority.Nearest;
        public HitPriority HitPriority => _hitPriority;

        private void ValidateSkillType()
        {
            if (_skillType == Definition.SkillType.BasicAttack)
            {
                _skillType = Definition.SkillType.Special;
            }
        }

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

        public int GetTotalChannelTicks()
        {
            if (ChannelDuration <= 0 || TickInterval <= 0) return 0;
            return Mathf.FloorToInt(ChannelDuration / TickInterval);
        }
    }

    public enum AOEDamageType
    {
        [Tooltip("AOE centered on the target's position")]
        Center,
        [Tooltip("AOE centered on the caster's position")]
        Origin,
        [Tooltip("AOE in a cone / line forward from the caster")]
        Direction
    }

    public enum HitPriority
    {
        [Tooltip("Prioritize the closest target")]
        Nearest,
        [Tooltip("Prioritize the farthest target")]
        Furthest,
        [Tooltip("Prioritize the target with the lowest HP")]
        LowestHP,
        [Tooltip("Prioritize the target with the highest HP")]
        HighestHP,
        [Tooltip("Prioritize the target with the highest threat / aggro")]
        HighestThreat
    }
}
