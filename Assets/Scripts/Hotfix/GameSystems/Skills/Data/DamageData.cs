using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    /// <summary>
    /// 伤害数据类型
    /// </summary>
    [System.Serializable]
    public class DamageData
    {
        [Header("=== Base Damage ===")]
        [SerializeField] private float _baseDamage;
        public float BaseDamage => _baseDamage;

        [SerializeField] private float _attackRatio = 1f;   // 攻击力缩放
        public float AttackRatio => _attackRatio;

        [SerializeField] private AttributeType _scalingAttribute = AttributeType.AttackPower;
        public AttributeType ScalingAttribute => _scalingAttribute;

        [Header("=== Damage Type ===")]
        [SerializeField] private DamageType _damageType = DamageType.Physical;
        public DamageType DamageType => _damageType;

        [Header("=== Critical ===")]
        [SerializeField] private float _criticalRateBonus;
        public float CriticalRateBonus => _criticalRateBonus;

        [SerializeField] private float _criticalDamageBonus;
        public float CriticalDamageBonus => _criticalDamageBonus;

        [Header("=== Special ===")]
        [SerializeField] private bool _isTrueDamage;
        public bool IsTrueDamage => _isTrueDamage;

        [SerializeField] private float _armorPenetration;
        public float ArmorPenetration => _armorPenetration;

        [Header("=== Over Time ===")]
        [SerializeField] private bool _isDOT;
        public bool IsDOT => _isDOT;

        [SerializeField] private float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        [SerializeField] private int _totalTicks = 5;
        public int TotalTicks => _totalTicks;

        /// <summary>
        /// 计算最终伤害
        /// </summary>
        public float CalculateFinalDamage(IEffectStats attackerStats)
        {
            if (attackerStats == null)
            {
                return _baseDamage;
            }

            float damage = _baseDamage;

            // 属性缩放
            float scalingValue = attackerStats.GetAttribute(_scalingAttribute);
            damage += scalingValue * _attackRatio;

            // 暴击计算（如果有加成）
            if (_criticalRateBonus > 0)
            {
                float critChance = 0.05f + _criticalRateBonus; // 默认暴击率5%
                if (UnityEngine.Random.value < critChance)
                {
                    damage *= (1f + 1.5f + _criticalDamageBonus); // 默认暴击伤害150%
                }
            }

            // 持续伤害按间隔计算
            return _isDOT ? damage * _tickInterval : damage;
        }

        /// <summary>
        /// 创建默认伤害数据
        /// </summary>
        public static DamageData CreateDefault(float baseDamage, float attackRatio = 1f)
        {
            return new DamageData
            {
                _baseDamage = baseDamage,
                _attackRatio = attackRatio
            };
        }
    }

    /// <summary>
    /// 伤害类型
    /// </summary>
    public enum DamageType
    {
        Physical,
        Magic,
        True
    }

    /// <summary>
    /// 属性类型
    /// </summary>
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

    /// <summary>
    /// 修改器类型
    /// </summary>
    public enum ModifierType
    {
        Flat,
        PercentAdd,
        PercentMult
    }
}