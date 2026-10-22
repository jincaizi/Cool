using Hotfix.GameSystems.UI;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class SprintGestureStateTests
    {
        [Test]
        public void test_sprint_gesture_below_enter_threshold_stays_walking()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsFalse(state.Tick(0.89f, 1f));
        }

        [Test]
        public void test_sprint_gesture_requires_hold_time()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsFalse(state.Tick(0.95f, 0.19f));
            Assert.IsFalse(state.IsSprinting);
            Assert.IsTrue(state.Tick(0.95f, 0.02f));
        }

        [Test]
        public void test_sprint_gesture_enters_after_hold_and_keeps_through_hysteresis()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsTrue(state.Tick(0.95f, 0.2f));
            Assert.IsTrue(state.Tick(0.85f, 0.016f));
        }

        [Test]
        public void test_sprint_gesture_exits_at_exit_threshold()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);
            state.Tick(0.95f, 0.2f);

            Assert.IsFalse(state.Tick(0.8f, 0.016f));
        }

        [Test]
        public void test_sprint_gesture_below_enter_resets_hold_timer()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsFalse(state.Tick(0.95f, 0.15f));
            Assert.IsFalse(state.Tick(0.85f, 0f));   // 跌破进入阈值，计时归零
            Assert.IsFalse(state.Tick(0.95f, 0.1f)); // 需要重新累计满 0.2s
            Assert.IsTrue(state.Tick(0.95f, 0.1f));
        }

        [Test]
        public void test_sprint_gesture_reset_clears_state()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);
            state.Tick(0.95f, 0.2f);

            state.Reset();

            Assert.IsFalse(state.IsSprinting);
            Assert.IsFalse(state.Tick(0.95f, 0.19f));
        }

        [Test]
        public void test_sprint_gesture_ignores_non_finite_inputs()
        {
            var state = new SprintGestureState(0.9f, 0.8f, 0.2f);

            Assert.IsFalse(state.Tick(float.NaN, 1f));
            Assert.IsFalse(state.Tick(0.95f, float.NaN));
            Assert.IsFalse(state.Tick(float.PositiveInfinity, 1f));
        }
    }
}
