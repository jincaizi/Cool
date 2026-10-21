using System;
using System.Collections;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class PlayerHealTests
    {
        private GameObject _player;
        private Sys3CEntry _entry;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab");
            _player = UnityEngine.Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry, "PlayerEntryForTest 上没有 Sys3CEntry");
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_player != null) UnityEngine.Object.DestroyImmediate(_player);
        }

        [UnityTest]
        public IEnumerator test_heal_positive_amount_restores_hp_and_emits_event()
        {
            _entry.ApplyServerDamage(60f, Vector3.zero, 40f);

            PlayerHealedEvent? captured = null;
            Action<PlayerHealedEvent> handler = e => captured = e;
            EventBus.Subscribe(handler);
            try
            {
                _entry.Heal(30f);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(70f, _entry.CurrentHp, 0.01f);
            Assert.IsTrue(captured.HasValue, "实际回血必须发 PlayerHealedEvent");
            Assert.AreEqual(30f, captured.Value.Amount, 0.01f);
        }

        [UnityTest]
        public IEnumerator test_heal_clamps_to_max_hp()
        {
            _entry.ApplyServerDamage(60f, Vector3.zero, 40f);
            _entry.Heal(999f);
            yield return null;

            Assert.AreEqual(_entry.MaxHp, _entry.CurrentHp, 0.01f);
        }

        [UnityTest]
        public IEnumerator test_heal_over_max_hp_does_not_lower_hp_or_emit()
        {
            _entry.ApplyServerDamage(0f, Vector3.zero, _entry.MaxHp + 50f);
            Assert.AreEqual(_entry.MaxHp + 50f, _entry.CurrentHp, 0.01f, "前置：HP 已被推到上限之上");

            bool emitted = false;
            Action<PlayerHealedEvent> handler = e => emitted = true;
            EventBus.Subscribe(handler);
            try
            {
                _entry.Heal(30f);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(_entry.MaxHp + 50f, _entry.CurrentHp, 0.01f,
                "HP 已超上限时治疗不应把血量拉低");
            Assert.IsFalse(emitted, "超上限治疗没有实际回血，不应发治疗事件");
        }

        [UnityTest]
        public IEnumerator test_heal_dead_player_is_ignored_and_emits_nothing()
        {
            _entry.ApplyServerDamage(100f, Vector3.zero, 0f);

            bool emitted = false;
            Action<PlayerHealedEvent> handler = e => emitted = true;
            EventBus.Subscribe(handler);
            try
            {
                _entry.Heal(30f);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(0f, _entry.CurrentHp, 0.01f);
            Assert.IsFalse(emitted, "死亡状态不应回血，也不应发治疗事件");
        }
    }
}
