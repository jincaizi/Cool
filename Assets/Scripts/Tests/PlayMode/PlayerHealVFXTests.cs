using System.Collections;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class PlayerHealVFXTests
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
            _player = Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry);
            _player.AddComponent<PlayerHealVFX>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_player != null) Object.DestroyImmediate(_player);
        }

        [UnityTest]
        public IEnumerator test_heal_spawns_green_particle_child()
        {
            _entry.ApplyServerDamage(50f, Vector3.zero, 50f);
            _entry.Heal(10f);
            yield return null;

            var spawned = _player.transform.Find("HealGreenParticles(Clone)");
            Assert.IsNotNull(spawned, "实际回血后应生成 HealGreenParticles(Clone)");
            Assert.IsNotNull(spawned.GetComponent<ParticleSystem>());
        }
    }
}
