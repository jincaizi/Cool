using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 地面检测器 — 使用 SphereCast 实现可靠地面检测
    /// </summary>
    public class GroundDetector
    {
        private readonly Transform _transform;
        private readonly UnityEngine.CharacterController _controller;
        private readonly LayerMask _groundLayer;

        // 检测参数
        private const float MAX_GROUND_ANGLE = 45f;      // 最大地面角度

        public GroundDetector(Transform transform, UnityEngine.CharacterController controller, LayerMask groundLayer)
        {
            _transform = transform;
            _controller = controller;
            _groundLayer = groundLayer;
        }

        /// <summary>
        /// 检测是否在地面上
        /// </summary>
        public bool IsGrounded()
        {
            float capsuleRadius = _controller.radius * 0.9f;  // 90% 半径
            float capsuleCenterY = _controller.center.y;

            // 胶囊底部的世界坐标
            float capsuleBottomWorldY = _transform.position.y + capsuleCenterY - _controller.height * 0.5f;

            // 从胶囊底部稍微往下的位置发射球体
            Vector3 sphereOrigin = new Vector3(_transform.position.x, capsuleBottomWorldY + capsuleRadius, _transform.position.z);
            float checkDistance = capsuleRadius + 0.5f;

            // 主要检测：SphereCast
            if (Physics.SphereCast(sphereOrigin, capsuleRadius, Vector3.down, out RaycastHit hit, checkDistance, _groundLayer))
            {
                float angle = Vector3.Angle(Vector3.up, hit.normal);
                if (angle <= MAX_GROUND_ANGLE)
                {
                    return true;
                }
            }

            // 备选：CheckSphere 在预期地面位置检测
            Vector3 checkPos = new Vector3(_transform.position.x, capsuleBottomWorldY + 0.05f, _transform.position.z);
            if (Physics.CheckSphere(checkPos, capsuleRadius * 0.5f, _groundLayer))
            {
                return true;
            }

            return false;
        }
    }
}
