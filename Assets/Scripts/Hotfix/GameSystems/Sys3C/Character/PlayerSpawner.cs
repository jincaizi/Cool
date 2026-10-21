using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.VFX;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 玩家生成器：订阅角色选择事件，按性别实例化对应 prefab。
    /// 相机/HUD 绑定由 Sys3CEntry.Start 自动完成，本组件不持有相机引用。
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject _malePlayerPrefab;
        [SerializeField] private GameObject _femalePlayerPrefab;
        [SerializeField] private Transform _spawnPoint;

        private GameObject _currentPlayer;

        private void OnEnable()
        {
            EventBus.Subscribe<CharacterSelectedEvent>(OnCharacterSelected);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<CharacterSelectedEvent>(OnCharacterSelected);
        }

        private void OnCharacterSelected(CharacterSelectedEvent e)
        {
            var prefab = e.Gender == CharacterGender.Female ? _femalePlayerPrefab : _malePlayerPrefab;
            if (prefab == null)
            {
                UnityEngine.Debug.LogError("[PlayerSpawner] Player prefab missing for gender " + e.Gender);
                return;
            }

            // 幂等：重复确认先销毁旧玩家
            if (_currentPlayer != null)
                Destroy(_currentPlayer);

            var spawnPos = _spawnPoint != null ? _spawnPoint.position : Vector3.zero;
            _currentPlayer = Instantiate(prefab, spawnPos, Quaternion.identity);
            _currentPlayer.AddComponent<PlayerBodyTint>();
            _currentPlayer.AddComponent<PlayerHealVFX>();
            EventBus.Emit(new PlayerSpawnedEvent(_currentPlayer.GetComponent<Animator>()));
        }

        private void Start()
        {
            // 场景加载后从保存的选择生成（选人场景发的事件本场景收不到）
            if (_currentPlayer == null)
                OnCharacterSelected(new CharacterSelectedEvent(CharacterSelectionService.Load()));
        }
    }
}
