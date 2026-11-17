using Hotfix.GameSystems.Sys3C.Character;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.Sys3C
{
    public class MovementCompositionTests
    {
        [Test]
        public void ComposeMoveSpeed_Multiplies_Base_Defend_Skill_And_Status()
        {
            Assert.AreEqual(5f * 0.4f * 0.5f * 0.7f,
                CharacterController.ComposeMoveSpeed(5f, true, 0.5f, 0.7f), 0.0001f);
        }

        [Test]
        public void ComposeMoveSpeed_Clamps_Invalid_Scale_To_Zero()
        {
            Assert.AreEqual(0f, CharacterController.ComposeMoveSpeed(5f, false, 1f, float.NaN), 0.0001f);
            Assert.AreEqual(0f, CharacterController.ComposeMoveSpeed(5f, false, 1f, -1f), 0.0001f);
        }
    }
}
