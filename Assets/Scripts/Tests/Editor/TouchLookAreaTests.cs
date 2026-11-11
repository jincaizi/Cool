using System.Reflection;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameSys.EditorTests
{
    public class TouchLookAreaTests
    {
        private GameObject _go;
        private TouchLookArea _area;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TouchLookAreaTests");
            _area = _go.AddComponent<TouchLookArea>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private static PointerEventData Pointer(int id, Vector2 position)
            => new PointerEventData(null)
            {
                pointerId = id,
                button = PointerEventData.InputButton.Left,
                position = position,
            };

        private static void Publish(TouchLookArea area)
            => typeof(TouchLookArea)
                .GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(area, null);

        private static float Threshold()
            => TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

        [Test]
        public void test_default_sensitivity_is_quarter_degree_per_pixel()
        {
            // Arrange & Act
            float sensitivity = TouchLookArea.DefaultSensitivity;

            // Assert
            Assert.AreEqual(0.125f, sensitivity, 0.0001f,
                "触屏灵敏度应为 0.125（乘相机 MouseSensitivityX 2.0 后为 0.25°/px）");
            Assert.AreEqual(TouchLookArea.DefaultSensitivity, _area.Sensitivity, 0.0001f);
        }

        [Test]
        public void test_drag_below_threshold_does_not_emit()
        {
            var press = new Vector2(100f, 100f);
            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(Threshold() - 1f, 0f)));
            Publish(_area);

            Assert.AreEqual(Vector2.zero, _area.Delta);
        }

        [Test]
        public void test_drag_above_threshold_emits_accumulated_delta()
        {
            var press = new Vector2(100f, 100f);
            float threshold = Threshold();
            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold + 1f, 0f)));
            Publish(_area);

            Assert.AreEqual((threshold + 1f) * TouchLookArea.DefaultSensitivity, _area.Delta.x, 0.001f);
        }

        [Test]
        public void test_pointer_up_resets_dead_zone()
        {
            var press = new Vector2(100f, 100f);
            float threshold = Threshold();

            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold, 0f)));
            Publish(_area);
            _area.OnPointerUp(Pointer(0, press));

            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold - 1f, 0f)));
            Publish(_area);

            Assert.AreEqual(Vector2.zero, _area.Delta);
        }

        [Test]
        public void test_mouse_pointers_do_not_change_touch_gesture()
        {
            var press = new Vector2(100f, 100f);
            float threshold = Threshold();

            _area.OnPointerDown(Pointer(0, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold, 0f)));
            Publish(_area);

            _area.OnDrag(Pointer(-1, press + new Vector2(threshold * 10f, 0f)));
            _area.OnPointerUp(Pointer(-1, press));
            Publish(_area);

            Assert.AreEqual(Vector2.zero, _area.Delta);
        }

        [Test]
        public void test_only_active_touch_pointer_can_drag_or_release()
        {
            var press = new Vector2(100f, 100f);
            float threshold = Threshold();

            _area.OnPointerDown(Pointer(0, press));
            _area.OnPointerDown(Pointer(1, press));
            _area.OnDrag(Pointer(0, press + new Vector2(threshold, 0f)));
            Publish(_area);

            _area.OnDrag(Pointer(1, press + new Vector2(threshold * 10f, 0f)));
            _area.OnPointerUp(Pointer(1, press));
            Publish(_area);

            Assert.AreEqual(Vector2.zero, _area.Delta);
        }

        [Test]
        public void test_initialize_potential_drag_disables_only_owned_touch_threshold()
        {
            var press = new Vector2(100f, 100f);
            _area.OnPointerDown(Pointer(0, press));

            var touchEvent = Pointer(0, press);
            _area.OnInitializePotentialDrag(touchEvent);
            Assert.IsFalse(touchEvent.useDragThreshold);

            var mouseEvent = Pointer(-1, press);
            _area.OnInitializePotentialDrag(mouseEvent);
            Assert.IsTrue(mouseEvent.useDragThreshold);
        }

        [Test]
        public void test_drag_threshold_scales_with_dpi()
        {
            Assert.AreEqual(12f, TouchLookArea.ResolveDragThresholdPixels(12f, 160f), 0.001f);
            Assert.AreEqual(24f, TouchLookArea.ResolveDragThresholdPixels(12f, 320f), 0.001f);
            Assert.AreEqual(12f, TouchLookArea.ResolveDragThresholdPixels(12f, 0f), 0.001f);
        }
    }
}
