using System;
using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class ClockSyncAlgorithmTests
    {
        [Test]
        public void Offset_Converges_To_True_Value_With_MinFilter()
        {
            var algo = new ClockSyncAlgorithm(8);
            long t1 = 1_000_000_000L;
            const long offsetTicks = 1_000_000L;
            const long rttTicks = 500_000L;
            const long outlierRttTicks = 5_000_000L;

            for (int i = 0; i < 7; i++)
            {
                var t2 = t1 + offsetTicks + rttTicks / 2;
                var t3 = t1 + rttTicks;
                algo.AddSample(t1, t2, t3);
                t1 += 1_000_000L;
            }
            {
                var t2 = t1 + offsetTicks + outlierRttTicks / 2;
                var t3 = t1 + outlierRttTicks;
                algo.AddSample(t1, t2, t3);
            }

            Assert.IsTrue(algo.HasConverged);
            Assert.GreaterOrEqual(algo.OffsetTicks, offsetTicks - 50_000);
            Assert.LessOrEqual(algo.OffsetTicks, offsetTicks + 50_000);
        }

        [Test]
        public void ToServerTime_Applies_Offset()
        {
            var algo = new ClockSyncAlgorithm(8);
            long t1 = 5_000_000L;
            algo.AddSample(t1, t1 + 1_000_000L, t1 + 2_000_000L);

            long serverTime = algo.ToServerTime(t1 + 1_000_000L);
            Assert.AreEqual(t1 + 1_000_000L, serverTime);
        }

        [Test]
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

            Assert.IsTrue(algo.HasConverged);
            Assert.AreEqual(1_000_000L, algo.OffsetTicks); // min-filter 选正常样本
        }

        [Test]
        public void Window_Rolls_Over_And_Evicts_Oldest()
        {
            var algo = new ClockSyncAlgorithm(4);
            long t1 = 1_000_000L;
            for (int i = 0; i < 4; i++)
            {
                algo.AddSample(t1, t1 + 500_000L, t1 + 1_000_000L); // rtt=1M, offset=0
                t1 += 1_000_000L;
            }
            Assert.AreEqual(0L, algo.OffsetTicks);

            // 第 5 个：rtt=100k（更小）offset=3M → 应被选中；且最旧样本被剔除
            algo.AddSample(t1, t1 + 3_050_000L, t1 + 100_000L); // rtt=100k, offset=3M
            Assert.AreEqual(3_000_000L, algo.OffsetTicks);
            Assert.IsTrue(algo.HasConverged);
        }

        [Test]
        public void Negative_Rtt_Sample_Is_Ignored()
        {
            var algo = new ClockSyncAlgorithm(8);
            long t1 = 1_000_000L;
            algo.AddSample(t1, t1 + 500_000L, t1 + 1_000_000L); // 正常：offset=0
            algo.AddSample(t1 + 2_000_000L, t1 + 2_500_000L, t1 + 1_000_000L); // rtt 为负（时钟回跳）

            Assert.AreEqual(0L, algo.OffsetTicks);   // 负样本被忽略
            Assert.IsFalse(algo.HasConverged);       // 样本数未增加
            algo.AddSample(t1 + 3_000_000L, t1 + 3_500_000L, t1 + 4_000_000L); // rtt=1M offset=0
            Assert.IsFalse(algo.HasConverged);       // 仍 2 个有效样本（8 窗）
        }
    }
}
