using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    public enum EffectType
    {
        [Tooltip("Positive status effect")]
        Buff,
        [Tooltip("Negative status effect")]
        Debuff,
        [Tooltip("Restores health")]
        Heal,
        [Tooltip("Absorbs incoming damage")]
        Shield,
        [Tooltip("Removes buffs/debuffs")]
        Dispel,
        [Tooltip("Spawns a creature or object")]
        Summon,
        [Tooltip("Teleports the target")]
        Teleport,
        [Tooltip("Pushes the target away")]
        Knockback,
        [Tooltip("Stuns the target (cannot act)")]
        Stun,
        [Tooltip("Silences the target (cannot cast)")]
        Silence
    }

    public enum StackingRule
    {
        [Tooltip("Re-application refreshes the duration")]
        Refresh,
        [Tooltip("Re-application adds a stack (up to MaxStacks)")]
        Stack,
        [Tooltip("Re-application is ignored while active")]
        Ignore
    }

    [System.Serializable]
    public class EffectData
    {
        [Header("=== Basic ===")]
        [Tooltip("Effect category (buff, debuff, heal, stun, etc.)")]
        [SerializeField] protected EffectType _type;
        public EffectType Type => _type;

        [Tooltip("Unique ID for stacking and removal (e.g. 'warrior_rage_buff')")]
        [SerializeField] protected string _effectId;
        public string EffectId => _effectId;
        public string SetEffectId { set => _effectId = value; }

        [Tooltip("Duration in seconds (0 = instant / one-shot)")]
        [SerializeField] protected float _duration;
        public float Duration => _duration;

        [Header("=== Stacking ===")]
        [Tooltip("Maximum stacks when StackingRule is Stack")]
        [SerializeField] protected int _maxStacks = 1;
        public int MaxStacks => _maxStacks;

        [Tooltip("How re-application behaves (Refresh duration, Stack, or Ignore)")]
        [SerializeField] protected StackingRule _stackingRule = StackingRule.Refresh;
        public StackingRule StackingRule => _stackingRule;

        [Header("=== Tick ===")]
        [Tooltip("Does this effect apply periodically (DOT, HOT, etc.)?")]
        [SerializeField] protected bool _isTickEffect;
        public bool IsTickEffect => _isTickEffect;

        [Tooltip("Seconds between tick applications")]
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
        [Tooltip("Which attribute this buff modifies (AttackPower, Defense, Speed, etc.)")]
        [SerializeField] private AttributeType _attributeToModify;
        public AttributeType AttributeToModify => _attributeToModify;
        public AttributeType SetAttributeToModify { set => _attributeToModify = value; }

        [Tooltip("Value of the modification (interpreted according to ModifierType)")]
        [SerializeField] private float _value;
        public float Value => _value;
        public float SetValue { set => _value = value; }

        [Tooltip("How the value modifies the attribute: Flat (+N), PercentAdd (+N%), PercentMult (*N%)")]
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
        [Tooltip("Base heal amount before scaling")]
        [SerializeField] private float _baseHeal;
        public float BaseHeal => _baseHeal;

        [Tooltip("Spell power scaling ratio (e.g. 0.5 = 50% of SpellPower added to heal)")]
        [SerializeField] private float _spellPowerRatio;
        public float SpellPowerRatio => _spellPowerRatio;

        [Tooltip("Treat BaseHeal as percentage of target's max HP? (e.g. 10 = 10% max HP)")]
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
        [Tooltip("Damage absorption amount")]
        [SerializeField] private float _shieldAmount;
        public float ShieldAmount => _shieldAmount;

        [Tooltip("Absorb all damage types? (false = only absorb AbsorbedDamageType)")]
        [SerializeField] private bool _absorbAllDamageTypes = true;
        public bool AbsorbAllDamageTypes => _absorbAllDamageTypes;

        [Tooltip("Damage type absorbed when AbsorbAllDamageTypes is false")]
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
        [Tooltip("Horizontal knockback force")]
        [SerializeField] private float _force;
        public float Force => _force;

        [Tooltip("Upward (vertical) knockback force")]
        [SerializeField] private float _upwardForce;
        public float UpwardForce => _upwardForce;

        [Tooltip("AOE knockback radius (0 = single target)")]
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
        [Tooltip("Can this stun be cleansed / dispelled?")]
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
