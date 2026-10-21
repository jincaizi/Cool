using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>生产接线回归：PlayerSpawner 生成的玩家必须同时挂上泛红与回血粒子组件。</summary>
    public class PlayerSpawnerFeedbackWiringTests
    {
        private GameObject _spawnerGo;
        private PlayerSpawner _spawner;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab");

            _spawnerGo = new GameObject("PlayerSpawnerTest");
            _spawner = _spawnerGo.AddComponent<PlayerSpawner>();
            SetPrivateField(_spawner, "_malePlayerPrefab", prefab);
            SetPrivateField(_spawner, "_femalePlayerPrefab", prefab);
            yield break;
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);

            if (_spawner != null)
            {
                var player = CurrentPlayer();
                if (player != null) Object.DestroyImmediate(player);
            }
            if (_spawnerGo != null) Object.DestroyImmediate(_spawnerGo);
        }

        [UnityTest]
        public IEnumerator test_character_selected_spawns_player_with_feedback_components()
        {
            EventBus.Emit(new CharacterSelectedEvent(CharacterGender.Male));
            yield return null;

            var player = CurrentPlayer();
            Assert.IsNotNull(player, "选择角色后应实例化玩家");
            Assert.IsNotNull(player.GetComponent<PlayerBodyTint>(), "生成的玩家应挂 PlayerBodyTint");
            Assert.IsNotNull(player.GetComponent<PlayerHealVFX>(), "生成的玩家应挂 PlayerHealVFX");
        }

        private GameObject CurrentPlayer()
        {
            var field = typeof(PlayerSpawner).GetField(
                "_currentPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "PlayerSpawner._currentPlayer 字段不存在");
            return (GameObject)field.GetValue(_spawner);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName + " 字段不存在");
            field.SetValue(target, value);
        }
    }
}
