using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    [Fact]
    public void Expired_Projectile_Is_Removed_After_Flight_Window()
    {
        var store = new EntityStore();
        var caster = store.AddPlayer(1, "P1");
        caster.IsLoggedIn = true;
        caster.ZoneId = "room-a";
        var resolver = new CombatResolver(store, new System.Random(1));

        Assert.True(resolver.Decide(1, SwordQi(), 10).Accepted);
        Assert.Contains(resolver.DrainResolved(21), r => r.Launch != null);
        var launch = Assert.Single(resolver.ActiveLaunchesForTest(1));

        // 12m / 18m/s × 15Hz = 10 tick；+2 tick 裕量内保留，+3 即过期清理。
        uint travelTicks = (uint)Math.Ceiling(
            launch.Range / launch.Speed * CombatRules.TickRateHz);
        resolver.DrainResolved(launch.LaunchTick + travelTicks + 2);
        Assert.Single(resolver.ActiveLaunchesForTest(1));

        resolver.DrainResolved(launch.LaunchTick + travelTicks + 3);
        Assert.Empty(resolver.ActiveLaunchesForTest(1));
    }

    [Fact]
    public void Active_Launches_Are_Bounded_To_128()
    {
        var store = new EntityStore();
        const int casters = 140;
        for (int i = 1; i <= casters; i++)
        {
            var player = store.AddPlayer(i, "P" + i);
            player.IsLoggedIn = true;
            player.ZoneId = "zone-" + i; // 每个施法者独占场景：首段不会命中其他施法者
        }
        var resolver = new CombatResolver(store, new System.Random(1));
        for (int i = 1; i <= casters; i++)
            Assert.True(resolver.Decide(i, SwordQi(), 0).Accepted);

        Assert.Contains(resolver.DrainResolved(6), r => r.Launch != null);

        int active = 0;
        for (int i = 1; i <= casters; i++) active += resolver.ActiveLaunchesForTest(i).Count;
        Assert.Equal(128, active);
    }

    [Fact]
    public void Resend_Only_Targets_Observer_In_Same_Combat_Scene()
    {
        using var server = new Server3C(new TestLogger());
        server.RegisterEntity(2, "ObsA").ZoneId = "room-a";
        server.RegisterEntity(3, "ObsB").ZoneId = "room-b";
        server.BindPlayerToSession(2, new FakeSession(20));
        server.BindPlayerToSession(3, new FakeSession(30));

        var sent = new List<(long SessionId, MessageId Id, IMessage Message)>();
        server.OfflineSendSink = (session, id, message) =>
            sent.Add((session.SessionId, id, message));

        var store = new EntityStore();
        store.AddPlayer(1, "P1").ZoneId = "room-a";
        var resolver = new CombatResolver(store, new System.Random(1));
        var cast = resolver.Decide(1, SwordQi(), 0);
        Assert.True(cast.Accepted);
        Assert.Contains(resolver.DrainResolved(6), r => r.Launch != null);

        var handler = new CastHandler(server, new TestLogger(), resolver);
        handler.ResendActiveProjectiles(2, 1);   // 同场景：补发
        handler.ResendActiveProjectiles(3, 1);   // 异场景：跳过
        handler.ResendActiveProjectiles(99, 1);  // observer 不存在：跳过

        var launched = Assert.Single(sent);
        Assert.Equal(20L, launched.SessionId);
        Assert.Equal(MessageId.ProjectileLaunchBroadcast, launched.Id);
        Assert.Equal(cast.CastId, ((ProjectileLaunchBroadcast)launched.Message).CastId);
    }

    private sealed class FakeSession : IServerSession
    {
        public FakeSession(long sessionId) => SessionId = sessionId;
        public long SessionId { get; }
        public bool IsConnected => true;
        public DateTime LastActivityTime => DateTime.UtcNow;
        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;
        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
        public void UpdateActivity() { }
        public bool IsTimedOut(TimeSpan timeout) => false;
        public Task CloseAsync(string reason = "Normal") => Task.CompletedTask;
        public void Dispose() { }
    }
}
