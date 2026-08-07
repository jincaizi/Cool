using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.EditorTests
{
    public class SwordQiSpawnerTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos)
                if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
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

        private static GameObject CreateQiPrefab()
        {
            var go = new GameObject("QiPrefab");
            go.AddComponent<SwordQiProjectile>();
            return go;
        }

        [Test]
        public void test_sword_qi_spawner_creates_projectile_from_pool()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            _gos.Add(prefab);
            var cfg = CreateConfig(prefab);
            var before = Object.FindObjectsOfType<SwordQiProjectile>().Length;

            // Act
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
            // 手动步进：Spawn 复用的实例配置了 config，会飞行；惰性预热实例 config 为空原地不动
            foreach (var qi in Object.FindObjectsOfType<SwordQiProjectile>())
                qi.Tick(0.5f);

            // Assert
            // 池懒初始化：预热 PrewarmCount 个实例（Spawn 复用其一，不新建）
            Assert.AreEqual(before + SwordQiSpawner.PrewarmCount,
                            Object.FindObjectsOfType<SwordQiProjectile>().Length,
                            "懒初始化池创建后实例数应为预热数量");
            int moving = 0;
            foreach (var qi in Object.FindObjectsOfType<SwordQiProjectile>())
            {
                Assert.IsTrue(qi.gameObject.activeSelf);
                if (qi.transform.position.z > 5f) moving++;
            }
            Assert.AreEqual(1, moving, "应恰好生成 1 道飞行剑气（其余为惰性预热实例）");
        }

        [Test]
        public void test_sword_qi_spawner_skips_when_prefab_missing()
        {
            // Arrange
            var cfg = ScriptableObject.CreateInstance<SwordQiConfig>(); // _prefab = null

            // Act & Assert（LogWarning + 不生成）
            // UTF 1.1.33 的 LogAssert.Expect(string) 是精确匹配，模糊匹配须用 Regex 重载
            LogAssert.Expect(LogType.Warning, new Regex("config or prefab is null"));
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            Assert.AreEqual(0, Object.FindObjectsOfType<SwordQiProjectile>().Length);
        }

        [Test]
        public void test_sword_qi_spawner_reuses_pooled_instance()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            _gos.Add(prefab);
            var cfg = CreateConfig(prefab);

            // Act
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
            // 全部回池（含池内预热实例）：活跃实例归零
            foreach (var qi in Object.FindObjectsOfType<SwordQiProjectile>())
                qi.Release();
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            // Assert
            // 回池后再次 Spawn 复用池内实例：不新建 → 活跃恰 1 个，总实例数保持预热数量
            Assert.AreEqual(1, Object.FindObjectsOfType<SwordQiProjectile>().Length,
                            "回池后再次 Spawn 应复用池内实例（无新建）");
            // includeInactive 会把测试场景中的预制体模板本体（带 SwordQiProjectile）也算进来，须排除
            int pooledTotal = 0;
            foreach (var qi in Object.FindObjectsOfType<SwordQiProjectile>(true))
                if (qi.gameObject != prefab) pooledTotal++;
            Assert.AreEqual(SwordQiSpawner.PrewarmCount, pooledTotal,
                            "总实例数应保持池预热数量（复用而非新建）");
        }
    }
}
