using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Stamina;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameSys.PlayModeTests
{
    public class StaminaRingTests
    {
        private GameObject _canvasGo;
        private GameObject _cameraGo;
        private GameObject _targetGo;
        private Canvas _canvas;
        private Camera _camera;
        private StaminaSystem _stamina;
        private StaminaRingRenderer _ring;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _canvasGo = new GameObject("StaminaRingTestCanvas");
            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasGo.AddComponent<CanvasScaler>();
            _canvasGo.AddComponent<GraphicRaycaster>();

            _cameraGo = new GameObject("StaminaRingTestCamera");
            _camera = _cameraGo.AddComponent<Camera>();
            _camera.transform.position = new Vector3(0f, 1f, -5f);
            _camera.transform.rotation = Quaternion.identity;

            _targetGo = new GameObject("StaminaRingTarget");
            _targetGo.transform.position = Vector3.zero;

            _stamina = new StaminaSystem(StaminaConfig.Default);
            _ring = new StaminaRingRenderer(_canvas);
            _ring.Bind(_targetGo.transform, _stamina, () => true);
            yield return null; // 等 CanvasScaler 生效
        }

        [TearDown]
        public void TearDown()
        {
            _ring?.Cleanup();
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            if (_cameraGo != null) Object.Destroy(_cameraGo);
            if (_targetGo != null) Object.Destroy(_targetGo);
        }

        private GameObject Root =>
            (GameObject)typeof(StaminaRingRenderer)
                .GetField("_root", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_ring);

        private Image Track =>
            (Image)typeof(StaminaRingRenderer)
                .GetField("_track", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_ring);

        private Image Fill =>
            (Image)typeof(StaminaRingRenderer)
                .GetField("_fill", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(_ring);

        [Test]
        public void test_stamina_ring_fill_matches_ratio()
        {
            _stamina.TryConsume(40f);
            _ring.Tick(_camera, 0.1f);

            Assert.AreEqual(0.6f, _ring.FillAmount, 0.01f);
        }

        [Test]
        public void test_stamina_ring_hidden_at_full_visible_when_not_full()
        {
            _ring.Tick(_camera, 0.3f);
            Assert.IsFalse(_ring.IsVisible, "满精力应隐藏");

            _stamina.TryConsume(40f);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsVisible, "未满应显示");
        }

        [Test]
        public void test_stamina_ring_position_is_right_of_head_and_scales_with_canvas()
        {
            _stamina.TryConsume(40f);
            _ring.Tick(_camera, 0.1f);

            Vector3 head = _targetGo.transform.position + Vector3.up * 1.7f;
            Vector3 screen = _camera.WorldToScreenPoint(head);
            float scale = _canvas.scaleFactor;

            Assert.AreEqual(screen.x + 64f * scale, Root.transform.position.x, 1f);
            Assert.AreEqual(screen.y + 24f * scale, Root.transform.position.y, 1f);
        }

        [Test]
        public void test_stamina_ring_images_disable_raycast()
        {
            var images = Root.GetComponentsInChildren<Image>();
            Assert.IsNotEmpty(images);
            foreach (var image in images)
                Assert.IsFalse(image.raycastTarget, "精力环不得拦截触屏/鼠标");
        }

        [Test]
        public void test_stamina_ring_exhaust_flash_triggers_once()
        {
            _stamina.TryConsume(100f);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsFlashing, "力竭上升沿应闪红");

            // 力竭瞬间 fillAmount=0，Fill 是 0 像素；闪烁必须染在完整环 Track 上才可见。
            var trackFlash = Track.color;
            Assert.Greater(trackFlash.r, 0.9f, "闪烁时 Track 应染红，否则画面上看不到红闪");
            Assert.Less(trackFlash.g, 0.5f, "闪烁时 Track 应染红");
            var fillFlash = Fill.color;
            Assert.Greater(fillFlash.r, 0.9f, "闪烁时 Fill 也应染红");
            Assert.Less(fillFlash.g, 0.5f, "闪烁时 Fill 也应染红");

            _ring.Tick(_camera, 0.4f);
            Assert.IsFalse(_ring.IsFlashing, "0.35s 后应停止");

            var trackRestored = Track.color;
            Assert.Greater(trackRestored.g, trackRestored.r, "闪烁结束 Track 应恢复深色");
            Assert.Greater(Fill.color.g, 0.9f, "闪烁结束 Fill 应恢复绿色");

            _ring.Tick(_camera, 0.1f);
            Assert.IsFalse(_ring.IsFlashing, "力竭持续期间不得重复闪");
        }

        [Test]
        public void test_stamina_ring_rebind_resets_visual_state()
        {
            _stamina.TryConsume(100f);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsFlashing);

            var fresh = new StaminaSystem(StaminaConfig.Default);
            _ring.Bind(_targetGo.transform, fresh, () => true);

            Assert.AreEqual(1f, _ring.FillAmount, 0.001f);
            Assert.AreEqual(0f, _ring.Alpha, 0.001f);
            Assert.IsFalse(_ring.IsFlashing);
            Assert.IsFalse(_ring.IsVisible);
        }

        [Test]
        public void test_stamina_ring_unbind_ignores_other_owner()
        {
            _stamina.TryConsume(40f);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsVisible);

            var other = new GameObject("OtherOwner");
            _ring.Unbind(other.transform);
            _ring.Tick(_camera, 0.1f);
            Assert.IsTrue(_ring.IsVisible, "别的 Transform 不得解绑当前环");
            Object.Destroy(other);

            _ring.Unbind(_targetGo.transform);
            _ring.Tick(_camera, 0.1f);
            Assert.IsFalse(_ring.IsVisible);
        }

        [Test]
        public void test_stamina_ring_hides_when_anchor_offscreen()
        {
            _stamina.TryConsume(40f);
            _targetGo.transform.position = new Vector3(0f, 0f, -10f); // 相机背后
            _ring.Tick(_camera, 0.1f);

            Assert.IsFalse(_ring.IsVisible);
        }
    }
}
