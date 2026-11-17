using System;
using System.Collections.Generic;
using System.Linq;
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class SwordQiEchoRulesTests
{
    private static CombatCandidate Candidate(long id, float x, float z,
        bool alive = true, float radius = 0f)
        => new CombatCandidate(id, new Vec3(x, 0f, z), alive, radius);

    [Fact]
    public void SelectNearest_MixedTargets_UsesSurfaceDistanceAndId()
    {
        var origin = new Vec3(0f, 0f, 0f);
        var candidates = new List<CombatCandidate>
        {
            Candidate(1, 10f, 0f, radius: 9f),
            Candidate(2, 0f, 2f),
            Candidate(3, 0f, 3f),
            Candidate(4, 0f, 4f),
            Candidate(5, 0f, 5f),
            Candidate(6, 0f, 6f),
            Candidate(7, 0f, 7f),
            Candidate(10, 0f, 9f),
            Candidate(20, 0f, 9f),
            Candidate(99, 0f, 0.5f, alive: false),
            Candidate(100, 0f, 12.5f),
        };

        var selected = SwordQiEchoRules.SelectTargets(origin, 12f, 8, candidates);

        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 10 },
            selected.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void SelectTargets_Includes_Exact_Radius_Boundary()
    {
        var selected = SwordQiEchoRules.SelectTargets(
            new Vec3(0f, 0f, 0f), 12f, 2,
            new[]
            {
                Candidate(1, 0f, 12f),
                Candidate(2, 0f, 12.001f),
            });

        Assert.Single(selected);
        Assert.Equal(1, selected[0].Id);
    }

    [Fact]
    public void BuildContacts_ThreeSwordsTenTargets_EachOnceBeforeEnd()
    {
        var targets = Enumerable.Range(1, 10)
            .Select(i => Candidate(i, (float)Math.Cos(i * 0.6), (float)Math.Sin(i * 0.6)))
            .ToList();

        var contacts = SwordQiEchoRules.BuildContacts(
            new Vec3(0f, 0f, 0f), targets, swordCount: 3, durationTicks: 30);

        Assert.Equal(10, contacts.Count);
        Assert.Equal(10, contacts.Select(c => c.TargetId).Distinct().Count());
        Assert.All(contacts, c => Assert.InRange(c.OffsetTicks, 1u, 29u));

        for (int sword = 0; sword < 3; sword++)
        {
            var offsets = contacts.Where(c => c.SwordIndex == sword)
                .Select(c => c.OffsetTicks).ToArray();
            Assert.Equal(offsets.OrderBy(x => x).ToArray(), offsets);
            Assert.Equal(offsets.Length, offsets.Distinct().Count());
        }

        Assert.Equal(new[] { 0, 1, 2 },
            contacts.Take(3).Select(c => c.SwordIndex).ToArray());
    }

    [Fact]
    public void BuildContacts_TwoRounds_UsesExactTicks()
    {
        var targets = Enumerable.Range(1, 8)
            .Select(i => Candidate(i, (float)Math.Cos(i), (float)Math.Sin(i)))
            .ToList();

        var contacts = SwordQiEchoRules.BuildContacts(
            new Vec3(0f, 0f, 0f), targets, swordCount: 4, durationTicks: 30);

        Assert.Equal(8, contacts.Count);
        for (int sword = 0; sword < 4; sword++)
        {
            var offsets = contacts.Where(c => c.SwordIndex == sword)
                .Select(c => c.OffsetTicks).ToArray();
            Assert.Equal(new uint[] { 6u, 24u }, offsets);
        }
    }

    [Fact]
    public void BuildContacts_FourRounds_UsesExactTicks()
    {
        var targets = Enumerable.Range(1, 10)
            .Select(i => Candidate(i, (float)Math.Cos(i * 0.6), (float)Math.Sin(i * 0.6)))
            .ToList();

        var contacts = SwordQiEchoRules.BuildContacts(
            new Vec3(0f, 0f, 0f), targets, swordCount: 3, durationTicks: 30);

        var sword0 = contacts.Where(c => c.SwordIndex == 0)
            .Select(c => c.OffsetTicks).ToArray();
        Assert.Equal(new uint[] { 6u, 12u, 18u, 24u }, sword0);
    }

    [Fact]
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

    [Fact]
    public void DefaultDamage_UsesBasicAttackBase()
    {
        Assert.True(CombatRules.TryGet(10001, out var light));
        Assert.Equal(light.BaseDamage * SwordQiEchoRules.Spec.DamageMultiplier,
            SwordQiEchoRules.DefaultDamage, 4);
        Assert.Equal(100f, SwordQiEchoRules.DefaultDamage, 4);

        Assert.True(CombatRules.TryGet(20006, out var echo));
        Assert.Equal(1, echo.ShapeType);
        Assert.Equal(12f, echo.MaxAreaRadius, 4);
        Assert.Equal(12f, echo.CooldownSeconds, 4);
        Assert.Equal(100f, echo.BaseDamage, 4);
        Assert.Equal(light.CriticalRate, echo.CriticalRate, 4);
    }

    [Fact]
    public void DefaultSpec_MatchesDesignValues()
    {
        Assert.Equal(12f, SwordQiEchoRules.Spec.Radius, 4);
        Assert.Equal(8, SwordQiEchoRules.Spec.MaxTargets);
        Assert.Equal(2f, SwordQiEchoRules.Spec.DamageMultiplier, 4);
        Assert.Equal(2f, SwordQiEchoRules.Spec.DurationSeconds, 4);
        Assert.Equal(4, SwordQiEchoRules.Spec.SwordCount);
        Assert.Equal(0.75f, SwordQiEchoRules.Spec.OvershootMeters, 4);
    }
}
