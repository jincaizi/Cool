using System.Collections.Generic;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorMeteorTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;
        private int _spawnEventCount;

        [SetUp]
        public void SetUp()
        {
            _spawnEventCount = 0;
            EventBus.Subscribe<MeteorSpawnedEvent>(OnMeteorSpawned);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<MeteorSpawnedEvent>(OnMeteorSpawned);
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
            MeteorSpawner.DisposeForTests();
        }

        // 事件订阅必须用字段方法（lambda 两次引用不同，Unsubscribe 无法移除）
        private void OnMeteorSpawned(MeteorSpawnedEvent e) { _spawnEventCount++; }

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
            so.FindProperty("_damagePercent").floatValue = 2.5f;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private SkillExecutor CreateExecutor(InstantSkillData data)
        {
            _ownerGo = new GameObject("MeteorOwner");
            _ownerGo.transform.position = Vector3.zero;
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            executor.SetTargetPosition(new Vector3(0f, 0f, 12f));
            return executor;
        }

        /// <summary>找活跃（已取出未回池）的陨石实例——池预热实例不激活，需过滤</summary>
        private static MeteorStrike FindActiveStrike()
        {
            foreach (var s in Object.FindObjectsOfType<MeteorStrike>())
                if (s.gameObject.activeSelf) return s;
            return null;
        }

        [Test]
        public void test_skill_executor_spawns_meteor_on_execution_entry()
        {
            // Arrange
            var prefab = NewGo("MeteorPrefab");
            prefab.AddComponent<MeteorStrike>();
            prefab.SetActive(false); // 模板不激活（真实 prefab 资产即非活跃场景对象），避免计入预警数
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 30001);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(100f, 0f));
            TestHelpers.SetField(data, "_meteor", CreateConfig(prefab));
            var executor = CreateExecutor(data);

            // Act（TryStart → Execution 同步生成，落点 = SetTargetPosition）
            Assert.IsTrue(executor.TryStart());

            // Assert
            Assert.AreEqual(1, _spawnEventCount, "应发出 MeteorSpawnedEvent 快照");
            int active = 0;
            foreach (var s in Object.FindObjectsOfType<MeteorStrike>())
                if (s.gameObject.activeSelf) active++;
            Assert.AreEqual(1, active, "应生成 1 个预警");
        }

        [Test]
        public void test_skill_executor_meteor_applies_damage_percent()
        {
            // Arrange
            var prefab = NewGo("MeteorPrefab");
            prefab.AddComponent<MeteorStrike>();
            prefab.SetActive(false); // 模板不激活，FindActiveStrike 才唯一
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 30001);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(100f, 0f));
            TestHelpers.SetField(data, "_meteor", CreateConfig(prefab));
            var executor = CreateExecutor(data);

            // Act
            executor.TryStart();

            // Assert（活跃预警携带伤害 = 100 × 2.5 = 250）
            var strike = FindActiveStrike();
            Assert.IsNotNull(strike, "应生成 1 个活跃预警");
            Assert.AreEqual(250f,
                            ((DamageBlock)TestHelpers.GetField(strike, "_damage")).CalculatedDamage,
                            0.001f);
        }

        [Test]
        public void test_skill_executor_no_meteor_without_config()
        {
            // Arrange（无 Meteor 配置）
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 10002);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(10f, 0f));
            var executor = CreateExecutor(data);

            // Act
            executor.TryStart();

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<MeteorStrike>().Length, "无配置不生成陨石");
        }
    }
}
