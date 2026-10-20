using System.Collections.Generic;
using System.Linq;
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
            // 手动步进：Spawn 复用的实例配置了 config，会飞行；预制体模板 config 为空原地不动
            foreach (var qi in Object.FindObjectsOfType<SwordQiProjectile>())
                qi.Tick(0.5f);

            // Assert
            // 池懒初始化：预热实例未取出前不激活（inactive），FindObjectsOfType 只统计活跃实例
            Assert.AreEqual(before + 1,
                            Object.FindObjectsOfType<SwordQiProjectile>().Length,
                            "懒初始化池创建后活跃实例数应为 Spawn 的 1 个（预热实例不激活）");
            int moving = 0;
            foreach (var qi in Object.FindObjectsOfType<SwordQiProjectile>())
            {
                Assert.IsTrue(qi.gameObject.activeSelf);
                if (qi.transform.position.z > 5f) moving++;
            }
            Assert.AreEqual(1, moving, "应恰好生成 1 道飞行剑气");
        }

        [Test]
        public void test_sword_qi_spawner_pool_root_under_game_root_container()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            _gos.Add(prefab);
            var cfg = CreateConfig(prefab);

            // Act
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            // Assert
            var poolRoot = GameObject.Find("SwordQiPool");
            Assert.IsNotNull(poolRoot, "池根应存在");
            var container = GameObject.Find("GameRoot");
            Assert.IsNotNull(container, "场景容器 GameRoot 应被创建");
            Assert.AreSame(container.transform, poolRoot.transform.parent,
                           "池根应挂在 GameRoot 容器下（不散落在场景根）");
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

        [Test]
        public void test_sword_qi_spawner_stop_nearest_picks_projectile_on_its_line()
        {
            var prefab = CreateQiPrefab();
            _gos.Add(prefab);
            var cfg = CreateConfig(prefab);
            var template = prefab.GetComponent<SwordQiProjectile>();

            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), new Vector3(10f, 0f, 0f), Vector3.forward);

            var qis = Object.FindObjectsOfType<SwordQiProjectile>().Where(q => q != template).ToArray();
            Assert.AreEqual(2, qis.Length);
            var first = qis.Single(q => Mathf.Abs(q.transform.position.x) < 0.1f);
            var second = qis.Single(q => Mathf.Abs(q.transform.position.x - 10f) < 0.1f);
            first.Tick(1f / 18f);
            second.Tick(1f / 18f);

            SwordQiSpawner.StopNearest(new Vector3(10f, 0f, 5f));

            Assert.AreEqual(5f, second.StopDistance, 0.01f);
            Assert.AreEqual(cfg.Range, first.StopDistance, 0.01f);
        }

        [Test]
        public void test_sword_qi_spawner_stop_nearest_prefers_projectile_not_passed()
        {
            var prefab = CreateQiPrefab();
            _gos.Add(prefab);
            var cfg = CreateConfig(prefab);
            var template = prefab.GetComponent<SwordQiProjectile>();

            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
            var first = Object.FindObjectsOfType<SwordQiProjectile>()
                .Single(q => q != template);
            first.Tick(7f / 18f);                     // 已飞 7m，越过 5m 停点 2m

            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
            var second = Object.FindObjectsOfType<SwordQiProjectile>()
                .Single(q => q != template && q != first);
            second.Tick(1f / 18f);                    // 只飞 1m

            SwordQiSpawner.StopNearest(new Vector3(0f, 0f, 5f));

            Assert.AreEqual(5f, second.StopDistance, 0.01f);
            Assert.AreEqual(cfg.Range, first.StopDistance, 0.01f,
                "已飞过停点的旧发不应被选中");
        }

        [Test]
        public void test_sword_qi_spawner_stop_without_active_is_noop()
        {
            var prefab = CreateQiPrefab();
            _gos.Add(prefab);
            var cfg = CreateConfig(prefab);
            var template = prefab.GetComponent<SwordQiProjectile>();

            SwordQiSpawner.Spawn(cfg, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);
            var qi = Object.FindObjectsOfType<SwordQiProjectile>().Single(q => q != template);
            qi.Release();

            Assert.DoesNotThrow(() => SwordQiSpawner.StopNearest(new Vector3(0f, 0f, 5f)));
        }
    }
}
