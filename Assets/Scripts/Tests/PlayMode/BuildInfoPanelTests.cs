using DataDefinition;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace GameSys.PlayModeTests
{
    public class BuildInfoPanelTests
    {
        private GameObject _instance;

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.Destroy(_instance);
        }

        [Test]
        public void test_prefab_shows_runtime_build_version_without_blocking_input()
        {
            var prefab = Resources.Load<GameObject>("Prefabs/UI/BuildInfoPanel");
            Assert.IsNotNull(prefab, "Resources/Prefabs/UI/BuildInfoPanel 缺失");
            _instance = Object.Instantiate(prefab);

            var panel = _instance.GetComponent<BuildInfoPanel>();
            Assert.IsNotNull(panel, "预制体上没有 BuildInfoPanel");
            Assert.AreEqual(LayerType.Top, panel.Layer);
            Assert.AreEqual(VisibilityMode.CanvasGroup, panel.Mode);
            Assert.IsNotNull(_instance.GetComponent<CanvasGroup>(), "预制体根节点缺 CanvasGroup");

            var text = _instance.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(text, "预制体里没有 TMP 文本");
            Assert.AreEqual(BuildVersion.Display, text.text);
            Assert.IsFalse(text.raycastTarget, "版本文本不能拦截点击");
        }
    }
}
