using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class MovementValidatorTests
    {
        [Test]
        public void Speed_Over_Limit_Clamped_And_Corrected()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), 0f, 30f, 0, 1_000_000L);
            Assert.IsTrue(result.NeedsCorrection);
            Assert.AreEqual(MovementValidator.MaxSpeed, result.ClampedSpeed, 0.0001f);
        }

        [Test]
        public void Teleport_Over_Limit_Corrected_To_Plausible_Position()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(100, 0, 0), 0f, 5f, 0, 10_000_000L);
            Assert.IsTrue(result.NeedsCorrection);
            Assert.GreaterOrEqual(result.AuthoritativePosition.X, 14.9f);
            Assert.LessOrEqual(result.AuthoritativePosition.X, 15.1f);
        }

        [Test]
        public void Teleport_Under_Distance_Threshold_Accepted()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(1, 0, 0), 0f, 5f, 0, 1_000_000L);
            Assert.IsFalse(result.NeedsCorrection);
            Assert.AreEqual(5f, result.ClampedSpeed, 0.0001f);
        }

        [Test]
        public void Vertical_Jump_Over_Height_Corrected()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(0, 10, 0), 0f, 0f, 0, 1_000_000L);
            Assert.IsTrue(result.NeedsCorrection);
        }

        [Test]
        public void Zero_TimeDelta_No_Teleport_Check()
        {
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(100, 0, 0), 0f, 5f, 100, 100);
            Assert.IsFalse(result.NeedsCorrection);
        }

        [Test]
        public void NonFinite_Input_Corrected_To_Previous_Position()
        {
            // Infinity 坐标（损坏/恶意包）→ 归一化 NaN 风险：守卫必须纠偏回 prev，不广播 NaN
            var result = MovementValidator.Validate(
                new Vec3(0, 0, 0), new Vec3(float.PositiveInfinity, 0, 0), 0f, 5f, 0, 1_000_000L);
            Assert.IsTrue(result.NeedsCorrection);
            Assert.AreEqual(0f, result.AuthoritativePosition.X, 0.0001f);
            Assert.AreEqual(0f, result.AuthoritativePosition.Y, 0.0001f);
            Assert.AreEqual(0f, result.AuthoritativePosition.Z, 0.0001f);
        }
    }
}
