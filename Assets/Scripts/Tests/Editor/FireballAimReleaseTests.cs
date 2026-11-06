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
