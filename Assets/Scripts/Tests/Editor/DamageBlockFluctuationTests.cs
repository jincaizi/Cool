using System.Reflection;
using DataDefinition;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class DamageBlockFluctuationTests
    {
        private static readonly FieldInfo InstanceField =
            typeof(GameSettings).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        // 注入测试用 GameSettings（不依赖 Resources 资产），TearDown 恢复 null
        private static void SetFluctuation(float value)
        {
            InstanceField.SetValue(null, null);
            var settings = ScriptableObject.CreateInstance<GameSettings>();
            settings.DamageFluctuation = value;
            InstanceField.SetValue(null, settings);
        }

        private static void SetDamageField(DamageBlock block, string name, object value)
        {
            typeof(DamageBlock).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(block, value);
        }

        [TearDown]
        public void TearDown()
        {
            InstanceField.SetValue(null, null);
        }

        [Test]
        public void ZeroFluctuation_ReturnsExactBaseDamage()
        {
            SetFluctuation(0f);
            var block = DamageBlock.CreateDefault(100f);
            Assert.AreEqual(100f, block.CalculateFinalDamage(null), 0.0001f);
        }

        [Test]
        public void ZeroFluctuation_PreservesAttributeScaling()
        {
            SetFluctuation(0f);
            var block = DamageBlock.CreateDefault(100f, 1f);
            var stats = new StubStats(attackPower: 50f);
            Assert.AreEqual(150f, block.CalculateFinalDamage(stats), 0.0001f);
        }

        [Test]
        public void Fluctuation_StaysWithinBounds_AndActuallyVaries()
        {
            SetFluctuation(0.1f);
            var block = DamageBlock.CreateDefault(100f);

            bool varied = false;
            for (int i = 0; i < 200; i++)
            {
                float d = block.CalculateFinalDamage(null);
                Assert.That(d, Is.InRange(90f, 110f), "iteration " + i);
                if (Mathf.Abs(d - 100f) > 0.01f) varied = true;
            }
            Assert.IsTrue(varied, "200 次 roll 应至少出现一次偏离基础值");
        }

        [Test]
        public void Fluctuation_ClampsToMinimumOne()
        {
            SetFluctuation(0.5f);
            var block = DamageBlock.CreateDefault(1f);

            for (int i = 0; i < 200; i++)
            {
                float d = block.CalculateFinalDamage(null);
                Assert.That(d, Is.GreaterThanOrEqualTo(1f), "iteration " + i);
                Assert.That(d, Is.LessThanOrEqualTo(1.5f), "iteration " + i);
            }
        }

        [Test]
        public void Fluctuation_AppliesToCritDamage()
        {
            SetFluctuation(0.1f);
            var block = new DamageBlock();
            SetDamageField(block, "_baseDamage", 100f);
            SetDamageField(block, "_criticalRateBonus", 1f); // 必暴击，基础 1.5 倍

            for (int i = 0; i < 200; i++)
            {
                float d = block.CalculateFinalDamage(null);
                Assert.That(d, Is.InRange(135f, 165f), "iteration " + i);
                Assert.IsTrue(block.WasCritical, "iteration " + i);
            }
        }

        // IEffectStats 测试桩：只实现 AttackPower，其余返回 0
        private class StubStats : IEffectStats
        {
            private readonly float _attackPower;
            public StubStats(float attackPower) { _attackPower = attackPower; }

            public float GetAttribute(AttributeType type)
                => type == AttributeType.AttackPower ? _attackPower : 0f;
            public float GetMaxHealth() => 0f;
            public void AddModifier(AttributeType type, string id, float value, ModifierType modType) { }
            public void RemoveModifier(AttributeType type, string id) { }
        }
    }
}
