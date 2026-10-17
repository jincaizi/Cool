using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MeteorStrikeTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private readonly List<IDamageable> _registered = new List<IDamageable>();
        // 爆炸伤害入口读全局 NetworkModeService：测试必须钉死单机（存档为 Online 时
        // 本地伤害被（正确地）短路，本文件的"应命中"断言会全部变红）
        private NetworkMode _previousMode;

        private class FakeDamageable : IDamageable
        {
            public bool IsAlive => true;
            public Transform Transform { get; }
            public int HitCount { get; private set; }
            public DamageBlock LastDamage;
            public FakeDamageable(Transform t) { Transform = t; }
            public void TakeDamage(DamageBlock d, Vector3 dir) { HitCount++; LastDamage = d; }
            public void TakeDamage(DamageBlock d, Vector3 dir, HitContact c) { TakeDamage(d, dir); }
        }

        [SetUp]
        public void SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            foreach (var e in _registered)
                PhysicsRegistry.Instance.Unregister(e);
            _registered.Clear();
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private void Register(IDamageable target)
        {
            PhysicsRegistry.Instance.Register(target, EntityType.Monster);
            _registered.Add(target);
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static MeteorConfig CreateConfig(float warn, float radius)
        {
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_warnDuration").floatValue = warn;
            so.FindProperty("_explosionRadius").floatValue = radius;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private MeteorStrike CreateStrike(MeteorConfig cfg, Vector3 impactPoint)
        {
            var damage = DamageBlock.CreateDefault(60f, 0f);
            // 真实流程由 executor 经 FillDamageContext 预填充；此处手动填充（同剑气测试约定）
            damage.CalculatedDamage = 60f;
            var go = NewGo("Meteor").AddComponent<MeteorStrike>();
            go.OnSpawn(cfg, damage, impactPoint, null);
            return go;
        }

        [Test]
        public void test_meteor_no_explosion_before_warn_duration()
        {
            // Arrange
            var target = new FakeDamageable(NewGo("Target").transform);
            target.Transform.position = new Vector3(0f, 1f, 0f); // 落点 +1m
            Register(target);
            var strike = CreateStrike(CreateConfig(0.8f, 4f), Vector3.zero);

            // Act（0.5s，未到预警时长）
            strike.Tick(0.5f);

            // Assert
            Assert.AreEqual(0, target.HitCount, "预警期未结束不造成伤害");
            Assert.IsTrue(strike.gameObject.activeSelf);
        }

        [Test]
        public void test_meteor_explodes_at_impact_after_warn_duration()
        {
            // Arrange
            var target = new FakeDamageable(NewGo("Target").transform);
            target.Transform.position = new Vector3(0f, 1f, 0f);
            Register(target);
            var strike = CreateStrike(CreateConfig(0.8f, 4f), Vector3.zero);

            // Act（0.9s，预警结束）
            strike.Tick(0.9f);

            // Assert（落点 +1m 中心爆炸命中；回池）
            Assert.AreEqual(1, target.HitCount, "预警结束后落地爆炸");
            Assert.AreEqual(60f, target.LastDamage.CalculatedDamage, 0.001f);
            Assert.IsFalse(strike.gameObject.activeSelf, "爆炸后回池");
            strike.Tick(0.1f); // 回池后再步进不抛异常
        }

        [Test]
        public void test_meteor_explosion_center_is_one_meter_above_impact()
        {
            // 目标在落点正上方 4.1m：无 +1m 偏移时 4.1 > 4 不中；有偏移时距中心 3.1 < 4 必中——判别脚踝高度偏移
            var target = new FakeDamageable(NewGo("Target").transform);
            target.Transform.position = new Vector3(0f, 4.1f, 0f);
            Register(target);
            var strike = CreateStrike(CreateConfig(0.8f, 4f), Vector3.zero);

            // Act
            strike.Tick(0.9f);

            // Assert（垂直差 4.1m - 偏移 1m → 距爆炸中心 3.1m < 4m）
            Assert.AreEqual(1, target.HitCount, "爆炸中心在脚踝高度，站地目标必须命中");
        }

        [Test]
        public void test_meteor_does_not_double_hit_same_target()
        {
            // Arrange
            var target = new FakeDamageable(NewGo("Target").transform);
            target.Transform.position = new Vector3(0f, 1f, 0f);
            Register(target);
            var strike = CreateStrike(CreateConfig(0.8f, 4f), Vector3.zero);

            // Act（多帧越过预警点）
            for (int i = 0; i < 20; i++)
                strike.Tick(0.1f);

            // Assert
            Assert.AreEqual(1, target.HitCount, "同一陨石同一目标只结算一次");
        }
    }
}
