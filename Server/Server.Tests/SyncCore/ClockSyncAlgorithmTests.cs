using System;
using Cool.SyncCore;
using Xunit;

namespace Cool.SyncCore.Tests;

public class ClockSyncAlgorithmTests
{
    /// <summary>
    /// 构造 8 样本：真实偏移 +100ms（server 快 100ms），RTT 均为 50ms；
    /// 混入一个 RTT 500ms 的离群样本，min-filter 应不受其干扰。
    /// </summary>
    [Fact]
    public void Offset_Converges_To_True_Value_With_MinFilter()
    {
        var algo = new ClockSyncAlgorithm(8);
        long t1 = 1_000_000_000L; // 客户端发送墙钟（ticks）
        // 100ms 偏移（ticks：100ms * 10_000 = 1_000_000 ticks）
        const long offsetTicks = 1_000_000L;
        const long rttTicks = 500_000L;        // 50ms
        const long outlierRttTicks = 5_000_000L; // 500ms 离群

        for (int i = 0; i < 7; i++)
        {
            var t2 = t1 + offsetTicks + rttTicks / 2; // server 收到时刻（server 时钟）
            var t3 = t1 + rttTicks;                   // 客户端收到时刻（墙钟）
            algo.AddSample(t1, t2, t3);
            t1 += 1_000_000L;
        }
        // 离群样本
        {
            var t2 = t1 + offsetTicks + outlierRttTicks / 2;
            var t3 = t1 + outlierRttTicks;
            algo.AddSample(t1, t2, t3);
        }

        Assert.True(algo.HasConverged, "8 样本后应收敛");
        Assert.InRange(algo.OffsetTicks, offsetTicks - 50_000, offsetTicks + 50_000);
    }

    [Fact]
    public void ToServerTime_Applies_Offset()
    {
        var algo = new ClockSyncAlgorithm(8);
        long t1 = 5_000_000L;
        algo.AddSample(t1, t1 + 1_000_000L, t1 + 2_000_000L); // RTT 200ms，offset 0

        long serverTime = algo.ToServerTime(t1 + 1_000_000L);
        // offset=0（两端时钟对齐）：墙钟 t1+1M 即服务器时钟 t1+1M
        Assert.Equal(t1 + 1_000_000L, serverTime);
    }
}
