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
            // 6 × 1/15 = 0.4m 恰好等于预算；0.39 留出 float 边界余量。
            X = 0.39f, Y = 0.7f, Z = 0f, Speed = 6f, Sequence = 2,
            JumpPhase = PlayerJumpPhase.Air, JumpSeq = 3, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 2);

        Assert.False(air.Value.NeedsCorrection);
    }

    [Fact]
    public void Jump_Start_Is_Grounded_Against_Previous_Accepted_Y()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        handler.SetGroundHeight(_ => 0f);
        // 上一包已接受位置 Y=0.3（出生点胶囊中心）；本次 Start 上报 Y=0.7——
        // 15Hz 采样已在起跳初段，仍必须接受（用 request.Y 判定会误拒）。
        player.UpdatePosition(new Vector3(0f, 0.3f, 0f), 0f, 0f, 0, 0);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 0.7f, Z = 0f, Speed = 6f, Sequence = 1,
            JumpPhase = PlayerJumpPhase.Start, JumpSeq = 5, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 1);

        Assert.True(player.Movement.JumpActive);
        Assert.Equal(0.3f, player.Movement.JumpStartY, 4);
    }

    [Fact]
    public void Jump_Start_Is_Rejected_When_Previous_Y_Is_Airborne()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        handler.SetGroundHeight(_ => 0f);
        player.UpdatePosition(new Vector3(0f, 2f, 0f), 0f, 0f, 0, 0);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 2f, Z = 0f, Speed = 6f, Sequence = 1,
            JumpPhase = PlayerJumpPhase.Start, JumpSeq = 6, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 1);

        Assert.False(player.Movement.JumpActive);
        Assert.Equal(6u, player.Movement.LastJumpSeq); // 序号仍记录：重复包不会再次尝试起跳
    }

    [Fact]
    public void ClampY_Clamps_Reported_Y_To_Ground_And_Jump_Arc()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        handler.SetGroundHeight(_ => 0f);
        player.UpdatePosition(new Vector3(0f, 0.3f, 0f), 0f, 0f, 0, 0);

        handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 0.3f, Z = 0f, Speed = 6f, Sequence = 1,
            JumpPhase = PlayerJumpPhase.Start, JumpSeq = 7, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 1);
        Assert.True(player.Movement.JumpActive);

        // 离谱高的 Y：夹到起跳弧线 + 0.05m（远低于 ground + MaxJumpHeight = 3m）。
        var high = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 50f, Z = 0f, Speed = 6f, Sequence = 2,
            JumpPhase = PlayerJumpPhase.Air, JumpSeq = 7, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 2);
        Assert.True(high.Value.NeedsCorrection);
        // 0.3 + 10.95/15 − 0.5×30/15² + 0.05 = 1.0133
        Assert.Equal(0.3f + 10.95f / 15f - 0.5f * 30f / 225f + 0.05f,
            high.Value.AuthoritativePosition.Y, 3);

        // 离谱低的 Y：夹到地面 −1m。
        var low = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = -50f, Z = 0f, Speed = 6f, Sequence = 3,
            JumpPhase = PlayerJumpPhase.Air, JumpSeq = 7, LifeId = player.LifeId,
            ClientControlVersion = player.ControlVersion
        }, player, nowTick: 3);
        Assert.True(low.Value.NeedsCorrection);
        Assert.Equal(-1f, low.Value.AuthoritativePosition.Y, 3);
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
