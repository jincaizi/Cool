using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 地面检测器 — 分层射线实现（CharacterController 风格）
    /// </summary>
    public class GroundDetector
    {
        private readonly Transform _transform;
        private readonly UnityEngine.CharacterController _controller;
        private readonly LayerMask _groundLayer;

        // 射线检测参数
        private const int RAY_COUNT = 5;
        private const float RAY_SPREAD = 0.2f;
        private const float MAX_GROUND_ANGLE = 45f;
        private const float GROUND_CHECK_DISTANCE = 0.15f;

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
            float capsuleHeight = _controller.height;
            float capsuleRadius = _controller.radius;
            float capsuleCenterY = _controller.center.y;

            Vector3 origin = _transform.position + Vector3.up * (capsuleRadius + GROUND_CHECK_DISTANCE);

            // 中心射线
            if (CheckRay(origin, Vector3.down, capsuleHeight * 0.5f + GROUND_CHECK_DISTANCE))
                return true;

            // 4 方向脚底射线
            Vector3[] directions = new Vector3[]
            {
                Vector3.forward,
                Vector3.back,
                Vector3.left,
                Vector3.right
            };

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3 offset = directions[i] * RAY_SPREAD;
                Vector3 rayOrigin = origin + offset;

                if (CheckRay(rayOrigin, Vector3.down, capsuleHeight * 0.5f + GROUND_CHECK_DISTANCE))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 单根射线检测
        /// </summary>
        private bool CheckRay(Vector3 origin, Vector3 direction, float distance)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, distance, _groundLayer))
            {
                // 检查地面角度
                float angle = Vector3.Angle(Vector3.up, hit.normal);
                return angle <= MAX_GROUND_ANGLE;
            }
            return false;
        }

        /// <summary>
        /// 获取地面法线（用于坡道减速）
        /// </summary>
        public Vector3 GetGroundNormal()
        {
            Vector3 origin = _transform.position + Vector3.up * (_controller.radius + GROUND_CHECK_DISTANCE);

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, _controller.height * 0.5f + GROUND_CHECK_DISTANCE, _groundLayer))
            {
                return hit.normal;
            }

            return Vector3.up;
        }
    }
}