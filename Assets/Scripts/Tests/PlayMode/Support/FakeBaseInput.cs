using UnityEngine;
using UnityEngine.EventSystems;

namespace KcpNet.PlayModeTests.Support
{
    public sealed class FakeBaseInput : BaseInput
    {
        public Touch[] Touches = System.Array.Empty<Touch>();
        public bool MousePresent = true;
        public Vector2 MousePosition;

        private readonly bool[] _down = new bool[3];
        private readonly bool[] _held = new bool[3];
        private readonly bool[] _up = new bool[3];

        public override bool mousePresent => MousePresent;
        public override bool touchSupported => true;
        public override int touchCount => Touches.Length;
        public override Touch GetTouch(int index) => Touches[index];
        public override Vector2 mousePosition => MousePosition;
        public override bool GetMouseButtonDown(int button) => _down[button];
        public override bool GetMouseButton(int button) => _held[button];
        public override bool GetMouseButtonUp(int button) => _up[button];

        public void SetTouch(int fingerId, Vector2 position, TouchPhase phase)
            => Touches = new[] { MakeTouch(fingerId, position, phase) };

        public void SetTouches(params Touch[] touches) => Touches = touches;

        public void ClearTouches() => Touches = System.Array.Empty<Touch>();

        public void SetMouse(int button, bool down, bool held, bool up)
        {
            _down[button] = down;
            _held[button] = held;
            _up[button] = up;
        }

        public static Touch MakeTouch(int fingerId, Vector2 position, TouchPhase phase)
            => new Touch
            {
                fingerId = fingerId,
                position = position,
                phase = phase,
                type = TouchType.Direct,
                pressure = 1f,
                maximumPossiblePressure = 1f,
                radius = 1f,
                radiusVariance = 0f,
                deltaPosition = Vector2.zero,
                deltaTime = 1f / 60f,
                tapCount = 1,
            };
    }
}
