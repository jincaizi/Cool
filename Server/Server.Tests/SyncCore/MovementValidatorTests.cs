using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class MovementValidatorTests
{
    [Fact]
    public void Speed_Over_Limit_Clamped_And_Corrected()
    {
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(1, 0, 0),
            rotation: 0f, speed: 30f,
            prevTimestamp: 0, nextTimestamp: 1_000_000L);

        Assert.True(result.NeedsCorrection);
        Assert.Equal(MovementValidator.MaxSpeed, result.ClampedSpeed, 4);
    }

    [Fact]
    public void Teleport_Over_Limit_Corrected_To_Plausible_Position()
    {
        // 1 秒内移动 100m（远超 15m/s × 1.2 容差）→ 纠偏到 prev + 方向 × MaxSpeed × dt
        // dir = (100,0,0) 归一化 = (1,0,0)；权威位置 = 0 + (1,0,0) × 15 × 1.0s = (15, 0, 0)
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(100, 0, 0),
            rotation: 0f, speed: 5f,
            prevTimestamp: 0, nextTimestamp: 10_000_000L);

        Assert.True(result.NeedsCorrection);
        Assert.InRange(result.AuthoritativePosition.X, 14.9f, 15.1f); // 15 × 1.0s
    }

    [Fact]
    public void Teleport_Under_Distance_Threshold_Accepted()
    {
        // 0.1s 内移动 1m（合理）→ 无纠偏
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(1, 0, 0),
            rotation: 0f, speed: 5f,
            prevTimestamp: 0, nextTimestamp: 1_000_000L);

        Assert.False(result.NeedsCorrection);
        Assert.Equal(5f, result.ClampedSpeed, 4);
    }

    [Fact]
    public void Vertical_Jump_Over_Height_Corrected()
    {
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(0, 10, 0),
            rotation: 0f, speed: 0f,
            prevTimestamp: 0, nextTimestamp: 1_000_000L);

        Assert.True(result.NeedsCorrection);
    }

    [Fact]
    public void Zero_TimeDelta_No_Teleport_Check()
    {
        // 时间戳相同（时钟未走）→ 跳过瞬移判定，仅速度判定
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(100, 0, 0),
            rotation: 0f, speed: 5f,
            prevTimestamp: 100, nextTimestamp: 100);

        Assert.False(result.NeedsCorrection);
    }

    [Fact]
    public void NonFinite_Input_Corrected_To_Previous_Position()
    {
        // Infinity 坐标（损坏/恶意包）→ 归一化 NaN 风险：守卫必须纠偏回 prev，不广播 NaN
        var result = MovementValidator.Validate(
            prev: new Vec3(0, 0, 0), next: new Vec3(float.PositiveInfinity, 0, 0),
            rotation: 0f, speed: 5f,
            prevTimestamp: 0, nextTimestamp: 1_000_000L);

        Assert.True(result.NeedsCorrection);
        Assert.Equal(0f, result.AuthoritativePosition.X, 4);
        Assert.Equal(0f, result.AuthoritativePosition.Y, 4);
        Assert.Equal(0f, result.AuthoritativePosition.Z, 4);
    }
}
