using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// Character Stats 适配器 - 实现 IEffectStats 接口
    /// </summary>
    public class CharacterStatsAdapter : IEffectStats
    {
        // 属性存储
        private readonly Dictionary<AttributeType, float> _baseAttributes = new();
        private readonly Dictionary<AttributeType, Dictionary<string, Modifier>> _modifiers = new();

        public CharacterStatsAdapter()
        {
            // 初始化默认属性
            _baseAttributes[AttributeType.AttackPower] = 100f;
            _baseAttributes[AttributeType.SpellPower] = 50f;
            _baseAttributes[AttributeType.Health] = 1000f;
            _baseAttributes[AttributeType.Defense] = 50f;
            _baseAttributes[AttributeType.Resistance] = 30f;
            _baseAttributes[AttributeType.CriticalRate] = 0.05f;
            _baseAttributes[AttributeType.CriticalDamage] = 1.5f;
            _baseAttributes[AttributeType.Speed] = 1f;

            // 初始化修饰符字典
            foreach (AttributeType type in Enum.GetValues(typeof(AttributeType)))
            {
                _modifiers[type] = new Dictionary<string, Modifier>();
            }
        }

        public float GetAttribute(AttributeType type)
        {
            float value = _baseAttributes.TryGetValue(type, out var baseVal) ? baseVal : 0f;

            // 应用修饰符
            if (_modifiers.TryGetValue(type, out var mods))
            {
                float flatBonus = 0f;
                float percentAdd = 0f;
                float percentMult = 1f;

                foreach (var mod in mods.Values)
                {
                    switch (mod.Type)
                    {
                        case ModifierType.Flat:
                            flatBonus += mod.Value;
                            break;
                        case ModifierType.PercentAdd:
                            percentAdd += mod.Value;
                            break;
                        case ModifierType.PercentMult:
                            percentMult *= (1f + mod.Value);
                            break;
                    }
                }

                value = (value + flatBonus) * (1f + percentAdd) * percentMult;
            }

            return value;
        }

        public float GetMaxHealth() => GetAttribute(AttributeType.Health);

        public float GetCriticalChance() => GetAttribute(AttributeType.CriticalRate);

        public float GetCriticalDamage() => GetAttribute(AttributeType.CriticalDamage);

        public void AddModifier(AttributeType type, string id, float value, ModifierType modType)
        {
            if (!_modifiers.ContainsKey(type))
                _modifiers[type] = new Dictionary<string, Modifier>();

            _modifiers[type][id] = new Modifier { Type = modType, Value = value };
        }

        public void RemoveModifier(AttributeType type, string id)
        {
            if (_modifiers.TryGetValue(type, out var mods))
            {
                mods.Remove(id);
            }
        }

        /// <summary>
        /// 设置基础属性值
        /// </summary>
        public void SetBaseAttribute(AttributeType type, float value)
        {
            _baseAttributes[type] = value;
        }

        /// <summary>
        /// 获取最终属性值
        /// </summary>
        public float GetFinalAttribute(AttributeType type) => GetAttribute(type);

        private struct Modifier
        {
            public ModifierType Type;
            public float Value;
        }
    }

    /// <summary>
    /// 护盾系统适配器
    /// </summary>
    public class ShieldSystemAdapter : IShieldSystem
    {
        private readonly Dictionary<string, ShieldInstance> _activeShields = new();

        public event Action<string, float> OnShieldAdded;
        public event Action<string> OnShieldRemoved;

        public void AddShield(string id, float amount, float duration)
        {
            if (_activeShields.TryGetValue(id, out var existing))
            {
                existing.Amount += amount;
                existing.Duration = Mathf.Max(existing.Duration, duration);
            }
            else
            {
                _activeShields[id] = new ShieldInstance
                {
                    Id = id,
                    Amount = amount,
                    Duration = duration,
                    RemainingTime = duration
                };
            }

            OnShieldAdded?.Invoke(id, amount);
            Debug.Log($"[ShieldSystem] AddShield: {id} +{amount}");
        }

        public void RemoveShield(string id)
        {
            if (_activeShields.Remove(id))
            {
                OnShieldRemoved?.Invoke(id);
                Debug.Log($"[ShieldSystem] RemoveShield: {id}");
            }
        }

        /// <summary>
        /// 吸收伤害，返回实际吸收量
        /// </summary>
        public float AbsorbDamage(float damage)
        {
            float totalAbsorbed = 0f;
            float remainingDamage = damage;

            var shieldsToRemove = new List<string>();

            foreach (var kvp in _activeShields)
            {
                if (remainingDamage <= 0) break;

                var shield = kvp.Value;
                if (shield.Amount >= remainingDamage)
                {
                    totalAbsorbed += remainingDamage;
                    shield.Amount -= remainingDamage;
                    remainingDamage = 0f;
                    _activeShields[kvp.Key] = shield;

                    if (shield.Amount <= 0)
                        shieldsToRemove.Add(kvp.Key);
                }
                else
                {
                    totalAbsorbed += shield.Amount;
                    remainingDamage -= shield.Amount;
                    shieldsToRemove.Add(kvp.Key);
                }
            }

            foreach (var id in shieldsToRemove)
            {
                RemoveShield(id);
            }

            return totalAbsorbed;
        }

        /// <summary>
        /// 获取总护盾值
        /// </summary>
        public float GetTotalShield()
        {
            float total = 0f;
            foreach (var kvp in _activeShields)
            {
                total += kvp.Value.Amount;
            }
            return total;
        }

        /// <summary>
        /// 更新护盾持续时间
        /// </summary>
        public void Update(float deltaTime)
        {
            var expiredShields = new List<string>();

            foreach (var kvp in _activeShields)
            {
                var shield = kvp.Value;
                shield.RemainingTime -= deltaTime;
                _activeShields[kvp.Key] = shield;

                if (shield.RemainingTime <= 0)
                {
                    expiredShields.Add(kvp.Key);
                }
            }

            foreach (var id in expiredShields)
            {
                RemoveShield(id);
            }
        }

        private class ShieldInstance
        {
            public string Id;
            public float Amount;
            public float Duration;
            public float RemainingTime;
        }
    }

    /// <summary>
    /// 物理系统适配器
    /// </summary>
    public class PhysicsSystemAdapter : IPhysicsSystem
    {
        private CharacterController _characterController;

        public PhysicsSystemAdapter(CharacterController characterController)
        {
            _characterController = characterController;
        }

        public void ApplyKnockback(Vector3 direction, float force)
        {
            Debug.Log($"[PhysicsSystem] ApplyKnockback: dir={direction}, force={force}");
            // TODO: 实现击退效果
            // 可以设置角色的速度或者通知 CharacterController 应用特殊移动
        }

        /// <summary>
        /// 应用击飞（垂直方向更强的击退）
        /// </summary>
        public void ApplyLaunch(Vector3 direction, float force, float upwardForce)
        {
            Debug.Log($"[PhysicsSystem] ApplyLaunch: dir={direction}, force={force}, upward={upwardForce}");
            // TODO: 实现击飞效果
        }
    }

    /// <summary>
    /// 状态控制器适配器
    /// </summary>
    public class StatusControllerAdapter : IStatusController
    {
        private float _stunDuration;
        private float _silenceDuration;
        private bool _isStunned;
        private bool _isSilenced;

        public event Action OnStunAdded;
        public event Action OnStunRemoved;
        public event Action OnSilenceAdded;
        public event Action OnSilenceRemoved;

        public bool IsStunned => _isStunned && _stunDuration > 0;
        public bool IsSilenced => _isSilenced && _silenceDuration > 0;

        public void AddStun(float duration)
        {
            _stunDuration = Mathf.Max(_stunDuration, duration);
            _isStunned = true;
            OnStunAdded?.Invoke();
            Debug.Log($"[StatusController] AddStun: {duration}s");
        }

        public void RemoveStun()
        {
            _stunDuration = 0f;
            _isStunned = false;
            OnStunRemoved?.Invoke();
            Debug.Log($"[StatusController] RemoveStun");
        }

        public void AddSilence(float duration)
        {
            _silenceDuration = Mathf.Max(_silenceDuration, duration);
            _isSilenced = true;
            OnSilenceAdded?.Invoke();
            Debug.Log($"[StatusController] AddSilence: {duration}s");
        }

        public void RemoveSilence()
        {
            _silenceDuration = 0f;
            _isSilenced = false;
            OnSilenceRemoved?.Invoke();
        }

        /// <summary>
        /// 更新状态持续时间
        /// </summary>
        public void Update(float deltaTime)
        {
            if (_stunDuration > 0)
            {
                _stunDuration -= deltaTime;
                if (_stunDuration <= 0)
                {
                    RemoveStun();
                }
            }

            if (_silenceDuration > 0)
            {
                _silenceDuration -= deltaTime;
                if (_silenceDuration <= 0)
                {
                    RemoveSilence();
                }
            }
        }

        /// <summary>
        /// 清除所有状态效果
        /// </summary>
        public void ClearAll()
        {
            _stunDuration = 0f;
            _silenceDuration = 0f;
            _isStunned = false;
            _isSilenced = false;
        }
    }

    /// <summary>
    /// Character Controller 扩展 - 添加技能系统适配器
    /// </summary>
    public static class CharacterControllerExtensions
    {
        /// <summary>
        /// 为 CharacterController 添加技能系统适配器
        /// </summary>
        public static void AddSkillSystemAdapters(this CharacterController controller)
        {
            // 添加各个适配器（通过反射或接口方式）
            // 这里我们使用简单的属性注入方式
        }
    }
}