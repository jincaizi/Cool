using UnityEngine;
using Effect = Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Skills.Data
{
    [System.Serializable]
    public class DamageBlock
    {
        [Header("=== Base Damage ===")]
        // 缩放前的固定伤害
        [Tooltip("缩放前的固定伤害")]
        [SerializeField] private float _baseDamage;
        public float BaseDamage => _baseDamage;

        // 应用于缩放属性的乘数 (如 1.0 = 100% AttackPower加成)
        [Tooltip("应用于缩放属性的乘数 (如 1.0 = 100% AttackPower加成)")]
        [SerializeField] private float _attackRatio = 1f;
        public float AttackRatio => _attackRatio;

        // 哪个角色属性缩放此伤害 (AttackPower, SpellPower等)
        [Tooltip("哪个角色属性缩放此伤害 (AttackPower, SpellPower等)")]
        [SerializeField] private Effect.AttributeType _scalingAttribute = Effect.AttributeType.AttackPower;
        public Effect.AttributeType ScalingAttribute => _scalingAttribute;

        [Header("=== Damage Type ===")]
        // 用于抗性/护甲计算的伤害类别
        [Tooltip("用于抗性/护甲计算的伤害类别")]
        [SerializeField] private Effect.DamageType _damageType = Effect.DamageType.Physical;
        public Effect.DamageType DamageType => _damageType;

        [Header("=== Critical ===")]
        // 加到角色基础暴击率的额外暴击几率 (0.05 = +5%)
        [Tooltip("加到角色基础暴击率的额外暴击几率 (0.05 = +5%)")]
        [SerializeField] private float _criticalRateBonus;
        public float CriticalRateBonus => _criticalRateBonus;

        // 加到基础1.5倍的额外暴击伤害乘数 (0.5 = 总计2.0倍)
        [Tooltip("加到基础1.5倍的额外暴击伤害乘数 (0.5 = 总计2.0倍)")]
        [SerializeField] private float _criticalDamageBonus;
        public float CriticalDamageBonus => _criticalDamageBonus;

        [Header("=== Special ===")]
        // 真伤无视护甲和抗性
        [Tooltip("真伤无视护甲和抗性")]
        [SerializeField] private bool _isTrueDamage;
        public bool IsTrueDamage => _isTrueDamage;

        // 被忽略的目标护甲百分比 (0-1, 0.3 = 忽略30%)
        [Tooltip("被忽略的目标护甲百分比 (0-1, 0.3 = 忽略30%)")]
        [SerializeField] private float _armorPenetration;
        public float ArmorPenetration => _armorPenetration;

        [Header("=== Knockback ===")]
        [Tooltip("击退力度")]
        [SerializeField] private float _knockbackForce;
        public float KnockbackForce => _knockbackForce;

        [Header("=== Over Time ===")]
        // 是否为持续伤害效果?
        [Tooltip("是否为持续伤害效果?")]
        [SerializeField] private bool _isDOT;
        public bool IsDOT => _isDOT;

        // DOT检测的间隔秒数
        [Tooltip("DOT检测的间隔秒数")]
        [SerializeField] private float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        // DOT总检测次数
        [Tooltip("DOT总检测次数")]
        [SerializeField] private int _totalTicks = 5;
        public int TotalTicks => _totalTicks;

        public bool WasCritical { get; private set; }

        public float CalculateFinalDamage(Effect.IEffectStats attackerStats)
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
                    WasCritical = true;
                    damage *= (1f + 1.5f + _criticalDamageBonus);
                }
                else
                {
                    WasCritical = false;
                }
            }
            else
            {
                WasCritical = false;
            }

            return _isDOT ? damage * _tickInterval : damage;
        }

        public static DamageBlock CreateDefault(float baseDamage, float attackRatio = 1f)
        {
            return new DamageBlock
            {
                _baseDamage = baseDamage,
                _attackRatio = attackRatio
            };
        }
    }
}
