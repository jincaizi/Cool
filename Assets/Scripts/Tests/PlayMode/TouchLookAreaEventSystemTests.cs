using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.UI;
using KcpNet.PlayModeTests.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    public class TouchLookAreaEventSystemTests
    {
        private EventSystem _eventSystem;
        private StandaloneInputModule _module;
        private BaseInput _previousOverride;
        private FakeBaseInput _input;
        private GameObject _inputGo;
        private GameObject _canvasGo;
        private GameObject _eventSystemGo;
        private TouchLookArea _area;
        private NetworkMode _previousMode;
        private string _previousHost;
        private int _previousPort;
        private string _previousUser;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            _previousHost = NetworkModeService.LoadHost();
            _previousPort = NetworkModeService.LoadPort();
            _previousUser = NetworkModeService.LoadUsername();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            _eventSystem = EventSystem.current;
            if (_eventSystem == null)
            {
                _eventSystemGo = new GameObject("TouchLookAreaEventSystemTests_EventSystem");
                _eventSystem = _eventSystemGo.AddComponent<EventSystem>();
            }

            _module = _eventSystem.GetComponent<StandaloneInputModule>();
            if (_module == null)
                _module = _eventSystem.gameObject.AddComponent<StandaloneInputModule>();

            _previousOverride = _module.inputOverride;
            _inputGo = new GameObject("TouchLookAreaEventSystemTests_Input");
            _input = _inputGo.AddComponent<FakeBaseInput>();
            _module.inputOverride = _input;

            _canvasGo = new GameObject("TouchLookAreaEventSystemTests_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // 压过当前场景里可能已存在的全屏 UI（如 DemoDay 的 Bg）
            var scaler = _canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var lookGo = new GameObject("LookArea",
                typeof(RectTransform), typeof(Image), typeof(TouchLookArea));
            lookGo.transform.SetParent(_canvasGo.transform, false);
            var rect = lookGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var image = lookGo.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            _area = lookGo.GetComponent<TouchLookArea>();

            yield return null;
            Assert.IsTrue(_eventSystem.isFocused, "UGUI 事件测试需要 Game View 焦点");
        }

        [TearDown]
        public void TearDown()
        {
            if (_module != null) _module.inputOverride = _previousOverride;
            if (_inputGo != null) Object.DestroyImmediate(_inputGo);
            if (_canvasGo != null) Object.DestroyImmediate(_canvasGo);
            if (_eventSystemGo != null) Object.DestroyImmediate(_eventSystemGo);
            NetworkModeService.Save(_previousMode);
            NetworkModeService.SaveConnection(
                _previousHost, _previousPort.ToString(), _previousUser);
        }

        private Vector2 Center()
        {
            var rect = (RectTransform)_area.transform;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return new Vector2((corners[0].x + corners[2].x) * 0.5f,
                (corners[0].y + corners[2].y) * 0.5f);
        }

        private IEnumerator Touch(int fingerId, Vector2 position, TouchPhase phase)
        {
            _input.SetTouch(fingerId, position, phase);
            // 第一帧：EventSystem.Update 处理本次触摸；Delta 在帧末 LateUpdate 才发布。
            // 测试协程在 Update 之后、LateUpdate 之前恢复，故需再等一帧才能读到发布值。
            yield return null;
            yield return null;
        }

        private IEnumerator ReleaseTouch(int fingerId, Vector2 position)
        {
            yield return Touch(fingerId, position, TouchPhase.Ended);
            _input.ClearTouches();
            yield return null;
        }

        private IEnumerator Mouse(Vector2 position, int button, bool down, bool held, bool up)
        {
            _input.ClearTouches();
            _input.MousePosition = position;
            _input.SetMouse(button, down, held, up);
            yield return null;
            yield return null; // 同 Touch：等 LateUpdate 发布本帧 Delta 后再断言
        }

        private static void SetPrivate(object target, string field, object value)
            => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        [UnityTest]
        public IEnumerator test_touch_dead_zone_starts_at_total_displacement_threshold()
        {
            var center = Center();
            float threshold = TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold - 1f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center + new Vector2(threshold + 1f, 0f), TouchPhase.Moved);
            Assert.AreEqual((threshold + 1f) * TouchLookArea.DefaultSensitivity, _area.Delta.x, 0.02f);

            yield return ReleaseTouch(0, center + new Vector2(threshold + 1f, 0f));
        }

        [UnityTest]
        public IEnumerator test_touch_dead_zone_can_start_below_global_drag_threshold()
        {
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            SetPrivate(_area, "_minDragPixelsAt160Dpi",
                5f / Mathf.Max(1f, dpi / 160f));
            var center = Center();

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(4f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center + new Vector2(6f, 0f), TouchPhase.Moved);
            Assert.AreEqual(6f * TouchLookArea.DefaultSensitivity, _area.Delta.x, 0.02f);

            yield return ReleaseTouch(0, center + new Vector2(6f, 0f));
        }

        [UnityTest]
        public IEnumerator test_second_touch_gesture_requires_threshold_again()
        {
            var center = Center();
            float threshold = TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold + 1f, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);

            yield return ReleaseTouch(0, center + new Vector2(threshold + 1f, 0f));
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold - 1f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return ReleaseTouch(0, center + new Vector2(threshold - 1f, 0f));
        }

        [UnityTest]
        public IEnumerator test_touch_cancel_focus_loss_and_disable_clear_gesture()
        {
            var center = Center();
            float threshold = TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold + 1f, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);

            yield return Touch(0, center + new Vector2(threshold + 1f, 0f), TouchPhase.Canceled);
            _input.ClearTouches();
            yield return null;
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold + 1f, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);
            typeof(TouchLookArea)
                .GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_area, new object[] { false });
            yield return null;
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold + 1f, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);
            typeof(TouchLookArea)
                .GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_area, new object[] { true });
            // 不重新 Began：暂停清理后这次越界 Moved 必须被忽略。
            // （若 OnApplicationPause 缺失，_dragStarted 仍为 true，会产生非零 Delta。）
            yield return Touch(0, center + new Vector2(threshold + 11f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);

            _area.gameObject.SetActive(false);
            _area.gameObject.SetActive(true);
            yield return null;
            yield return Touch(0, center + new Vector2(threshold * 2f, 0f), TouchPhase.Moved);
            Assert.AreEqual(0f, _area.Delta.x, 0.001f);
            _input.ClearTouches();
        }

        [UnityTest]
        public IEnumerator test_mouse_drag_over_look_area_never_becomes_touch_look()
        {
            var center = Center();

            for (int button = 0; button < 3; button++)
            {
                yield return Mouse(center, button, down: true, held: true, up: false);
                yield return Mouse(center + new Vector2(100f, 0f), button,
                    down: false, held: true, up: false);
                Assert.AreEqual(0f, _area.Delta.x, 0.001f);

                yield return Mouse(center + new Vector2(100f, 0f), button,
                    down: false, held: false, up: true);
                yield return Mouse(center, button, down: false, held: false, up: false);
            }
        }

        [UnityTest]
        public IEnumerator test_unowned_pointer_release_preserves_active_touch()
        {
            var center = Center();
            float threshold = TouchLookArea.ResolveDragThresholdPixels(12f, Screen.dpi);

            yield return Touch(0, center, TouchPhase.Began);
            yield return Touch(0, center + new Vector2(threshold + 1f, 0f), TouchPhase.Moved);
            Assert.Greater(_area.Delta.x, 0f);

            // 第二根手指按下并抬起，不得影响第一根
            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved),
                FakeBaseInput.MakeTouch(1, center, TouchPhase.Began));
            yield return null;
            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold, 0f), TouchPhase.Moved),
                FakeBaseInput.MakeTouch(1, center, TouchPhase.Ended));
            yield return null;

            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold + 10f, 0f),
                    TouchPhase.Moved));
            yield return null;
            yield return null; // 等 LateUpdate 发布本帧 Delta 后再断言（同上）
            Assert.Greater(_area.Delta.x, 0f,
                "第二根手指释放后第一根手指仍应能继续拖拽");

            // 第三根手指在第一根按住时按下；第一根释放后不得自动接棒
            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold + 10f, 0f),
                    TouchPhase.Moved),
                FakeBaseInput.MakeTouch(2, center, TouchPhase.Began));
            yield return null;
            _input.SetTouches(
                FakeBaseInput.MakeTouch(0, center + new Vector2(threshold + 10f, 0f),
                    TouchPhase.Ended),
                FakeBaseInput.MakeTouch(2, center + new Vector2(threshold, 0f),
                    TouchPhase.Moved));
            yield return null;
            _input.ClearTouches();
            yield return null;
            Assert.AreEqual(0f, _area.Delta.x, 0.001f,
                "第一个触摸释放后，已按住的第二根手指不得自动接棒");
        }
    }
}
