using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 测试辅助：反射设置私有字段 + IEffectTarget / IEffectStats 假实现
    /// </summary>
    public static class TestHelpers
    {
        public static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        public static object GetField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"Field {fieldName} not found on {target.GetType().Name}");
            return field.GetValue(target);
        }

        public class FakeStatusController : IStatusController
        {
            public float StunDuration;
            public int AddCalls;
            public int RemoveCalls;

            public void AddStun(float duration)
            {
                StunDuration = Mathf.Max(StunDuration, duration);
                AddCalls++;
            }

            public void RemoveStun()
            {
                StunDuration = 0f;
                RemoveCalls++;
            }
        }

        public class FakeStats : IEffectStats
        {
            public float AttackPower;

            public float GetAttribute(AttributeType type)
                => type == AttributeType.AttackPower ? AttackPower : 0f;

            public float GetMaxHealth() => 1000f;

            public void AddModifier(AttributeType type, string id, float value, ModifierType modType) { }

            public void RemoveModifier(AttributeType type, string id) { }
        }

        public class FakeTarget : IEffectTarget, IDamageable
        {
            public Transform transform { get; }
            public IEffectStats Stats { get; }
            public IShieldSystem ShieldSystem => null;
            public IPhysicsSystem PhysicsSystem => null;
            public IStatusController StatusController { get; }
            public bool IsAlive { get; set; }
            public int DotDamageTotal { get; private set; }

            public FakeTarget(
                Transform t,
                IStatusController status = null,
                IEffectStats stats = null,
                bool isAlive = true)
            {
                transform = t;
                StatusController = status;
                Stats = stats;
                IsAlive = isAlive;
            }

            public void Heal(float amount) { }

            public void ApplyDamageOverTime(float amount)
            {
                DotDamageTotal += Mathf.RoundToInt(amount);
            }

            public Transform Transform => transform;

            public void TakeDamage(DamageBlock data, Vector3 hitDirection) { }

            public void TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact) { }
        }

        public class CountingEffect : EffectData
        {
            public int ApplyCount;
            public int TickCount;
            public int RemoveCount;
            public int LastStacks;

            public CountingEffect(
                float duration = 0f,
                bool isTick = false,
                float interval = 1f,
                StackingRule stackingRule = StackingRule.Refresh,
                int maxStacks = 1)
            {
                _duration = duration;
                _isTickEffect = isTick;
                _tickInterval = interval;
                _stackingRule = stackingRule;
                _maxStacks = maxStacks;
                _effectId = "counting";
            }

            public override void Apply(IEffectTarget caster, IEffectTarget target)
                => ApplyCount++;

            public override void OnTick(IEffectTarget caster, IEffectTarget target, int stacks)
            {
                TickCount++;
                LastStacks = stacks;
            }

            public override void Remove(IEffectTarget caster, IEffectTarget target)
                => RemoveCount++;
        }
    }
}
