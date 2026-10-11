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
            fsm.ForceAdvanceForTest();                       // → Down

            // Act
            fsm.EnterHit(new HitData { Damage = 50f });

            // Assert
            Assert.AreEqual(HitState.Down, fsm.CurrentState, "倒地期间不应被后续命中断出");
        }
    }
}
