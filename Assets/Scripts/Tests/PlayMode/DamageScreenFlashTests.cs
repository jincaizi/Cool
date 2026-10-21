using System;
using System.Collections;
using Hotfix.GameSystems.Nameplate;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameSys.PlayModeTests
{
    public class DamageScreenFlashTests
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
            Assert.IsNotNull(prefab);
            _player = UnityEngine.Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_player != null) UnityEngine.Object.DestroyImmediate(_player);

            var display = UnityEngine.Object.FindObjectOfType<EntityDisplayManager>();
            if (display != null) UnityEngine.Object.DestroyImmediate(display.gameObject);
        }

        [UnityTest]
        public IEnumerator test_npc_source_damage_does_not_emit_flash_event()
        {
            int count = 0;
            Action<LocalPlayerHitByPlayerEvent> handler = e => count++;
            EventBus.Subscribe(handler);
            try
            {
                _entry.ApplyServerDamage(10f, Vector3.zero, 90f,
                    isBlocked: false, sourceIsPlayer: false);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(0, count, "怪物来源不应触发玩家攻击闪屏事件");
        }

        [UnityTest]
        public IEnumerator test_player_source_gating_ignores_blocked_hit()
        {
            int count = 0;
            Action<LocalPlayerHitByPlayerEvent> handler = e => count++;
            EventBus.Subscribe(handler);
            try
            {
                _entry.ApplyServerDamage(10f, Vector3.zero, 90f,
                    isBlocked: true, sourceIsPlayer: true);
                yield return null;

                _entry.ApplyServerDamage(10f, Vector3.zero, 80f,
                    isBlocked: false, sourceIsPlayer: true);
                yield return null;
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }

            Assert.AreEqual(1, count, "格挡不闪屏；非格挡玩家来源应闪一次");
        }

        [UnityTest]
        public IEnumerator test_player_source_event_flashes_overlay()
        {
            var existing = UnityEngine.Object.FindObjectOfType<EntityDisplayManager>();
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var display = new GameObject("DisplayTest");
            display.AddComponent<EntityDisplayManager>();
            yield return null;

            EventBus.Emit(new LocalPlayerHitByPlayerEvent(123));
            yield return new WaitForSeconds(0.12f);

            var overlay = GameObject.Find("DamageOverlay");
            Assert.IsNotNull(overlay, "闪屏 overlay 未创建");
            var image = overlay.GetComponent<Image>();
            Assert.Greater(image.color.a, 0f, "玩家来源受击后 overlay 应可见");

            UnityEngine.Object.Destroy(display);
        }
    }
}
