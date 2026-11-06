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

        // ====================================================================================
        // 默认落点（点按）：选中目标优先，超程钳制；无目标 → 前方最大射程。
        // ====================================================================================

        private static readonly object[] DefaultTargetCases =
        {
            // scenario, owner, forward, selected, maxDistance, expected
            new object[]
            {
                "selected-inside-range", new Vector3(1f, 0f, 2f), Vector3.forward,
                new Vector3(4f, 1.5f, 6f), 15f, new Vector3(4f, 1.5f, 6f)
            },
            new object[]
            {
                "selected-beyond-range", Vector3.zero, Vector3.forward,
                new Vector3(0f, 1f, 30f), 15f, new Vector3(0f, 1f, 15f)
            },
            new object[]
            {
                "no-selected-pitched-forward", new Vector3(2f, 0f, 3f), new Vector3(0f, -0.5f, 1f),
                null, 15f, new Vector3(2f, 0f, 18f)
            },
        };

        [TestCaseSource(nameof(DefaultTargetCases))]
        public void test_default_target_resolution(string scenario, Vector3 owner, Vector3 forward,
            Vector3? selected, float maxDistance, Vector3 expected)
        {
            // Act
            var target = SkillAimMath.ResolveDefaultTarget(owner, forward, maxDistance, selected);

            // Assert
            Assert.AreEqual(expected.x, target.x, 0.001f, scenario);
            Assert.AreEqual(expected.y, target.y, 0.001f, scenario);
            Assert.AreEqual(expected.z, target.z, 0.001f, scenario);
        }

        [Test]
        public void test_report_distance_margin_is_5cm()
        {
            Assert.AreEqual(0.05f, SkillAimMath.ReportDistanceMargin, 0.0001f,
                "上报落点必须预留 5cm 余量，服务端只容忍 +1cm");
        }
    }
}
