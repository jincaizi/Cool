using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>N2：决策 / 判定两段化——本任务内两段在同 tick 完成（行为零变化），
/// N3 起 resolveTick = decideTick + HitDelayTicks。</summary>
public class CastResolutionSchedulingTests
{
    private static (EntityStore Store, CombatResolver Resolver, long Caster, long Target) World(float targetZ)
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P");
        caster.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);
        var target = store.AddPlayer(2, "T");
        target.UpdatePosition(new Vector3(0f, 0f, targetZ), 0f, 0f, 1L, 0U);
        return (store, new CombatResolver(store, new System.Random(42)), 1, 2);
    }

    private static CastRequest Swing(long targetId = 0) => new CastRequest
    {
        SkillId = 10001, // BA_Combo1：Single range 2
        TargetPlayerId = targetId,
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
    };

    [Fact]
    public void Decide_Returns_Broadcast_But_No_Hits()
    {
        var (store, resolver, caster, target) = World(1.5f);

        var decision = resolver.Decide(caster, Swing(), 10);

        Assert.True(decision.Accepted);
        Assert.NotNull(decision.Broadcast);
        Assert.Equal(caster, decision.Broadcast!.CasterPlayerId);
        Assert.Equal(10001, decision.Broadcast.SkillId);
        Assert.NotEqual(0L, decision.Broadcast.ServerTimestamp);  // 决策时刻（wall-clock）
        Assert.Empty(decision.Hits);                              // 判定还没发生
        Assert.Equal(100f, store.GetPlayer(target)!.Hp, 4);       // 目标未掉血
    }

    [Fact]
    public void DrainResolved_At_DecideTick_Yields_This_Casts_Hits()
    {
        var (store, resolver, caster, target) = World(1.5f);
        resolver.Decide(caster, Swing(), 10);

        var resolved = resolver.DrainResolved(10);          // N2：同 tick 到期

        var one = Assert.Single(resolved);
        Assert.Equal(caster, one.Broadcast!.CasterPlayerId);
        var hit = Assert.Single(one.Hits);
        Assert.Equal(target, hit.TargetPlayerId);
        Assert.Equal(100f - hit.Damage, store.GetPlayer(target)!.Hp, 4);
    }
}
