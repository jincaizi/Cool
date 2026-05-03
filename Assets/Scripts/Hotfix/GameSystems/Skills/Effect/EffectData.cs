using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    /// <summary>
    /// 效果类型枚举
    /// </summary>
    public enum EffectType
    {
        Buff,           // 增益
        Debuff,         // 减益
        Heal,           // 治疗
        Shield,         // 护盾
        Dispel,         // 驱散
        Summon,         // 召唤
        Teleport,       // 位移
        Knockback,      // 击退
        Stun,           // 眩晕
        Silence         // 沉默
    }

    /// <summary>
    /// 堆叠规则
    /// </summary>
    public enum StackingRule
    {
        Refresh,        // 刷新持续时间
        Stack,          // 叠加层数
        Ignore          // 忽略新效果
    }

    /// <summary>
    /// 效果数据基类
    /// </summary>
    [System.Serializable]
    public class EffectData
    {
        [Header("=== Basic ===")]
        [SerializeField] protected EffectType _type;
        public EffectType Type => _type;

        [SerializeField] protected string _effectId;
        public string EffectId => _effectId;
        public string SetEffectId { set => _effectId = value; }

        [SerializeField] protected float _duration;
        public float Duration => _duration;

        [Header("=== Stacking ===")]
        [SerializeField] protected int _maxStacks = 1;
        public int MaxStacks => _maxStacks;

        [SerializeField] protected StackingRule _stackingRule = StackingRule.Refresh;
        public StackingRule StackingRule => _stackingRule;

        [Header("=== Tick ===")]
        [SerializeField] protected bool _isTickEffect;
        public bool IsTickEffect => _isTickEffect;

        [SerializeField] protected float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        /// <summary>
        /// 获取效果名称（用于显示）
        /// </summary>
        public virtual string GetDisplayName() => _effectId;

        /// <summary>
        /// 应用效果
        /// </summary>
        public virtual void Apply(IEffectTarget caster, IEffectTarget target)
        {
            // 基类实现为空，子类重写具体逻辑
        }

        /// <summary>
        /// 移除效果
        /// </summary>
        public virtual void Remove(IEffectTarget caster, IEffectTarget target)
        {
            // 基类实现为空，子类重写具体逻辑
        }

        /// <summary>
        /// 每tick调用
        /// </summary>
        public virtual void OnTick(IEffectTarget caster, IEffectTarget target)
        {
            // 基类实现为空，子类重写具体逻辑
        }
    }

    /// <summary>
    /// 效果目标接口 - 解耦Character依赖
    /// </summary>
    public interface IEffectTarget
    {
        IEffectStats Stats { get; }
        IShieldSystem ShieldSystem { get; }
        IPhysicsSystem PhysicsSystem { get; }
        IStatusController StatusController { get; }
        Transform transform { get; }
        void Heal(float amount);
    }

    /// <summary>
    /// 属性统计接口
    /// </summary>
    public interface IEffectStats
    {
        float GetAttribute(AttributeType type);
        float GetMaxHealth();
        void AddModifier(AttributeType type, string id, float value, ModifierType modType);
        void RemoveModifier(AttributeType type, string id);
    }

    /// <summary>
    /// 护盾系统接口
    /// </summary>
    public interface IShieldSystem
    {
        void AddShield(string id, float amount, float duration);
        void RemoveShield(string id);
    }

    /// <summary>
    /// 物理系统接口
    /// </summary>
    public interface IPhysicsSystem
    {
        void ApplyKnockback(Vector3 direction, float force);
    }

    /// <summary>
    /// 状态控制器接口
    /// </summary>
    public interface IStatusController
    {
        void AddStun(float duration);
        void RemoveStun();
    }

    /// <summary>
    /// Buff效果 - 属性加成/减益
    /// </summary>
    [System.Serializable]
    public class BuffEffectData : EffectData
    {
        [Header("=== Buff Properties ===")]
        [SerializeField] private AttributeType _attributeToModify;
        public AttributeType AttributeToModify => _attributeToModify;
        public AttributeType SetAttributeToModify { set => _attributeToModify = value; }

        [SerializeField] private float _value;
        public float Value => _value;
        public float SetValue { set => _value = value; }

        [SerializeField] private ModifierType _modifierType = ModifierType.Flat;
        public ModifierType ModifierType => _modifierType;
        public ModifierType SetModifierType { set => _modifierType = value; }

        public BuffEffectData()
        {
            _type = EffectType.Buff;
        }

        /// <summary>
        /// 初始化方法
        /// </summary>
        public void Initialize(string effectId, AttributeType attribute, float value, ModifierType modType)
        {
            _effectId = effectId;
            _attributeToModify = attribute;
            _value = value;
            _modifierType = modType;
        }

        public override string GetDisplayName() => $"Buff: {_attributeToModify}";

        public override void Apply(IEffectTarget caster, IEffectTarget target)
        {
            target.Stats.AddModifier(_attributeToModify, _effectId, _value, _modifierType);
        }

        public override void Remove(IEffectTarget caster, IEffectTarget target)
        {
            target.Stats.RemoveModifier(_attributeToModify, _effectId);
        }
    }

    /// <summary>
    /// 治疗效果
    /// </summary>
    [System.Serializable]
    public class HealEffectData : EffectData
    {
        [Header("=== Heal Properties ===")]
        [SerializeField] private float _baseHeal;
        public float BaseHeal => _baseHeal;

        [SerializeField] private float _spellPowerRatio;
        public float SpellPowerRatio => _spellPowerRatio;

        [SerializeField] private bool _percentOfMaxHealth;
        public bool PercentOfMaxHealth => _percentOfMaxHealth;

        public HealEffectData()
        {
            _type = EffectType.Heal;
            _isTickEffect = true;
        }

        public override string GetDisplayName() => $"Heal: {_baseHeal}";

        public override void Apply(IEffectTarget caster, IEffectTarget target)
        {
            float healAmount = _baseHeal;

            if (_percentOfMaxHealth)
            {
                healAmount = target.Stats.GetMaxHealth() * (_baseHeal / 100f);
            }
            else if (_spellPowerRatio > 0)
            {
                healAmount += caster.Stats.GetAttribute(AttributeType.SpellPower) * _spellPowerRatio;
            }

            target.Heal(healAmount);
        }
    }

    /// <summary>
    /// 护盾效果
    /// </summary>
    [System.Serializable]
    public class ShieldEffectData : EffectData
    {
        [Header("=== Shield Properties ===")]
        [SerializeField] private float _shieldAmount;
        public float ShieldAmount => _shieldAmount;

        [SerializeField] private bool _absorbAllDamageTypes = true;
        public bool AbsorbAllDamageTypes => _absorbAllDamageTypes;

        [SerializeField] private DamageType _absorbedDamageType = DamageType.Physical;
        public DamageType AbsorbedDamageType => _absorbedDamageType;

        public ShieldEffectData()
        {
            _type = EffectType.Shield;
        }

        public override string GetDisplayName() => $"Shield: {_shieldAmount}";

        public override void Apply(IEffectTarget caster, IEffectTarget target)
        {
            target.ShieldSystem.AddShield(_effectId, _shieldAmount, _duration);
        }

        public override void Remove(IEffectTarget caster, IEffectTarget target)
        {
            target.ShieldSystem.RemoveShield(_effectId);
        }
    }

    /// <summary>
    /// 击退效果
    /// </summary>
    [System.Serializable]
    public class KnockbackEffectData : EffectData
    {
        [Header("=== Knockback Properties ===")]
        [SerializeField] private float _force;
        public float Force => _force;

        [SerializeField] private float _upwardForce;
        public float UpwardForce => _upwardForce;

        [SerializeField] private float _radius;
        public float Radius => _radius;

        public KnockbackEffectData()
        {
            _type = EffectType.Knockback;
            _duration = 0f; // 即时效果
        }

        public override string GetDisplayName() => $"Knockback: {_force}";

        public override void Apply(IEffectTarget caster, IEffectTarget target)
        {
            Vector3 direction = (target.transform.position - caster.transform.position).normalized;
            direction.y = _upwardForce;
            target.PhysicsSystem.ApplyKnockback(direction, _force);
        }
    }

    /// <summary>
    /// 眩晕效果
    /// </summary>
    [System.Serializable]
    public class StunEffectData : EffectData
    {
        [Header("=== Stun Properties ===")]
        [SerializeField] private bool _canBeCleanse = true;
        public bool CanBeCleanse => _canBeCleanse;

        public StunEffectData()
        {
            _type = EffectType.Stun;
        }

        public override string GetDisplayName() => $"Stun: {_duration}s";

        public override void Apply(IEffectTarget caster, IEffectTarget target)
        {
            target.StatusController.AddStun(_duration);
        }

        public override void Remove(IEffectTarget caster, IEffectTarget target)
        {
            target.StatusController.RemoveStun();
        }
    }
}