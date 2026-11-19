using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class ElementVfxFallbackTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private GameObject NewResource()
        {
            var go = new GameObject("ElementVfxResource");
            _gos.Add(go);
            return go;
        }

        [Test]
        public void test_element_vfx_fallback_empty_view_key_uses_palette_colors()
        {
            // Arrange：四种元素的程序化颜色
            var expected = new Dictionary<int, Color>
            {
                { 1, ElementPalette.ColorOf(1) },
                { 2, ElementPalette.ColorOf(2) },
                { 3, ElementPalette.ColorOf(3) },
                { 4, ElementPalette.ColorOf(4) }
            };

            // Act & Assert
            foreach (var pair in expected)
                Assert.AreEqual(pair.Value, ElementVfxFallback.ResolveColor(pair.Key, "", null),
                    $"元素 {pair.Key} 空 ViewKey 应回退到调色板颜色");

            var colors = new HashSet<Color>();
            foreach (var pair in expected)
            {
                Assert.AreNotEqual(Color.white, pair.Value, $"元素 {pair.Key} 调色板颜色不能是白色");
                colors.Add(pair.Value);
            }
            Assert.AreEqual(4, colors.Count, "四种元素回退颜色必须互异");
        }

        [Test]
        public void test_element_vfx_fallback_unknown_element_falls_back_to_white()
        {
            // Arrange / Act / Assert
            Assert.AreEqual(Color.white, ElementVfxFallback.ResolveColor(0, "", null), "元素 0 = 白（默认）");
            Assert.AreEqual(Color.white, ElementVfxFallback.ResolveColor(99, "", null), "未知元素 = 白");
        }

        [Test]
        public void test_element_vfx_fallback_loaded_resource_returns_white()
        {
            // Arrange：loader 命中资源
            var resource = NewResource();
            Func<string, GameObject?> loader = key => key == "vfx/ice" ? resource : null;

            // Act
            var color = ElementVfxFallback.ResolveColor(1, "vfx/ice", loader);

            // Assert：资源自带颜色，Host 负责实例化，不做程序化染色
            Assert.AreEqual(Color.white, color, "命中资源时应返回白色（资源自带颜色）");
        }

        [Test]
        public void test_element_vfx_fallback_missing_resource_uses_palette_color()
        {
            // Arrange：loader 对缺失 key 返回 null
            Func<string, GameObject?> loader = key => key == "vfx/ice" ? NewResource() : null;

            // Act
            var color = ElementVfxFallback.ResolveColor(2, "vfx/missing", loader);

            // Assert
            Assert.AreEqual(ElementPalette.ColorOf(2), color, "资源缺失应回退到元素调色板颜色");
        }

        [Test]
        public void test_element_vfx_fallback_null_or_unknown_loader_does_not_throw()
        {
            // Arrange
            Func<string, GameObject?> returnsNull = key => null;
            var calls = 0;
            Func<string, GameObject?> counting = key =>
            {
                calls++;
                return null;
            };

            // Act & Assert
            Assert.DoesNotThrow(
                () => Assert.AreEqual(ElementPalette.ColorOf(3), ElementVfxFallback.ResolveColor(3, "", counting),
                    "空 ViewKey 不应调用 loader"),
                "空 ViewKey 不应抛异常");
            Assert.AreEqual(0, calls, "空 ViewKey 时 loader 不得被调用");

            Assert.DoesNotThrow(
                () => Assert.AreEqual(ElementPalette.ColorOf(3), ElementVfxFallback.ResolveColor(3, "vfx/x", null)),
                "loader 为 null 时不应抛异常");
            Assert.DoesNotThrow(
                () => Assert.AreEqual(ElementPalette.ColorOf(3), ElementVfxFallback.ResolveColor(3, "vfx/x", returnsNull)),
                "loader 返回 null 时不应抛异常");
        }
    }
}
