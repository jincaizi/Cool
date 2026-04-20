using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Camera
{
    /// <summary>
    /// 第三人称相机控制器 — 平滑跟随
    /// </summary>
    public class ThirdPersonCameraController
    {
        private readonly Transform _cameraTransform;
        private readonly Transform _targetTransform;

        // 相机参数
        public float Distance { get; set; } = 8.0f;         // 相机距离
        public float Height { get; set; } = 2.0f;           // 相机高度
        public float PositionDamping { get; set; } = 5.0f;  // 位置平滑
        public float RotationDamping { get; set; } = 8.0f;   // 旋转平滑

        public float MinPitch { get; set; } = -30f;         // 最小俯仰角
        public float MaxPitch { get; set; } = 60f;          // 最大俯仰角
        public float MouseSensitivityX { get; set; } = 2.0f;
        public float MouseSensitivityY { get; set; } = 2.0f;

        // 当前旋转角度
        private float _horizontalAngle;
        private float _verticalAngle = 20f;

        public ThirdPersonCameraController(Transform cameraTransform, Transform targetTransform)
        {
            _cameraTransform = cameraTransform;
            _targetTransform = targetTransform;

            // 初始化相机角度
            if (_targetTransform != null)
            {
                _horizontalAngle = _targetTransform.eulerAngles.y;
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
            if (_targetTransform == null) return;

            // 计算目标位置（球坐标）
            Quaternion rotation = Quaternion.Euler(_verticalAngle, _horizontalAngle, 0f);
            Vector3 offset = rotation * new Vector3(0f, 0f, -Distance);
            offset.y = Height;

            Vector3 targetPosition = _targetTransform.position + offset;
            Vector3 currentPosition = _cameraTransform.position;

            // 平滑跟随
            _cameraTransform.position = Vector3.Lerp(currentPosition, targetPosition, PositionDamping * Time.deltaTime);

            // 平滑看向目标
            Vector3 lookTarget = _targetTransform.position + Vector3.up * Height * 0.5f;
            Quaternion targetRotation = Quaternion.LookRotation(lookTarget - _cameraTransform.position);
            _cameraTransform.rotation = Quaternion.Slerp(
                _cameraTransform.rotation,
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