using Cool.SyncCore;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class PredictionReconcilerTests
    {
        [Test]
        public void Deviation_Under_Threshold_No_Correction()
        {
            var reconciler = new PredictionReconciler();
            reconciler.RecordFrame(1, new Vec3(10, 0, 0), 0f);

            bool corrected = reconciler.TryGetCorrection(1, new Vec3(10.2f, 0, 0), 0f,
                out _, out _);

            Assert.IsFalse(corrected); // 0.2m < 0.5m 阈值
        }

        [Test]
        public void Deviation_Over_Threshold_Returns_Server_Position()
        {
            var reconciler = new PredictionReconciler();
            reconciler.RecordFrame(2, new Vec3(10, 0, 0), 0f);

            bool corrected = reconciler.TryGetCorrection(2, new Vec3(20, 0, 0), 0f,
                out Vec3 targetPos, out float targetRot);

            Assert.IsTrue(corrected); // 10m > 0.5m
            Assert.AreEqual(20f, targetPos.X, 0.0001f);
            Assert.AreEqual(0f, targetRot, 0.0001f);
        }

        [Test]
        public void Rotation_Deviation_Triggers_Correction()
        {
            var reconciler = new PredictionReconciler();
            reconciler.RecordFrame(3, new Vec3(0, 0, 0), 90f);

            bool corrected = reconciler.TryGetCorrection(3, new Vec3(0, 0, 0), 120f,
                out _, out float targetRot);

            Assert.IsTrue(corrected); // 30° > 5°
            Assert.AreEqual(120f, targetRot, 0.0001f);
        }

        [Test]
        public void Unknown_Sequence_No_Correction()
        {
            var reconciler = new PredictionReconciler();
            reconciler.RecordFrame(1, new Vec3(0, 0, 0), 0f);

            bool corrected = reconciler.TryGetCorrection(99, new Vec3(50, 0, 0), 0f,
                out _, out _);

            Assert.IsFalse(corrected); // 无对应预测帧：不纠偏（首次连接/帧被清理）
        }

        [Test]
        public void Prune_Removes_Old_Frames()
        {
            var reconciler = new PredictionReconciler();
            for (uint i = 1; i <= 70; i++)
                reconciler.RecordFrame(i, new Vec3(i, 0, 0), 0f);

            Assert.AreEqual(60, reconciler.FrameCount); // 上限 60

            reconciler.Prune(50);
            Assert.AreEqual(21, reconciler.FrameCount); // 保留 seq 50..70 共 21 帧
            Assert.IsFalse(reconciler.TryGetCorrection(10, new Vec3(0, 0, 0), 0f, out _, out _));
        }

        [Test]
        public void NonFinite_Ack_Ignored_No_Correction()
        {
            // 损坏 ack（NaN 权威位置 + 旋转同时超阈值）→ 守卫必须忽略：
            // 无守卫时 NaN 目标会随纠偏事件污染拉回（rotation 超阈值使 OR 短路为 true）
            var reconciler = new PredictionReconciler();
            reconciler.RecordFrame(1, new Vec3(10, 0, 0), 90f);

            bool corrected = reconciler.TryGetCorrection(1,
                new Vec3(float.NaN, 0, 0), 120f, out _, out _);

            Assert.IsFalse(corrected);
        }
    }
}
