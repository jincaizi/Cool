using Hotfix.GameSystems.Nameplate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    public class NameplateRendererHealthTests
    {
        private GameObject _canvasGo = null!;
        private NameplateRenderer _renderer = null!;

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("Canvas", typeof(Canvas));
            var settings = ScriptableObject.CreateInstance<NameplateSettings>();
            _renderer = new NameplateRenderer(settings, _canvasGo.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _renderer.Cleanup();
            Object.DestroyImmediate(_canvasGo);
        }

        [Test]
        public void RegisterWithHealth_Creates_A_Filled_Bar_And_Updates_It()
        {
            var owner = new GameObject("Owner").transform;
            _renderer.RegisterWithHealth(7, owner, new NameplateConfig("Wolf", showHPBar: true));
            _renderer.UpdateHealth(7, 0.4f);

            var fill = _canvasGo.transform.Find("NameplateHealth/HPBar/Fill");
            Assert.IsNotNull(fill, "带血条模板必须有 HPBar/Fill");
            var image = fill!.GetComponent<Image>();
            Assert.IsNotNull(image);
            Assert.AreEqual(Image.Type.Filled, image!.type);
            Assert.AreEqual(0.4f, image.fillAmount, 0.001f);
        }

        [Test]
        public void Register_Without_Health_Keeps_The_Existing_Template()
        {
            var owner = new GameObject("Owner").transform;
            _renderer.Register(8, owner, new NameplateConfig("A"));
            Assert.IsNull(_canvasGo.transform.Find("Nameplate/HPBar"), "旧 Register 不得长出 HP 条");
        }
    }
}
