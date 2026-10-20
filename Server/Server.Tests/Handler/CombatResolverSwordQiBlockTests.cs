using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class CombatResolverSwordQiBlockTests
{
    private const long Caster = 1L;
    private const long TargetNpc = 2L;
    private const long Defender = 3L;
    private const long FarDefender = 4L;

    private static PlayerState AddPlayerAt(EntityStore store, long id,
        float x, float z, float yaw, bool defending)
    {
        var p = store.AddPlayer(id, "p" + id);
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

    private static void AssertBlockedDamage(float damage)
        => Assert.True(Math.Abs(damage - 27f) < 0.01f || Math.Abs(damage - 40.5f) < 0.01f,
            "格挡伤害应落在 {27, 40.5}：实际 " + damage);

    [Fact]
    public void SwordQi_Stops_At_Blocking_Player_And_Skips_Targets_Behind()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true);
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        var blocked = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.True(blocked.IsBlocked);
        AssertBlockedDamage(blocked.Damage);
        Assert.DoesNotContain(hits, h => h.TargetPlayerId == TargetNpc);

        var stop = Assert.Single(results.Where(r => r.ProjectileStopped));
        Assert.Equal(0f, stop.StopPosition.X, 0.001f);
        Assert.Equal(5f, stop.StopPosition.Z, 0.001f);
    }

    [Fact]
    public void SwordQi_Passes_Through_Defender_Facing_Away()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, Defender, 0f, 5f, 0f, defending: true); // 背对：yaw 0
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        var defenderHit = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.False(defenderHit.IsBlocked);
        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        Assert.DoesNotContain(results, r => r.ProjectileStopped);
    }

    [Fact]
    public void SwordQi_Passes_Through_NonDefending_Player()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: false);
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        Assert.Contains(hits, h => h.TargetPlayerId == Defender);
        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        Assert.DoesNotContain(results, r => r.ProjectileStopped);
    }

    [Fact]
    public void SwordQi_Closer_Targets_Before_Blocker_Are_Still_Hit()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        store.AddNpc(TargetNpc, "near", 1, 100f, new Vector3(0f, 0f, 2f));
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true);
        const long farNpc = 5L;
        store.AddNpc(farNpc, "far", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        var blocked = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.True(blocked.IsBlocked);
        Assert.DoesNotContain(hits, h => h.TargetPlayerId == farNpc);
    }

    [Fact]
    public void SwordQi_Stops_At_Nearest_Blocker_When_Far_Blocker_Is_Added_First()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, FarDefender, 0f, 6f, 180f, defending: true);
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true);
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        var near = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.True(near.IsBlocked);
        Assert.DoesNotContain(hits, h => h.TargetPlayerId == FarDefender);

        var stop = Assert.Single(results.Where(r => r.ProjectileStopped));
        Assert.Equal(5f, stop.StopPosition.Z, 0.001f);
    }

    [Fact]
    public void SwordQi_Block_Uses_Captured_Origin_After_Caster_Moves()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        AddPlayerAt(store, Defender, 0f, 5f, 180f, defending: true);
        var resolver = MakeResolver(store);
        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);

        resolver.DrainResolved(6); // 首段：捕获 pending.Origin
        store.GetPlayer(Caster)!.UpdatePosition(new Vector3(10f, 0f, 0f), 0f, 0f, 0L, 0U);
        var results = resolver.DrainResolved(10); // 第二段

        var blocked = Assert.Single(results.SelectMany(r => r.Hits)
            .Where(h => h.TargetPlayerId == Defender));
        Assert.True(blocked.IsBlocked);
        Assert.Contains(results, r => r.ProjectileStopped);
    }

    [Fact]
    public void SwordQi_Swing_Block_Outside_Line_Band_Does_Not_Stop_Projectile()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        // 距原点 2.482m、与 +Z 夹角 55.66°（挥击锥内），横向 2.05m（剑气半宽 2m 外）
        AddPlayerAt(store, Defender, 2.05f, 1.4f, -124.34f, defending: true);
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();

        var swingHit = Assert.Single(hits.Where(h => h.TargetPlayerId == Defender));
        Assert.True(swingHit.IsBlocked);
        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        Assert.DoesNotContain(results, r => r.ProjectileStopped);
    }

    [Fact]
    public void SwordQi_Does_Not_Stop_On_Dead_Defender_Killed_By_Swing()
    {
        var store = new EntityStore();
        store.AddPlayer(Caster, "caster");
        var defender = AddPlayerAt(store, Defender, 0f, 2f, 180f, defending: true);
        defender.Hp = 10f;
        store.AddNpc(TargetNpc, "target", 1, 100f, new Vector3(0f, 0f, 9f));
        var resolver = MakeResolver(store);

        Assert.True(resolver.Decide(Caster, SwordQi(), 0).Accepted);
        var results = DrainTo(resolver, 40);
        var hits = results.SelectMany(r => r.Hits).ToList();
        var defenderHits = hits.Where(h => h.TargetPlayerId == Defender).ToList();

        Assert.Equal(2, defenderHits.Count);            // 挥击 + 线形
        Assert.True(defenderHits[0].IsBlocked);         // 挥击先结算且格挡
        Assert.False(defenderHits[1].IsBlocked);        // 线形对死者不再格挡
        Assert.Contains(hits, h => h.TargetPlayerId == TargetNpc);
        Assert.DoesNotContain(results, r => r.ProjectileStopped);
    }
}
