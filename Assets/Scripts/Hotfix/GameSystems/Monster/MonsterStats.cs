using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterStats
    {
        private readonly Dictionary<AttributeType, float> _attributes = new();

        public float HP => _attributes[AttributeType.Health];
        public float MaxHP { get; private set; }
        public float AttackPower => _attributes[AttributeType.AttackPower];
        public float Defense => _attributes[AttributeType.Defense];
        public bool IsDead => HP <= 0;

        public event Action OnDeath;
        public event Action<float, float> OnHPChanged;

        public MonsterStats(MonsterConfig config)
        {
            _attributes[AttributeType.Health] = config.MaxHP;
            _attributes[AttributeType.AttackPower] = config.AttackPower;
            _attributes[AttributeType.Defense] = config.Defense;
            _attributes[AttributeType.Speed] = config.MoveSpeed;
            MaxHP = config.MaxHP;
        }

        // ApplyDamage is the new damage entry point called by DamagePipeline.
        // Unlike TakeDamage, this is a pure HP subtraction — no defense formula,
        // no death event emission. Defense is handled upstream by DamagePipeline.
        // Death is handled by MonsterEntity after the pipeline completes.
        public void ApplyDamage(float damage)
        {
            if (IsDead || damage <= 0) return;

            _attributes[AttributeType.Health] -= damage;
            if (_attributes[AttributeType.Health] < 0)
                _attributes[AttributeType.Health] = 0;

            OnHPChanged?.Invoke(HP, MaxHP);

            if (HP <= 0)
            {
                _attributes[AttributeType.Health] = 0;
                OnDeath?.Invoke();
            }
        }

        public void TakeDamage(DamageBlock damageData)
        {
            if (IsDead) return;

            float def = _attributes[AttributeType.Defense];
            float finalDamage = UnityEngine.Mathf.Max(1, damageData.BaseDamage - def * 0.3f);

            _attributes[AttributeType.Health] -= finalDamage;
            OnHPChanged?.Invoke(HP, MaxHP);

            if (HP <= 0)
            {
                _attributes[AttributeType.Health] = 0;
                OnDeath?.Invoke();
            }
        }
    }
}
