using System;
using System.Collections.Generic;
using System.Reflection;
using Cool.SyncCore;
using DataDefinition;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace GameSys.EditorTests
{
    public class OfflineElementalWeaponTests
    {
        private static readonly FieldInfo SettingsField = typeof(GameSettings)
            .GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
        private readonly List<Object> _objects = new List<Object>();
        private readonly List<IDamageable> _registeredMonsters = new List<IDamageable>();
        private GameSettings _previousSettings;
        private NetworkMode _previousMode;

        [SetUp]
        public void SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);
            _previousSettings = (GameSettings)SettingsField.GetValue(null);
            SettingsField.SetValue(null, Track(ScriptableObject.CreateInstance<GameSettings>()));
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            SettingsField.SetValue(null, _previousSettings);
            SwordQiSpawner.DisposeForTests();
            foreach (var monster in _registeredMonsters)
                PhysicsRegistry.Instance.Unregister(monster);
            _registeredMonsters.Clear();
            foreach (var obj in _objects)
            {
                if (obj != null) Object.DestroyImmediate(obj);
            }
            _objects.Clear();
        }

        [Test]
        public void HeavySwingAndSwordQiUseFrozenWeaponAndShareOneElementApplication()
        {
            var monster = CreateMonster(out var stats, out _);
            monster.transform.position = Vector3.forward;
            RegisterMonster(monster);

            var owner = Track(new GameObject("Owner"));
            var prefab = Track(new GameObject("Qi Template")).AddComponent<SwordQiProjectile>();
            var qiConfig = Track(ScriptableObject.CreateInstance<SwordQiConfig>());
            TestHelpers.SetField(qiConfig, "_prefab", prefab.gameObject);
            TestHelpers.SetField(qiConfig, "_detectRadius", 2f);
            var data = Track(ScriptableObject.CreateInstance<InstantSkillData>());
            SetSkillField(data, "_skillId", 10002);
            SetSkillField(data, "_damage", DamageBlock.CreateDefault(10f, 0f));
            SetSkillField(data, "_swordQi", qiConfig);
            SetSkillField(data, "_cooldown", 0.5f);
            TestHelpers.SetField(data, "_shape", new ShapeBlock());

            var weapon = new WeaponDefinition
            {
                ElementId = 4, BaseAttack = 2f, SwordQiDamageMultiplier = 1.5f,
                AttackSpeedMultiplier = 2f,
            };
            var coordinator = new SkillCoordinator(new TestHelpers.FakeTarget(owner.transform))
            {
                OfflineWeaponProvider = () => weapon,
            };
            coordinator.RegisterSkill(data);
            coordinator.HandleHeavyFire();
            var executor = coordinator.CurrentSkill;
            Assert.That(executor, Is.Not.Null);

            weapon.ElementId = 2;
            weapon.BaseAttack = 99f;
            weapon.SwordQiDamageMultiplier = 99f;
            weapon.AttackSpeedMultiplier = 3f;
            Assert.That(executor.AttackSpeed, Is.EqualTo(2f));
            Assert.That(coordinator.GetCooldownInfo(10002).Total, Is.EqualTo(0.25f).Within(0.001f));

            executor.SetTarget(monster);
            var machine = TestHelpers.GetField(executor, "_stateMachine");
            ((Action<int>)TestHelpers.GetField(machine, "_onHitboxFrame")).Invoke(0);
            Assert.That(stats.HP, Is.EqualTo(80f).Within(0.001f));
            var cast = (OfflineWeaponCast)TestHelpers.GetField(executor, "_offlineWeaponCast");
            Assert.That(cast.ClaimElement(monster.GetInstanceID(), 0), Is.Null);

            var flying = Array.Find(Object.FindObjectsOfType<SwordQiProjectile>(), qi => qi != prefab);
            Assert.That(flying, Is.Not.Null);
            Assert.That(flying.ElementId, Is.EqualTo(4));
            flying.Tick(0f);
            Assert.That(stats.HP, Is.EqualTo(20f).Within(0.001f));
        }

        [Test]
        public void VisualOnlySwordQiNeverDamagesOfflineMonsters()
        {
            var monster = CreateMonster(out var stats, out _);
            RegisterMonster(monster);
            var visual = Track(new GameObject("Remote Qi")).AddComponent<SwordQiProjectile>();
            var config = Track(ScriptableObject.CreateInstance<SwordQiConfig>());
            visual.OnSpawn(config, null, Vector3.zero, Vector3.forward, null, elementId: 2);

            visual.Tick(0f);
            Assert.That(stats.HP, Is.EqualTo(100f));
            Assert.That(TestHelpers.GetField(monster, "_elementalStatuses"), Is.Null);
        }

        [Test]
        public void FireTicksOnceAtExpiryAndUsesOnlyWindStillActiveAtThatTick()
        {
            var go = Track(new GameObject("Status target"));
            var damage = new List<float>();
            var statuses = new OfflineMonsterStatuses(go.transform, go.GetInstanceID(), damage.Add);
            statuses.Apply(ElementalCatalogDefaults.FindElement(2), NewCast(2), 0, false);
            statuses.AdvanceTo(22);
            statuses.Apply(ElementalCatalogDefaults.FindElement(4), NewCast(4), 22, false);
            statuses.AdvanceTo(45);
            statuses.AdvanceTo(60);

            CollectionAssert.AreEqual(new[] { 8f, 16f, 8f }, damage);
            Assert.That(statuses.IncomingDamageMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void IceSlowsActualMovementAndThunderDoesNotLockArmoredWindup()
        {
            var monster = CreateMonster(out _, out var config);
            var agent = monster.GetComponent<NavMeshAgent>();
            TestHelpers.SetField(monster, "_movement", new MonsterMovement(agent, monster.transform, config));
            config.ArmoredWindup = true;
            var ctx = new AIContext { Config = config, CurrentState = MonsterAIState.Attack };
            TestHelpers.SetField(monster, "_ctx", ctx);

            NewCast(1).Hit(monster, Damage(10f), Vector3.back);
            Assert.That(agent.speed, Is.EqualTo(config.MoveSpeed * 0.7f).Within(0.001f));

            var statuses = (OfflineMonsterStatuses)TestHelpers.GetField(monster, "_elementalStatuses");
            var thunder = NewCast(3);
            thunder.Hit(monster, Damage(10f), Vector3.back);
            Assert.That(statuses.ActionLock, Is.False);

            ctx.StateTimer = config.AttackWindupTime;
            NewCast(3).Hit(monster, Damage(10f), Vector3.back);
            Assert.That(statuses.ActionLock, Is.True);
            uint tick = OfflineMonsterStatuses.LocalTick;
            statuses.AdvanceTo(tick + 8);
            Assert.That(statuses.ActionLock, Is.True);
            statuses.AdvanceTo(tick + 9);
            Assert.That(statuses.ActionLock, Is.False);
        }

        [TestCase("blocked")]
        [TestCase("invulnerable")]
        [TestCase("zero damage")]
        [TestCase("defeated")]
        public void InvalidHitsDoNotApplyOrConsumeElement(string scenario)
        {
            var monster = CreateMonster(out _, out _);
            var pipeline = (DamagePipeline)TestHelpers.GetField(monster, "_damagePipeline");
            var amount = 10f;
            switch (scenario)
            {
                case "blocked":
                    pipeline.AddModifier(new DefendModifier(DefendConfig.Default, monster.transform, () => true));
                    break;
                case "invulnerable":
                    pipeline.SetIFrameActive(true);
                    break;
                case "zero damage":
                    amount = 0f;
                    break;
                case "defeated":
                    amount = 100f;
                    break;
            }

            var cast = NewCast(2);
            cast.Hit(monster, Damage(amount), Vector3.back);
            Assert.That(TestHelpers.GetField(monster, "_elementalStatuses"), Is.Null);
            Assert.That(cast.ClaimElement(monster.GetInstanceID(), 0), Is.Not.Null);
        }

        private T Track<T>(T obj) where T : Object
        {
            _objects.Add(obj);
            return obj;
        }

        private void RegisterMonster(IDamageable monster)
        {
            PhysicsRegistry.Instance.Register(monster, EntityType.Monster);
            _registeredMonsters.Add(monster);
        }

        private MonsterEntity CreateMonster(out MonsterStats stats, out MonsterConfig config)
        {
            var monster = Track(new GameObject("Monster")).AddComponent<MonsterEntity>();
            config = Track(ScriptableObject.CreateInstance<MonsterConfig>());
            stats = new MonsterStats(config);
            TestHelpers.SetField(monster, "_stats", stats);
            TestHelpers.SetField(monster, "_damagePipeline", new DamagePipeline(config, stats));
            return monster;
        }

        private static OfflineWeaponCast NewCast(int elementId)
            => new OfflineWeaponCast(new WeaponDefinition { ElementId = elementId },
                ElementalCatalogDefaults.Create(), 10002, 1, 1);

        private static DamageBlock Damage(float amount)
        {
            var damage = DamageBlock.CreateDefault(10f, 0f);
            damage.CalculatedDamage = amount;
            return damage;
        }

        private static void SetSkillField(SkillData data, string field, object value)
            => typeof(SkillData).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(data, value);
    }
}
