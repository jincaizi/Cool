using System.Linq;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class SnapshotAssemblerTests
{
    [Fact]
    public void Dirty_Entity_Produces_FullField_Snapshot()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");
        player.UpdatePosition(new Vector3(1, 0, 2), 90f, 4f, 100L, 1U);

        var assembler = new SnapshotAssembler();
        var snapshots = assembler.BuildDirtySnapshot(store, tick: 10);

        var snap = Assert.Single(snapshots);
        Assert.Equal(1L, snap.PlayerId);
        Assert.True((snap.FieldMask & (ushort)EntityFieldMask.Position) != 0);
        Assert.True((snap.FieldMask & (ushort)EntityFieldMask.Rotation) != 0);
        Assert.True((snap.FieldMask & (ushort)EntityFieldMask.Speed) != 0);
        Assert.Equal(1f, snap.X, 4);
        Assert.Equal(90f, snap.Rotation, 4);
        Assert.Equal(10U, snap.Tick);
    }

    [Fact]
    public void Dirty_Entity_Is_Cleared_After_Build()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");
        store.MarkDirty(player);

        var assembler = new SnapshotAssembler();
        assembler.BuildDirtySnapshot(store, tick: 1);

        Assert.Empty(assembler.BuildDirtySnapshot(store, tick: 2));
    }

    [Fact]
    public void Idle_Entity_Beacon_After_Interval()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");

        var assembler = new SnapshotAssembler();
        const uint beaconInterval = 4;

        // 前 3 个 tick 无快照
        for (uint tick = 1; tick < beaconInterval; tick++)
            Assert.Empty(assembler.BuildBeaconSnapshot(store, tick, beaconInterval));

        // 第 4 个 tick 产出 beacon（仅位置字段）
        var beacon = Assert.Single(assembler.BuildBeaconSnapshot(store, beaconInterval, beaconInterval));
        Assert.Equal(EntityFieldMask.Position, (EntityFieldMask)beacon.FieldMask);
    }

    [Fact]
    public void AllVisibleFilter_Returns_All_Except_Self()
    {
        var filter = new AllVisiblePerceptionFilter();
        var players = new System.Collections.Generic.List<PlayerState>
        {
            new PlayerState(1), new PlayerState(2), new PlayerState(3)
        };
        var visible = filter.GetVisiblePlayerIds(observerPlayerId: 1, players);
        Assert.Equal(new[] { 2L, 3L }, visible);
    }
}
