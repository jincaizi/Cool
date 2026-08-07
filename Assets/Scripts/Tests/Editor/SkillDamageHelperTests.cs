using System.Reflection;
using DataDefinition;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillDamageHelperTests
    {
        private static readonly FieldInfo InstanceField =
            typeof(GameSettings).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);

        private GameSettings _injectedSettings;

        [SetUp]
        public void SetUp()
        {
            // 钳制全局伤害波动为 0，保证 CalculatedDamage 断言确定性
            // （即使 GameSettings 资源后续调大 DamageFluctuation 也不 flaky）
            if (_injectedSettings != null)
                Object.DestroyImmediate(_injectedSettings);
            _injectedSettings = ScriptableObject.CreateInstance<GameSettings>();
            _injectedSettings.DamageFluctuation = 0f;
            InstanceField.SetValue(null, _injectedSettings);
        }

        [TearDown]
        public void TearDown()
        {
            InstanceField.SetValue(null, null);
            if (_injectedSettings != null)
            {
                Object.DestroyImmediate(_injectedSettings);
                _injectedSettings = null;
            }
        }

        private class FakeStats : IEffectStats
        {
            public float GetAttribute(AttributeType type) => 100f;
            public float GetMaxHealth() => 1000f;
            public void AddModifier(AttributeType type, string id, float value, ModifierType modType) { }
            public void RemoveModifier(AttributeType type, string id) { }
        }

        [Test]
        public void test_skill_damage_helper_fill_context_sets_runtime_fields()
        {
            // Arrange
            var damage = DamageBlock.CreateDefault(50f, 0f); // base 50 + 100*0 = 50

            // Act
            SkillDamageHelper.FillDamageContext(damage, 10002, 2, null, new FakeStats());

            // Assert
            Assert.AreEqual(10002, damage.SkillId);
            Assert.AreEqual(2, damage.ComboIndex);
            Assert.AreEqual(50f, damage.CalculatedDamage, 0.001f);
        }

        [Test]
        public void test_skill_damage_helper_effect_knockback_overrides_damage_default()
        {
            // Arrange
            var damage = DamageBlock.CreateDefault(50f, 0f);
            damage.KnockbackForce = 1f;
            var effect = new EffectBlock();
            TestHelpers.SetField(effect, "_knockbackForce", 8f);

            // Act
            SkillDamageHelper.FillDamageContext(damage, 10002, 2, effect, new FakeStats());

            // Assert
            Assert.AreEqual(8f, damage.KnockbackForce, "EffectBlock 击退应覆盖 DamageBlock 默认");
        }

        [Test]
        public void test_skill_damage_helper_effect_knockback_zero_keeps_damage_default()
        {
            // Arrange
            var damage = DamageBlock.CreateDefault(50f, 0f);
            damage.KnockbackForce = 1f;
            var effect = new EffectBlock(); // KnockbackForce = 0

            // Act
            SkillDamageHelper.FillDamageContext(damage, 10002, 2, effect, new FakeStats());

            // Assert
            Assert.AreEqual(1f, damage.KnockbackForce, "击退为 0 时保留 DamageBlock 默认值");
        }

        [Test]
        public void test_skill_damage_helper_null_damage_is_noop()
        {
            // Act & Assert（不应抛异常）
            Assert.DoesNotThrow(() => SkillDamageHelper.FillDamageContext(null, 1, 1, null, null));
        }
    }
}
