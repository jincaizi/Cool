using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Camera
{
    /// <summary>
    /// 第三人称相机控制器 — 平滑跟随
    /// </summary>
    public class ThirdPersonCameraController : MonoBehaviour
    {
        [Header("Target")]
        public Transform Target;

        [Header("Distance & Height")]
        public float Distance = 8.0f;
        public float Height = 2.0f;

        [Header("Damping")]
        public float PositionDamping = 5.0f;
        public float RotationDamping = 8.0f;

        [Header("Rotation Limits")]
        public float MinPitch = -30f;
        public float MaxPitch = 60f;
        public float MouseSensitivityX = 2.0f;
        public float MouseSensitivityY = 2.0f;

        // 当前旋转角度
        private float _horizontalAngle;
        private float _verticalAngle = 20f;

        private void Start()
        {
            if (Target != null)
            {
                _horizontalAngle = Target.eulerAngles.y;
            }
        }

        /// <summary>
        /// 处理相机旋转输入
        /// </summary>
        public void HandleRotationInput(Vector2 input)
        {
            if (input.sqrMagnitude < 0.001f) return;

            _horizontalAngle += input.x * MouseSensitivityX;
            _verticalAngle -= input.y * MouseSensitivityY;

            // 限制俯仰角
            _verticalAngle = Mathf.Clamp(_verticalAngle, MinPitch, MaxPitch);
        }

        /// <summary>
        /// 每帧更新相机位置和旋转
        /// </summary>
        public void Update()
        {
            if (Target == null) return;

            // 计算目标位置（球坐标）
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -Distance);
            offset.y = Height;

            Vector3 targetPosition = Target.position + offset;
            Vector3 currentPosition = transform.position;

            // 平滑跟随
            transform.position = Vector3.Lerp(currentPosition, targetPosition, PositionDamping * Time.deltaTime);

            // 平滑看向目标
            Vector3 lookTarget = Target.position + Vector3.up * Height * 0.5f;
            Quaternion targetRotation = Quaternion.LookRotation(lookTarget - transform.position);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                RotationDamping * Time.deltaTime
            );
        }

        /// <summary>
        /// 获取当前相机旋转
        /// </summary>
        public Quaternion GetRotation()
        {
            return Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
        }
    }
}
