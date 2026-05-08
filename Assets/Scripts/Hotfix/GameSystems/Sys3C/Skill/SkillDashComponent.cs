using UnityEngine;
using Hotfix.GameSystems.Skills.Runtime;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// 技能突进组件 — 处理技能位移逻辑
    /// </summary>
    public class SkillDashComponent : IDashComponent
    {
        private readonly UnityEngine.CharacterController _controller;
        private readonly Transform _transform;

        // 突进状态
        private bool _isDashing;
        private float _dashTimer;
        private float _dashDuration;
        private Vector3 _dashDirection;
        private float _dashSpeed;

        // 阶段时间（秒）
        private const float STARTUP_TIME = 0.05f;
        private const float RECOVERY_TIME = 0.05f;

        // 碰撞检测参数
        private float _checkRadius = 0.3f;
        private int _obstacleLayerMask;

        public bool IsDashing => _isDashing;

        public SkillDashComponent(UnityEngine.CharacterController controller, Transform transform)
        {
            _controller = controller;
            _transform = transform;

            // 默认只检测静态障碍物（Wall, Floor, Obstacle）
            _obstacleLayerMask = LayerMask.GetMask("Default", "Wall", "Floor");
        }

        /// <summary>
        /// 开始突进
        /// </summary>
        /// <param name="direction">突进方向（单位向量）</param>
        /// <param name="distance">突进距离（米）</param>
        /// <param name="duration">突进持续时间（秒）</param>
        public void StartDash(Vector3 direction, float distance, float duration)
        {
            // 参数校验
            float effectiveDuration = duration - STARTUP_TIME - RECOVERY_TIME;
            if (effectiveDuration <= 0 || distance <= 0)
            {
                return;
            }

            if (direction.sqrMagnitude < 0.01f)
            {
                return;
            }

            _isDashing = true;
            _dashTimer = 0f;
            _dashDuration = duration;
            _dashDirection = direction.normalized;
            _dashSpeed = distance / effectiveDuration;
        }

        /// <summary>
        /// 立即停止突进
        /// </summary>
        public void StopDash()
        {
            if (_isDashing)
            {
                _isDashing = false;
                _dashTimer = 0f;
            }
        }

        /// <summary>
        /// 每帧更新突进逻辑
        /// </summary>
        /// <returns>本次更新移动的距离</returns>
        public float Update()
        {
            if (!_isDashing) return 0f;

            _dashTimer += Time.deltaTime;
            float movedDistance = 0f;

            // 起手阶段：不动
            if (_dashTimer < STARTUP_TIME)
            {
                return 0f;
            }

            // 突进阶段：移动
            float dashTime = _dashTimer - STARTUP_TIME;
            float maxDashTime = _dashDuration - STARTUP_TIME - RECOVERY_TIME;

            if (dashTime < maxDashTime)
            {
                // 计算本帧移动量
                float frameMove = _dashSpeed * Time.deltaTime;

                // 碰撞检测
                Vector3 targetPos = _transform.position + _dashDirection * frameMove;
                if (!CheckCollision(targetPos))
                {
                    _controller.Move(_dashDirection * frameMove);
                    movedDistance = frameMove;
                }
                else
                {
                    // 碰到障碍物，停止突进
                    StopDash();
                    return movedDistance;
                }
            }

            // 收尾阶段：不动
            if (_dashTimer >= _dashDuration)
            {
                StopDash();
            }

            return movedDistance;
        }

        /// <summary>
        /// 检测目标位置是否会发生碰撞
        /// </summary>
        private bool CheckCollision(Vector3 targetPosition)
        {
            Vector3 checkOrigin = _transform.position + Vector3.up * 0.5f;
            Vector3 checkDirection = (targetPosition - _transform.position).normalized;
            float checkDistance = Vector3.Distance(_transform.position, targetPosition) + _checkRadius;

            if (checkDistance < 0.01f) return false;

            return Physics.SphereCast(checkOrigin, _checkRadius, checkDirection, out RaycastHit hit, checkDistance, _obstacleLayerMask);
        }
    }
}
