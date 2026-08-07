using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorSwordQiTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;

        [TearDown]
        public void TearDown()
        {
            if (_ownerGo != null)
                Object.DestroyImmediate(_ownerGo);
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
            so.FindProperty("_spawnFrameIndex").intValue = 0;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private GameObject CreateQiPrefab()
        {
            var go = new GameObject("QiPrefab");
            _gos.Add(go);
            go.AddComponent<SwordQiProjectile>();
            return go;
        }

        /// <summary>
        /// 反射触发 SkillStateMachine.OnHitboxFrame 事件（C# 事件无 raise 方法，
        /// 通过私有事件 backing 字段 delegate 触发；该事件为自定义访问器形式，
        /// backing 字段名为 _onHitboxFrame）
        /// </summary>
        private static void RaiseHitboxFrame(SkillExecutor executor, int frameIndex)
        {
            var stateMachine = TestHelpers.GetField(executor, "_stateMachine");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var eventField = stateMachine.GetType().GetField("_onHitboxFrame", flags)
                ?? stateMachine.GetType().GetField("OnHitboxFrame", flags); // 兼容 field-like event 写法
            Assert.IsNotNull(eventField, "OnHitboxFrame backing field not found");
            var del = (System.Action<int>)eventField.GetValue(stateMachine);
            del?.Invoke(frameIndex);
        }

        private SkillExecutor CreateExecutor(SwordQiConfig swordQi)
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 10002);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(50f, 0f));
            TestHelpers.SetField(data, "_swordQi", swordQi);

            _ownerGo = new GameObject("SwordQiOwner");
            _ownerGo.transform.position = Vector3.zero;
            _ownerGo.transform.rotation = Quaternion.identity;
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            executor.TryStart();
            return executor;
        }

        /// <summary>
        /// 统计"真·生成"的剑气：手动步进一帧，只有配置了 config 的实例会飞行
        /// （预制体模板 config 为空原地不动；池预热实例未取出前不激活，不会被步进），
        /// 按 SwordQiSpawnerTests 的 T6 约定以位移区分生成实例
        /// </summary>
        private static int CountFlyingQi()
        {
            foreach (var qi in Object.FindObjectsOfType<SwordQiProjectile>())
                qi.Tick(0.5f);
            int moving = 0;
            foreach (var qi in Object.FindObjectsOfType<SwordQiProjectile>())
                if (qi.transform.position.z > 5f) moving++;
            return moving;
        }

        [Test]
        public void test_skill_executor_spawns_sword_qi_on_matching_frame()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            var executor = CreateExecutor(CreateConfig(prefab));
            var before = Object.FindObjectsOfType<SwordQiProjectile>().Length;

            // Act（反射触发判定帧事件链：OnHitboxFrame → OnSwordQiFrame）
            TestHelpers.SetField(executor, "_swordQiSpawned", false);
            RaiseHitboxFrame(executor, 0);

            // Assert
            // 池懒初始化：预热实例未取出前不激活，活跃数 = 模板 + Spawn 的 1 个
            Assert.AreEqual(before + 1,
                            Object.FindObjectsOfType<SwordQiProjectile>().Length,
                            "匹配的判定帧应触发池初始化并激活 1 个实例");
            Assert.AreEqual(1, CountFlyingQi(), "应恰好生成 1 道飞行剑气");
        }

        [Test]
        public void test_skill_executor_sword_qi_spawns_only_once_per_activation()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            var executor = CreateExecutor(CreateConfig(prefab));
            var before = Object.FindObjectsOfType<SwordQiProjectile>().Length;

            // Act（同一帧触发两次）
            RaiseHitboxFrame(executor, 0);
            RaiseHitboxFrame(executor, 0);

            // Assert
            // 同一次施放只生成一道剑气：第二次触发被 _swordQiSpawned 拦截。
            // （若拦截失效，第二道剑气会复用第二只预热实例——活跃数变 before+2 且飞行数变 2）
            Assert.AreEqual(before + 1,
                            Object.FindObjectsOfType<SwordQiProjectile>().Length,
                            "活跃实例数应保持模板 + 1 个（无额外实例化）");
            Assert.AreEqual(1, CountFlyingQi(), "同一次施放只生成一道剑气");
        }

        [Test]
        public void test_skill_executor_sword_qi_spawns_at_configured_frame_index()
        {
            // Arrange
            var prefab = CreateQiPrefab();
            var cfg = CreateConfig(prefab);
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_spawnFrameIndex").intValue = 2;
            so.ApplyModifiedProperties();
            var executor = CreateExecutor(cfg);
            var before = Object.FindObjectsOfType<SwordQiProjectile>().Length;

            // Act（非配置帧）
            RaiseHitboxFrame(executor, 1);
            // Assert（非配置帧不生成、不初始化池）
            Assert.AreEqual(before, Object.FindObjectsOfType<SwordQiProjectile>().Length,
                            "非配置帧不应生成剑气");

            // Act（配置帧）
            RaiseHitboxFrame(executor, 2);

            // Assert
            Assert.AreEqual(before + 1,
                            Object.FindObjectsOfType<SwordQiProjectile>().Length,
                            "配置帧应触发池初始化并激活 1 个实例");
            Assert.AreEqual(1, CountFlyingQi(), "仅在配置帧索引生成");
        }

        [Test]
        public void test_skill_executor_sword_qi_skipped_when_config_null()
        {
            // Arrange
            var executor = CreateExecutor(null); // 无剑气配置

            // Act
            RaiseHitboxFrame(executor, 0);

            // Assert
            Assert.AreEqual(0, Object.FindObjectsOfType<SwordQiProjectile>().Length, "无配置不生成");
        }
    }
}
