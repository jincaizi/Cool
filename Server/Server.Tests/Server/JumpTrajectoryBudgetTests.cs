using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class JumpTrajectoryBudgetTests
{
    [Fact]
    public void Jump_Start_Records_Sequence_And_Air_Inertia_Under_ActionLock()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 0f, Z = 0f, Speed = 6f, Sequence = 1,
            JumpPhase = PlayerJumpPhase.Start, JumpSeq = 3, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 1);

        Assert.True(player.Movement.JumpActive);
        Assert.Equal(3u, player.Movement.LastJumpSeq);
        Assert.Equal(6f, player.Movement.JumpHorizontalSpeed, 4);

        // 起跳记录之后再麻痹：起跳本身在未锁状态被接受，空中惯性在锁定时发放。
        // （计划原文把 ApplyControl 放在起跳前，与"麻痹禁止新起跳"规则及本用例断言矛盾。）
        player.ApplyControl(0f, true);

        var air = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.4f, Y = 0.7f, Z = 0f, Speed = 6f, Sequence = 2,
            JumpPhase = PlayerJumpPhase.Air, JumpSeq = 3, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 2);

        Assert.False(air.Value.NeedsCorrection); // 6 × 1/15 = 0.4m
    }

    [Fact]
    public void Jump_Start_Is_Rejected_While_ActionLocked()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.ApplyControl(0f, true);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 0f, Z = 0f, Speed = 6f, Sequence = 1,
            JumpPhase = PlayerJumpPhase.Start, JumpSeq = 4, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 1);

        Assert.False(player.Movement.JumpActive);
        Assert.Equal(4u, player.Movement.LastJumpSeq);
    }
}
