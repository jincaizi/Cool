using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class SwordQiDamageBlockCloneTests
    {
        [Test]
        public void test_damage_block_clone_returns_independent_instance()
        {
            // Arrange
            var original = DamageBlock.CreateDefault(50f, 1f);

            // Act
            var copy = original.Clone();

            // Assert
            Assert.IsNotNull(copy);
            Assert.AreNotSame(original, copy, "Clone 必须返回独立实例");
        }

        [Test]
        public void test_damage_block_clone_preserves_runtime_context()
        {
            // Arrange
            var original = DamageBlock.CreateDefault(50f, 1f);
            original.SkillId = 10002;
            original.ComboIndex = 2;
            original.CalculatedDamage = 123f;

            // Act
            var copy = original.Clone();

            // Assert
            Assert.AreEqual(10002, copy.SkillId);
            Assert.AreEqual(2, copy.ComboIndex);
            Assert.AreEqual(123f, copy.CalculatedDamage);
        }

        [Test]
        public void test_damage_block_clone_mutation_does_not_affect_original()
        {
            // Arrange
            var original = DamageBlock.CreateDefault(50f, 1f);

            // Act
            var copy = original.Clone();
            copy.CalculatedDamage = 999f;

            // Assert
            Assert.AreEqual(0f, original.CalculatedDamage, "修改拷贝不得影响原实例");
        }
    }
}
