using System.Linq;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CastCancelBroadcastTests
{
    private static (EntityStore Store, CombatResolver Resolver) NewWorld()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        return (store, new CombatResolver(store, new System.Random(1)));
    }

    // Range 2f 同时落在 10001（上限 2f）与 10002（上限 2.5f）之内；
    // ClientCastSeq 0 = 旧客户端路径，避免同一序号被 Decide 幂等重放而不再入队。
    private static CastRequest Cast(int skillId = 10002) => new CastRequest
    {
        SkillId = skillId,
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f },
        ClientCastSeq = 0
    };

    [Fact]
    public void CancelPending_By_CastId_Returns_One_Authoritative_Cancel()
    {
        var (store, resolver) = NewWorld();
        var cast = resolver.Decide(1, Cast(), 10);
        Assert.True(cast.Accepted);

        var cancels = resolver.CancelPending(1, 10002, cast.CastId, 12, NpcActionCancelReason.Forced);

        Assert.Single(cancels);
        Assert.Equal(cast.CastId, cancels[0].CastId);
        Assert.Equal(1L, cancels[0].CasterPlayerId);
        Assert.Equal(12u, cancels[0].EffectiveTick);
        Assert.Equal(NpcActionCancelReason.Forced, cancels[0].Reason);
        Assert.Empty(resolver.CancelPending(1, 10002, cast.CastId, 13));
    }

    [Fact]
    public void CancelCaster_Removes_All_Pending()
    {
        var (store, resolver) = NewWorld();
        var first = resolver.Decide(1, Cast(10001), 0);
        var second = resolver.Decide(1, Cast(10002), 100);
        Assert.True(first.Accepted);
        Assert.True(second.Accepted);

        var cancels = resolver.CancelCaster(1, 120, NpcActionCancelReason.Death);

        Assert.Equal(2, cancels.Count);
        Assert.Contains(cancels, c => c.CastId == first.CastId);
        Assert.Contains(cancels, c => c.CastId == second.CastId);
        Assert.Empty(resolver.CancelCaster(1, 121, NpcActionCancelReason.Death));
    }

    [Fact]
    public void CancelPending_Without_CastId_Cancels_Only_The_Latest_Instance()
    {
        var (store, resolver) = NewWorld();
        var first = resolver.Decide(1, Cast(), 10);
        var second = resolver.Decide(1, Cast(), 100);
        Assert.True(first.Accepted);
        Assert.True(second.Accepted);

        var cancels = resolver.CancelPending(1, 10002, 0, 120,
            NpcActionCancelReason.Forced);

        Assert.Single(cancels);
        Assert.Equal(second.CastId, cancels[0].CastId);

        var older = resolver.CancelPending(1, 10002, 0, 121,
            NpcActionCancelReason.Forced);
        Assert.Single(older);
        Assert.Equal(first.CastId, older[0].CastId);
    }

    [Fact]
    public void Cancel_After_Launch_Keeps_Pending_And_Deduplicates()
    {
        var (store, resolver) = NewWorld();
        var cast = resolver.Decide(1, Cast(), 10);
        Assert.True(cast.Accepted);

        // 10002 首段 = decide + ceil(0.35s*15) = 16；三段的段末在 16/20/23。
        var firstStages = resolver.DrainResolved(21);
        Assert.Contains(firstStages, r => r.Launch != null);
        Assert.True(cast.CastId != 0);

        var cancels = resolver.CancelPending(1, 10002, cast.CastId, 22,
            NpcActionCancelReason.Hit);
        Assert.Single(cancels);
        Assert.Empty(resolver.CancelPending(1, 10002, cast.CastId, 23,
            NpcActionCancelReason.Hit));
        Assert.NotEmpty(resolver.DrainResolved(30));
    }
}
