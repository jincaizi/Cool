using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    /// <summary>
    /// 特殊技能数据 - 包含引导、蓄力、AOE等高级配置
    /// </summary>
    [CreateAssetMenu(fileName = "SpecialSkill", menuName = "Game/Skills/Special Skill")]
    public class SpecialSkillData : SkillData
    {
        [Header("=== Charge Skill Properties ===")]
        [SerializeField] private AnimationCurve _chargeDamageCurve = AnimationCurve.Linear(0f, 1f, 1f, 2f);
        public AnimationCurve ChargeDamageCurve => _chargeDamageCurve;

        [SerializeField] private AnimationCurve _chargeAreaCurve = AnimationCurve.Linear(0f, 1f, 1f, 1.5f);
        public AnimationCurve ChargeAreaCurve => _chargeAreaCurve;

        [SerializeField] private bool _holdToCharge = true;              // 按住继续蓄力
        public bool HoldToCharge => _holdToCharge;

        [SerializeField] private bool _releaseToFire = true;             // 松开发射
        public bool ReleaseToFire => _releaseToFire;

        [Header("=== Channel Properties ===")]
        [SerializeField] private float _tickInterval = 1f;               // 引导伤害间隔
        public float TickInterval => _tickInterval;

        [SerializeField] [Range(0f, 1f)] private float _tickDamagePercent = 0.2f;  // 每跳伤害百分比
        public float TickDamagePercent => _tickDamagePercent;

        [SerializeField] private bool _channelFollowsTarget;           // 引导跟随目标
        public bool ChannelFollowsTarget => _channelFollowsTarget;

        [SerializeField] private bool _breakOnTargetMove;               // 目标移动时中断
        public bool BreakOnTargetMove => _breakOnTargetMove;

        [Header("=== Casting Bar ===")]
        [SerializeField] private bool _showCastingBar = true;
        public bool ShowCastingBar => _showCastingBar;

        [SerializeField] private bool _canMoveWhileCastingBar = true;   // 读条时可移动
        public bool CanMoveWhileCastingBar => _canMoveWhileCastingBar;

        [SerializeField] private Color _castingBarColor = Color.blue;
        public Color CastingBarColor => _castingBarColor;

        [Header("=== AOE Properties ===")]
        [SerializeField] private bool _isAOE;
        public bool IsAOE => _isAOE;

        [SerializeField] private AOEDamageType _aoeDamageType = AOEDamageType.Center;
        public AOEDamageType AOEDamageType => _aoeDamageType;

        [SerializeField] private bool _damageFalloff = true;          // 伤害衰减
        public bool DamageFalloff => _damageFalloff;

        [SerializeField] private AnimationCurve _damageFalloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.5f);
        public AnimationCurve DamageFalloffCurve => _damageFalloffCurve;

        [Header("=== Projectile ===")]
        [SerializeField] private GameObject _projectilePrefab;
        public GameObject ProjectilePrefab => _projectilePrefab;

        [SerializeField] private float _projectileSpeed = 20f;
        public float ProjectileSpeed => _projectileSpeed;

        [SerializeField] private bool _projectilePierce;                 // 穿透
        public bool ProjectilePierce => _projectilePierce;

        [SerializeField] private int _maxPierceTargets = 3;
        public int MaxPierceTargets => _maxPierceTargets;

        [SerializeField] private bool _homing;                           // 制导
        public bool Homing => _homing;

        [Header("=== Multi-Hit ===")]
        [SerializeField] private int _maxHitTargets = 1;                // 最大击中目标数
        public int MaxHitTargets => _maxHitTargets;

        [SerializeField] private HitPriority _hitPriority = HitPriority.Nearest;
        public HitPriority HitPriority => _hitPriority;

        private void ValidateSkillType()
        {
            // 特殊技能类型
            if (_skillType == Definition.SkillType.BasicAttack)
            {
                _skillType = Definition.SkillType.Special;
            }
        }

        /// <summary>
        /// 根据蓄力时间获取伤害缩放
        /// </summary>
        public float GetDamageScaleForCharge(float chargeProgress)
        {
            if (chargeProgress <= 0) return 1f;
            return _chargeDamageCurve?.Evaluate(chargeProgress) ?? (1f + chargeProgress);
        }

        /// <summary>
        /// 根据蓄力时间获取范围缩放
        /// </summary>
        public float GetAreaScaleForCharge(float chargeProgress)
        {
            if (chargeProgress <= 0) return 1f;
            return _chargeAreaCurve?.Evaluate(chargeProgress) ?? 1f;
        }

        /// <summary>
        /// 获取引导总tick数
        /// </summary>
        public int GetTotalChannelTicks()
        {
            if (ChannelDuration <= 0 || TickInterval <= 0) return 0;
            return Mathf.FloorToInt(ChannelDuration / TickInterval);
        }
    }

    /// <summary>
    /// AOE伤害类型
    /// </summary>
    public enum AOEDamageType
    {
        Center,         // 以目标为中心
        Origin,         // 以施法者为中心
        Direction       // 以施法者向前方向
    }

    /// <summary>
    /// 命中优先级
    /// </summary>
    public enum HitPriority
    {
        Nearest,        // 最近
        Furthest,       // 最远
        LowestHP,       // 血量最低
        HighestHP,      // 血量最高
        HighestThreat   // 仇恨最高（坦克游戏）
    }
}