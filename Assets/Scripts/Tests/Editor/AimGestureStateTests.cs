using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class AimGestureStateTests
    {
        private static AimGestureState NewState()
            => new AimGestureState(radius: 100f, deadZone: 0.1f,
                cancelEnterScale: 1.4f, cancelExitScale: 1.2f);

        [Test]
        public void test_aim_gesture_press_without_drag_has_zero_value()
        {
            var state = NewState();
            state.Press(new Vector2(10f, 10f));

            Assert.IsTrue(state.IsActive);
            Assert.AreEqual(Vector2.zero, state.Value);
            Assert.AreEqual(Vector2.zero, state.IconOffset);
            Assert.IsFalse(state.CancelArmed);
        }

        [Test]
        public void test_aim_gesture_maps_delta_from_press_point()
        {
            var state = NewState();
            state.Press(new Vector2(10f, 10f));
            state.Drag(new Vector2(60f, 10f)); // 相对按下点 +50，半径 100

            Assert.AreEqual(new Vector2(0.5f, 0f), state.Value);
            Assert.AreEqual(new Vector2(50f, 0f), state.IconOffset);
            Assert.IsFalse(state.CancelArmed);
        }

        [Test]
        public void test_aim_gesture_clamps_icon_offset_and_value()
        {
            var state = NewState();
            state.Press(Vector2.zero);
            state.Drag(new Vector2(250f, 0f));

            Assert.AreEqual(new Vector2(1f, 0f), state.Value);
            Assert.AreEqual(100f, state.IconOffset.x, 0.001f);
        }

        [Test]
        public void test_aim_gesture_dead_zone_zeroes_value_but_not_icon()
        {
            var state = NewState();
            state.Press(Vector2.zero);
            state.Drag(new Vector2(5f, 0f)); // 0.05 < 0.1

            Assert.AreEqual(Vector2.zero, state.Value);
            Assert.AreEqual(5f, state.IconOffset.x, 0.001f);
        }

        [Test]
        public void test_aim_gesture_cancel_has_hysteresis()
        {
            var state = NewState();
            state.Press(Vector2.zero);
            state.Drag(new Vector2(150f, 0f)); // > 1.4×100
            Assert.IsTrue(state.CancelArmed);

            state.Drag(new Vector2(125f, 0f)); // 1.25×：仍在回滞区内
            Assert.IsTrue(state.CancelArmed);

            state.Drag(new Vector2(110f, 0f)); // < 1.2×：退出
            Assert.IsFalse(state.CancelArmed);
        }

        [Test]
        public void test_aim_gesture_release_kind_and_reset()
        {
            var state = NewState();
            state.Press(Vector2.zero);
            state.Drag(new Vector2(150f, 0f));
            Assert.AreEqual(AimReleaseKind.Cancel, state.Release());
            Assert.IsFalse(state.IsActive);

            state.Press(Vector2.zero);
            state.Drag(new Vector2(50f, 0f));
            Assert.AreEqual(AimReleaseKind.Fire, state.Release());
            Assert.AreEqual(AimReleaseKind.None, state.Release());
        }
    }
}
