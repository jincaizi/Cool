using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 任务 9 修复轮：移动端松手帧必须保留拖动落点。
    /// 时序：MobileInputBinder.LateUpdate 同帧写入 aim=false 与 release 锁存；
    /// Sys3CEntry 下一帧才消费锁存——此时 mobileAim 已为 false，若走 PC 兜底
    /// 会把拖动落点改写成前方最大射程。
    /// </summary>
    public class FireballAimReleaseTests
    {
        [Test]
        public void test_should_keep_last_aim_truth_table()
        {
            Assert.IsFalse(Sys3CEntry.ShouldKeepLastAim(true, true),
                "移动端瞄准中（手势仍活）走实时落点，不保留");
            Assert.IsFalse(Sys3CEntry.ShouldKeepLastAim(true, false),
                "移动端瞄准中且无释放锁存：走实时落点");
            Assert.IsTrue(Sys3CEntry.ShouldKeepLastAim(false, true),
                "松手帧：mobileAim 已归零但释放锁存未消费 ⇒ 必须保留上一帧落点");
            Assert.IsFalse(Sys3CEntry.ShouldKeepLastAim(false, false),
                "键鼠蓄力帧无释放锁存 ⇒ 正常走前方最大射程");
        }

        [Test]
        public void test_aim_latches_survive_casting_until_skill_ends()
        {
            // Casting（技能活跃但还不是 Charging）不得清锁存——0.1s 点按的释放锁存
            // 正是在 Casting 帧被消费的；清掉就会在首个 Charging 帧退回最大射程。
            Assert.IsFalse(Sys3CEntry.ShouldResetAimLatches(aimCharging: false, skillActive: true),
                "Casting 期必须保留锁存");
            Assert.IsTrue(Sys3CEntry.ShouldResetAimLatches(aimCharging: false, skillActive: false),
                "技能真正结束（CurrentSkill == null）才清锁存");
            Assert.IsFalse(Sys3CEntry.ShouldResetAimLatches(aimCharging: true, skillActive: true),
                "瞄准蓄力中不清锁存");
            Assert.IsFalse(Sys3CEntry.ShouldResetAimLatches(aimCharging: true, skillActive: false));
        }

        [Test]
        public void test_should_re_resolve_default_aim_truth_table()
        {
            Assert.IsTrue(Sys3CEntry.ShouldReResolveDefaultAim(keepLastAim: true, wasDragged: false),
                "点按（未拖动）在松手帧必须重新解析默认落点（激活后才完成自动锁定）");
            Assert.IsFalse(Sys3CEntry.ShouldReResolveDefaultAim(keepLastAim: true, wasDragged: true),
                "拖动过的松手帧保留最后拖拽落点");
            Assert.IsFalse(Sys3CEntry.ShouldReResolveDefaultAim(keepLastAim: false, wasDragged: false));
            Assert.IsFalse(Sys3CEntry.ShouldReResolveDefaultAim(keepLastAim: false, wasDragged: true));
        }

        [Test]
        public void test_tap_release_during_casting_re_resolves_default_on_first_charging_frame()
        {
            // 1) 点按在 Casting 帧松手：释放锁存已被消费（latched=true），技能仍活跃
            bool latched = true;
            Assert.IsTrue(latched, "前置：释放锁存已消费并锁存");
            Assert.IsFalse(Sys3CEntry.ShouldResetAimLatches(aimCharging: false, skillActive: true),
                "Casting 帧不得清锁存");

            // 2) 首个 Charging 帧（gesture 已结束）：必须保留落点路径
            bool keepLastAim = Sys3CEntry.ShouldKeepLastAim(
                mobileAim: false, releasePendingOrLatched: latched);
            Assert.IsTrue(keepLastAim, "锁存跨过 Casting 后，首个 Charging 帧仍须保留落点");

            // 3) 从未拖动 ⇒ 此时自动锁定已完成，重新解析默认落点
            Assert.IsTrue(Sys3CEntry.ShouldReResolveDefaultAim(keepLastAim, wasDragged: false),
                "点按必须重新解析默认落点，读取激活后锁定的目标");
        }

        [Test]
        public void test_release_frame_after_aim_reset_reports_keep_last_aim()
        {
            // Arrange（复现 binder 帧末状态：手势结束 → aim 归零 + 释放锁存同帧写入）
            var go = new GameObject("FireballAimReleaseTests");
            try
            {
                var input = go.AddComponent<InputManager>();
                input.SetSkillFireballAimFromUI(false, Vector2.zero, false);
                input.SetSkillFireballReleaseFromUI(canceled: false);

                // Act（下一次 Sys3CEntry.Update 的决策输入）
                bool keepLastAim = Sys3CEntry.ShouldKeepLastAim(
                    input.IsSkillFireballAimActive(), input.HasSkillFireballReleasePending());

                // Assert（不保留就会在 TryReleaseCharge 前把落点改写成最大射程）
                Assert.IsFalse(input.IsSkillFireballAimActive(), "前置：松手后 aim 已归零");
                Assert.IsTrue(keepLastAim, "松手帧必须保留上一帧移动端落点");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
