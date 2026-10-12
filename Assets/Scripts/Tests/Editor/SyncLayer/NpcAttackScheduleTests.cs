using System.Collections.Generic;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>攻击排程：变体必须由广播时间戳推出（不能靠客户端自增计数——AOI 边缘进出的客户端会错位）。</summary>
    public class NpcAttackScheduleTests
    {
        [Test]
        public void Variant_Is_A_Pure_Function_Of_The_Timestamp()
        {
            // 钉住实际取值（只断言 f(t) == f(t) 是恒真式，挡不住"改成按客户端计数"这种回归）
            Assert.AreEqual(0, NpcAttackSchedule.VariantOf(0L));
            Assert.AreEqual(1, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks));
            Assert.AreEqual(0, NpcAttackSchedule.VariantOf(NpcAttackSchedule.VariantBucketTicks * 2));

            // 纯函数：同一时间戳任意次调用结果一致（所有客户端各算各的也必须同结论）
            long t = 638_000_000_000_000_000L;
            Assert.AreEqual(NpcAttackSchedule.VariantOf(t), NpcAttackSchedule.VariantOf(t));
        }

        [Test]
        public void A_Swing_Sequence_Shows_Both_Variants()
        {
            // 变体是时间戳的纯函数（跨客户端一致，这是硬要求）；"交替"只是 cadence 的副产物——
            // 1.5s 冷却 = 22.5 个桶，桶序号每次 +22 或 +23，奇偶每两刀翻一次。
            // 这里断言**真正成立**的性质：一段连续挥砍里两段攻击都出现过（否则玩家永远只看同一段）。
            long t0 = 638_000_000_000_000_000L;
            var seen = new HashSet<int>();
            for (int k = 0; k < 10; k++)
                seen.Add(NpcAttackSchedule.VariantOf(t0 + k * 1_500L * 10_000L));

            Assert.AreEqual(2, seen.Count, "连续 10 刀里两段攻击都应出现过");
        }

        [Test]
        public void Drain_Only_Fires_Items_Whose_Time_Has_Come()
        {
            var schedule = new NpcAttackSchedule();
            schedule.Schedule(entityId: 7, serverTicks: 1_000L);
            var due = new List<NpcAttackSchedule.DueAttack>();

            schedule.Drain(renderAt: 999L, due);
            Assert.AreEqual(0, due.Count, "未到点不得触发");

            schedule.Drain(renderAt: 1_000L, due);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(7, due[0].EntityId);

            due.Clear();
            schedule.Drain(renderAt: 1_001L, due);
            Assert.AreEqual(0, due.Count, "**不得重播**——drain 要出队");
        }

        [Test]
        public void Late_Arrival_Fires_Immediately_Instead_Of_Being_Dropped()
        {
            var schedule = new NpcAttackSchedule();
            schedule.Schedule(7, serverTicks: 1_000L);
            var due = new List<NpcAttackSchedule.DueAttack>();

            schedule.Drain(renderAt: 5_000L, due);   // 卡顿/刚进视野：时间戳早已过去

            Assert.AreEqual(1, due.Count, "迟到要立即播，不能丢");
        }

        [Test]
        public void Clear_For_Entity_Drops_Its_Pending_Attacks()
        {
            var schedule = new NpcAttackSchedule();
            schedule.Schedule(7, 1_000L);
            schedule.Schedule(8, 1_000L);
            schedule.Clear(7);

            var due = new List<NpcAttackSchedule.DueAttack>();
            schedule.Drain(2_000L, due);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(8, due[0].EntityId);
        }
    }
}
