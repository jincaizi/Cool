using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class BurnEffectDataTests
    {
        [Test]
        public void test_burn_defaults()
        {
            var burn = new BurnEffectData();

            Assert.AreEqual(EffectType.Burn, burn.Type);
            Assert.AreEqual("burn", burn.EffectId);
            Assert.IsTrue(burn.IsTickEffect);
            Assert.AreEqual(1f, burn.TickInterval, 0.001f);
            Assert.AreEqual(0.2f, burn.AttackPowerRatio, 0.001f);
            Assert.AreEqual(5f, burn.BaseDamage, 0.001f);
        }

        [Test]
        public void test_burn_tick_damage_uses_caster_attack_power()
        {
            var burn = new BurnEffectData();
            burn.Initialize(0.2f, 5f, 3f, 1f);
            var target = new TestHelpers.FakeTarget(null);
            var caster = new TestHelpers.FakeTarget(
                null, stats: new TestHelpers.FakeStats { AttackPower = 100f });

            burn.OnTick(caster, target, 1);

            Assert.AreEqual(20f, target.DotDamageTotal, 0.001f);
        }

        [Test]
        public void test_burn_tick_damage_falls_back_without_caster_stats()
        {
            var burn = new BurnEffectData();
            burn.Initialize(0.2f, 5f, 3f, 1f);
            var target = new TestHelpers.FakeTarget(null);

            burn.OnTick(null, target, 1);

            Assert.AreEqual(5f, target.DotDamageTotal, 0.001f);
        }

        [Test]
        public void test_burn_tick_damage_multiplies_by_stacks()
        {
            var burn = new BurnEffectData();
            burn.Initialize(0.2f, 5f, 3f, 1f);
            var target = new TestHelpers.FakeTarget(null);
            var caster = new TestHelpers.FakeTarget(
                null, stats: new TestHelpers.FakeStats { AttackPower = 100f });

            burn.OnTick(caster, target, 3);

            Assert.AreEqual(60f, target.DotDamageTotal, 0.001f);
        }
    }
}
