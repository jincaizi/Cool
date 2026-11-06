using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillInterruptionMatrixCancelTests
    {
        [Test]
        public void test_player_cancel_allowed_in_casting_and_charging_only()
        {
            // Arrange
            var matrix = new SkillInterruptionMatrix();
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            TestHelpers.SetField(data, "_skillType", SkillType.Projectile);

            // Assert
            Assert.IsTrue(matrix.CanBeInterruptedInState(data, SkillSubState.Casting, InterruptionSource.PlayerCancel));
            Assert.IsTrue(matrix.CanBeInterruptedInState(data, SkillSubState.Charging, InterruptionSource.PlayerCancel));
            Assert.IsFalse(matrix.CanBeInterruptedInState(data, SkillSubState.Execution, InterruptionSource.PlayerCancel));
        }
    }
}
