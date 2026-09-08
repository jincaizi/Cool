using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class PositionSyncEntityStoreTests
{
    [Fact]
    public void PositionSync_Updates_EntityStore_And_Marks_Dirty()
    {
        // 最小闭环：EntityStore + PositionSyncHandler 注入依赖（不依赖 Server3C 全量）
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");

        var handler = new PositionSyncHandler(new Server3C(new TestLogger()), new TestLogger(), store);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 5f, Y = 0f, Z = 0f, Rotation = 90f, Speed = 4f,
            Timestamp = 10_000_000L, Sequence = 1U
        }, player);

        Assert.True(player.IsDirty, "位置更新后必须标脏（快照链激活的前提）");
        Assert.Equal(5f, player.Position.X, 4);
        Assert.Equal(90f, player.Rotation, 4);
    }

    [Fact]
    public void OverSpeed_Report_Corrected_To_EntityStore()
    {
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");
        player.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 0L, 0U);

        var handler = new PositionSyncHandler(new Server3C(new TestLogger()), new TestLogger(), store);
        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 1f, Y = 0f, Z = 0f, Rotation = 0f, Speed = 99f,
            Timestamp = 1_000_000L, Sequence = 1U
        }, player);

        Assert.Equal(MovementValidator.MaxSpeed, player.Speed, 4); // 速度被钳制
        Assert.True(player.IsDirty);
    }

    [Fact]
    public void NanRotation_Request_Falls_Back_To_Last_Known_Rotation()
    {
        // 守卫回退：NaN 旋转请求不得进 store（防 NaN 经快照广播扩散）——
        // 位置纠偏回 prev、旋转保持上次已知有限值
        var store = new EntityStore();
        var player = store.AddPlayer(1, "A");
        player.UpdatePosition(new Vector3(5, 0, 0), 90f, 4f, 1_000_000L, 0U);

        var handler = new PositionSyncHandler(new Server3C(new TestLogger()), new TestLogger(), store);
        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 6f, Y = 0f, Z = 0f, Rotation = float.NaN, Speed = 4f,
            Timestamp = 2_000_000L, Sequence = 2U
        }, player);

        Assert.Equal(90f, player.Rotation, 4);   // 回退到最后已知有限旋转
        Assert.Equal(5f, player.Position.X, 4);  // 位置纠偏回 prev（守卫路径）
        Assert.True(player.IsDirty);
    }
}
