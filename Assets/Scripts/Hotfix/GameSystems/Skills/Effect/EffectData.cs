using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    public enum EffectType
    {
        // 正面状态效果
        [Tooltip("正面状态效果")]
        Buff,
        // 负面状态效果
        [Tooltip("负面状态效果")]
        Debuff,
        // 恢复生命
        [Tooltip("恢复生命")]
        Heal,
        // 吸收即将到来的伤害
        [Tooltip("吸收即将到来的伤害")]
        Shield,
        // 移除buff/debuff
        [Tooltip("移除buff/debuff")]
        Dispel,
        // 生成生物或物体
        [Tooltip("生成生物或物体")]
        Summon,
        // 传送目标
        [Tooltip("传送目标")]
        Teleport,
        // 将目标推开
        [Tooltip("将目标推开")]
        Knockback,
        // 晕眩目标(无法行动)
        [Tooltip("晕眩目标(无法行动)")]
        Stun,
        // 沉默目标(无法施法)
        [Tooltip("沉默目标(无法施法)")]
        Silence
    }

    public enum StackingRule
    {
        // 重新应用刷新持续时间
        [Tooltip("重新应用刷新持续时间")]
        Refresh,
        // 重新应用添加一层(至多MaxStacks)
        [Tooltip("重新应用添加一层(至多MaxStacks)")]
        Stack,
        // 重新应用时在激活期间被忽略
        [Tooltip("重新应用时在激活期间被忽略")]
        Ignore
    }

    [System.Serializable]
    public class EffectData
    {
        [Header("=== Basic ===")]
        // 效果类别 (buff, debuff, heal, stun等)
        [Tooltip("效果类别 (buff, debuff, heal, stun等)")]
        [SerializeField] protected EffectType _type;
        public EffectType Type => _type;

        // 用于叠加和移除的唯一ID (如 'warrior_rage_buff')
        [Tooltip("用于叠加和移除的唯一ID (如 'warrior_rage_buff')")]
        [SerializeField] protected string _effectId;
        public string EffectId => _effectId;
        public string SetEffectId { set => _effectId = value; }

        // 持续时间(秒) (0 = 瞬间/一次性)
        [Tooltip("持续时间(秒) (0 = 瞬间/一次性)")]
        [SerializeField] protected float _duration;
        public float Duration => _duration;
        public float SetDuration { set => _duration = value; }

        [Header("=== Stacking ===")]
        // 当StackingRule为Stack时的最大层数
        [Tooltip("当StackingRule为Stack时的最大层数")]
        [SerializeField] protected int _maxStacks = 1;
        public int MaxStacks => _maxStacks;

        // 重新应用如何处理 (Refresh刷新持续时间, Stack叠加, 或Ignore忽略)
        [Tooltip("重新应用如何处理 (Refresh刷新持续时间, Stack叠加, 或Ignore忽略)")]
        [SerializeField] protected StackingRule _stackingRule = StackingRule.Refresh;
        public StackingRule StackingRule => _stackingRule;

        [Header("=== Tick ===")]
        // 此效果是否周期性应用? (DOT, HOT等)
        [Tooltip("此效果是否周期性应用? (DOT, HOT等)")]
        [SerializeField] protected bool _isTickEffect;
        public bool IsTickEffect => _isTickEffect;

        // 检测应用之间的时间秒数
        [Tooltip("检测应用之间的时间秒数")]
        [SerializeField] protected float _tickInterval = 1f;
        public float TickInterval => _tickInterval;

        public virtual string GetDisplayName() => _effectId;

        public virtual void Apply(IEffectTarget caster, IEffectTarget target) { }

        public virtual void Remove(IEffectTarget caster, IEffectTarget target) { }

        public virtual void OnTick(IEffectTarget caster, IEffectTarget target) { }
    }

    public interface IEffectTarget
    {
        IEffectStats Stats { get; }
        IShieldSystem ShieldSystem { get; }
        IPhysicsSystem PhysicsSystem { get; }
        IStatusController StatusController { get; }
        Transform transform { get; }
        void Heal(float amount);
    }

    public interface IEffectStats
    {
        float GetAttribute(AttributeType type);
        float GetMaxHealth();
        void AddModifier(AttributeType type, string id, float value, ModifierType modType);
        void RemoveModifier(AttributeType type, string id);
    }

    public interface IShieldSystem
    {
        void AddShield(string id, float amount, float duration);
        void RemoveShield(string id);
    }

    public interface IPhysicsSystem
    {
        void ApplyKnockback(Vector3 direction, float force);
    }

    public interface IStatusController
    {
        void AddStun(float duration);
        void RemoveStun();
    }

    [System.Serializable]
    public class BuffEffectData : EffectData
    {
        [Header("=== Buff Properties ===")]
        // 此buff修改哪个属性 (AttackPower, Defense, Speed等)
        [Tooltip("此buff修改哪个属性 (AttackPower, Defense, Speed等)")]
        [SerializeField] private AttributeType _attributeToModify;
        public AttributeType AttributeToModify => _attributeToModify;
        public AttributeType SetAttributeToModify { set => _attributeToModify = value; }

        // 修改的值(根据ModifierType解释)
        [Tooltip("修改的值(根据ModifierType解释)")]
        [SerializeField] private float _value;
        public float Value => _value;
        public float SetValue { set => _value = value; }

        // 值如何修改属性: Flat (+N), PercentAdd (+N%), PercentMult (*N%)
        [Tooltip("值如何修改属性: Flat (+N), PercentAdd (+N%), PercentMult (*N%)")]
        [SerializeField] private ModifierType _modifierType = ModifierType.Flat;
        public ModifierType ModifierType => _modifierType;
        public ModifierType SetModifierType { set => _modifierType = value; }

        public BuffEffectData()
        {
            _type = EffectType.Buff;
        }

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

    [System.Serializable]
    public class HealEffectData : EffectData
    {
        [Header("=== Heal Properties ===")]
        // 缩放前的基准治疗量
        [Tooltip("缩放前的基准治疗量")]
        [SerializeField] private float _baseHeal;
        public float BaseHeal => _baseHeal;

        // 法术强度缩放比 (如 0.5 = 50% SpellPower加成到治疗)
        [Tooltip("法术强度缩放比 (如 0.5 = 50% SpellPower加成到治疗)")]
        [SerializeField] private float _spellPowerRatio;
        public float SpellPowerRatio => _spellPowerRatio;

        // 将BaseHeal视为目标最大生命值的百分比? (如 10 = 10%最大生命值)
        [Tooltip("将BaseHeal视为目标最大生命值的百分比? (如 10 = 10%最大生命值)")]
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

    [System.Serializable]
    public class ShieldEffectData : EffectData
    {
        [Header("=== Shield Properties ===")]
        // 伤害吸收量
        [Tooltip("伤害吸收量")]
        [SerializeField] private float _shieldAmount;
        public float ShieldAmount => _shieldAmount;

        // 吸收所有伤害类型? (false = 只吸收AbsorbedDamageType)
        [Tooltip("吸收所有伤害类型? (false = 只吸收AbsorbedDamageType)")]
        [SerializeField] private bool _absorbAllDamageTypes = true;
        public bool AbsorbAllDamageTypes => _absorbAllDamageTypes;

        // 当AbsorbAllDamageTypes为false时吸收的伤害类型
        [Tooltip("当AbsorbAllDamageTypes为false时吸收的伤害类型")]
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

    [System.Serializable]
    public class KnockbackEffectData : EffectData
    {
        [Header("=== Knockback Properties ===")]
        // 水平击退力
        [Tooltip("水平击退力")]
        [SerializeField] private float _force;
        public float Force => _force;

        // 向上(垂直)击退力
        [Tooltip("向上(垂直)击退力")]
        [SerializeField] private float _upwardForce;
        public float UpwardForce => _upwardForce;

        // AOE击退半径 (0 = 单目标)
        [Tooltip("AOE击退半径 (0 = 单目标)")]
        [SerializeField] private float _radius;
        public float Radius => _radius;

        public KnockbackEffectData()
        {
            _type = EffectType.Knockback;
            _duration = 0f;
        }

        public override string GetDisplayName() => $"Knockback: {_force}";

        public override void Apply(IEffectTarget caster, IEffectTarget target)
        {
            Vector3 direction = (target.transform.position - caster.transform.position).normalized;
            direction.y = _upwardForce;
            target.PhysicsSystem.ApplyKnockback(direction, _force);
        }
    }

    [System.Serializable]
    public class StunEffectData : EffectData
    {
        [Header("=== Stun Properties ===")]
        // 此晕眩是否可以被净化/驱散?
        [Tooltip("此晕眩是否可以被净化/驱散?")]
        [SerializeField] private bool _canBeCleanse = true;
        public bool CanBeCleanse => _canBeCleanse;
        public bool SetCanBeCleanse { set => _canBeCleanse = value; }

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