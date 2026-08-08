using System.Collections.Generic;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorFireballTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;
        private int _spawnEventCount;

        [SetUp]
        public void SetUp()
        {
            _spawnEventCount = 0;
            EventBus.Subscribe<FireballSpawnedEvent>(OnFireballSpawned);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<FireballSpawnedEvent>(OnFireballSpawned);
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            FireballSpawner.DisposeForTests();
        }

        // 事件订阅必须用字段方法（lambda 两次引用不同，Unsubscribe 无法移除）
        private void OnFireballSpawned(FireballSpawnedEvent e) { _spawnEventCount++; }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static ProjectileSkillData CreateFireballData(GameObject prefab)
        {
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_projectilePrefab").objectReferenceValue = prefab;
            so.FindProperty("_projectileSpeed").floatValue = 18f;
            so.FindProperty("_projectileRange").floatValue = 15f;
            so.ApplyModifiedProperties();
            TestHelpers.SetField(data, "_skillId", 20001);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(50f, 0f));
            return data;
        }

        private SkillExecutor CreateExecutor(SkillData data)
        {
            _ownerGo = new GameObject("FireballOwner");
            _ownerGo.transform.position = Vector3.zero;
            _ownerGo.transform.rotation = Quaternion.identity;
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            return executor;
        }

        [Test]
        public void test_skill_executor_spawns_fireball_on_execution_entry()
        {
            // Arrange
            var prefab = NewGo("FireballPrefab");
            prefab.AddComponent<FireballProjectile>();
            var executor = CreateExecutor(CreateFireballData(prefab));
            var before = Object.FindObjectsOfType<FireballProjectile>().Length;

            // Act（TryStart → Execution 同步生成）
            Assert.IsTrue(executor.TryStart());

            // Assert（池懒初始化：活跃数 = 模板 + 生成的 1 个）
            Assert.AreEqual(before + 1, Object.FindObjectsOfType<FireballProjectile>().Length,
                            "Execution 进入应触发火球生成");
            Assert.AreEqual(1, _spawnEventCount, "应发出 FireballSpawnedEvent 快照");
        }

        [Test]
        public void test_skill_executor_fireball_spawns_only_once_per_activation()
        {
            // Arrange
            var prefab = NewGo("FireballPrefab");
            prefab.AddComponent<FireballProjectile>();
            var data = CreateFireballData(prefab);
            var executor = CreateExecutor(data);
            var before = Object.FindObjectsOfType<FireballProjectile>().Length;

            // Act（完成后再 TryStart：状态机已完成态拒绝重复启动（连击复用由协调器新建执行器），不得重复生成）
            executor.TryStart();
            executor.ForceComplete();
            bool second = executor.TryStart();

            // Assert
            Assert.IsFalse(second, "Completed 后不可重复启动同一执行器");
            Assert.AreEqual(before + 1, Object.FindObjectsOfType<FireballProjectile>().Length,
                            "每次施放恰好 1 个火球，不重复生成");
            Assert.AreEqual(1, _spawnEventCount, "事件只发一次");
        }

        [Test]
        public void test_skill_executor_no_fireball_without_projectile_data()
        {
            // Arrange（InstantSkillData 无投射物字段）
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 10002);
            var executor = CreateExecutor(data);

            // Act
            executor.TryStart();

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<FireballProjectile>().Length, "非投射物技能不生成火球");
        }
    }
}
