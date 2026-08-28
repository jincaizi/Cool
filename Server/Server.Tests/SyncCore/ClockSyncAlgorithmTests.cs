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

    /// <summary>
    /// 离群样本 RTT 巨大（9M）且 offset 偏离（0.5M vs 正常 1M）；
    /// min-filter 按 RTT 选择，必须仍返回正常样本的 1M（平均/末样本规则会失败）。
    /// </summary>
    [Fact]
    public void Outlier_With_Asymmetric_Delay_Is_Rejected()
    {
        var algo = new ClockSyncAlgorithm(8);
        long t1 = 1_000_000_000L;
        for (int i = 0; i < 7; i++)
        {
            algo.AddSample(t1, t1 + 1_000_000L + 250_000L, t1 + 500_000L);
            t1 += 1_000_000L;
        }
        // 离群：rtt = 9M，offset = 5M − 9M/2 = 0.5M（与正常 1M 偏离）
        algo.AddSample(t1, t1 + 5_000_000L, t1 + 9_000_000L);

        Assert.True(algo.HasConverged);
        Assert.Equal(1_000_000L, algo.OffsetTicks); // min-filter 选正常样本
    }

    [Fact]
    public void Window_Rolls_Over_And_Evicts_Oldest()
    {
        var algo = new ClockSyncAlgorithm(4);
        long t1 = 1_000_000L;
        for (int i = 0; i < 4; i++)
        {
            algo.AddSample(t1, t1 + 500_000L, t1 + 1_000_000L); // rtt=1M, offset=0
            t1 += 1_000_000L;
        }
        Assert.Equal(0L, algo.OffsetTicks);

        // 第 5 个：rtt=100k（更小）offset=3M → 应被选中；且最旧样本被剔除
        algo.AddSample(t1, t1 + 3_050_000L, t1 + 100_000L); // rtt=100k, offset=3M
        Assert.Equal(3_000_000L, algo.OffsetTicks);
        Assert.True(algo.HasConverged);
    }

    [Fact]
    public void Negative_Rtt_Sample_Is_Ignored()
    {
        var algo = new ClockSyncAlgorithm(8);
        long t1 = 1_000_000L;
        algo.AddSample(t1, t1 + 500_000L, t1 + 1_000_000L); // 正常：offset=0
        algo.AddSample(t1 + 2_000_000L, t1 + 2_500_000L, t1 + 1_000_000L); // rtt 为负（时钟回跳）

        Assert.Equal(0L, algo.OffsetTicks);   // 负样本被忽略
        Assert.False(algo.HasConverged);      // 样本数未增加
        algo.AddSample(t1 + 3_000_000L, t1 + 3_500_000L, t1 + 4_000_000L); // rtt=1M offset=0
        Assert.False(algo.HasConverged);      // 仍 2 个有效样本（8 窗）
    }
}
