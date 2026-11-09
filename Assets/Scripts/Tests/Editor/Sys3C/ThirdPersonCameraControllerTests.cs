using Hotfix.GameSystems.Sys3C.Camera;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class ThirdPersonCameraControllerTests
    {
        [Test]
        public void test_mobile_pitch_limits_clamp_to_minus10_and_25()
        {
            var go = new GameObject("CameraMobilePitch");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.UseMobilePitchLimits = true;

                cam.HandleRotationInput(new Vector2(0f, -1000f));
                Assert.AreEqual(25f, cam.VerticalAngle, 0.001f);

                cam.HandleRotationInput(new Vector2(0f, 1000f));
                Assert.AreEqual(-10f, cam.VerticalAngle, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_desktop_pitch_limits_stay_minus30_and_60()
        {
            var go = new GameObject("CameraDesktopPitch");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.UseMobilePitchLimits = false;

                cam.HandleRotationInput(new Vector2(0f, -1000f));
                Assert.AreEqual(60f, cam.VerticalAngle, 0.001f);

                cam.HandleRotationInput(new Vector2(0f, 1000f));
                Assert.AreEqual(-30f, cam.VerticalAngle, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
