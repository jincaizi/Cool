using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// D17：速度由**渲染位移反推**（动画要匹配的是屏幕上的实际移动），不穿协议。
    /// 两条工程约束各有用例：钳位（瞬移不让导数爆掉）、平滑（快照 15Hz 的台阶）。
    /// </summary>
    public class RenderSpeedEstimatorTests
    {
        [Test]
        public void Step_Returns_Zero_Delta_Wins_As_Keep()
        {
            // 首帧（dt<=0）保持上一值——新建视图不该凭空冒出速度
            Assert.AreEqual(0f, RenderSpeedEstimator.Step(0f, Vector3.zero, new Vector3(9f, 0f, 0f), 0f, 12f));
            Assert.AreEqual(3f, RenderSpeedEstimator.Step(3f, Vector3.zero, new Vector3(9f, 0f, 0f), -1f, 12f));
        }

        [Test]
        public void Constant_Motion_Converges_To_The_True_Speed()
        {
            float speed = 0f;
            // 8 m/s 直线移动，每帧 66ms（服务器 tick），跑 1s（15 帧）——平滑应基本收敛
            for (int i = 0; i < 15; i++)
                speed = RenderSpeedEstimator.Step(speed,
                    new Vector3(i * 0.528f, 0f, 0f), new Vector3((i + 1) * 0.528f, 0f, 0f), 0.066f, 12f);
            Assert.That(speed, Is.EqualTo(8f).Within(0.6f), "匀速 8 m/s 应收敛到 8（平滑只该延迟，不该改终值）");
        }

        [Test]
        public void Teleport_Is_Clamped()
        {
            // 重生/服务器校正：一帧内瞬移 100m ⇒ 原始导数约 1500 m/s，必须被钳住
            float speed = RenderSpeedEstimator.Step(0f, Vector3.zero, new Vector3(100f, 0f, 0f), 0.066f, 12f);
            Assert.LessOrEqual(speed, 12f, "瞬移的导数必须钳到 maxSpeed（否则 Blend 会跳到跑档）");
        }

        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SpeedTestRoot");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            _registry.Clear();
            Object.DestroyImmediate(_root);
        }

        private static EntitySnapshot Snap(long id, float x, long tick)
            => new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)EntityFieldMask.Position,
                X = x, Y = 0f, Z = 0f, Tick = (uint)tick
            };

        [Test]
        public void Registry_Derives_Speed_From_Rendered_Motion()
        {
            // 两个样本把渲染时刻夹在中间（**必须插值**：两次渲染都落在最后一个样本之后的话，
            // 位置会保持不动 ⇒ 反推速度为 0，这条用例就测不到东西了）。
            // 样本 A=(1_000_000, x=0) 样本 B=(2_320_000, x=1.056) ⇒ 1.056m / 0.132s = 8 m/s。
            // 渲染时刻取 1_660_000（t=0.5，x=0.528）与 1_990_000（t=0.75，x=0.792）：
            // Δx=0.264m / Δt=0.033s = 8 m/s。
            _registry.ApplySnapshot(Snap(1, 0f, 0), serverTicks: 1_000_000L);
            _registry.ApplySnapshot(Snap(1, 1.056f, 1), serverTicks: 2_320_000L);

            _registry.UpdateRender(nowServerTicks: 1_660_000L, renderDelayTicks: 0L);
            _registry.UpdateRender(nowServerTicks: 1_990_000L, renderDelayTicks: 0L);

            var view = _registry.GetView(1)!;
            Assert.That(view.Speed, Is.GreaterThan(1f), "渲染位移 8 m/s ⇒ 反推速度必须非零");
            Assert.LessOrEqual(view.Speed, 12f, "任何情况下不超过钳位上限");
        }

        [Test]
        public void Stationary_Entity_Has_Zero_Speed()
        {
            _registry.ApplySnapshot(Snap(2, 5f, 0), serverTicks: 1_000_000L);
            _registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);
            _registry.UpdateRender(nowServerTicks: 2_000_000L, renderDelayTicks: 0L);

            Assert.AreEqual(0f, _registry.GetView(2)!.Speed, "位置不动 ⇒ 速度 0（Idle）");
        }
    }
}
