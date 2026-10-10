using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// T12 甲-3：HUD 血条真的跟着 HP 走。原缺陷是 `PlayerHudPanel.Bind` 全项目零调用者
    /// ——血条永远停在预制体烘焙值（7500/10000），面试官看不到"我在掉血"。
    ///
    /// 测的是"面板 + 数据源 + 脏检查推动"这一条链；"谁去调 Bind"（Sys3CEntry.Start 经
    /// UIManager.Instance 取面板）在浏览器/编辑器里端到端验（测试场景没有 UIManager）。
    /// </summary>
    public class PlayerHudBindingTests
    {
        private GameObject _playerInstance;
        private GameObject _panelInstance;
        private Sys3CEntry _entry;
        private PlayerHudPanel _panel;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var playerPrefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(playerPrefab,
                "测试用玩家 Prefab 缺失：Assets/Resources/ 下需有含 Sys3CEntry 的 PlayerEntryForTest");
            _playerInstance = Object.Instantiate(playerPrefab);
            _entry = _playerInstance.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry, "PlayerEntryForTest 上没有 Sys3CEntry");

            var panelPrefab = Resources.Load<GameObject>("Prefabs/UI/PlayerHudPanel");
            Assert.IsNotNull(panelPrefab, "HUD 预制体缺失：Resources/Prefabs/UI/PlayerHudPanel");
            _panelInstance = Object.Instantiate(panelPrefab);
            _panel = _panelInstance.GetComponent<PlayerHudPanel>();
            Assert.IsNotNull(_panel, "HUD 预制体上没有 PlayerHudPanel 组件");

            yield return null;   // 等 Sys3CEntry.Start 建立数据源（provider）
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_panelInstance != null) Object.DestroyImmediate(_panelInstance);
            if (_playerInstance != null) Object.DestroyImmediate(_playerInstance);
        }

        [UnityTest]
        public IEnumerator test_hud_hp_bar_follows_authoritative_hp()
        {
            // Arrange：面板绑到 Sys3CEntry 的数据源（正式路径由 Sys3CEntry.Start 完成绑定）
            Assert.IsNotNull(_entry.StatsProvider, "Sys3CEntry.Start 必须建立 HUD 数据源");
            _panel.Bind(_entry.StatsProvider);
            yield return Frames(2);

            var hpFill = GetPrivateField<Image>(_panel, "_hpFill");
            var hpOverlay = GetPrivateField<TMP_Text>(_panel, "_hpOverlay");
            Assert.IsNotNull(hpFill, "_hpFill 未连线（预制体改动？）");
            Assert.IsNotNull(hpOverlay, "_hpOverlay 未连线（预制体改动？）");
            Assert.AreEqual(1f, hpFill.fillAmount, 0.001f, "满血应渲染成满条");
            Assert.AreEqual("100/100", hpOverlay.text, "满血浮层数字");

            // Act：服务端权威余血 60（甲-2 的入口）
            _entry.ApplyServerDamage(40f, Vector3.back, 60f);
            yield return Frames(2);   // 等 Sys3CEntry.Update 的脏检查把新血量推给面板

            // Assert
            Assert.AreEqual(0.6f, hpFill.fillAmount, 0.01f,
                "血条必须跟着 HP 走——原缺陷是 Bind 零调用者，血条永远是 7500/10000 的烘焙值");
            Assert.AreEqual("60/100", hpOverlay.text, "浮层数字必须跟着 HP 走");
        }

        /// <summary>帧边界与 Update 的先后顺序对协程不保证——多等一帧再断言，避免假红。</summary>
        private static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
                yield return null;
        }

        private static T GetPrivateField<T>(object target, string name) where T : class
        {
            var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "找不到私有字段 " + name + "（面板实现变了？）");
            return field.GetValue(target) as T;
        }
    }
}
