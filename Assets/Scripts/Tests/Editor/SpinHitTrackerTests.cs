using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class SpinHitTrackerTests
    {
        [Test]
        public void TryRecordHit_CountsPerTargetUpToCap()
        {
            var tracker = new SpinHitTracker(2);

            Assert.IsTrue(tracker.TryRecordHit(100), "第一次命中允许");
            Assert.IsTrue(tracker.TryRecordHit(100), "第二次命中允许");
            Assert.IsFalse(tracker.TryRecordHit(100), "第三次命中被上限拒绝");
            Assert.IsTrue(tracker.TryRecordHit(200), "其他目标独立计数");
        }

        [Test]
        public void TryRecordHit_ZeroCap_MeansUnlimited()
        {
            var tracker = new SpinHitTracker(0);
            for (int i = 0; i < 100; i++)
                Assert.IsTrue(tracker.TryRecordHit(100), "上限<=0 时永不拒绝");
        }

        [Test]
        public void TryRecordHit_NegativeCap_MeansUnlimited()
        {
            var tracker = new SpinHitTracker(-1);
            Assert.IsTrue(tracker.TryRecordHit(100));
            Assert.IsTrue(tracker.TryRecordHit(100));
        }

        [Test]
        public void GetHitCount_TracksPerTarget()
        {
            var tracker = new SpinHitTracker(5);
            tracker.TryRecordHit(100);
            tracker.TryRecordHit(100);
            tracker.TryRecordHit(200);

            Assert.AreEqual(2, tracker.GetHitCount(100));
            Assert.AreEqual(1, tracker.GetHitCount(200));
            Assert.AreEqual(0, tracker.GetHitCount(300));
        }

        [Test]
        public void Clear_ResetsAllCounts()
        {
            var tracker = new SpinHitTracker(1);
            tracker.TryRecordHit(100);
            Assert.IsFalse(tracker.TryRecordHit(100));

            tracker.Clear();

            Assert.IsTrue(tracker.TryRecordHit(100), "Clear 后重新计数");
        }
    }
}
