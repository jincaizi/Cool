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

        // 先建立一个已接受基准（LastBudgetTick=1）——否则首个包走默认 1 tick 额度，
        // stale 包无论是否消耗预算，结果都一样，用例证明不了"不消耗"。
        var baseline = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            ClientControlVersion = player.ControlVersion, LifeId = player.LifeId
        }, player, nowTick: 1);
        Assert.False(baseline.Value.NeedsCorrection);

        var stale = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 3f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 2,
            ClientControlVersion = 0, LifeId = player.LifeId
        }, player, nowTick: 5);

        Assert.True(stale.Value.NeedsCorrection);
        Assert.Equal(0f, stale.Value.AuthoritativePosition.X, 3);

        var fresh = handler.ValidateAndApply(new PositionSyncRequest
        {
            // stale 不消耗预算 ⇒ dt = (6−1)/15 = 0.333s，0.7 减速预算 ≈1.167m；
            // 若 stale 错误推进游标 ⇒ 只剩 1 tick 0.233m，0.5m 会被纠偏。
            X = 0.5f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 3,
            ClientControlVersion = player.ControlVersion, LifeId = player.LifeId
        }, player, nowTick: 6);

        Assert.False(fresh.Value.NeedsCorrection);
    }

    [Fact]
    public void Dash_Window_Only_Grants_For_Matching_CastId_And_Skill()
    {
        // mismatch：错误 CastId 只有普通预算（5 × 1/15 ≈ 0.333m），0.5m 必须纠偏。
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

        // match：首个包按默认上报间隔 66ms 发放，额度 = normal 0.33 + dash 10×0.066 = 0.99m；
        // 0.9m 若没有突进额度（只剩 normal 0.33）就必须被纠偏
        //（旧用例 0.17m 落在普通预算内，证明不了发放）。
        var matchHandler = MovementBudgetTestHost.Create(out var matchPlayer, out _);
        matchPlayer.Displacement = new DisplacementWindow
        {
            CastId = 42, SkillId = 20001, StartTick = 1, EndTick = 19,
            TotalDistance = 2f, Speed = 10f
        };

        var match = matchHandler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.9f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            DisplacementSkillId = 20001, CastId = 42, LifeId = matchPlayer.LifeId
        }, matchPlayer, nowTick: 2);
        Assert.False(match.Value.NeedsCorrection);
    }

    [Fact]
    public void Dash_Grant_Is_Capped_By_Total_Distance_Plus_One_Tick()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.Displacement = new DisplacementWindow
        {
            CastId = 42, SkillId = 20001, StartTick = 1, EndTick = 19,
            TotalDistance = 0.5f, Speed = 10f
        };

        // 第一包：首个包按默认上报间隔 66ms 发放（normal 0.33 + 突进 10×0.066 = 0.99m），
        // 0.9m 通过；已用突进 0.66m（累计上限 = 0.5 + 10×1/15 = 1.167m）。
        var first = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.9f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            DisplacementSkillId = 20001, CastId = 42, LifeId = player.LifeId
        }, player, nowTick: 2);
        Assert.False(first.Value.NeedsCorrection);

        // 第二包请求再走 1.0m：首包按默认上报间隔 66ms 发放（突进 0.66），
        // 剩余突进额度 = 1.1667−0.66 = 0.5067m；总允许 = 0.3333 + 0.5067 = 0.84m，必须截断。
        var second = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 1.9f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 2,
            DisplacementSkillId = 20001, CastId = 42, LifeId = player.LifeId
        }, player, nowTick: 3);
        Assert.True(second.Value.NeedsCorrection);
        Assert.Equal(1.74f, second.Value.AuthoritativePosition.X, 3);
    }

    [Fact]
    public void Dash_Window_Expires_At_EndTick()
    {
        var handler = MovementBudgetTestHost.Create(out var player, out _);
        player.Displacement = new DisplacementWindow
        {
            CastId = 42, SkillId = 20001, StartTick = 1, EndTick = 5,
            TotalDistance = 2f, Speed = 10f
        };

        // nowTick >= EndTick：即使 CastId/SkillId 匹配，也只有普通预算 0.333m。
        var expired = handler.ValidateAndApply(new PositionSyncRequest
        {
            X = 0.5f, Y = 0f, Z = 0f, Speed = 5f, Sequence = 1,
            DisplacementSkillId = 20001, CastId = 42, LifeId = player.LifeId
        }, player, nowTick: 5);

        Assert.True(expired.Value.NeedsCorrection);
        Assert.Null(player.Displacement); // 到期即清，后续重复包不再命中旧窗口
    }
}
