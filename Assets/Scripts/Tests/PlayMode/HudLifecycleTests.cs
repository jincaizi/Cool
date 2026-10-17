using System.Collections;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// HUD 显隐的生命周期守卫（2026-10-17 缺陷的回归测试）。
    ///
    /// 原缺陷：确认角色时 `PlayerSpawner.OnCharacterSelected` **先 Destroy(旧) 再 Instantiate(新)**。
    /// 新玩家 Start 里的 `BindHudWhenReady` 会显示 HUD，而旧玩家的 `Sys3CEntry.OnDestroy`
    /// （帧末才跑）随后又把它收起并解绑 —— 没有任何人再显示 ⇒ HUD 永久消失。
    /// 两条用例各钉一条边沿：重建后仍可见 / 销毁最后一个后收起。
    /// </summary>
    public class HudLifecycleTests
    {
        private GameObject _playerA;
        private GameObject _playerB;
        private PlayerHudPanel _hud;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // 真面板：PlayMode 会话里 UIBootstrap 在 AfterSceneLoad 已建 UIManager（幂等，不重复建）
            UIBootstrap.EnsureUIManager();
            Assert.IsNotNull(UIManager.Instance, "UIBootstrap 应创建 UIManager");

            for (int i = 0; i < 600 && UIManager.Instance.GetPanel<PlayerHudPanel>() == null; i++)
                yield return null;
            _hud = UIManager.Instance.GetPanel<PlayerHudPanel>();
            Assert.IsNotNull(_hud, "PlayerHudPanel 未注册（检查 Resources/Prefabs/UI/PlayerHudPanel）");

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab（含 Sys3CEntry 的测试玩家）");
            _playerA = Object.Instantiate(prefab);

            // 等 A 的 Start 建立数据源并显示 HUD（BindHudWhenReady 在 Start 里同步完成）
            for (int i = 0; i < 600 && !PlayerStarted(_playerA); i++)
                yield return null;
            Assert.IsTrue(PlayerStarted(_playerA), "实例 A 应已 Start（StatsProvider 非空）");
            Assert.IsTrue(_hud.IsVisible, "A 启动后 HUD 应已显示（前置条件）");
        }

        [TearDown]
        public void TearDown()
        {
            // 逐个销毁：最后一个被销毁时 HUD 会自我收起，正好把共享面板留在干净状态
            if (_playerA != null) Object.DestroyImmediate(_playerA);
            _playerA = null;
            if (_playerB != null) Object.DestroyImmediate(_playerB);
            _playerB = null;
        }

        [UnityTest]
        public IEnumerator test_hud_stays_visible_when_player_rebuilt_while_old_dies()
        {
            // Arrange：A 已完成 Start、HUD 可见（SetUp 已断言）
            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");

            // Act：逐字复刻 PlayerSpawner.OnCharacterSelected 的两步（同一帧，先 Destroy 后 Instantiate）。
            // 顺序反了就走不到真实时序，测试会假绿。
            Object.Destroy(_playerA);
            _playerA = null;
            _playerB = Object.Instantiate(prefab);

            // 等 B Start（HUD 由 B 的 Start 重新显示），再等 2 帧让 A 的帧末销毁跑完
            for (int i = 0; i < 600 && !PlayerStarted(_playerB); i++)
                yield return null;
            Assert.IsTrue(PlayerStarted(_playerB), "重建后应存在已 Start 的新玩家");
            yield return Frames(2);

            // Assert
            Assert.IsTrue(_hud.IsVisible,
                "重建玩家后 HUD 必须仍然可见（原缺陷：旧玩家 OnDestroy 把它收了）");
            Assert.AreEqual(1f, _hud.CanvasGroup.alpha, 0.001f,
                "HUD 的 CanvasGroup alpha 必须回到 1");
        }

        [UnityTest]
        public IEnumerator test_hud_hides_when_last_local_entry_dies()
        {
            // Arrange：A 已完成 Start、HUD 可见（SetUp 已断言）
            Assert.IsTrue(_hud.IsVisible, "前置：A 已让 HUD 可见");

            // Act：销毁唯一的本地角色
            Object.Destroy(_playerA);
            _playerA = null;
            yield return Frames(3);   // 帧末销毁 + OnDestroy 里的收起

            // Assert：T12 的意图必须保住（回选人/角色没了 ⇒ 收起 HUD）
            Assert.IsFalse(_hud.IsVisible, "最后一个本地角色销毁后 HUD 必须收起");
            Assert.AreEqual(0f, _hud.CanvasGroup.alpha, 0.001f, "HUD 的 CanvasGroup alpha 必须为 0");
        }

        /// <summary>该实例的 Start 是否已跑完（`_statsProvider` 在 Start 里、绑定 HUD 之前建立）。</summary>
        private static bool PlayerStarted(GameObject go)
        {
            if (go == null) return false;
            var entry = go.GetComponent<Sys3CEntry>();
            return entry != null && entry.StatsProvider != null;
        }

        /// <summary>帧边界与 Update 的先后顺序对协程不保证——多等一帧再断言，避免假红。</summary>
        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }
    }
}
