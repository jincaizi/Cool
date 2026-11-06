using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillAimMathTests
    {
        [Test]
        public void test_aim_direction_is_camera_relative()
        {
            // 相机朝 +Z；摇杆右推 → 世界 +X
            var dir = SkillAimMath.CameraRelativeDirection(new Vector2(1f, 0f), Vector3.forward);
            Assert.AreEqual(1f, dir.x, 0.001f);
            Assert.AreEqual(0f, dir.z, 0.001f);
        }

        [Test]
        public void test_aim_direction_uses_yaw_only()
        {
            // 相机带俯仰：方向仍应水平
            var dir = SkillAimMath.CameraRelativeDirection(new Vector2(0f, 1f),
                new Vector3(0f, -0.5f, 0.8f));
            Assert.AreEqual(0f, dir.y, 0.001f);
            Assert.AreEqual(1f, dir.z, 0.01f);
        }

        [Test]
        public void test_aim_distance_maps_magnitude()
        {
            Assert.AreEqual(2f, SkillAimMath.DistanceFromMagnitude(0f, 2f, 15f), 0.001f);
            Assert.AreEqual(8.5f, SkillAimMath.DistanceFromMagnitude(0.5f, 2f, 15f), 0.001f);
            Assert.AreEqual(15f, SkillAimMath.DistanceFromMagnitude(1f, 2f, 15f), 0.001f);
        }

        [Test]
        public void test_aim_clamp_keeps_direction_and_max_distance()
        {
            var origin = Vector3.zero;
            var target = new Vector3(0f, 2f, 30f);
            var clamped = SkillAimMath.ClampToRange(origin, target, 15f);
            Assert.AreEqual(15f, clamped.z, 0.001f);
            Assert.AreEqual(2f, clamped.y, 0.001f);
        }
    }
}
