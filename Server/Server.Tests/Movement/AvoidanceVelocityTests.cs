using KcpServer;
using KcpServer.Monster;
using KcpServer.Movement;
using Xunit;

namespace KcpServer.Tests.Movement;

public class AvoidanceVelocityTests
{
    [Fact]
    public void Snapshot_Uses_Actual_Displacement_Not_Facing()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long npcId = module.SpawnMonster(1, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 0f));
        var entity = store.GetPlayer(npcId)!;

        var avoidance = new AvoidanceSystem(store, _ => 0.5f);
        avoidance.EnsureSnapshot(0);
        Assert.Equal(0f, avoidance.SnapshotVelocityOf(npcId).VX, 4);

        entity.UpdatePosition(new Vector3(0.5f, 0f, 0f), 90f, 0f, 1L, 0U); // 朝向 +X，实际也 +X
        avoidance.EnsureSnapshot(1);
        Assert.Equal(0.5f, avoidance.SnapshotVelocityOf(npcId).VX, 4);
        Assert.Equal(0f, avoidance.SnapshotVelocityOf(npcId).VZ, 4);

        entity.UpdatePosition(new Vector3(0.5f, 0f, 0.5f), 90f, 0f, 1L, 0U); // 朝向 +X，实际 +Z
        avoidance.EnsureSnapshot(2);
        Assert.Equal(0f, avoidance.SnapshotVelocityOf(npcId).VX, 4);
        Assert.Equal(0.5f, avoidance.SnapshotVelocityOf(npcId).VZ, 4);
    }
}
