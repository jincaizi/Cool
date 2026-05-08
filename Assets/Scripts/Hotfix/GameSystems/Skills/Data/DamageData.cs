using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    [System.Serializable]
    public class DamageData
    {
        [Header("=== Base Damage ===")]
        [Tooltip("Flat damage before scaling")]
        [SerializeField] private float _baseDamage;
        public float BaseDamage => _baseDamage;

        [Tooltip("Multiplier applied to the scaling attribute (e.g. 1.0 = 100% of AttackPower added)")]
        [SerializeField] private float _attackRatio = 1f;
        public float AttackRatio => _attackRatio;

        [Tooltip("Which character attribute scales this damage (AttackPower, SpellPower, etc.)")]
        [SerializeField] private AttributeType _scalingAttribute = AttributeType.AttackPower;
        public AttributeType ScalingAttribute => _scalingAttribute;

        [Header("=== Damage Type ===")]
        [Tooltip("Damage category for resistance/armor calculation")]
        [SerializeField] private DamageType _damageType = DamageType.Physical;
        public DamageType DamageType => _damageType;

        [Header("=== Critical ===")]
        [Tooltip("Bonus crit chance added to the character's base crit rate (0.05 = +5%)")]
        [SerializeField] private float _criticalRateBonus;
        public float CriticalRateBonus => _criticalRateBonus;

        [Tooltip("Bonus crit damage multiplier added to the base 1.5x (0.5 = 2.0x total)")]
        [SerializeField] private float _criticalDamageBonus;
        public float CriticalDamageBonus => _criticalDamageBonus;

        [Header("=== Special ===")]
        [Tooltip("True damage ignores armor and resistances")]
        [SerializeField] private bool _isTrueDamage;
        public bool IsTrueDamage => _isTrueDamage;

        [Tooltip("Percentage of target armor ignored (0-1, 0.3 = ignore 30%)")]
        [SerializeField] private float _armorPenetration;
        public float ArmorPenetration => _armorPenetration;

        [Header("=== Over Time ===")]
        [Tooltip("Is this a damage-over-time effect?")]
        [SerializeField] private bool _isDOT;
        public bool IsDOT => _isDOT;

        [Tooltip("Seconds between DOT ticks")]
        [SerializeField] private float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        [Tooltip("Total number of DOT ticks")]
        [SerializeField] private int _totalTicks = 5;
        public int TotalTicks => _totalTicks;

        public float CalculateFinalDamage(IEffectStats attackerStats)
        {
            if (attackerStats == null)
            {
                return _baseDamage;
            }

            float damage = _baseDamage;
            float scalingValue = attackerStats.GetAttribute(_scalingAttribute);
            damage += scalingValue * _attackRatio;

            if (_criticalRateBonus > 0)
            {
                float critChance = 0.05f + _criticalRateBonus;
                if (UnityEngine.Random.value < critChance)
                {
                    damage *= (1f + 1.5f + _criticalDamageBonus);
                }
            }

            return _isDOT ? damage * _tickInterval : damage;
        }

        public static DamageData CreateDefault(float baseDamage, float attackRatio = 1f)
        {
            return new DamageData
            {
                _baseDamage = baseDamage,
                _attackRatio = attackRatio
            };
        }
    }

    public enum DamageType
    {
        [Tooltip("Reduced by armor")]
        Physical,
        [Tooltip("Reduced by magic resistance")]
        Magic,
        [Tooltip("Ignores all defenses")]
        True
    }

    public enum AttributeType
    {
        AttackPower,
        SpellPower,
        Health,
        Defense,
        Resistance,
        CriticalRate,
        CriticalDamage,
        Speed,
    }

    public enum ModifierType
    {
        [Tooltip("Adds a flat value: final = base + value")]
        Flat,
        [Tooltip("Adds a percentage of base: final = base * (1 + value)")]
        PercentAdd,
        [Tooltip("Multiplies the final value: final = final * (1 + value)")]
        PercentMult
    }
}
