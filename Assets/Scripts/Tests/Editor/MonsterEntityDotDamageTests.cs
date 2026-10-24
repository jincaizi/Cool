using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MonsterEntityDotDamageTests
    {
        private GameObject _go;
        private MonsterEntity _entity;
        private MonsterConfig _config;
        private MonsterStats _stats;
        private MonsterDotDamageEvent _lastEvent;
        private bool _received;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("DotTarget");
            _entity = _go.AddComponent<MonsterEntity>();

            _config = ScriptableObject.CreateInstance<MonsterConfig>();
            _config.MaxHP = 100f;
            _config.AttackPower = 10f;
            _config.Defense = 0f;
            _config.MoveSpeed = 1f;

            _stats = new MonsterStats(_config);
            TestHelpers.SetField(_entity, "_stats", _stats);

            _received = false;
            EventBus.Subscribe<MonsterDotDamageEvent>(OnDot);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<MonsterDotDamageEvent>(OnDot);
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_config);
        }

        private void OnDot(MonsterDotDamageEvent e)
        {
            _received = true;
            _lastEvent = e;
        }

        [Test]
        public void test_monster_apply_dot_damage_reduces_hp_and_emits_event()
        {
            ((IEffectTarget)_entity).ApplyDamageOverTime(10f);

            Assert.AreEqual(90f, _stats.HP, 0.001f);
            Assert.IsTrue(_received);
            Assert.AreEqual(10, _lastEvent.Damage);
        }
    }
}
