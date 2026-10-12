using Cool.SyncCore;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 远程插值器语义（spec D1：渲染时刻 = now − renderDelay，前后快照插值）——
    /// A_Moves 只能证明渲染管线执行，无法区分插值与贴最新；这里直接锁插值数学。
    /// </summary>
    public class RemoteInterpolatorTests
    {
        [Test]
        public void Render_At_Bracket_Midpoint_Interpolates_Between_Samples()
        {
            var interp = new RemoteInterpolator();
            interp.AddSample(1, 1_000_000L, new Vec3(0, 0, 0), 0f, actionState: 0, dead: false);
            interp.AddSample(1, 2_000_000L, new Vec3(10, 0, 0), 0f, actionState: 0, dead: false);

            // now = 3_000_000 − delay 1_500_000 → renderAt = 1_500_000（两样本中点）
            bool ok = interp.TryGetRenderState(1, 3_000_000L, 1_500_000L, out var pos, out _, out _, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(5f, pos.x, 0.0001f);
        }

        [Test]
        public void Render_At_Bracket_Interpolates_Rotation()
        {
            var interp = new RemoteInterpolator();
            interp.AddSample(1, 1_000_000L, new Vec3(0, 0, 0), 0f, actionState: 0, dead: false);
            interp.AddSample(1, 2_000_000L, new Vec3(0, 0, 0), 90f, actionState: 0, dead: false);

            bool ok = interp.TryGetRenderState(1, 3_000_000L, 1_500_000L, out _, out var rot, out _, out _);

            Assert.IsTrue(ok);
            Assert.AreEqual(45f, rot.eulerAngles.y, 0.1f); // Slerp 半程
        }

        [Test]
        public void Render_At_Newest_When_Clock_Lags_Samples()
        {
            // renderAt ≥ 最新样本（快照不足/时钟滞后）→ 贴最新（不插未来）
            var interp = new RemoteInterpolator();
            interp.AddSample(1, 1_000_000L, new Vec3(0, 0, 0), 0f, actionState: 0, dead: false);
            interp.AddSample(1, 2_000_000L, new Vec3(10, 0, 0), 0f, actionState: 0, dead: false);

            bool ok = interp.TryGetRenderState(1, 2_500_000L, 1_000_000L, out var pos, out _, out _, out _);
            // renderAt = 1_500_000 < newest 2_000_000 → 仍插值
            Assert.IsTrue(ok);
            Assert.AreEqual(5f, pos.x, 0.0001f);

            ok = interp.TryGetRenderState(1, 3_500_000L, 1_000_000L, out pos, out _, out _, out _);
            // renderAt = 2_500_000 ≥ newest → 贴最新
            Assert.IsTrue(ok);
            Assert.AreEqual(10f, pos.x, 0.0001f);
        }

        [Test]
        public void Render_At_Oldest_When_Clock_Ahead_Of_Samples()
        {
            // renderAt 早于全部样本（时钟未追平）→ 贴最旧
            var interp = new RemoteInterpolator();
            interp.AddSample(1, 1_000_000L, new Vec3(0, 0, 0), 0f, actionState: 0, dead: false);
            interp.AddSample(1, 2_000_000L, new Vec3(10, 0, 0), 0f, actionState: 0, dead: false);

            bool ok = interp.TryGetRenderState(1, 1_500_000L, 2_000_000L, out var pos, out _, out _, out _);
            // renderAt = −500_000 < 最旧 1_000_000
            Assert.IsTrue(ok);
            Assert.AreEqual(0f, pos.x, 0.0001f);
        }

        [Test]
        public void Missing_Player_Or_Empty_Returns_False()
        {
            var interp = new RemoteInterpolator();
            Assert.IsFalse(interp.TryGetRenderState(1, 0L, 0L, out _, out _, out _, out _));
            interp.AddSample(2, 1_000_000L, new Vec3(0, 0, 0), 0f, actionState: 0, dead: false);
            Assert.IsFalse(interp.TryGetRenderState(99, 0L, 0L, out _, out _, out _, out _));
        }

        [Test]
        public void Buffer_Caps_At_MaxSamples_Trimming_Oldest()
        {
            var interp = new RemoteInterpolator();
            for (int i = 1; i <= 20; i++)
                interp.AddSample(1, i * 1_000_000L, new Vec3(i, 0, 0), 0f, actionState: 0, dead: false);

            // 超 16 裁剪最旧：最早样本 (1,0,0) 已被裁——贴最旧应给第 5 个样本 (5,0,0)
            bool ok = interp.TryGetRenderState(1, 5_000_000L, 0L, out var pos, out _, out _, out _);
            Assert.IsTrue(ok);
            Assert.AreEqual(5f, pos.x, 0.0001f);
        }

        [Test]
        public void Render_State_Is_A_Step_Function_Of_The_Earlier_Sample()
        {
            var interp = new RemoteInterpolator();
            interp.AddSample(1, 1_000L, new Vec3(0f, 0f, 0f), 0f, actionState: 0, dead: false);
            interp.AddSample(1, 1_100L, new Vec3(10f, 0f, 0f), 0f, actionState: 2, dead: false);

            // renderAt = 1_150 − 100 = 1_050，落在 [1_000, 1_100] 正中 → 状态取**较早**那个样本（阶梯，不插值）
            Assert.IsTrue(interp.TryGetRenderState(1, 1_150L, 100L,
                out var pos, out _, out var actionState, out var dead));
            Assert.AreEqual(0, actionState, "状态是阶梯函数——取时间戳 ≤ renderAt 的最后一个样本");
            Assert.IsFalse(dead);
            Assert.AreEqual(5f, pos.x, 0.001f, "位置仍然插值");

            // 右端点：renderAt 恰等于区间右端样本的时间戳（且该样本**不是**最新样本，否则会走"贴最新"
            // 那条路、绕开区间）→ 状态取该样本，与 TryGetSampleState 同一条规则——
            // 两个入口不许在这里分叉（审查修复的守护点）
            interp.AddSample(1, 1_200L, new Vec3(20f, 0f, 0f), 0f, actionState: 5, dead: false);
            Assert.IsTrue(interp.TryGetRenderState(1, 1_200L, 100L, out _, out _, out var atRightEdge, out _));
            Assert.AreEqual(2, atRightEdge, "renderAt == 区间右端样本时间戳 ⇒ 取该样本");
        }

        [Test]
        public void Dead_Flag_Is_Sampled_Too()
        {
            var interp = new RemoteInterpolator();
            interp.AddSample(7, 500L, new Vec3(1f, 0f, 1f), 0f, actionState: 6, dead: false);
            interp.AddSample(7, 600L, new Vec3(1f, 0f, 1f), 0f, actionState: 6, dead: true);

            Assert.IsTrue(interp.TryGetRenderState(7, 650L, 100L, out _, out _, out var a, out var dead));
            Assert.AreEqual(6, a);
            Assert.IsFalse(dead, "renderAt=550 时第二样本还没到");

            Assert.IsTrue(interp.TryGetRenderState(7, 750L, 100L, out _, out _, out _, out dead));
            Assert.IsTrue(dead, "renderAt=650 时已越过第二样本");
        }

        [Test]
        public void Sample_State_Steps_At_The_Sample_Boundary()
        {
            var interp = new RemoteInterpolator();
            interp.AddSample(1, 1_000L, new Vec3(0f, 0f, 0f), 0f, actionState: 0, dead: false);
            interp.AddSample(1, 1_100L, new Vec3(10f, 0f, 0f), 0f, actionState: 2, dead: true);

            Assert.IsTrue(interp.TryGetSampleState(1, 1_050L, out var a, out var d));
            Assert.AreEqual(0, a, "renderAt 落在两样本之间 ⇒ 取较早那个");
            Assert.IsFalse(d);

            Assert.IsTrue(interp.TryGetSampleState(1, 1_100L, out a, out d));
            Assert.AreEqual(2, a, "renderAt 恰等于右端样本 ⇒ 取该样本（这是与 TryGetRenderState 曾经分叉的那个点）");
            Assert.IsTrue(d);
        }

        [Test]
        public void Sample_State_Falls_Back_To_The_Oldest_When_Everything_Is_Later()
        {
            var interp = new RemoteInterpolator();
            interp.AddSample(1, 5_000L, new Vec3(0f, 0f, 0f), 0f, actionState: 4, dead: false);
            interp.AddSample(1, 5_100L, new Vec3(0f, 0f, 0f), 0f, actionState: 2, dead: false);

            Assert.IsTrue(interp.TryGetSampleState(1, 100L, out var a, out _));
            Assert.AreEqual(4, a, "全部样本都晚于 renderAt ⇒ 用最旧");
        }
    }
}
