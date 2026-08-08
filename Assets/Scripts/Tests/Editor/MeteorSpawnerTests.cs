using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MeteorSpawnerTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            MeteorSpawner.DisposeForTests();
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static MeteorConfig CreateConfig(GameObject prefab)
        {
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_prefab").objectReferenceValue = prefab;
            so.ApplyModifiedProperties();
            return cfg;
        }

        [Test]
        public void test_meteor_spawner_creates_strike_from_pool()
        {
            // Arrange
            var prefab = NewGo("MeteorPrefab");
            prefab.AddComponent<MeteorStrike>();
            var cfg = CreateConfig(prefab);

            // Act
            MeteorSpawner.Spawn(cfg, DamageBlock.CreateDefault(60f, 0f), new Vector3(0f, 0f, 12f));

            // Assert（池懒初始化：FindObjectsOfType 只数活跃 = 模板 + Spawn 的 1 个；
            // 预热 1 个为未激活，不计入。模板停在原点，用落点区分真实预警）
            Assert.AreEqual(2, Object.FindObjectsOfType<MeteorStrike>().Length);
            int warned = 0;
            foreach (var s in Object.FindObjectsOfType<MeteorStrike>())
                if (s.transform.position.z > 10f) warned++;
            Assert.AreEqual(1, warned, "应恰好 1 个活跃预警（落点 (0,0,12)）");
        }

        [Test]
        public void test_meteor_spawner_skips_when_prefab_missing()
        {
            // Arrange（预制体为空）
            var cfg = CreateConfig(null);

            // Act（不抛异常，跳过）
            MeteorSpawner.Spawn(cfg, DamageBlock.CreateDefault(60f, 0f), Vector3.zero);

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<MeteorStrike>().Length, "缺预制体不生成");
        }
    }
}
