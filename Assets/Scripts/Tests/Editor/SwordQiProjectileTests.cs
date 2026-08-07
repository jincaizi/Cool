using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SwordQiProjectileTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        // PhysicsRegistry 是静态单例：TearDown 必须 Unregister，否则已销毁的
        // 假目标（普通 C# 类，entity == null 永远为 false）会在后续 FindNearby
        // 的补充路径里触发 MissingReferenceException（entity.Transform.position）
        private readonly List<IDamageable> _registered = new List<IDamageable>();

        private class FakeDamageable : IDamageable
        {
            public bool IsAlive => true;
            public Transform Transform { get; }
            public int HitCount { get; private set; }
            public DamageBlock LastDamage;

            public FakeDamageable(Transform t) { Transform = t; }

            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection)
            {
                HitCount++;
                LastDamage = damageData;
            }

            public void TakeDamage(DamageBlock damageData, Vector3 hitDirection, HitContact contact)
            {
                TakeDamage(damageData, hitDirection);
            }
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var e in _registered)
                PhysicsRegistry.Instance.Unregister(e);
            _registered.Clear();

            foreach (var go in _gos)
                if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private void Register(IDamageable target)
        {
            PhysicsRegistry.Instance.Register(target, EntityType.Monster);
            _registered.Add(target);
        }

        private static SwordQiConfig CreateConfig()
        {
            var cfg = ScriptableObject.CreateInstance<SwordQiConfig>();
            var so = new UnityEditor.SerializedObject(cfg);
            so.FindProperty("_speed").floatValue = 18f;
            so.FindProperty("_range").floatValue = 12f;
            so.FindProperty("_detectRadius").floatValue = 1f;
            so.FindProperty("_damagePercent").floatValue = 1f;
            so.ApplyModifiedProperties();
            return cfg;
        }

        private SwordQiProjectile CreateProjectile(SwordQiConfig cfg, DamageBlock damage,
                                                   Vector3 origin, Vector3 dir)
        {
            var qi = NewGo("Qi").AddComponent<SwordQiProjectile>();
            qi.OnSpawn(cfg, damage, origin, dir, null);
            return qi;
        }

        [Test]
        public void test_sword_qi_projectile_moves_forward_at_config_speed()
        {
            // Arrange
            var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                                      Vector3.zero, Vector3.forward);

            // Act
            qi.Tick(0.5f);

            // Assert
            Assert.AreEqual(0f, qi.transform.position.x, 0.001f);
            Assert.AreEqual(0f, qi.transform.position.y, 0.001f);
            Assert.AreEqual(9f, qi.transform.position.z, 0.001f); // 18 * 0.5
        }

        [Test]
        public void test_sword_qi_projectile_hits_target_within_detect_radius()
        {
            // Arrange
            var targetGo = NewGo("Target");
            targetGo.transform.position = new Vector3(0f, 0f, 3f);
            var target = new FakeDamageable(targetGo.transform);
            Register(target);
            var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                                      Vector3.zero, Vector3.forward);

            // Act（飞 3m 越过目标）
            qi.Tick(3f / 18f);

            // Assert
            Assert.AreEqual(1, target.HitCount, "目标在判定带内应被命中");
        }

        [Test]
        public void test_sword_qi_projectile_hits_each_target_only_once()
        {
            // Arrange
            var targetGo = NewGo("Target");
            targetGo.transform.position = new Vector3(0f, 0f, 3f);
            var target = new FakeDamageable(targetGo.transform);
            Register(target);
            var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                                      Vector3.zero, Vector3.forward);

            // Act（多帧停留在目标附近，共飞 4m）
            for (int i = 0; i < 20; i++)
                qi.Tick(4f / 18f / 20f);

            // Assert
            Assert.AreEqual(1, target.HitCount, "同一目标每道剑气只命中一次");
        }

        [Test]
        public void test_sword_qi_projectile_releases_after_max_range()
        {
            // Arrange
            var qi = CreateProjectile(CreateConfig(), DamageBlock.CreateDefault(10f, 0f),
                                      Vector3.zero, Vector3.forward);

            // Act（超过 12m 射程）
            qi.Tick(13f / 18f);

            // Assert
            Assert.IsFalse(qi.gameObject.activeSelf, "飞满射程后应回池（SetActive false）");
            qi.Tick(0.1f); // 回池后再步进不抛异常
        }

        [Test]
        public void test_sword_qi_projectile_applies_calculated_damage()
        {
            // Arrange
            var targetGo = NewGo("Target");
            targetGo.transform.position = new Vector3(0f, 0f, 2f);
            var target = new FakeDamageable(targetGo.transform);
            Register(target);
            var damage = DamageBlock.CreateDefault(30f, 0f);
            damage.CalculatedDamage = 42f; // 预填充（真实流程由 FillDamageContext 填充）
            var qi = CreateProjectile(CreateConfig(), damage, Vector3.zero, Vector3.forward);

            // Act
            qi.Tick(2f / 18f);

            // Assert
            Assert.AreEqual(42f, target.LastDamage.CalculatedDamage, 0.001f);
        }
    }
}
