using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>格挡火花：位置、缩放、金色与命中火花共用池。</summary>
    public class BlockFeedbackSparkTests
    {
        private GameObject _host;
        private GameObject _sparkPrefab;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            HitFeedbackSystem.EnableVFX = true;
            var existing = Object.FindObjectOfType<HitFeedbackSystem>();
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            _sparkPrefab = new GameObject("BlockSparkPrefab");
            _sparkPrefab.AddComponent<ParticleSystem>();
            _sparkPrefab.AddComponent<HitSpark>();

            _host = new GameObject("BlockSparkHost");
            _host.SetActive(false);
            var system = _host.AddComponent<HitFeedbackSystem>();
            typeof(HitFeedbackSystem)
                .GetField("_sparkPrefab", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(system, _sparkPrefab);
            _host.SetActive(true);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_sparkPrefab != null) Object.DestroyImmediate(_sparkPrefab);
            var left = Object.FindObjectOfType<HitFeedbackSystem>();
            if (left != null) Object.DestroyImmediate(left.gameObject);
        }

        [UnityTest]
        public IEnumerator test_block_feedback_plays_gold_spark_at_hit_position()
        {
            // Arrange
            var hitPos = new Vector3(1f, 2f, 3f);

            // Act
            EventBus.Emit(new BlockSucceededEvent(42, hitPos, Vector3.up * 1.5f));
            yield return null;

            // Assert
            var spark = _host.GetComponentInChildren<HitSpark>(true);
            Assert.IsNotNull(spark, "格挡成功应播放火花");
            Assert.IsTrue(spark.gameObject.activeSelf, "格挡成功应激活火花");
            Assert.AreEqual(hitPos, spark.transform.position, "火花必须落在格挡接触点");
            Assert.AreEqual(0.8f, spark.transform.localScale.x, 0.001f, "格挡火花缩放应为 0.8");

            var color = spark.GetComponent<ParticleSystem>().main.startColor.color;
            Assert.AreEqual(1f, color.r, 0.001f);
            Assert.AreEqual(0.84f, color.g, 0.001f);
            Assert.AreEqual(0f, color.b, 0.001f);
        }
    }
}
