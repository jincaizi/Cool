using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SwordQiElementTintTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private readonly List<Material> _materials = new List<Material>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos)
                if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            foreach (var material in _materials)
                if (material != null) Object.DestroyImmediate(material);
            _materials.Clear();
            SwordQiSpawner.DisposeForTests();
        }

        private static SwordQiConfig CreateConfig(GameObject prefab)
        {
            var cfg = ScriptableObject.CreateInstance<SwordQiConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.FindProperty("_speed").floatValue = 18f;
            so.FindProperty("_range").floatValue = 12f;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private Material NewMaterial()
        {
            var material = new Material(Shader.Find("Standard"));
            _materials.Add(material);
            return material;
        }

        /// <summary>剑气预制体：根 + Trail（拖尾）+ Arc（材质带 _Color）。</summary>
        private GameObject CreateElementPrefab(Color defaultTrailColor)
        {
            var root = new GameObject("QiPrefab");
            root.AddComponent<SwordQiProjectile>();
            _gos.Add(root);

            var trailGo = new GameObject("Trail");
            trailGo.transform.SetParent(root.transform, false);
            var trail = trailGo.AddComponent<TrailRenderer>();
            trail.sharedMaterial = NewMaterial();
            trail.startColor = defaultTrailColor;
            trail.endColor = new Color(defaultTrailColor.r, defaultTrailColor.g, defaultTrailColor.b, 0f);

            var arcGo = new GameObject("Arc");
            arcGo.transform.SetParent(root.transform, false);
            arcGo.AddComponent<MeshFilter>();
            var arcRenderer = arcGo.AddComponent<MeshRenderer>();
            arcRenderer.sharedMaterial = NewMaterial();
            return root;
        }

        private static void AssertColor(Color expected, Color actual, string message)
        {
            // TrailRenderer 的颜色按 8 位通道存储（0.7 → 179/255 ≈ 0.70196），容差放宽到 8 位量化
            Assert.AreEqual(expected.r, actual.r, 0.01f, message + " (r)");
            Assert.AreEqual(expected.g, actual.g, 0.01f, message + " (g)");
            Assert.AreEqual(expected.b, actual.b, 0.01f, message + " (b)");
        }

        [Test]
        public void test_element_palette_maps_four_elements_to_distinct_colors()
        {
            // Arrange & Act
            var ice = ElementPalette.ColorOf(1);
            var fire = ElementPalette.ColorOf(2);
            var thunder = ElementPalette.ColorOf(3);
            var wind = ElementPalette.ColorOf(4);

            // Assert
            Assert.AreEqual(Color.white, ElementPalette.ColorOf(0), "元素 0 = 白（默认）");
            AssertColor(new Color(0.55f, 0.75f, 1f), ice, "冰蓝");
            AssertColor(new Color(1f, 0.35f, 0.1f), fire, "火橙");
            AssertColor(new Color(0.7f, 0.4f, 1f), thunder, "雷紫");
            AssertColor(new Color(0.3f, 1f, 0.9f), wind, "风青");

            var colors = new[] { ice, fire, thunder, wind };
            for (int i = 0; i < colors.Length; i++)
            for (int j = i + 1; j < colors.Length; j++)
                Assert.AreNotEqual(colors[i], colors[j], $"元素 {i + 1} 与 {j + 1} 颜色必须不同");
        }

        [Test]
        public void test_sword_qi_spawner_applies_element_color_when_element_set()
        {
            // Arrange
            var prefab = CreateElementPrefab(Color.white);
            var cfg = CreateConfig(prefab);

            // Act
            var qi = SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero,
                Vector3.forward, 0f, 2);

            // Assert
            Assert.IsNotNull(qi);
            Assert.AreEqual(2, qi.ElementId, "剑气应记录本次元素");
            var trail = qi.GetComponentInChildren<TrailRenderer>();
            AssertColor(ElementPalette.ColorOf(2), trail.startColor, "拖尾应为火橙");

            var renderer = qi.GetComponentInChildren<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            AssertColor(ElementPalette.ColorOf(2), block.GetColor("_Color"), "材质 _Color 应为火橙");
        }

        [Test]
        public void test_sword_qi_spawner_leaves_prefab_color_when_element_zero()
        {
            // Arrange
            var defaultColor = new Color(0.7f, 0.9f, 1f, 0.8f);
            var prefab = CreateElementPrefab(defaultColor);
            var cfg = CreateConfig(prefab);

            // Act
            var qi = SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero,
                Vector3.forward, 0f, 0);

            // Assert
            Assert.IsNotNull(qi);
            Assert.AreEqual(0, qi.ElementId);
            var trail = qi.GetComponentInChildren<TrailRenderer>();
            AssertColor(defaultColor, trail.startColor, "元素 0 应保持预制体拖尾色");

            var renderer = qi.GetComponentInChildren<MeshRenderer>();
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, "元素 0 不应写材质 _Color");
        }

        [Test]
        public void test_sword_qi_element_tint_restores_prefab_color_after_elemental_round()
        {
            // Arrange：同一实例先染冰，再回无元素（池复用的关键路径）
            var defaultColor = new Color(0.7f, 0.9f, 1f, 0.8f);
            var prefab = CreateElementPrefab(defaultColor);
            var qi = prefab.GetComponent<SwordQiProjectile>();
            var tint = new SwordQiElementTint(
                qi.GetComponentsInChildren<TrailRenderer>(true),
                qi.GetComponentsInChildren<ParticleSystem>(true),
                qi.GetComponentsInChildren<Renderer>(true));
            var trail = qi.GetComponentInChildren<TrailRenderer>();
            var renderer = qi.GetComponentInChildren<MeshRenderer>();

            // Act
            tint.Apply(1);

            // Assert
            AssertColor(ElementPalette.ColorOf(1), trail.startColor, "先用冰蓝染色");

            // Act
            tint.Apply(0);

            // Assert
            AssertColor(defaultColor, trail.startColor, "回无元素应恢复预制体拖尾色");
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.IsTrue(block.isEmpty, "回无元素应清除材质 _Color");
        }
    }
}
