using System.Collections.Generic;
using Hotfix.GameSystems.Nameplate;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    public class NameplateRendererHealthTests
    {
        private GameObject _canvasGo = null!;
        private NameplateSettings _settings = null!;
        private NameplateRenderer _renderer = null!;
        private readonly List<GameObject> _owners = new();

        [SetUp]
        public void SetUp()
        {
            _canvasGo = new GameObject("Canvas", typeof(Canvas));
            _settings = ScriptableObject.CreateInstance<NameplateSettings>();
            _renderer = new NameplateRenderer(_settings, _canvasGo.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _renderer.Cleanup();
            foreach (var owner in _owners)
                if (owner != null) Object.DestroyImmediate(owner);
            _owners.Clear();
            Object.DestroyImmediate(_settings);
            Object.DestroyImmediate(_canvasGo);
        }

        private Transform CreateOwner(string name)
        {
            var go = new GameObject(name);
            _owners.Add(go);
            return go.transform;
        }

        [Test]
        public void RegisterWithHealth_Creates_A_Filled_Bar_And_Updates_It()
        {
            _renderer.RegisterWithHealth(7, CreateOwner("Owner"), new NameplateConfig("Wolf", showHPBar: true));
            _renderer.UpdateHealth(7, 0.4f);

            var fill = _canvasGo.transform.Find("NameplateHealth/HPBar/Fill");
            Assert.IsNotNull(fill, "带血条模板必须有 HPBar/Fill");
            var image = fill!.GetComponent<Image>();
            Assert.IsNotNull(image);
            Assert.AreEqual(Image.Type.Filled, image!.type);
            Assert.AreEqual(0.4f, image.fillAmount, 0.001f);

            var nameRow = _canvasGo.transform.Find("NameplateHealth/NameRow");
            Assert.IsNotNull(nameRow, "带血条模板必须有 NameRow");
            Assert.IsNotNull(nameRow!.GetComponent<ContentSizeFitter>(),
                "NameRow 需要 ContentSizeFitter 给根纵向布局提供首选尺寸");
        }

        [Test]
        public void Register_Without_Health_Keeps_The_Existing_Template()
        {
            _renderer.Register(8, CreateOwner("Owner"), new NameplateConfig("A"));
            Assert.IsNull(_canvasGo.transform.Find("Nameplate/HPBar"), "旧 Register 不得长出 HP 条");
        }

        [Test]
        public void Pool_Reuse_Resets_Fill_Amount_And_Color()
        {
            // 第一个实体把血条打到 40% 后离开（Unregister → Return 入 _healthPool）
            _renderer.RegisterWithHealth(7, CreateOwner("Owner7"), new NameplateConfig("Wolf", showHPBar: true));
            _renderer.UpdateHealth(7, 0.4f);
            _renderer.Unregister(7);

            // 下一个实体复用池对象：Fill 必须复位为满血且是血条色，而非 Reset 后的白色
            _renderer.RegisterWithHealth(8, CreateOwner("Owner8"), new NameplateConfig("Bear", showHPBar: true));

            var fill = _canvasGo.transform.Find("NameplateHealth/HPBar/Fill");
            Assert.IsNotNull(fill, "池复用后应仍是带血条模板");
            Assert.AreEqual("NameplateHealth", fill!.parent.parent.name);
            var image = fill.GetComponent<Image>();
            Assert.AreEqual(1f, image.fillAmount, 0.001f, "池复用必须复位为满血");
            Assert.AreEqual(NameplateRenderer.HealthFillColor.r, image.color.r, 0.001f);
            Assert.AreEqual(NameplateRenderer.HealthFillColor.g, image.color.g, 0.001f);
            Assert.AreEqual(NameplateRenderer.HealthFillColor.b, image.color.b, 0.001f);
            Assert.AreEqual(NameplateRenderer.HealthFillColor.a, image.color.a, 0.001f);
        }
    }
}
