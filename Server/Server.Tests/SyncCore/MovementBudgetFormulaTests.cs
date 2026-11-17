using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class MovementBudgetFormulaTests
{
    [Fact]
    public void NormalDistance_Composes_Walk_Defend_And_Status()
    {
        float distance = MovementBudgetMath.NormalDistance(
            LocomotionMode.Walk, defending: true, statusScale: 0.7f, seconds: 0.1f);

        Assert.Equal(5f * 0.4f * 0.7f * 0.1f, distance, 4);
    }

    [Fact]
    public void DeltaSeconds_Is_Capped_At_Half_A_Second()
    {
        Assert.Equal(0.5f, MovementBudgetMath.ClampDeltaSeconds(100u, 90u), 4);
        Assert.Equal(0f, MovementBudgetMath.ClampDeltaSeconds(10u, 10u), 4);
        Assert.Equal(0f, MovementBudgetMath.ClampDeltaSeconds(9u, 10u), 4);
    }

    [Fact]
    public void DashDistance_Is_Capped_By_Remaining_Distance()
    {
        float dash = MovementBudgetMath.DashSpeed(2f, 0.3f, 0.05f, 0.05f);
        Assert.Equal(10f, dash, 4);

        float granted = MovementBudgetMath.DashDistance(dash, 0.7f, 0.5f, remaining: 1.2f);
        Assert.Equal(1.2f, granted, 4);
    }

    [Fact]
    public void ClampHorizontal_Preserves_Direction_And_Length()
    {
        var prev = new Vec3(0f, 0f, 0f);
        var next = new Vec3(3f, 0f, 4f);

        var clamped = MovementBudgetMath.ClampHorizontal(prev, next, allowedDistance: 2.5f);

        Assert.Equal(1.5f, clamped.X, 4);
        Assert.Equal(2f, clamped.Z, 4);
        Assert.Equal(0f, clamped.Y, 4);
    }

    [Fact]
    public void MaxJumpY_Follows_Projectile_Motion_To_Apex()
    {
        float apex = 10.95f / 30f;
        float y = MovementBudgetMath.MaxJumpY(0.3f, 10.95f, -30f, apex);
        Assert.Equal(0.3f + 10.95f * 10.95f / (2f * 30f), y, 4);

        float after = MovementBudgetMath.MaxJumpY(0.3f, 10.95f, -30f, 10f);
        Assert.Equal(y, after, 4);
    }

    [Fact]
    public void DeltaSeconds_Uses_Tick_Rate_Below_The_Cap()
    {
        Assert.Equal(3f / 15f, MovementBudgetMath.ClampDeltaSeconds(4u, 1u), 4);
    }

    [Fact]
    public void NormalDistance_Covers_Sprint_Status_Clamp_And_Zero_Seconds()
    {
        Assert.Equal(8f * 0.7f * 0.5f,
            MovementBudgetMath.NormalDistance(LocomotionMode.Sprint, false, 0.7f, 0.5f), 4);
        Assert.Equal(8f * 4f * 0.5f,
            MovementBudgetMath.NormalDistance(LocomotionMode.Sprint, false, 99f, 0.5f), 4);
        Assert.Equal(0f,
            MovementBudgetMath.NormalDistance(LocomotionMode.Walk, false, 1f, 0f), 4);
    }

    [Fact]
    public void DashDistance_Uses_Product_When_Not_Capped()
    {
        Assert.Equal(10f * 0.7f * 0.1f,
            MovementBudgetMath.DashDistance(10f, 0.7f, 0.1f, remaining: 5f), 4);
    }

    [Fact]
    public void JumpInertiaDistance_Caps_At_Remaining()
    {
        Assert.Equal(2f, MovementBudgetMath.JumpInertiaDistance(10f, 0.5f, 2f), 4);
    }

    [Fact]
    public void NonFinite_Budget_Inputs_Return_Zero()
    {
        Assert.Equal(0f, MovementBudgetMath.DashDistance(
            float.PositiveInfinity, 0f, 0.1f, remaining: 2f), 4);
        Assert.Equal(0f, MovementBudgetMath.JumpInertiaDistance(
            float.PositiveInfinity, 0.1f, 2f), 4);
    }

    [Fact]
    public void ClampHorizontal_Freezes_Horizontal_On_NonFinite_Budget_And_Keeps_Y()
    {
        var clamped = MovementBudgetMath.ClampHorizontal(
            new Vec3(0f, 0f, 0f), new Vec3(3f, 1.7f, 4f), float.NaN);

        Assert.Equal(0f, clamped.X, 4);
        Assert.Equal(1.7f, clamped.Y, 4);
        Assert.Equal(0f, clamped.Z, 4);
    }
}
