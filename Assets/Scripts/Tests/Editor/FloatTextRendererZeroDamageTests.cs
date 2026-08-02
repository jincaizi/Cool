using System.Collections;
using System.Linq;
using System.Reflection;
using Hotfix.GameSystems.Nameplate;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class FloatTextRendererZeroDamageTests
    {
        private GameObject _host;
        private FloatTextRenderer _renderer;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("TestCanvas");
            _renderer = new FloatTextRenderer(_host.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _renderer.Cleanup();
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void test_float_text_renderer_zero_damage_no_spawn()
        {
            // Arrange
            var settings = ScriptableObject.CreateInstance<FloatTextSettings>();
            var activeField = typeof(FloatTextRenderer)
                .GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance);
            var mergeField = typeof(FloatTextRenderer)
                .GetField("_mergeTracker", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act
            _renderer.ShowDamageText(1, Vector3.zero, settings, 0);

            // Assert
            var activeCount = ((IEnumerable)activeField.GetValue(_renderer)).Cast<object>().Count();
            var mergeCount = ((ICollection)mergeField.GetValue(_renderer)).Count;
            Assert.AreEqual(0, activeCount, "0 伤害不应创建飘字对象");
            Assert.AreEqual(0, mergeCount, "0 伤害不应进入合并跟踪");
        }

        [Test]
        public void test_float_text_renderer_negative_damage_no_spawn()
        {
            // Arrange
            var settings = ScriptableObject.CreateInstance<FloatTextSettings>();
            var activeField = typeof(FloatTextRenderer)
                .GetField("_active", BindingFlags.NonPublic | BindingFlags.Instance);

            // Act
            _renderer.ShowDamageText(1, Vector3.zero, settings, -5);

            // Assert
            var activeCount = ((IEnumerable)activeField.GetValue(_renderer)).Cast<object>().Count();
            Assert.AreEqual(0, activeCount, "负数伤害不应创建飘字对象");
        }
    }
}
