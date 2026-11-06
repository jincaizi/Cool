using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class FireballSpawnerTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            FireballSpawner.DisposeForTests();
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static ProjectileSkillData CreateData(GameObject prefab)
        {
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_projectilePrefab").objectReferenceValue = prefab;
            so.FindProperty("_projectileSpeed").floatValue = 18f;
            so.FindProperty("_projectileRange").floatValue = 15f;
            so.ApplyModifiedProperties();
            return data;
        }

        [Test]
        public void test_fireball_spawner_creates_flying_projectile_from_pool()
        {
            // Arrange
            var prefab = NewGo("FireballPrefab");
            prefab.AddComponent<FireballProjectile>();
            var data = CreateData(prefab);

            // Act
            FireballSpawner.Spawn(data, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            // Assert（池懒初始化，预热实例未取出前不激活——活跃数 = 模板 + Spawn 的 1 个）
            Assert.AreEqual(2, Object.FindObjectsOfType<FireballProjectile>().Length);
            int moving = 0;
            foreach (var fb in Object.FindObjectsOfType<FireballProjectile>())
            {
                if (!fb.gameObject.activeSelf) continue;
                fb.Tick(0.5f);
                if (fb.transform.position.z > 5f) moving++;
            }
            Assert.AreEqual(1, moving, "应恰好生成 1 个飞行的火球");
        }

        [Test]
        public void test_fireball_spawner_skips_when_prefab_missing()
        {
            // Arrange（预制体为空）
            var data = CreateData(null);

            // Act（不抛异常，跳过）
            FireballSpawner.Spawn(data, DamageBlock.CreateDefault(10f, 0f), Vector3.zero, Vector3.forward);

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<FireballProjectile>().Length, "缺预制体不生成");
        }

        [Test]
        public void test_fireball_spawner_skips_too_close_target()
        {
            var prefab = NewGo("FireballPrefab");
            prefab.AddComponent<FireballProjectile>();
            var data = CreateData(prefab);

            var before = Object.FindObjectsOfType<FireballProjectile>().Length;
            var result = FireballSpawner.Spawn(data, DamageBlock.CreateDefault(10f, 0f),
                Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 0.05f));

            Assert.IsNull(result, "目标 < 0.1m 不生成投射物");
            Assert.AreEqual(before, Object.FindObjectsOfType<FireballProjectile>().Length,
                "只应有场景里的模板，不生成新实例");
        }

        [Test]
        public void test_fireball_spawner_passes_target_point_to_projectile()
        {
            var prefab = NewGo("FireballPrefab");
            prefab.AddComponent<FireballProjectile>();
            var data = CreateData(prefab);

            var fb = FireballSpawner.Spawn(data, DamageBlock.CreateDefault(10f, 0f),
                Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 8f));

            Assert.IsNotNull(fb);
            for (int i = 0; i < 300 && fb.gameObject.activeSelf; i++) fb.Tick(0.005f);
            Assert.IsFalse(fb.gameObject.activeSelf, "应在目标点落地爆炸");
            Assert.AreEqual(8f, fb.transform.position.z, 0.4f, "Spawn 必须把 targetPoint 透传给弹体");
        }
    }
}
