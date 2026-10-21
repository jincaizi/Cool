using Hotfix.GameSystems.VFX;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class HitTintStateTests
    {
        [Test]
        public void test_hit_tint_state_ignores_retrigger_while_active()
        {
            var state = new HitTintState();

            Assert.IsTrue(state.TryTrigger(1f), "首次受击应触发泛红");
            Assert.IsFalse(state.TryTrigger(1f), "泛红期间再次受击必须被忽略");
        }

        [Test]
        public void test_hit_tint_state_allows_retrigger_after_expiry()
        {
            var state = new HitTintState();
            state.TryTrigger(1f);

            state.Tick(1.01f);

            Assert.IsTrue(state.TryTrigger(1f), "泛红结束后应允许下一次触发");
        }

        [Test]
        public void test_hit_tint_state_amount_decays_to_zero()
        {
            var state = new HitTintState();
            state.TryTrigger(1f);

            Assert.AreEqual(1f, state.Amount(1f), 0.001f);
            state.Tick(0.5f);
            Assert.AreEqual(0.5f, state.Amount(1f), 0.001f);
            state.Tick(0.6f);
            Assert.AreEqual(0f, state.Amount(1f), 0.001f);
        }
    }
}
