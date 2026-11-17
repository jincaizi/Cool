using Cool.Protocol;
using Cool.SyncCore;
using NUnit.Framework;

namespace Cool.SyncCore.Tests
{
    public class MovementBudgetMathTests
    {
        [Test]
        public void NormalDistance_Composes_Walk_Defend_And_Status()
        {
            float distance = MovementBudgetMath.NormalDistance(
                LocomotionMode.Walk, defending: true, statusScale: 0.7f, seconds: 0.1f);

            Assert.AreEqual(5f * 0.4f * 0.7f * 0.1f, distance, 0.0001f);
        }

        [Test]
        public void ClampHorizontal_Preserves_Direction_And_Length()
        {
            var clamped = MovementBudgetMath.ClampHorizontal(
                new Vec3(0f, 0f, 0f), new Vec3(3f, 0f, 4f), allowedDistance: 2.5f);

            Assert.AreEqual(1.5f, clamped.X, 0.0001f);
            Assert.AreEqual(2f, clamped.Z, 0.0001f);
        }

        [Test]
        public void MaxJumpY_Follows_Projectile_Motion_To_Apex()
        {
            float apex = 10.95f / 30f;
            float expected = 0.3f + 10.95f * 10.95f / (2f * 30f);

            Assert.AreEqual(expected,
                MovementBudgetMath.MaxJumpY(0.3f, 10.95f, -30f, apex), 0.0001f);
        }

        [Test]
        public void JumpInertiaDistance_Caps_At_Remaining()
        {
            Assert.AreEqual(2f,
                MovementBudgetMath.JumpInertiaDistance(10f, 0.5f, 2f), 0.0001f);
        }

        [Test]
        public void ClampHorizontal_Freezes_Horizontal_On_NonFinite_Budget_And_Keeps_Y()
        {
            var clamped = MovementBudgetMath.ClampHorizontal(
                new Vec3(0f, 0f, 0f), new Vec3(3f, 1.7f, 4f), float.NaN);

            Assert.AreEqual(0f, clamped.X, 0.0001f);
            Assert.AreEqual(1.7f, clamped.Y, 0.0001f);
            Assert.AreEqual(0f, clamped.Z, 0.0001f);
        }
    }
}
