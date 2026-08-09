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
            so.FindProperty("_projectileGravity").floatValue = 9.8f;
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

        /// <summary>
        /// 按火球实际积分方案（先施加重力再积分，半隐式欧拉）模拟弹道位置，供目标精确摆放。
        /// 必须与 FireballProjectile.Tick 的积分顺序一致，否则目标不在真实弹道上。
        /// </summary>
        private static Vector3 SimulateFlight(ProjectileSkillData data, Vector3 origin, Vector3 dir,
                                              float dt, int steps)
        {
            float t = data.ProjectileRange / data.ProjectileSpeed;
            float groundY = origin.y - data.SpawnOffset.y;
            float vy0 = (groundY - origin.y) / t + 0.5f * data.ProjectileGravity * t;
            Vector3 vel = dir * data.ProjectileSpeed + Vector3.up * vy0;
            Vector3 pos = origin;
            for (int i = 0; i < steps; i++)
            {
                vel.y -= data.ProjectileGravity * dt;
                pos += vel * dt;
            }
            return pos;
        }

        [Test]
        public void test_fireball_arcs_above_launch_height()
        {
            // Arrange（发射高 1.2m，小步长积分保弧线）
            var fb = CreateProjectile(CreateData(18f, 15f), new Vector3(0f, 1.2f, 0f), Vector3.forward);

            // Act（0.3s 弹道，30 × 0.01 小步长）
            for (int i = 0; i < 30; i++) fb.Tick(0.01f);

            // Assert（抛物线上升段：高于发射高度 1.2m，且向前推进）
            Assert.Greater(fb.transform.position.y, 1.5f, "弹道应高于发射高度（抛物线上升段）");
            Assert.Greater(fb.transform.position.z, 5f, "弹道应向前推进");
            Assert.IsTrue(fb.gameObject.activeSelf, "飞行中不应回池");
        }

        [Test]
        public void test_fireball_lands_at_ground_at_range_and_explodes()
        {
            // Arrange（射程 15m → 飞行时间 15/18 ≈ 0.833s；小步长逼近落点，避免大步长积分越过地面过远）
            var fb = CreateProjectile(CreateData(18f, 15f), new Vector3(0f, 1.2f, 0f), Vector3.forward);

            // Act（总 1.0s > 飞行时间，必落地）
            for (int i = 0; i < 200; i++) fb.Tick(0.005f);

            // Assert（落地爆炸回池；落点 ≈ 射程 15m 处地面）
            Assert.IsFalse(fb.gameObject.activeSelf, "落地应爆炸并回池");
            Assert.AreEqual(15f, fb.transform.position.z, 0.4f, "落点水平距离应≈射程 15m");
            Assert.LessOrEqual(fb.transform.position.y, 0.01f, "落地高度应≤地面（发射 1.2m − 偏移 1.2m）");
            Assert.Greater(fb.transform.position.y, -0.3f, "落地爆炸不应深埋地下");
            fb.Tick(0.1f); // 回池后再步进安全
        }

        [Test]
        public void test_fireball_explodes_on_first_target_and_damages_radius()
        {
            // Arrange（目标 A 精确摆在弹道上（0.4s 处）；目标 B 在 A 旁 1m，处于 1.5m 爆炸半径内）
            var data = CreateData(18f, 15f);
            var origin = new Vector3(0f, 1.2f, 0f);
            Vector3 posA = SimulateFlight(data, origin, Vector3.forward, 0.01f, 40);
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = posA;
            Register(targetA);
            var targetB = new FakeDamageable(NewGo("B").transform);
            targetB.Transform.position = posA + new Vector3(0.8f, 0f, 0.6f);
            Register(targetB);
            var fb = CreateProjectile(data, origin, Vector3.forward);

            // Act（小步长飞过 A 处）
            for (int i = 0; i < 40; i++) fb.Tick(0.01f);

            // Assert（A 被命中；B 在爆炸半径内也被命中；伤害正确）
            Assert.AreEqual(1, targetA.HitCount, "命中首个目标");
            Assert.AreEqual(1, targetB.HitCount, "爆炸波及爆炸半径内的其他目标");
            Assert.AreEqual(30f, targetA.LastDamage.CalculatedDamage, 0.001f);
        }

        [Test]
        public void test_fireball_does_not_double_hit_same_target()
        {
            // Arrange（目标 A 精确摆在弹道上）
            var data = CreateData(18f, 15f);
            var origin = new Vector3(0f, 1.2f, 0f);
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = SimulateFlight(data, origin, Vector3.forward, 0.01f, 40);
            Register(targetA);
            var fb = CreateProjectile(data, origin, Vector3.forward);

            // Act（多帧飞过命中位置）
            for (int i = 0; i < 60; i++)
                fb.Tick(0.01f);

            // Assert
            Assert.AreEqual(1, targetA.HitCount, "同一火球同一目标只结算一次");
        }

        [Test]
        public void test_fireball_releases_after_landing()
        {
            // Arrange
            var fb = CreateProjectile(CreateData(18f, 15f), new Vector3(0f, 1.2f, 0f), Vector3.forward);

            // Act（总 1.2s > 飞行时间 0.833s，落地后回池）
            for (int i = 0; i < 120; i++) fb.Tick(0.01f);

            // Assert（落地应回池（SetActive false））
            Assert.IsFalse(fb.gameObject.activeSelf, "落地应回池（SetActive false）");
            fb.Tick(0.1f); // 回池后再步进不抛异常
        }

        [Test]
        public void test_fireball_with_zero_explosion_radius_hits_single_target_only()
        {
            // Arrange（爆炸半径 0 = 纯单体；B 在 A 旁 1m 但不得被波及）
            var data = CreateData(18f, 15f, 0f);
            var origin = new Vector3(0f, 1.2f, 0f);
            Vector3 posA = SimulateFlight(data, origin, Vector3.forward, 0.01f, 40);
            var targetA = new FakeDamageable(NewGo("A").transform);
            targetA.Transform.position = posA;
            Register(targetA);
            var targetB = new FakeDamageable(NewGo("B").transform);
            targetB.Transform.position = posA + new Vector3(0.8f, 0f, 0.6f);
            Register(targetB);
            var fb = CreateProjectile(data, origin, Vector3.forward);

            // Act
            for (int i = 0; i < 40; i++) fb.Tick(0.01f);

            // Assert
            Assert.AreEqual(1, targetA.HitCount);
            Assert.AreEqual(0, targetB.HitCount, "爆炸半径 0 时只命中直接目标");
        }

        [Test]
        public void test_fireball_no_explosion_mid_flight()
        {
            // Arrange（地面目标在弹道正下方：弧线从上方掠过，飞行中不应被命中）
            var data = CreateData(18f, 15f);
            var origin = new Vector3(0f, 1.2f, 0f);
            var groundTarget = new FakeDamageable(NewGo("Ground").transform);
            groundTarget.Transform.position = new Vector3(0f, 0f, 5.4f);
            Register(groundTarget);
            var fb = CreateProjectile(data, origin, Vector3.forward);

            // Act（0.3s 飞行中段：弹道在 y≈1.55，地面目标在 y=0）
            for (int i = 0; i < 30; i++) fb.Tick(0.01f);

            // Assert（弧线掠过不命中地面目标，火球继续飞行）
            Assert.AreEqual(0, groundTarget.HitCount, "弹道中段不应命中正下方地面目标");
            Assert.IsTrue(fb.gameObject.activeSelf, "中段飞行不应回池");
        }
    }
}
