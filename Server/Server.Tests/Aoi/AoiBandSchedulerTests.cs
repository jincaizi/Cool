using Xunit;

namespace KcpServer.Tests;

public class AoiBandSchedulerTests
{
    // 统一签名：ShouldSend(observerId, entityId, distance, tick, x, z, rotY)
    // 语义（终审 I5 修正）：移动实体（坐标自上次发送变化）按带频；
    // 静止实体（坐标未变）统一 4 tick 保活——近带静止 15Hz 全字段是带宽回归。
    private static bool Send(AoiBandScheduler s, long o, long e, float dist, uint tick,
        float x = 0f, float z = 0f, float rot = 0f)
        => s.ShouldSend(o, e, dist, tick, x, z, rot);

    [Fact]
    public void Near_Band_Moving_Entity_Updates_Every_Tick()
    {
        var scheduler = new AoiBandScheduler();
        // 移动中（x 每 tick +1）：近带 29m → tick 1,2,3... 全发
        Assert.True(Send(scheduler, 1, 2, 29f, 1, x: 10f));
        Assert.True(Send(scheduler, 1, 2, 29f, 2, x: 11f));
        Assert.True(Send(scheduler, 1, 2, 29f, 3, x: 12f));
    }

    [Fact]
    public void Mid_Band_Moving_Entity_Updates_Every_Second_Tick()
    {
        var scheduler = new AoiBandScheduler();
        Assert.True(Send(scheduler, 1, 2, 45f, 1, x: 10f));
        Assert.False(Send(scheduler, 1, 2, 45f, 2, x: 11f)); // 间隔未到
        Assert.True(Send(scheduler, 1, 2, 45f, 3, x: 12f));
        Assert.False(Send(scheduler, 1, 2, 45f, 4, x: 13f));
    }

    [Fact]
    public void Far_Band_Moving_Entity_Updates_Every_Fourth_Tick()
    {
        var scheduler = new AoiBandScheduler();
        Assert.True(Send(scheduler, 1, 2, 70f, 1, x: 10f));
        Assert.False(Send(scheduler, 1, 2, 70f, 2, x: 11f));
        Assert.False(Send(scheduler, 1, 2, 70f, 3, x: 12f));
        Assert.False(Send(scheduler, 1, 2, 70f, 4, x: 13f));
        Assert.True(Send(scheduler, 1, 2, 70f, 5, x: 14f));
    }

    [Fact]
    public void Static_Entity_Keepalive_Every_Four_Ticks_In_Any_Band()
    {
        // 静止（坐标恒同）近带实体：首帧后仅每 4 tick 保活——不占每 tick 带宽
        var scheduler = new AoiBandScheduler();
        Assert.True(Send(scheduler, 1, 2, 10f, 1));   // 首帧
        Assert.False(Send(scheduler, 1, 2, 10f, 2));  // 静止：距上次 < 4 tick
        Assert.False(Send(scheduler, 1, 2, 10f, 3));
        Assert.False(Send(scheduler, 1, 2, 10f, 4));
        Assert.True(Send(scheduler, 1, 2, 10f, 5));   // 4 tick 保活

        // 静止中带实体 3：tick5 首现即发（首帧），此后 4-tick 保活
        Assert.True(Send(scheduler, 1, 3, 45f, 5, x: 1f));
        Assert.False(Send(scheduler, 1, 3, 45f, 6, x: 1f));
        Assert.False(Send(scheduler, 1, 3, 45f, 7, x: 1f));
        Assert.False(Send(scheduler, 1, 3, 45f, 8, x: 1f));
        Assert.True(Send(scheduler, 1, 3, 45f, 9, x: 1f)); // 4 tick 保活
    }

    [Fact]
    public void Static_To_Moving_Resumes_Band_Cadence()
    {
        var scheduler = new AoiBandScheduler();
        Assert.True(Send(scheduler, 1, 2, 10f, 1));          // 首帧
        Assert.False(Send(scheduler, 1, 2, 10f, 2));         // 静止 4-tick 窗口内
        Assert.True(Send(scheduler, 1, 2, 10f, 3, x: 11f));  // 动了 → 立即发（带频 1）
        Assert.True(Send(scheduler, 1, 2, 10f, 4, x: 12f));  // 继续动 → 每 tick
    }

    [Fact]
    public void Band_Change_Resets_Cadence()
    {
        var scheduler = new AoiBandScheduler();
        Assert.True(Send(scheduler, 1, 2, 45f, 1, x: 10f));  // 中带首帧
        Assert.True(Send(scheduler, 1, 2, 29f, 2, x: 11f));  // 移入近带：带变化立即发
        Assert.True(Send(scheduler, 1, 2, 29f, 3, x: 12f));  // 近带移动：每 tick
    }

    [Fact]
    public void Out_Of_Sight_Clears_Schedule()
    {
        var scheduler = new AoiBandScheduler();
        Assert.True(Send(scheduler, 1, 2, 45f, 1, x: 10f));
        scheduler.MarkOutOfSight(observerId: 1, entityId: 2);
        Assert.True(Send(scheduler, 1, 2, 45f, 10, x: 20f));  // 重见：节拍重置立即发
        Assert.False(Send(scheduler, 1, 2, 45f, 11, x: 21f)); // 中带间隔 2：下一发在 12
    }

    [Fact]
    public void Rotation_Change_Counts_As_Movement()
    {
        var scheduler = new AoiBandScheduler();
        Assert.True(Send(scheduler, 1, 2, 10f, 1));
        Assert.False(Send(scheduler, 1, 2, 10f, 2));          // 静止
        Assert.True(Send(scheduler, 1, 2, 10f, 3, rot: 90f)); // 原地转身 → 发
    }
}
