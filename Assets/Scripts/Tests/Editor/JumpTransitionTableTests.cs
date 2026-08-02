using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 回归测试：连续跳跃（落地瞬间再次起跳）后角色腿部失去移动动画、角色漂移。
    /// 根因之一：JumpEnd 状态没有 RequestJump → JumpStart 转换，
    /// 导致第二次跳跃被物理层（ProcessJumpRequest）消费后 FSM 直接冲刷回 Locomotion，
    /// 整个滞空过程 Animator 停在 Locomotion + Blend=0，腿部无动画。
    /// </summary>
    public class JumpTransitionTableTests
    {
        private static CharacterData GroundedData(bool requestJump)
        {
            return new CharacterData
            {
                IsGrounded = true,
                IsDead = false,
                RequestJump = requestJump
            };
        }

        [Test]
        public void Evaluate_JumpEndWithJumpRequest_ReturnsJumpStart()
        {
            // Arrange
            var table = new StateTransitionTable();
            var data = GroundedData(requestJump: true);

            // Act
            var target = table.Evaluate(BaseState.JumpEnd, data);

            // Assert
            Assert.AreEqual(BaseState.JumpStart, target,
                "落地帧仍有跳跃请求时应直接进入 JumpStart，而不是冲刷回 Locomotion");
        }

        [Test]
        public void Evaluate_JumpEndWithoutJumpRequest_ReturnsLocomotion()
        {
            // Arrange
            var table = new StateTransitionTable();
            var data = GroundedData(requestJump: false);

            // Act
            var target = table.Evaluate(BaseState.JumpEnd, data);

            // Assert
            Assert.AreEqual(BaseState.Locomotion, target);
        }

        [Test]
        public void Evaluate_JumpAirOnLanding_ReturnsJumpEnd()
        {
            // Arrange
            var table = new StateTransitionTable();
            var data = GroundedData(requestJump: false);
            data.Velocity = new Vector3(0f, -1f, 0f);

            // Act
            var target = table.Evaluate(BaseState.JumpAir, data);

            // Assert
            Assert.AreEqual(BaseState.JumpEnd, target);
        }
    }
}
