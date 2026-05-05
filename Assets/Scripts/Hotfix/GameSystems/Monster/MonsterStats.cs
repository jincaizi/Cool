using System;
using System.Collections.Generic;
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

        public void TakeDamage(DamageData damageData)
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
