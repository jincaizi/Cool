using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>Plan 04 任务 3：HitEntry 事件身份——EventSeq 每目标单调、EventId 全局唯一、
/// CombatVersion = 该次命中结算后的目标版本。</summary>
public class EventIdentityTests
{
    private static EntityStore MakeStore()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "caster");
        caster.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0L, 0U);
        // 2 = 显式挥砍目标（同时吃挥砍 + 剑气）；3 = 只在剑气线上（同段第二个目标）。
        store.AddNpc(2, "target", templateId: 1, hp: 10_000f, position: new Vector3(0f, 0f, 2f));
        store.AddNpc(3, "second", templateId: 1, hp: 10_000f, position: new Vector3(0.5f, 0f, 2f));
        return store;
    }

    private static CastRequest Heavy(long targetId = 2) => new()
    {
        SkillId = 10002,
        TargetPlayerId = targetId,
        Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f }
    };

    private static List<HitEntry> Drain(CombatResolver resolver, uint from, uint to)
    {
        var hits = new List<HitEntry>();
        for (uint t = from; t <= to; t++)
            foreach (var resolution in resolver.DrainResolved(t))
                hits.AddRange(resolution.Hits);
        return hits;
    }

    [Fact]
    public void EventSeq_Is_Monotonic_Per_Target_And_EventId_Is_Globally_Unique()
    {
        var store = MakeStore();
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var all = Drain(resolver, 1, 40);
        Assert.True(resolver.Decide(1, Heavy(), tick: 100).Accepted);   // 冷却 0.5s = 8 tick 后
        all.AddRange(Drain(resolver, 101, 140));

        var target2 = all.Where(h => h.TargetPlayerId == 2L).Select(h => h.EventSeq).ToList();
        Assert.Equal(new uint[] { 1, 2, 3, 4 }, target2);   // 挥砍+剑气 × 两次施法，逐次递增

        var target3 = all.Where(h => h.TargetPlayerId == 3L).Select(h => h.EventSeq).ToList();
        Assert.Equal(new uint[] { 1, 2 }, target3);         // 每目标独立从 1 起

        var eventIds = all.Select(h => h.EventId).ToList();
        Assert.All(eventIds, id => Assert.True(id > 0));
        Assert.Equal(eventIds.Count, eventIds.Distinct().Count());
    }

    [Fact]
    public void CombatVersion_On_HitEntry_Is_Target_Version_After_That_Hit()
    {
        var store = MakeStore();
        var target = store.GetPlayer(2)!;
        var second = store.GetPlayer(3)!;
        uint targetBefore = target.CombatVersion;
        uint secondBefore = second.CombatVersion;
        var resolver = new CombatResolver(store, new Random(1));

        Assert.True(resolver.Decide(1, Heavy(), tick: 0).Accepted);
        var hits = Drain(resolver, 1, 40);

        var targetHits = hits.Where(h => h.TargetPlayerId == 2L).ToList();
        Assert.Equal(2, targetHits.Count);
        Assert.Equal(targetBefore + 1u, targetHits[0].CombatVersion);   // 挥砍后版本
        Assert.Equal(targetBefore + 2u, targetHits[1].CombatVersion);   // 剑气后版本
        Assert.Equal(target.CombatVersion, targetHits[1].CombatVersion);

        var secondHit = Assert.Single(hits, h => h.TargetPlayerId == 3L);
        Assert.Equal(secondBefore + 1u, secondHit.CombatVersion);
        Assert.Equal(second.CombatVersion, secondHit.CombatVersion);
    }
}
