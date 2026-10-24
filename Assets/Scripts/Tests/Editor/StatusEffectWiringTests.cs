using System.Text.RegularExpressions;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.EditorTests
{
    public class StatusEffectWiringTests
    {
        private GameObject _go;
        private MonsterEntity _entity;
        private MonsterConfig _config;

        [TearDown]
        public void TearDown()
        {
            // Init 会把怪物注册进静态 PhysicsRegistry；销毁前显式反注册，避免污染后续测试。
            if (_entity != null) PhysicsRegistry.Instance.Unregister(_entity);
            if (_go != null) Object.DestroyImmediate(_go);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        [Test]
        public void test_spin_skill_r_has_freeze_effect()
        {
            var skill = AssetDatabase.LoadAssetAtPath<SpinSkillData>(
                "Assets/PreRes/SkillsCfg/Spin_SkillR.asset");
            Assert.IsNotNull(skill);
            Assert.IsNotNull(skill.Effect);

            FreezeEffectData freeze = null;
            foreach (var effect in skill.Effect.ApplyEffects)
            {
                if (effect is FreezeEffectData candidate)
                {
                    freeze = candidate;
                    break;
                }
            }

            Assert.IsNotNull(freeze, "Spin_SkillR 必须配置 FreezeEffectData");
            Assert.AreEqual(2f, freeze.Duration, 0.001f);
            Assert.AreEqual(StackingRule.Ignore, freeze.StackingRule);
        }

        [Test]
        public void test_monster_configs_have_combat_animator()
        {
            var slime = AssetDatabase.LoadAssetAtPath<MonsterConfig>("Assets/Monstor/SlimeConfig.asset");
            var turtle = AssetDatabase.LoadAssetAtPath<MonsterConfig>(
                "Assets/Monstor/TurtleShellConfig.asset");

            Assert.IsNotNull(slime);
            Assert.IsNotNull(turtle);
            Assert.IsNotNull(slime.CombatAnimatorController);
            Assert.IsNotNull(turtle.CombatAnimatorController);
            Assert.AreEqual("Slime", slime.CombatAnimatorController.name);
            Assert.AreEqual("TurtleShell", turtle.CombatAnimatorController.name);

            // 旧的多态效果条目已在迁移时清空，不能留下 null managed reference。
            Assert.IsNotNull(turtle.AttackEffect);
            Assert.AreEqual(0, turtle.AttackEffect.ApplyEffects.Length);
        }

        [Test]
        public void test_dogknight_and_spider_configs_have_combat_animator()
        {
            var dog = AssetDatabase.LoadAssetAtPath<MonsterConfig>(
                "Assets/Monstor/DogKnightConfig.asset");
            var spider = AssetDatabase.LoadAssetAtPath<MonsterConfig>(
                "Assets/Monstor/SpiderConfig.asset");

            Assert.IsNotNull(dog);
            Assert.IsNotNull(spider);
            Assert.IsNotNull(dog.Prefab);
            Assert.IsNotNull(spider.Prefab);
            Assert.IsNotNull(dog.CombatAnimatorController);
            Assert.IsNotNull(spider.CombatAnimatorController);
            Assert.AreEqual("DogKnightCombat", dog.CombatAnimatorController.name);
            Assert.AreEqual("SpiderCombat", spider.CombatAnimatorController.name);
        }

        [Test]
        public void test_dogknight_and_spider_combat_prefabs_have_gameplay_components()
        {
            var dog = AssetDatabase.LoadAssetAtPath<MonsterConfig>(
                "Assets/Monstor/DogKnightConfig.asset");
            var spider = AssetDatabase.LoadAssetAtPath<MonsterConfig>(
                "Assets/Monstor/SpiderConfig.asset");
            Assert.IsNotNull(dog);
            Assert.IsNotNull(spider);

            AssertCombatPrefab(dog.Prefab, "DogKnightCombat");
            AssertCombatPrefab(spider.Prefab, "SpiderCombat");
        }

        private static void AssertCombatPrefab(GameObject prefab, string expectedName)
        {
            Assert.IsNotNull(prefab);
            Assert.AreEqual(expectedName, prefab.name);
            Assert.IsNotNull(prefab.GetComponent<MonsterEntity>(), expectedName + " 缺少 MonsterEntity");
            Assert.IsNotNull(prefab.GetComponent<UnityEngine.AI.NavMeshAgent>(),
                expectedName + " 缺少 NavMeshAgent");
            Assert.IsNotNull(prefab.GetComponent<Collider>(), expectedName + " 缺少 Collider");
            Assert.IsNotNull(prefab.GetComponent<Hotfix.GameSystems.Combat.HitZone>(),
                expectedName + " 缺少 HitZone");

            var animator = prefab.GetComponent<Animator>();
            Assert.IsNotNull(animator, expectedName + " 根节点缺少 Animator");
            Assert.IsNotNull(animator.avatar, expectedName + " 根 Animator 缺少 Avatar");
        }

        [Test]
        public void test_dogknight_and_spider_combat_controllers_have_death_states()
        {
            var dog = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/Monstor/DogKnight/Animator/DogKnightCombat.controller");
            var spider = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/Monstor/fantasySpider/SpiderCombat.controller");

            Assert.IsNotNull(dog);
            Assert.IsNotNull(spider);
            AssertHasDeath(dog);
            AssertHasDeath(spider);
        }

        private static void AssertHasDeath(AnimatorController controller)
        {
            bool hasDeathParameter = false;
            foreach (var parameter in controller.parameters)
            {
                if (parameter.name == "Death")
                {
                    hasDeathParameter = true;
                    break;
                }
            }

            Assert.IsTrue(hasDeathParameter, controller.name + " 缺少 Death 参数");

            bool hasDieState = false;
            foreach (var layer in controller.layers)
            {
                foreach (var childState in layer.stateMachine.states)
                {
                    if (childState.state.name == "Die" && childState.state.motion != null)
                    {
                        hasDieState = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(hasDieState, controller.name + " 缺少 Die 状态或动画");
        }

        [Test]
        public void test_monster_init_swaps_to_combat_animator()
        {
            _go = new GameObject("CombatAnimatorProbe");
            var animator = _go.AddComponent<Animator>();
            var agent = _go.AddComponent<UnityEngine.AI.NavMeshAgent>();
            _go.AddComponent<CapsuleCollider>();
            _entity = _go.AddComponent<MonsterEntity>();

            _config = ScriptableObject.CreateInstance<MonsterConfig>();
            _config.CombatAnimatorController =
                AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    "Assets/Monstor/DuoPolyart/Animators/Slime.controller");

            _entity.Animator = animator;
            _entity.NavAgent = agent;

            // 测试场景没有 NavMesh，Init 里的 PatrolState 会触发该引擎错误。
            LogAssert.Expect(LogType.Error,
                new Regex("can only be called on an active agent that has been placed on a NavMesh"));

            _entity.Init(_config, Vector3.zero);

            Assert.IsNotNull(animator.runtimeAnimatorController);
            Assert.AreEqual("Slime", animator.runtimeAnimatorController.name);
        }
    }
}
