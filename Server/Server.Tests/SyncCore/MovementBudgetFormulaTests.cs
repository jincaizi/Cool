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
}
