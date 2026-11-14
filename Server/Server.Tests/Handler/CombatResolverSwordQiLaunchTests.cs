using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class CombatResolverSwordQiLaunchTests
{
    private const long Caster = 1L;
    private const long Defender = 2L;
    private const long Behind = 3L;

    private static PlayerState AddPlayerAt(EntityStore store, long id, float x, float z,
        float yaw, bool defending = false, string zone = "room-a")
    {
        var p = store.AddPlayer(id, "p" + id);
        p.ZoneId = zone;
        p.UpdatePosition(new Vector3(x, 0f, z), yaw, 0f, 0L, 0U);
        p.IsDefending = defending;
        return p;
    }

    private static CastRequest SwordQi() => new CastRequest
    {
        SkillId = 10002,
        Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
    };

    private static CombatResolver MakeResolver(EntityStore store)
        => new CombatResolver(store, new Random(1));

    private static List<CombatResolver.CastResolution> DrainTo(CombatResolver resolver, uint tick)
    {
        var all = new List<CombatResolver.CastResolution>();
        for (uint t = 1; t <= tick; t++) all.AddRange(resolver.DrainResolved(t));
        return all;
    }

    [Fact]
    public void Launch_And_Block_Stop_Carry_The_Same_CastId_As_Hits()
    {
        var store = new EntityStore();
        AddPlayerAt(store, Caster, 0f, 0f, 0f, zone: "room-a");
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true, zone: "room-a");
        AddPlayerAt(store, Behind, 0f, 9f, 180f, zone: "room-a");
        var resolver = MakeResolver(store);

        var decision = resolver.Decide(Caster, SwordQi(), 0);
        Assert.True(decision.Accepted);
        var results = DrainTo(resolver, 20);

        var launch = Assert.Single(results.Where(r => r.Launch != null).Select(r => r.Launch!));
        Assert.Equal(decision.Confirm!.CastId, launch.CastId);
        Assert.Equal(18f, launch.Speed, 0.001f);
        Assert.Equal(12f, launch.Range, 0.001f);
        Assert.Equal("room-a", launch.CombatSceneId);
        Assert.Equal(0f, launch.OriginX, 0.001f);
        Assert.Equal(0f, launch.OriginZ, 0.001f);

        var stop = Assert.Single(results.Where(r => r.Stop != null).Select(r => r.Stop!));
        Assert.Equal(decision.Confirm.CastId, stop.CastId);
        Assert.Equal(ProjectileStopReason.Blocked, stop.Reason);
        Assert.Equal(5f, stop.StopZ, 0.001f);

        var hits = results.SelectMany(r => r.Hits).ToList();
        Assert.DoesNotContain(hits, h => h.TargetPlayerId == Behind);
        Assert.All(hits, h => Assert.Equal(decision.Confirm.CastId, h.CastId));
    }

    [Fact]
    public void Launch_Is_Not_Repeated_For_Later_Stages()
    {
        var store = new EntityStore();
        AddPlayerAt(store, Caster, 0f, 0f, 0f);
        AddPlayerAt(store, Defender, 0f, 11f, 180f);
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 30);

        Assert.Single(results.Where(r => r.Launch != null));
        Assert.Contains(results.SelectMany(r => r.Hits), h => h.TargetPlayerId == Defender);
    }

    [Fact]
    public void Caster_Death_After_Launch_Produces_Stop_With_Death_Reason()
    {
        var store = new EntityStore();
        var caster = AddPlayerAt(store, Caster, 0f, 0f, 0f);
        AddPlayerAt(store, Defender, 0f, 5f, 180f);
        var resolver = MakeResolver(store);

        var decision = resolver.Decide(Caster, SwordQi(), 0);
        Assert.True(decision.Accepted);
        Assert.Single(DrainTo(resolver, 6).Where(r => r.Launch != null));

        caster.ApplyDamage(1000f);
        var results = DrainTo(resolver, 20);
        var stop = Assert.Single(results.Where(r => r.Stop != null).Select(r => r.Stop!));
        Assert.Equal(decision.Confirm!.CastId, stop.CastId);
        Assert.Equal(ProjectileStopReason.Death, stop.Reason);
    }

    [Fact]
    public void Default_SwordQi_Hits_First_Middle_And_Last_Stage_At_Expected_Ticks()
    {
        var store = new EntityStore();
        AddPlayerAt(store, Caster, 0f, 0f, 0f);
        var near = AddPlayerAt(store, 10L, 0f, 3.5f, 180f);   // 段 0: 0..4m
        var middle = AddPlayerAt(store, 11L, 0f, 7.5f, 180f); // 段 1: 4..8m
        var far = AddPlayerAt(store, 12L, 0f, 11.5f, 180f);   // 段 2: 8..12m
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);

        // firstTick = ceil(0.35 * 15) = 6；段长 4m / 18 m/s * 15Hz = 3.33 → ceil = 4 tick
        var stage0 = resolver.DrainResolved(6);
        Assert.Contains(stage0.SelectMany(r => r.Hits), h => h.TargetPlayerId == near.PlayerId);
        Assert.DoesNotContain(stage0.SelectMany(r => r.Hits), h => h.TargetPlayerId == middle.PlayerId);
        Assert.DoesNotContain(stage0.SelectMany(r => r.Hits), h => h.TargetPlayerId == far.PlayerId);

        var stage1 = resolver.DrainResolved(10);
        Assert.Contains(stage1.SelectMany(r => r.Hits), h => h.TargetPlayerId == middle.PlayerId);
        Assert.DoesNotContain(stage1.SelectMany(r => r.Hits), h => h.TargetPlayerId == far.PlayerId);

        var stage2 = resolver.DrainResolved(13);
        Assert.Contains(stage2.SelectMany(r => r.Hits), h => h.TargetPlayerId == far.PlayerId);
    }

    [Fact]
    public void Different_CombatScenes_Do_Not_Hit_Each_Other()
    {
        var store = new EntityStore();
        AddPlayerAt(store, Caster, 0f, 0f, 0f, zone: "room-a");
        AddPlayerAt(store, Defender, 0f, 1.5f, 180f, zone: "room-b");
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 20);
        Assert.DoesNotContain(results.SelectMany(r => r.Hits), h => h.TargetPlayerId == Defender);
        var launch = Assert.Single(results.Where(r => r.Launch != null).Select(r => r.Launch!));
        Assert.Equal("room-a", launch.CombatSceneId);
    }
}
