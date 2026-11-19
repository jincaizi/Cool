using System;
using System.Collections.Generic;
using System.Linq;
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class SwordQiEchoRulesTests
    {
        private static CombatCandidate Candidate(long id, float x, float z,
            bool alive = true, float radius = 0f)
            => new CombatCandidate(id, new Vec3(x, 0f, z), alive, radius);

        [Test]
        public void SelectNearest_MixedTargets_UsesSurfaceDistanceAndId()
        {
            var origin = new Vec3(0f, 0f, 0f);
            var candidates = new List<CombatCandidate>
            {
                Candidate(1, 10f, 0f, radius: 9f), // 中心距 10，表面距 1
                Candidate(2, 0f, 2f),
                Candidate(3, 0f, 3f),
                Candidate(4, 0f, 4f),
                Candidate(5, 0f, 5f),
                Candidate(6, 0f, 6f),
                Candidate(7, 0f, 7f),
                Candidate(10, 0f, 9f),
                Candidate(20, 0f, 9f),           // 与 ID 10 同表面距，ID 小的优先
                Candidate(99, 0f, 0.5f, alive: false),
                Candidate(100, 0f, 12.5f),       // 圆外
            };

            var selected = SwordQiEchoRules.SelectTargets(origin, 12f, 8, candidates);

            CollectionAssert.AreEqual(
                new long[] { 1, 2, 3, 4, 5, 6, 7, 10 },
                selected.Select(c => c.Id).ToArray());
        }

        [Test]
        public void SelectTargets_Includes_Exact_Radius_Boundary()
        {
            var selected = SwordQiEchoRules.SelectTargets(
                new Vec3(0f, 0f, 0f), 12f, 2,
                new[]
                {
                    Candidate(1, 0f, 12f),
                    Candidate(2, 0f, 12.001f),
                });

            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual(1, selected[0].Id);
        }

        [Test]
        public void BuildContacts_ThreeSwordsTenTargets_EachOnceBeforeEnd()
        {
            var targets = Enumerable.Range(1, 10)
                .Select(i => Candidate(i, (float)Math.Cos(i * 0.6), (float)Math.Sin(i * 0.6)))
                .ToList();

            var contacts = SwordQiEchoRules.BuildContacts(
                new Vec3(0f, 0f, 0f), targets, swordCount: 3, durationTicks: 30);

            Assert.AreEqual(10, contacts.Count);
            Assert.AreEqual(10, contacts.Select(c => c.TargetId).Distinct().Count());
            Assert.IsTrue(contacts.All(c => c.OffsetTicks >= 1 && c.OffsetTicks <= 29));

            for (int sword = 0; sword < 3; sword++)
            {
                var offsets = contacts.Where(c => c.SwordIndex == sword)
                    .Select(c => c.OffsetTicks).ToArray();
                CollectionAssert.AreEqual(offsets.OrderBy(x => x).ToArray(), offsets,
                    "同一道剑气的接触 tick 必须严格递增");
                Assert.AreEqual(offsets.Length, offsets.Distinct().Count());
            }

            CollectionAssert.AreEquivalent(
                new[] { 0, 1, 2 },
                contacts.Take(3).Select(c => c.SwordIndex).ToArray());
        }

        [Test]
        public void BuildContacts_TwoRounds_UsesExactTicks()
        {
            var targets = Enumerable.Range(1, 8)
                .Select(i => Candidate(i, (float)Math.Cos(i), (float)Math.Sin(i)))
                .ToList();

            var contacts = SwordQiEchoRules.BuildContacts(
                new Vec3(0f, 0f, 0f), targets, swordCount: 4, durationTicks: 30);

            Assert.AreEqual(8, contacts.Count);
            for (int sword = 0; sword < 4; sword++)
            {
                var offsets = contacts.Where(c => c.SwordIndex == sword)
                    .Select(c => c.OffsetTicks).ToArray();
                CollectionAssert.AreEqual(new uint[] { 6u, 24u }, offsets);
            }
        }

        [Test]
        public void BuildContacts_FourRounds_UsesExactTicks()
        {
            var targets = Enumerable.Range(1, 10)
                .Select(i => Candidate(i, (float)Math.Cos(i * 0.6), (float)Math.Sin(i * 0.6)))
                .ToList();

            var contacts = SwordQiEchoRules.BuildContacts(
                new Vec3(0f, 0f, 0f), targets, swordCount: 3, durationTicks: 30);

            var sword0 = contacts.Where(c => c.SwordIndex == 0)
                .Select(c => c.OffsetTicks).ToArray();
            CollectionAssert.AreEqual(new uint[] { 6u, 12u, 18u, 24u }, sword0);
        }

        [Test]
        public void BuildContacts_InvalidConfig_Throws()
        {
            var targets = new[] { Candidate(1, 0f, 1f) };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SwordQiEchoRules.BuildContacts(new Vec3(0f, 0f, 0f), targets, 0, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SwordQiEchoRules.BuildContacts(new Vec3(0f, 0f, 0f), targets, 1, 1));

            var many = Enumerable.Range(1, 10)
                .Select(i => Candidate(i, (float)Math.Cos(i * 0.6), (float)Math.Sin(i * 0.6)))
                .ToArray();
            Assert.Throws<InvalidOperationException>(() =>
                SwordQiEchoRules.BuildContacts(new Vec3(0f, 0f, 0f), many, 1, 2));
        }

        [Test]
        public void DefaultDamage_UsesBasicAttackBase()
        {
            Assert.IsTrue(CombatRules.TryGet(10001, out var light));
            Assert.AreEqual(light.BaseDamage * SwordQiEchoRules.Spec.DamageMultiplier,
                SwordQiEchoRules.DefaultDamage, 0.0001f);
            Assert.AreEqual(100f, SwordQiEchoRules.DefaultDamage, 0.0001f);

            Assert.IsTrue(CombatRules.TryGet(20006, out var echo));
            Assert.AreEqual(1, echo.ShapeType);
            Assert.AreEqual(12f, echo.MaxAreaRadius, 0.0001f);
            Assert.AreEqual(12f, echo.CooldownSeconds, 0.0001f);
            Assert.AreEqual(100f, echo.BaseDamage, 0.0001f);
            Assert.AreEqual(light.CriticalRate, echo.CriticalRate, 0.0001f);
        }

        [Test]
        public void DefaultSpec_MatchesDesignValues()
        {
            Assert.AreEqual(12f, SwordQiEchoRules.Spec.Radius, 0.0001f);
            Assert.AreEqual(8, SwordQiEchoRules.Spec.MaxTargets);
            Assert.AreEqual(2f, SwordQiEchoRules.Spec.DamageMultiplier, 0.0001f);
            Assert.AreEqual(2f, SwordQiEchoRules.Spec.DurationSeconds, 0.0001f);
            Assert.AreEqual(4, SwordQiEchoRules.Spec.SwordCount);
            Assert.AreEqual(0.75f, SwordQiEchoRules.Spec.OvershootMeters, 0.0001f);
        }
    }
}
