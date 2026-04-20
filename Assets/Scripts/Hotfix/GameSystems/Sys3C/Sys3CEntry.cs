using UnityEngine;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Network;

namespace Hotfix.GameSystems.Sys3C
{
    /// <summary>
    /// 3C 系统入口 — 绑定所有组件，在场景中挂载到角色实体
    /// </summary>
    public class Sys3CEntry : MonoBehaviour
    {
        [Header("Physics")]
        [SerializeField] private LayerMask _groundLayer = ~0;

        [Header("Movement")]
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _sprintSpeed = 9f;
        [SerializeField] private float _rotationSpeed = 10f;

        [Header("Camera")]
        [SerializeField] private float _cameraDistance = 5f;
        [SerializeField] private float _cameraHeight = 2f;
        [SerializeField] private float _cameraDamping = 5f;

        [Header("Camera Sensitivity")]
        [SerializeField] private float _mouseSensitivityX = 2f;
        [SerializeField] private float _mouseSensitivityY = 2f;

        // 各模块实例
        private InputManager _inputManager;
        private Hotfix.GameSystems.Sys3C.Character.CharacterController _characterController;
        private CharacterAnimationDriver _animationDriver;
        private ThirdPersonCameraController _cameraController;
        private NetworkBridge _networkBridge;
        private NetworkPrediction _networkPrediction;
        private PositionInterpolator _positionInterpolator;

        // 组件引用
        private UnityEngine.CharacterController _unityCharacterController;
        private Rigidbody _rigidbody;
        private Animator _animator;
        private UnityEngine.Camera _mainCamera;

        private void Awake()
        {
            _unityCharacterController = GetComponent<UnityEngine.CharacterController>();
            _rigidbody = GetComponent<Rigidbody>();
            _animator = GetComponent<Animator>();
            _mainCamera ??= UnityEngine.Camera.main;
        }

        private void Start()
        {
            // 输入管理器
            _inputManager = new InputManager();
            _inputManager.MoveSpeed = _moveSpeed;
            _inputManager.SprintSpeed = _sprintSpeed;
            _inputManager.CameraSensitivityX = _mouseSensitivityX;
            _inputManager.CameraSensitivityY = _mouseSensitivityY;

            // 角色控制器
            _characterController = new Hotfix.GameSystems.Sys3C.Character.CharacterController(
                transform,
                _unityCharacterController,
                _groundLayer
            );
            _characterController.MoveSpeed = _moveSpeed;
            _characterController.RotationSpeed = _rotationSpeed;

            // 动画驱动器
            if (_animator != null)
                _animationDriver = new CharacterAnimationDriver(_animator);

            // 相机控制器
            if (_mainCamera != null)
            {
                _cameraController = new ThirdPersonCameraController(
                    _mainCamera.transform,
                    transform
                );
                _cameraController.Distance = _cameraDistance;
                _cameraController.Height = _cameraHeight;
                _cameraController.PositionDamping = _cameraDamping;
                _cameraController.MouseSensitivityX = _mouseSensitivityX;
                _cameraController.MouseSensitivityY = _mouseSensitivityY;
            }

            // 网络模块
            _networkBridge = new NetworkBridge();
            _networkPrediction = new NetworkPrediction();
            _positionInterpolator = new PositionInterpolator();
            _networkBridge.RegisterPositionSyncCallback(OnPositionSyncResponse);
        }

        private void Update()
        {
            _inputManager.Update();

            // 相机旋转
            Vector2 cameraInput = _inputManager.GetCameraRotationInput();
            _cameraController?.HandleRotationInput(cameraInput);

            // 相机朝向（用于计算移动方向）
            Vector3 cameraForward = _mainCamera != null
                ? Vector3.ProjectOnPlane(_mainCamera.transform.forward, Vector3.up).normalized
                : Vector3.forward;

            // 移动命令
            MoveCommand command = _inputManager.GetMoveCommand(transform.forward, cameraForward);

            // === 输入事件处理 ===
            // 跳跃
            if (_inputManager.IsJumpPressed())
            {
                _characterController.RequestJump();
                _animationDriver?.OnJumpStart();
            }

            // 攻击
            if (_inputManager.IsAttackPressed())
            {
                _animationDriver?.OnAttack(1);
            }

            // 移动状态（同步到动画）
            _animationDriver?.SetMoving(_inputManager.IsMoving());

            // === 物理更新 ===
            _characterController.Update(command);

            // === 动画更新 ===
            _animationDriver?.Update(_characterController.Data);

            // 相机更新
            _cameraController?.Update();
        }

        private void FixedUpdate()
        {
            if (_networkBridge.IsConnected)
            {
                _networkBridge.SendPositionSync(
                    _characterController.GetPredictedPosition(),
                    _characterController.GetPredictedRotation(),
                    _characterController.Data.Velocity.magnitude
                );
            }
        }

        private void OnPositionSyncResponse(PositionSyncResponseData response)
        {
            // 服务端校验结果处理（后续实现）
        }

        /// <summary>
        /// 绑定网络客户端（外部调用）
        /// </summary>
        public void BindNetworkClient(INetworkClient networkClient)
        {
            _networkBridge.Initialize(networkClient);
        }
    }
}
