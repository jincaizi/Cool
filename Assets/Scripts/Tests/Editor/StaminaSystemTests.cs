using Hotfix.GameSystems.Stamina;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StaminaSystemTests
    {
        private const float Dt = 1f / 60f;

        private static StaminaSystem Create() => new StaminaSystem(StaminaConfig.Default);

        private static void TickFor(StaminaSystem stamina, float seconds)
        {
            int frames = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < frames; i++) stamina.Tick(Dt);
        }

        private static void DrainToExhaustion(StaminaSystem stamina)
        {
            for (int i = 0; i < 400 && !stamina.IsExhausted; i++)
            {
                stamina.TryConsume(20f * Dt);
                stamina.Tick(Dt);
            }
            Assert.IsTrue(stamina.IsExhausted, "前置条件：应已力竭");
        }

        [Test]
        public void test_stamina_sprint_rate_empties_in_five_seconds()
        {
            var stamina = Create();
            int frames = 0;
            while (!stamina.IsExhausted && frames < 400)
            {
                stamina.TryConsume(20f * Dt);
                stamina.Tick(Dt);
                frames++;
            }

            Assert.IsTrue(stamina.IsExhausted);
            Assert.AreEqual(0f, stamina.Current, 0.001f);
            Assert.LessOrEqual(Mathf.Abs(frames - 300), 1, "20/s 抽空 100 应约 5.0s");
        }

        [Test]
        public void test_stamina_exhausted_blocks_start_until_twenty_percent()
        {
            var stamina = Create();
            DrainToExhaustion(stamina);
            Assert.IsFalse(stamina.CanStartConsuming);

            TickFor(stamina, 1.30f);
            Assert.Less(stamina.Current, 20f);
            Assert.IsFalse(stamina.CanStartConsuming, "未到 20% 前不能开始消耗动作");

            TickFor(stamina, 0.10f);
            Assert.GreaterOrEqual(stamina.Current, 20f);
            Assert.IsFalse(stamina.IsExhausted);
            Assert.IsTrue(stamina.CanStartConsuming);
        }

        [Test]
        public void test_stamina_regen_starts_after_delay()
        {
            var stamina = Create();
            stamina.TryConsume(10f);
            float afterConsume = stamina.Current;

            TickFor(stamina, 0.15f);
            Assert.AreEqual(afterConsume, stamina.Current, 0.001f, "未力竭短延迟 0.2s 内不得回复");

            TickFor(stamina, 0.10f);
            Assert.Greater(stamina.Current, afterConsume, "短延迟结束后应开始回复");
        }

        [Test]
        public void test_stamina_short_delay_when_not_exhausted_long_after_exhaustion()
        {
            var notExhausted = Create();
            notExhausted.TryConsume(10f);
            float afterShort = notExhausted.Current;
            TickFor(notExhausted, 0.15f);
            Assert.AreEqual(afterShort, notExhausted.Current, 0.001f);
            TickFor(notExhausted, 0.10f);
            Assert.Greater(notExhausted.Current, afterShort, "未力竭 0.2s 后开始回复");

            var exhausted = Create();
            DrainToExhaustion(exhausted);
            TickFor(exhausted, 0.70f);
            Assert.AreEqual(0f, exhausted.Current, 0.001f, "力竭 0.75s 内不得回复");
            TickFor(exhausted, 0.10f);
            Assert.Greater(exhausted.Current, 0f, "力竭 0.75s 后开始回复");
        }

        [Test]
        public void test_stamina_consume_attempt_resets_regen_delay_even_when_insufficient()
        {
            var stamina = Create();
            DrainToExhaustion(stamina);
            TickFor(stamina, 0.70f);

            Assert.IsFalse(stamina.TryConsume(10f));
            Assert.AreEqual(0f, stamina.Current, 0.001f);

            TickFor(stamina, 0.70f);
            Assert.AreEqual(0f, stamina.Current, 0.001f, "失败尝试也必须重置力竭后的长延迟");

            TickFor(stamina, 0.10f);
            Assert.Greater(stamina.Current, 0f);
        }

        [Test]
        public void test_stamina_try_consume_insufficient_clamps_to_zero_and_returns_false()
        {
            var stamina = Create();
            stamina.TryConsume(95f);

            Assert.IsFalse(stamina.TryConsume(10f));
            Assert.AreEqual(0f, stamina.Current, 0.001f);
            Assert.IsTrue(stamina.IsExhausted);
        }

        [Test]
        public void test_stamina_try_consume_exact_remaining_triggers_exhaustion()
        {
            var stamina = Create();
            stamina.TryConsume(95f);

            Assert.IsFalse(stamina.TryConsume(5f), "恰好扣到 0 也算力竭");
            Assert.AreEqual(0f, stamina.Current, 0.001f);
            Assert.IsTrue(stamina.IsExhausted);
        }

        [Test]
        public void test_stamina_zero_consume_does_not_reset_regen_delay()
        {
            var stamina = Create();
            stamina.TryConsume(10f);
            TickFor(stamina, 0.15f);

            Assert.IsTrue(stamina.TryConsume(0f));
            TickFor(stamina, 0.08f);
            Assert.Greater(stamina.Current, 90f, "0 消耗不得重置短回复延迟");
        }

        [Test]
        public void test_stamina_refill_restores_full_and_clears_exhausted()
        {
            var stamina = Create();
            DrainToExhaustion(stamina);

            stamina.Refill();

            Assert.AreEqual(stamina.Max, stamina.Current, 0.001f);
            Assert.IsFalse(stamina.IsExhausted);
            Assert.IsTrue(stamina.CanStartConsuming);
        }

        [Test]
        public void test_stamina_non_finite_consume_is_noop()
        {
            var stamina = Create();
            float before = stamina.Current;

            Assert.IsTrue(stamina.TryConsume(float.NaN));
            Assert.IsTrue(stamina.TryConsume(float.PositiveInfinity));

            Assert.AreEqual(before, stamina.Current, 0.001f);
            Assert.IsFalse(stamina.IsExhausted);
        }

        [Test]
        public void test_stamina_non_finite_and_negative_dt_is_ignored()
        {
            var stamina = Create();
            stamina.TryConsume(10f);
            float before = stamina.Current;

            stamina.Tick(float.NaN);
            stamina.Tick(-1f);

            Assert.AreEqual(before, stamina.Current, 0.001f);
        }

        [Test]
        public void test_stamina_construct_clamps_non_finite_config()
        {
            var stamina = new StaminaSystem(new StaminaConfig
            {
                Max = float.NaN,
                SprintDrainPerSecond = 20f,
                DefendDrainPerSecond = 10f,
                RegenPerSecond = float.NaN,
                RegenDelay = float.NaN,
                ShortRegenDelay = float.NaN,
                ExhaustRecoverRatio = float.NaN
            });

            Assert.AreEqual(StaminaConfig.Default.Max, stamina.Max, 0.001f);

            stamina.TryConsume(10f);
            TickFor(stamina, 0.80f);
            Assert.Greater(stamina.Current, 90f, "非有限回复配置应回退到默认值");
        }
    }
}
