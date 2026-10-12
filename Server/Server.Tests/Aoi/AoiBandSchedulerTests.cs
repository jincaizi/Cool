using Xunit;

namespace KcpServer.Tests;

public class AoiBandSchedulerTests
{
    // 统一签名：ShouldSend(observerId, entityId, distance, tick, x, z, rotY, actionState, dead)
    // 语义（终审 I5 修正）：移动实体（坐标自上次发送变化）按带频；
    // 静止实体（坐标未变）统一 4 tick 保活——近带静止 15Hz 全字段是带宽回归。
    // 状态翻转（actionState/dead）立即发、不重置位置节拍——跨客户端动画一致性（2026-10-12）。
    private static bool Send(AoiBandScheduler s, long o, long e, float dist, uint tick,
        float x = 0f, float z = 0f, float rot = 0f, byte actionState = 0, bool dead = false)
        => s.ShouldSend(o, e, dist, tick, x, z, rot, actionState, dead);

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

    [Fact]
    public void State_Change_Sends_Immediately_For_A_Stationary_Entity()
    {
        var s = new AoiBandScheduler();
        Assert.True(s.ShouldSend(1, 2, 10f, 100, 0f, 0f, 0f, actionState: 0, dead: false));

        // 静止 + 状态未变：保活窗口（4 tick）未到，不发
        Assert.False(s.ShouldSend(1, 2, 10f, 101, 0f, 0f, 0f, actionState: 0, dead: false));

        // 状态翻转：立即发，不受保活间隔约束
        Assert.True(s.ShouldSend(1, 2, 10f, 102, 0f, 0f, 0f, actionState: 3, dead: false));
    }

    [Fact]
    public void State_Change_Does_Not_Push_Back_The_Position_Cadence()
    {
        var s = new AoiBandScheduler();
        const float mid = 45f;   // 中带 ⇒ IntervalOf = 2 tick

        Assert.True(s.ShouldSend(1, 2, mid, 100, 0f, 0f, 0f, 0, false));    // 首帧
        Assert.True(s.ShouldSend(1, 2, mid, 101, 0f, 0f, 0f, 0, true));     // 状态翻转（dead）——立即发，但**不**重置位置节拍
        Assert.True(s.ShouldSend(1, 2, mid, 102, 0.6f, 0f, 0f, 0, true));   // 距上次位置发送（tick 100）已 2 tick ⇒ 发
    }

    [Fact]
    public void State_Change_With_A_Move_Does_Not_Cause_A_Second_Send_Next_Tick()
    {
        // 翻转分支若只更新 actionState/dead、不写回锚点坐标：同一 tick 又带位移的话，
        // 下一 tick 会因 moved=（坐标 ≠ 旧锚点）再补发一帧（近带里就是同坐标的重复帧）。
        var s = new AoiBandScheduler();
        Assert.True(Send(s, 1, 2, 10f, 100, x: 0f, actionState: 0, dead: false));   // 首帧（锚点 x=0）
        Assert.True(Send(s, 1, 2, 10f, 101, x: 1f, actionState: 4, dead: false));   // 翻转 + 位移：立即发
        Assert.False(Send(s, 1, 2, 10f, 102, x: 1f, actionState: 4, dead: false));  // 坐标未变：不得再补一帧
    }

    [Fact]
    public void Same_Flip_Is_Sent_To_Two_Observers_On_The_Same_Tick()
    {
        var s = new AoiBandScheduler();
        const long entity = 9;
        Assert.True(s.ShouldSend(1, entity, 10f, 100, 0f, 0f, 0f, 0, false));   // 近带观察者
        Assert.True(s.ShouldSend(2, entity, 70f, 100, 0f, 0f, 0f, 0, false));   // 远带观察者

        // tick 102：两边保活都未到（100 + 4 = 104），若只看坐标/朝向则都不发
        Assert.True(s.ShouldSend(1, entity, 10f, 102, 0f, 0f, 0f, 4, false));
        Assert.True(s.ShouldSend(2, entity, 70f, 102, 0f, 0f, 0f, 4, false));
    }
}
