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
    }
}
