using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusEffectRegistryTests
    {
        private StatusEffectRegistry _registry;
        private GameObject _targetGo;
        private TestHelpers.FakeStatusController _status;
        private TestHelpers.FakeTarget _target;

        [SetUp]
        public void SetUp()
        {
            _registry = new StatusEffectRegistry();
            _targetGo = new GameObject("StatusTarget");
            _status = new TestHelpers.FakeStatusController();
            _target = new TestHelpers.FakeTarget(_targetGo.transform, _status);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_targetGo);
        }

        [Test]
        public void test_registry_apply_calls_effect_apply_once()
        {
            var effect = new TestHelpers.CountingEffect(2f);

            _registry.Apply(_target, effect, null);

            Assert.AreEqual(1, effect.ApplyCount);
        }

        [Test]
        public void test_registry_expiry_calls_remove_once()
        {
            var effect = new TestHelpers.CountingEffect(1f);

            _registry.Apply(_target, effect, null);
            _registry.Tick(0.5f);
            Assert.AreEqual(0, effect.RemoveCount);

            _registry.Tick(0.6f);
            Assert.AreEqual(1, effect.RemoveCount);
        }

        [Test]
        public void test_registry_tick_effect_fires_once_per_interval()
        {
            var effect = new TestHelpers.CountingEffect(5f, isTick: true, interval: 1f);

            _registry.Apply(_target, effect, null);
            for (int i = 0; i < 5; i++)
                _registry.Tick(1f);

            Assert.AreEqual(5, effect.TickCount);
        }

        [Test]
        public void test_registry_duration_zero_applies_immediately_without_registration()
        {
            var effect = new TestHelpers.CountingEffect(0f);

            _registry.Apply(_target, effect, null);
            _registry.Tick(10f);

            Assert.AreEqual(1, effect.ApplyCount);
            Assert.AreEqual(0, effect.RemoveCount);
        }
    }
}
