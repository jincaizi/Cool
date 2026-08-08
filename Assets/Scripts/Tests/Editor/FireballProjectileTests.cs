using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class FireballProjectileTests
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
            public void TakeDamage(DamageBlock d, Vector3 dir) { HitCount++; LastDamage = d; }
            public void TakeDamage(DamageBlock d, Vector3 dir, HitContact c) { TakeDamage(d, dir); }
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

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            return go;
        }

        private static ProjectileSkillData CreateData(float speed, float range, float explosionRadius = 1.5f)
        {
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            var so = new UnityEditor.SerializedObject(data);
            so.FindProperty("_projectileSpeed").floatValue = speed;
            so.FindProperty("_projectileRange").floatValue = range;
            so.FindProperty("_explosionRadius").floatValue = explosionRadius;
            so.ApplyModifiedProperties();
            return data;
        }

        private FireballProjectile CreateProjectile(ProjectileSkillData data, Vector3 origin, Vector3 dir)
        {
            var damage = DamageBlock.CreateDefault(30f, 0f);
            damage.CalculatedDamage = 30f; // 预填充（真实流程由 FillDamageContext 填充）
            var go = NewGo("Fireball").AddComponent<FireballProjectile>();
            go.OnSpawn(data, damage, origin, dir, null);
            return go;
        }

        [Test]
        public void test_fireball_moves_forward_at_speed()
        {
            // Arrange
            var fb = CreateProjectile(CreateData(18f, 15f), Vector3.zero, Vector3.forward);

            // Act
            fb.Tick(0.5f);

            // Assert
            Assert.AreEqual(9f, fb.transform.position.z, 0.001f); // 18 * 0.5
        }

        [Test]
        public void test_fireball_explodes_on_first_target_and_damages_radius()
        {
            // Arrange（目标 A 在正前方 3m 飞行路径上；目标 B 在 A 旁 1m，处于 1.5m 爆炸半径内）
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = new Vector3(0f, 0f, 3f);
            Register(targetA);
            var targetB = new FakeDamageable(NewGo("B").transform);
            targetB.Transform.position = new Vector3(0.8f, 0f, 3.6f);
            Register(targetB);
            var fb = CreateProjectile(CreateData(18f, 15f), Vector3.zero, Vector3.forward);

            // Act（飞过 A 处）
            fb.Tick(3f / 18f);

            // Assert（A 被命中；B 在爆炸半径内也被命中）
            Assert.AreEqual(1, targetA.HitCount, "命中首个目标");
            Assert.AreEqual(1, targetB.HitCount, "爆炸波及爆炸半径内的其他目标");
            Assert.AreEqual(30f, targetA.LastDamage.CalculatedDamage, 0.001f);
        }

        [Test]
        public void test_fireball_does_not_double_hit_same_target()
        {
            // Arrange
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = new Vector3(0f, 0f, 3f);
            Register(targetA);
            var fb = CreateProjectile(CreateData(18f, 15f), Vector3.zero, Vector3.forward);

            // Act（多帧停留在命中位置）
            for (int i = 0; i < 20; i++)
                fb.Tick(4f / 18f / 20f);

            // Assert
            Assert.AreEqual(1, targetA.HitCount, "同一火球同一目标只结算一次");
        }

        [Test]
        public void test_fireball_releases_after_max_range()
        {
            // Arrange
            var fb = CreateProjectile(CreateData(18f, 15f), Vector3.zero, Vector3.forward);

            // Act（超过 15m 射程）
            fb.Tick(16f / 18f);

            // Assert
            Assert.IsFalse(fb.gameObject.activeSelf, "飞满射程应回池（SetActive false）");
            fb.Tick(0.1f); // 回池后再步进不抛异常
        }

        [Test]
        public void test_fireball_with_zero_explosion_radius_hits_single_target_only()
        {
            // Arrange（爆炸半径 0 = 纯单体）
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = new Vector3(0f, 0f, 3f);
            Register(targetA);
            var targetB = new FakeDamageable(NewGo("B").transform);
            targetB.Transform.position = new Vector3(0.8f, 0f, 3.6f);
            Register(targetB);
            var fb = CreateProjectile(CreateData(18f, 15f, 0f), Vector3.zero, Vector3.forward);

            // Act
            fb.Tick(3f / 18f);

            // Assert
            Assert.AreEqual(1, targetA.HitCount);
            Assert.AreEqual(0, targetB.HitCount, "爆炸半径 0 时只命中直接目标");
        }
    }
}
