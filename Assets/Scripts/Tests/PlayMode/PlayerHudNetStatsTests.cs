using System.Reflection;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// HUD 联网读数：预制体自带默认隐藏的 TMP 子物体，SetNetStats 负责显隐与写入。
    /// 字段连线用反射断言——预制体被改坏时当场报红。
    /// </summary>
    public class PlayerHudNetStatsTests
    {
        private GameObject _panelInstance;
        private PlayerHudPanel _panel;

        [SetUp]
        public void SetUp()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/PlayerHudPanel");
            Assert.IsNotNull(prefab, "HUD 预制体缺失：Resources/Prefabs/UI/PlayerHudPanel");
            _panelInstance = Object.Instantiate(prefab);
            _panel = _panelInstance.GetComponent<PlayerHudPanel>();
            Assert.IsNotNull(_panel, "HUD 预制体上没有 PlayerHudPanel 组件");
        }

        [TearDown]
        public void TearDown()
        {
            if (_panelInstance != null) Object.DestroyImmediate(_panelInstance);
        }

        [Test]
        public void test_player_hud_net_stats_text_is_wired()
        {
            Assert.IsNotNull(GetNetStatsText(), "_netStatsText 未连线（预制体改动？）");
        }

        [Test]
        public void test_player_hud_set_net_stats_shows_text()
        {
            var text = GetNetStatsText();
            Assert.IsFalse(text.gameObject.activeSelf, "预制体里的 NetStatsText 必须默认隐藏");

            _panel.SetNetStats("FPS 60  延迟 23ms");

            Assert.IsTrue(text.gameObject.activeSelf);
            Assert.AreEqual("FPS 60  延迟 23ms", text.text);
        }

        [Test]
        public void test_player_hud_set_net_stats_null_hides_text()
        {
            var text = GetNetStatsText();
            _panel.SetNetStats("FPS 60  延迟 23ms");

            _panel.SetNetStats(null);

            Assert.IsFalse(text.gameObject.activeSelf, "null 必须隐藏读数（断线/回选人路径）");
        }

        [Test]
        public void test_player_hud_net_stats_uses_tmp_underlay_shadow()
        {
            var text = GetNetStatsText();
            Assert.IsNotNull(text.fontSharedMaterial, "NetStatsText 必须有独立材质");
            Assert.IsTrue(text.fontSharedMaterial.IsKeywordEnabled("UNDERLAY_ON"),
                "underlay 阴影依赖 UNDERLAY_ON 关键字；未启用时 shader 的 underlay 分支被编译掉");
        }

        private TMP_Text GetNetStatsText()
        {
            var field = typeof(PlayerHudPanel).GetField(
                "_netStatsText", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "找不到 _netStatsText 字段（PlayerHudPanel 改动？）");
            return field.GetValue(_panel) as TMP_Text;
        }
    }
}
