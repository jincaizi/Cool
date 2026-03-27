using System;
using AOT.Bridge;
using AOT.DataDefinition.Interfaces;
using UnityEngine;

namespace Hotfix.GameSystems
{
    /// <summary>
    /// 第三人称相机控制器
    /// 支持旋转、缩放、碰撞检测
    /// </summary>
    public class ThirdPersonCameraController : ICameraHandler
    {
        private Camera? _camera;
        private Transform? _target;
        private Transform? _cameraTransform;

        private float _distance = 5f;
        private float _minDistance = 1f;
        private float _maxDistance = 10f;
        private float _horizontalAngle;
        private float _verticalAngle = 30f;
        private float _minVerticalAngle = -30f;
        private float _maxVerticalAngle = 60f;

        private float _smoothTime = 0.1f;
        private float _rotationSpeed = 3f;
        private float _zoomSpeed = 2f;

        private Vector3 _currentVelocity;
        private float _currentDistance;
        private float _targetDistance;

        private LayerMask _collisionMask = ~0;
        private float _collisionRadius = 0.2f;

        public Transform? CameraTransform => _cameraTransform;
        public Transform? Target
        {
            get => _target;
            set => _target = value;
        }
        public float Distance
        {
            get => _distance;
            set => _distance = Mathf.Clamp(value, _minDistance, _maxDistance);
        }
        public float HorizontalAngle
        {
            get => _horizontalAngle;
            set => _horizontalAngle = value;
        }
        public float VerticalAngle
        {
            get => _verticalAngle;
            set => _verticalAngle = Mathf.Clamp(value, _minVerticalAngle, _maxVerticalAngle);
        }
        public float SmoothTime
        {
            get => _smoothTime;
            set => _smoothTime = value;
        }

        /// <summary>
        /// 初始化相机控制器
        /// </summary>
        public void Initialize()
        {
            Bridge_Camera.SetHandler(this);
            CreateCamera();
            _currentDistance = _distance;
            _targetDistance = _distance;
        }

        /// <summary>
        /// 创建相机
        /// </summary>
        private void CreateCamera()
        {
            var cameraGo = new GameObject("ThirdPersonCamera");
            _camera = cameraGo.AddComponent<Camera>();
            _cameraTransform = cameraGo.transform;
            _camera.depth = 0;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 1000f;
        }

        /// <summary>
        /// 设置跟随目标
        /// </summary>
        public void SetTarget(Transform target)
        {
            _target = target;
        }

        /// <summary>
        /// 更新相机
        /// </summary>
        public void UpdateCamera()
        {
            if (_camera == null || _target == null || _cameraTransform == null) return;

            UpdateRotation();
            UpdateDistance();
            UpdatePosition();
        }

        /// <summary>
        /// 更新旋转
        /// </summary>
        private void UpdateRotation()
        {
            Vector2 input = Bridge_Input.GetMoveInput();

            if (Mathf.Abs(input.x) > 0.01f)
            {
                _horizontalAngle += input.x * _rotationSpeed;
            }

            float mouseY = UnityEngine.Input.GetAxis("Mouse Y");
            if (Mathf.Abs(mouseY) > 0.01f)
            {
                VerticalAngle -= mouseY * _rotationSpeed;
            }

            Bridge_Camera.DispatchRotated(_horizontalAngle, _verticalAngle);
        }

        /// <summary>
        /// 更新距离
        /// </summary>
        private void UpdateDistance()
        {
            float scroll = UnityEngine.Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _targetDistance -= scroll * _zoomSpeed;
                _targetDistance = Mathf.Clamp(_targetDistance, _minDistance, _maxDistance);
            }

            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, _smoothTime);

            Vector3 direction = _cameraTransform.forward * -1;
            Ray ray = new Ray(_target.position, direction);
            if (Physics.SphereCast(ray, _collisionRadius, out RaycastHit hit, _currentDistance, _collisionMask))
            {
                _currentDistance = Mathf.Lerp(_currentDistance, hit.distance * 0.9f, _smoothTime);
            }

            _distance = _currentDistance;
            Bridge_Camera.DispatchZoomed(_distance);
        }

        /// <summary>
        /// 更新位置
        /// </summary>
        private void UpdatePosition()
        {
            if (_cameraTransform == null || _target == null) return;

            float horizontalRad = _horizontalAngle * Mathf.Deg2Rad;
            float verticalRad = _verticalAngle * Mathf.Deg2Rad;

            float x = _distance * Mathf.Cos(verticalRad) * Mathf.Sin(horizontalRad);
            float y = _distance * Mathf.Sin(verticalRad);
            float z = _distance * Mathf.Cos(verticalRad) * Mathf.Cos(horizontalRad);

            Vector3 offset = new Vector3(x, y, z);
            Vector3 targetPosition = _target.position + offset;

            _cameraTransform.position = Vector3.SmoothDamp(_cameraTransform.position, targetPosition, ref _currentVelocity, _smoothTime);
            _cameraTransform.LookAt(_target.position + Vector3.up * 1.5f);
        }

        /// <summary>
        /// 设置碰撞层
        /// </summary>
        public void SetCollisionMask(LayerMask mask)
        {
            _collisionMask = mask;
        }

        /// <summary>
        /// 设置旋转速度
        /// </summary>
        public void SetRotationSpeed(float speed)
        {
            _rotationSpeed = speed;
        }

        /// <summary>
        /// 设置缩放速度
        /// </summary>
        public void SetZoomSpeed(float speed)
        {
            _zoomSpeed = speed;
        }

        /// <summary>
        /// 获取相机
        /// </summary>
        public Camera? GetCamera()
        {
            return _camera;
        }

        /// <summary>
        /// 销毁
        /// </summary>
        public void Destroy()
        {
            Bridge_Camera.SetHandler(null!);

            if (_camera != null)
            {
                UnityEngine.Object.Destroy(_camera.gameObject);
                _camera = null;
            }
            _cameraTransform = null;
        }
    }
}
