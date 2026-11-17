using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

internal static class MovementBudgetTestHost
{
    public static PositionSyncHandler Create(out PlayerState player, out Server3C server)
    {
        var store = new EntityStore();
        server = new Server3C(new TestLogger());
        var handler = new PositionSyncHandler(server, new TestLogger(), store);
        player = store.AddPlayer(1, "P1");
        player.StrictMovementBudget = true;
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0, 0);
        return handler;
    }
}

public class PvpMovementBudgetTests
{

    [Fact]
    public void Walk_Budget_Is_One_Report_Interval()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);

        var result = handler.ValidateAndApply(new PositionSyncRequest
        {
            // 5m/s × 0.066s = 0.33m；取 0.32 留出 float 边界余量（0.33f > 5f×0.066f 约 1 ULP）。
            X = 0.32f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            LocomotionMode = LocomotionMode.Walk, LifeId = player.LifeId
        }, player, nowTick: 1);

        Assert.NotNull(result);
        Assert.False(result.Value.NeedsCorrection);
    }

    [Fact]
    public void Same_Tick_Second_Packet_Gets_No_Extra_Budget()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.33f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            LocomotionMode = LocomotionMode.Walk, LifeId = player.LifeId
        }, player, nowTick: 1);

        var second = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.66f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 2,
            LocomotionMode = LocomotionMode.Walk, LifeId = player.LifeId
        }, player, nowTick: 1);

        Assert.True(second.Value.NeedsCorrection);
        Assert.Equal(0.33f, second.Value.AuthoritativePosition.X, 3);
    }

    [Fact]
    public void Sprint_Defend_And_Status_Compose()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.ApplyControl(0.7f, false);

        var result = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 0f, Z = 0.5f, Speed = 8f, IsDefending = true, Sequence = 1,
            LocomotionMode = LocomotionMode.Sprint, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 1);

        // 8 × 0.4 × 0.7 × 0.066 = 0.14784m
        Assert.True(result.Value.NeedsCorrection);
        Assert.Equal(0.14784f, result.Value.AuthoritativePosition.Z, 3);
    }

    [Fact]
    public void Old_Control_Version_Is_Rejected_Without_Consuming_Budget()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.ApplyControl(0.7f, false);

        var stale = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 3f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            ClientControlVersion = 0, LifeId = player.LifeId
        }, player, nowTick: 1);

        Assert.True(stale.Value.NeedsCorrection);
        Assert.Equal(0f, stale.Value.AuthoritativePosition.X, 3);

        var fresh = handler.ValidateAndApply(new PositionSyncRequest
        {
            // 0.7 减速下 5m/s × 0.7 × 0.066s = 0.231m；取 0.23 留出 float 边界余量。
            X = 0.23f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 2,
            ClientControlVersion = player.ControlVersion, LifeId = player.LifeId
        }, player, nowTick: 2);

        Assert.False(fresh.Value.NeedsCorrection);
    }

    [Fact]
    public void Dash_Window_Only_Grants_For_Matching_CastId_And_Skill()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.Displacement = new DisplacementWindow
        {
            CastId = 42, SkillId = 20001, StartTick = 1, EndTick = 19,
            TotalDistance = 2f, Speed = 10f
        };

        var mismatch = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.5f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            DisplacementSkillId = 20001, CastId = 41, LifeId = player.LifeId
        }, player, nowTick: 2);
        Assert.True(mismatch.Value.NeedsCorrection);

        var match = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.5f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 2,
            DisplacementSkillId = 20001, CastId = 42, LifeId = player.LifeId
        }, player, nowTick: 3);
        Assert.False(match.Value.NeedsCorrection);
    }
}
