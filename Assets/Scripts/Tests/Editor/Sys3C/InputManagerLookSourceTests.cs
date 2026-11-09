using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class InputManagerLookSourceTests
    {
        private GameObject _go;
        private InputManager _input;

        private sealed class FakeLookAdapter : IInputAdapter
        {
            public Vector2 Look;

            public string AdapterName => "FakeLook";
            public Vector3 GetMoveInput() => Vector3.zero;
            public Vector2 GetCameraRotationInput() => Look;
            public bool HasMoveInput() => false;
            public bool HasCameraRotationInput() => Look.sqrMagnitude > 0f;
        }

        [SetUp]
        public void SetUp()
        {
            InputBlockGate.ClearAll();
            _go = new GameObject("InputManagerLookSourceTests");
            _input = _go.AddComponent<InputManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            InputBlockGate.ClearAll();
        }

        [Test]
        public void test_look_source_reports_touch_when_touch_look_active()
        {
            // Arrange
            _input.SetAdapter(new JoystickInputAdapter(
                () => Vector2.zero, () => new Vector2(3f, 0f)));

            // Act
            Vector2 look = _input.GetCameraRotationInput();

            // Assert
            Assert.AreEqual(new Vector2(3f, 0f), look);
            Assert.IsTrue(_input.IsLookFromTouch);
        }

        [Test]
        public void test_look_source_reports_mouse_when_touch_look_idle()
        {
            // Arrange
            _input.SetAdapter(new JoystickInputAdapter(
                () => Vector2.zero, () => Vector2.zero,
                new FakeLookAdapter { Look = new Vector2(5f, 0f) }));

            // Act
            Vector2 look = _input.GetCameraRotationInput();

            // Assert
            Assert.AreEqual(new Vector2(5f, 0f), look);
            Assert.IsFalse(_input.IsLookFromTouch, "键鼠回退不得使用触屏俯仰限位");
        }

        [Test]
        public void test_blocked_or_release_gated_look_clears_touch_source()
        {
            // Arrange
            Vector2 touchLook = new Vector2(3f, 0f);
            _input.SetAdapter(new JoystickInputAdapter(
                () => Vector2.zero, () => touchLook));
            _input.GetCameraRotationInput();
            Assert.IsTrue(_input.IsLookFromTouch);

            // Act：阻断时立即清来源
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
            Assert.AreEqual(Vector2.zero, _input.GetCameraRotationInput());
            Assert.IsFalse(_input.IsLookFromTouch);

            // 让 InputManager 记录释放锁存
            _input.Update();

            // Act：解除阻断后仍按住的触摸必须先释放
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, false);
            Assert.AreEqual(Vector2.zero, _input.GetCameraRotationInput());
            Assert.IsFalse(_input.IsLookFromTouch);

            // Act：释放后归零
            touchLook = Vector2.zero;
            Assert.AreEqual(Vector2.zero, _input.GetCameraRotationInput());
            Assert.IsFalse(_input.IsLookFromTouch);
        }
    }
}
