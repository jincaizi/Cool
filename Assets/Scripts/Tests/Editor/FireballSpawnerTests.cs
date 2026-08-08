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
    }
}
