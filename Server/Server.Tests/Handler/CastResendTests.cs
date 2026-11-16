using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class CastResendTests
{
    private static CastRequest SwordQi() => new CastRequest
    {
        SkillId = 10002,
        Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f },
        ClientCastSeq = 1
    };

    [Fact]
    public void Active_Projectile_Is_Retained_For_A_Late_Observer()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        caster.IsLoggedIn = true;
        caster.ZoneId = "room-a";
        var resolver = new CombatResolver(store, new System.Random(1));

        var cast = resolver.Decide(1, SwordQi(), 10);
        Assert.True(cast.Accepted);

        var resolutions = resolver.DrainResolved(100);
        Assert.Contains(resolutions, r => r.Launch != null);

        var launches = resolver.ActiveLaunchesForTest(1).ToList();
        Assert.Single(launches);
        Assert.Equal(cast.CastId, launches[0].CastId);
    }

    [Fact]
    public void Cancel_After_Launch_Keeps_Active_Projectile()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        caster.IsLoggedIn = true;
        var resolver = new CombatResolver(store, new System.Random(1));
        var cast = resolver.Decide(1, SwordQi(), 10);
        // 10002 首段 = decide + ceil(0.35s*15) = 16；三段段末 16/20/23。
        // 只推进到第二段（21 < 23），取消时该施法仍在恢复期且剑气在飞。
        resolver.DrainResolved(21);
        Assert.Single(resolver.ActiveLaunchesForTest(1));

        var cancels = resolver.CancelPending(1, 10002, cast.CastId, 120,
            NpcActionCancelReason.Hit);

        Assert.Single(cancels);
        // 已发射的剑气不随施法取消而消失（a launched projectile must keep flying）。
        Assert.Single(resolver.ActiveLaunchesForTest(1));
    }

    [Fact]
    public void Blocked_Projectile_Is_Removed_From_Active_Launches()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        caster.IsLoggedIn = true;
        caster.ZoneId = "room-a";
        caster.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0L, 0U);
        var blocker = store.AddPlayer(2, "P2");
        blocker.ZoneId = "room-a";
        blocker.UpdatePosition(new Vector3(0f, 0f, 5f), 180f, 0f, 0L, 0U);
        blocker.IsDefending = true;
        var resolver = new CombatResolver(store, new System.Random(1));

        Assert.True(resolver.Decide(1, SwordQi(), 0).Accepted);
        var results = new System.Collections.Generic.List<CombatResolver.CastResolution>();
        for (uint tick = 1; tick <= 20; tick++) results.AddRange(resolver.DrainResolved(tick));

        Assert.Contains(results, r => r.Stop != null);
        Assert.Empty(resolver.ActiveLaunchesForTest(1));
    }
}
