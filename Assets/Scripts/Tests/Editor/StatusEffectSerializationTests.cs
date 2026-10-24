using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusEffectSerializationTests
    {
        [Test]
        public void test_effect_block_apply_effects_supports_polymorphic_serialization()
        {
            // Arrange
            var field = typeof(EffectBlock).GetField(
                "_applyEffects", BindingFlags.NonPublic | BindingFlags.Instance);

            // Assert
            Assert.IsNotNull(field, "EffectBlock._applyEffects 字段不存在");
            Assert.IsTrue(field.IsDefined(typeof(SerializeReference), true),
                "EffectBlock._applyEffects 必须标记 [SerializeReference]，否则冰冻/灼烧无法配置在资产上");
        }

        [Test]
        public void test_stun_effect_without_status_controller_does_not_throw()
        {
            // Arrange
            var target = new TestHelpers.FakeTarget(null);
            var effect = new StunEffectData { SetDuration = 1f };

            // Act & Assert
            Assert.DoesNotThrow(() => effect.Apply(null, target));
            Assert.DoesNotThrow(() => effect.Remove(null, target));
        }
    }
}
