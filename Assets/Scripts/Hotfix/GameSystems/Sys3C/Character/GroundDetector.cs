using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 地面检测器 — 使用 CharacterController.isGrounded
    /// </summary>
    public class GroundDetector
    {
        private readonly CharacterController _controller;

        public GroundDetector(CharacterController controller)
        {
            _controller = controller;
        }

        /// <summary>
        /// 检测是否在地面上
        /// </summary>
        public bool IsGrounded()
        {
            return _controller.isGrounded;
        }
    }
}
