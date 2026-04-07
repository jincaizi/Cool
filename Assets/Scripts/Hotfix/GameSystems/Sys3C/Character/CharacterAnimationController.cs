using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色动画控制器 — Animator 参数驱动
    /// </summary>
    public class CharacterAnimationController
    {
        private readonly Animator _animator;

        //Animator 参数名常量
        private static readonly int PARAM_SPEED = Animator.StringToHash("Speed");
        private static readonly int PARAM_MOVE_SPEED = Animator.StringToHash("MoveSpeed");
        private static readonly int PARAM_GROUNDED = Animator.StringToHash("Grounded");
        private static readonly int PARAM_VERTICAL_VELOCITY = Animator.StringToHash("VerticalVelocity");

        public CharacterAnimationController(Animator animator)
        {
            _animator = animator;
        }

        /// <summary>
        /// 每帧更新动画参数
        /// </summary>
        public void Update(CharacterData data)
        {
            if (_animator == null) return;

            // 速度（用于混合树）
            _animator.SetFloat(PARAM_SPEED, data.Velocity.magnitude, 0.1f, Time.deltaTime);
            _animator.SetFloat(PARAM_MOVE_SPEED, data.Velocity.magnitude, 0.1f, Time.deltaTime);

            // 地面状态
            _animator.SetBool(PARAM_GROUNDED, data.IsGrounded);

            // 垂直速度（用于落地/跳跃动画）
            _animator.SetFloat(PARAM_VERTICAL_VELOCITY, data.VerticalVelocity, 0.1f, Time.deltaTime);
        }

        /// <summary>
        /// 触发动画事件（可选）
        /// </summary>
        public void SetTrigger(string triggerName)
        {
            if (_animator != null)
            {
                _animator.SetTrigger(Animator.StringToHash(triggerName));
            }
        }
    }
}