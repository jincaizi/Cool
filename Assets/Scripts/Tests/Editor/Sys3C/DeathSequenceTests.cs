using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 死亡序列的**状态机契约**（不碰 Animator，纯逻辑）。
    /// 存在理由：联网下死亡由服务端驱动，客户端一旦停在 Down 不出来，
    /// 被复活者也永远起不来——而那正是本次要消灭的失败形态。
    /// </summary>
    public class DeathSequenceTests
    {
        [Test]
        public void Death_Advances_To_Down_Then_GetUp_Then_None()
        {
            // Arrange
            var fsm = new HitFSM(animator: null);
            fsm.EnterDeath();

            // Act & Assert
            Assert.AreEqual(HitState.Death, fsm.CurrentState);
            fsm.ForceAdvanceForTest();                       // 死亡动画结束
            Assert.AreEqual(HitState.Down, fsm.CurrentState);
            fsm.ForceAdvanceForTest();                       // 服务端复活（RespawnNotice）
            Assert.AreEqual(HitState.GetUp, fsm.CurrentState);
            fsm.ForceAdvanceForTest();                       // 起身动画结束
            Assert.AreEqual(HitState.None, fsm.CurrentState);
        }

        [Test]
        public void Dead_Entity_Has_Super_Armor_And_Ignores_Later_Hits()
        {
            // Arrange
            var fsm = new HitFSM(animator: null);
            fsm.EnterDeath();
            Assert.IsTrue(fsm.HasSuperArmor, "死亡期间必须是霸体（既有条款）");

            fsm.ForceAdvanceForTest();                       // → Down

            // 霸体条款本身。必须单独断言：下面那条"不被断出"证不了它——
            // EnterHit 的优先级闸门（命中优先级 40 ≤ Down 的 90）先就把命中拒了，
            // 撤掉 HasSuperArmor 里的 Down 也照样绿。
            Assert.IsTrue(fsm.HasSuperArmor, "倒地（死亡后躺卧）期间必须保持霸体");

            // Act
            fsm.EnterHit(new HitData { Damage = 50f });

            // Assert
            Assert.AreEqual(HitState.Down, fsm.CurrentState, "倒地期间不应被后续命中断出");
        }
    }
}
