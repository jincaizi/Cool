using Hotfix.GameSystems.Skills.Effect;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusEffectManagerTests
    {
        private GameObject _managerGo;
        private GameObject _targetGo;

        [SetUp]
        public void SetUp()
        {
            _managerGo = new GameObject("StatusManager");
            _targetGo = new GameObject("StatusTarget");
        }

        [TearDown]
        public void TearDown()
        {
            if (_managerGo != null) Object.DestroyImmediate(_managerGo);
            if (_targetGo != null) Object.DestroyImmediate(_targetGo);
        }

        [Test]
        public void test_manager_apply_delegates_to_registry()
        {
            var manager = _managerGo.AddComponent<StatusEffectManager>();
            var target = new TestHelpers.FakeTarget(_targetGo.transform);
            var effect = new TestHelpers.CountingEffect(1f);

            manager.Apply(target, effect, null);

            Assert.AreEqual(1, effect.ApplyCount);
        }
    }
}
