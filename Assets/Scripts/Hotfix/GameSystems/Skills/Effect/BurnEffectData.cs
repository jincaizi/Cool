using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    [System.Serializable]
    public class BurnEffectData : EffectData
    {
        [Header("=== Burn Properties ===")]
        [Tooltip("每跳伤害 = 施法者攻击力 × 此比例")]
        [SerializeField] private float _attackPowerRatio = 0.2f;
        public float AttackPowerRatio => _attackPowerRatio;

        [Tooltip("施法者属性不可用时的每跳兜底伤害")]
        [SerializeField] private float _baseDamage = 5f;
        public float BaseDamage => _baseDamage;

        public BurnEffectData()
        {
            _type = EffectType.Burn;
            _effectId = "burn";
            _isTickEffect = true;
            _tickInterval = 1f;
        }

        public void Initialize(
            float attackPowerRatio,
            float baseDamage,
            float duration,
            float tickInterval)
        {
            _attackPowerRatio = attackPowerRatio;
            _baseDamage = baseDamage;
            _duration = duration;
            _tickInterval = tickInterval;
            _isTickEffect = true;
        }

        public override string GetDisplayName()
            => $"Burn: {_attackPowerRatio * 100f}% AP / {_tickInterval}s";

        public override void OnTick(IEffectTarget caster, IEffectTarget target, int stacks)
        {
            float attackPower = caster?.Stats?.GetAttribute(AttributeType.AttackPower) ?? 0f;
            float damage = attackPower > 0f
                ? attackPower * _attackPowerRatio
                : _baseDamage;

            damage *= Mathf.Max(1, stacks);
            target.ApplyDamageOverTime(damage);
        }
    }
}
