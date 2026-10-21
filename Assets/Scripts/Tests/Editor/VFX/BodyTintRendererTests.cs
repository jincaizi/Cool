using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class BodyTintRendererTests
    {
        private GameObject _go;
        private MeshRenderer _renderer;
        private Material _material;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TintTest");
            _renderer = _go.AddComponent<MeshRenderer>();
            _material = new Material(Shader.Find("Standard"));
            _renderer.sharedMaterial = _material;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_material);
        }

        [Test]
        public void test_body_tint_renderer_applies_low_layer()
        {
            var tint = new BodyTintRenderer(new Renderer[] { _renderer });

            tint.Apply(Color.red, 0.5f, Color.red, 0f);

            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);
            var c = block.GetColor("_Color");
            Assert.Greater(c.r, 0.9f);
            Assert.Less(c.g, 0.6f);
        }

        [Test]
        public void test_body_tint_renderer_clear_restores_original()
        {
            var tint = new BodyTintRenderer(new Renderer[] { _renderer });
            tint.Apply(Color.red, 1f, Color.red, 0f);

            tint.Clear();

            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, "清除后渲染器不应再带 property block");
        }
    }
}
