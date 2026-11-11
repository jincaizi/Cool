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

        [Test]
        public void test_aim_zoom_eases_toward_aim_distance()
        {
            var go = new GameObject("CameraAimZoomIn");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.AimZoomActive = true;

                for (int i = 0; i < 120; i++) cam.TickZoom(1f / 60f);

                Assert.AreEqual(11.5f, cam.CurrentDistance, 0.05f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_aim_zoom_returns_to_distance_when_disabled()
        {
            var go = new GameObject("CameraAimZoomOut");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.AimZoomActive = true;
                for (int i = 0; i < 120; i++) cam.TickZoom(1f / 60f);

                cam.AimZoomActive = false;
                for (int i = 0; i < 120; i++) cam.TickZoom(1f / 60f);

                Assert.AreEqual(8f, cam.CurrentDistance, 0.05f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_aim_zoom_target_is_clamped_to_min_camera_distance()
        {
            var go = new GameObject("CameraAimZoomClamp");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.AimDistance = 0.1f;
                cam.AimZoomActive = true;

                Assert.GreaterOrEqual(cam.TargetDistance, 0.8f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void test_snap_to_target_resets_aim_zoom_distance()
        {
            var go = new GameObject("CameraAimZoomSnap");
            var targetGo = new GameObject("CameraAimZoomTarget");
            try
            {
                var cam = go.AddComponent<ThirdPersonCameraController>();
                cam.Target = targetGo.transform;
                cam.AimZoomActive = true;
                for (int i = 0; i < 120; i++) cam.TickZoom(1f / 60f);

                cam.SnapToTarget();

                Assert.AreEqual(8f, cam.CurrentDistance, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(targetGo);
            }
        }
    }
}
