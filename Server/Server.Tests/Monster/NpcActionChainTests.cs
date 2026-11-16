using System.Linq;
using Cool.Protocol;
using KcpServer;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

public class NpcActionChainTests
{
    [Fact]
    public void Spawned_Monster_Exposes_Continuous_Action_Snapshot()
    {
        var store = new EntityStore();
        long seed = 2_000_000;
        var module = new MonsterModule(store, () => ++seed, actionV1: true);
        long id = module.SpawnMonster(1, new Vector3(0, 0, 0), new Vector3(0, 0, 0));

        var snapshot = module.GetActionSnapshot(id);

        Assert.NotNull(snapshot);
        Assert.Equal(NpcActionKind.Idle, snapshot!.Kind);
        Assert.Equal(NpcActionPhase.Continuous, snapshot.Phase);
    }

    [Fact]
    public void Aoi_Baseline_Carries_NpcAction_And_Mask()
    {
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "P1");
        observer.IsLoggedIn = true;
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        long seed = 2_000_000;
        var module = new MonsterModule(store, () => ++seed, actionV1: true);
        long id = module.SpawnMonster(1, new Vector3(1, 0, 1), new Vector3(1, 0, 1));

        var broadcaster = new AoiBroadcaster
        {
            NpcActionProvider = npcId => module.GetActionSnapshot(npcId)
        };
        var snapshots = broadcaster.BuildForObserver(observer, 1, store);

        var snapshot = snapshots.Single(s => s.PlayerId == id);
        Assert.NotNull(snapshot.NpcAction);
        Assert.NotEqual(0, snapshot.FieldMask & (ushort)EntityFieldMask.NpcAction);
    }

    [Theory]
    [InlineData(NpcActionKind.Idle, false)]
    [InlineData(NpcActionKind.Move, false)]
    [InlineData(NpcActionKind.Skill, true)]
    [InlineData(NpcActionKind.HitReaction, true)]
    [InlineData(NpcActionKind.Death, true)]
    public void Only_Non_Continuous_Actions_Broadcast(NpcActionKind kind, bool expected)
        => Assert.Equal(expected, Server3C.IsNpcActionBroadcastKind(kind));

    [Fact]
    public void Same_Instance_And_Revision_Is_Deduplicated()
    {
        Assert.True(Server3C.IsSameNpcAction(5, 2,
            new NpcActionSnapshot { InstanceId = 5, Revision = 2 }));
        Assert.False(Server3C.IsSameNpcAction(5, 2,
            new NpcActionSnapshot { InstanceId = 5, Revision = 3 }));
        Assert.False(Server3C.IsSameNpcAction(5, 2,
            new NpcActionSnapshot { InstanceId = 6, Revision = 2 }));
    }

    [Fact]
    public void Different_Zone_Is_Not_The_Same_Combat_Scene()
    {
        var a = new PlayerState(1) { ZoneId = "room-a" };
        var b = new PlayerState(2) { ZoneId = "room-b" };
        Assert.True(Server3C.IsSameCombatScene(a, a));
        Assert.False(Server3C.IsSameCombatScene(a, b));
    }
}
