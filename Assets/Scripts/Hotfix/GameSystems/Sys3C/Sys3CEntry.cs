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
        private CharacterController _characterController;
        private CharacterAnimationController _animationController;
        private ThirdPersonCameraController _cameraController;
        private NetworkBridge _networkBridge;
        private NetworkPrediction _networkPrediction;
        private PositionInterpolator _positionInterpolator;

        // 组件引用
        private UnityEngine.CharacterController _unityCharacterController;
        private Rigidbody _rigidbody;
        private Animator _animator;
        private Camera _mainCamera;

        private void Awake()
        {
            // 获取组件引用
            _unityCharacterController = GetComponent<UnityEngine.CharacterController>();
            _rigidbody = GetComponent<Rigidbody>();
            _animator = GetComponent<Animator>();

            if (_mainCamera == null)
                _mainCamera = Camera.main;
        }

        private void Start()
        {
            // 初始化输入管理器
            _inputManager = new InputManager();
            _inputManager.MoveSpeed = _moveSpeed;
            _inputManager.CameraSensitivityX = _mouseSensitivityX;
            _inputManager.CameraSensitivityY = _mouseSensitivityY;

            // 初始化角色控制器
            _characterController = new CharacterController(
                transform,
                _unityCharacterController,
                _rigidbody,
                _groundLayer
            );
            _characterController.MoveSpeed = _moveSpeed;
            _characterController.RotationSpeed = _rotationSpeed;

            // 初始化动画控制器
            if (_animator != null)
                _animationController = new CharacterAnimationController(_animator);

            // 初始化相机控制器
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

            // 初始化网络模块
            _networkBridge = new NetworkBridge();
            _networkPrediction = new NetworkPrediction();
            _positionInterpolator = new PositionInterpolator();

            // 注册网络回调
            _networkBridge.RegisterPositionSyncCallback(OnPositionSyncResponse);
        }

        private void Update()
        {
            // 输入更新
            _inputManager.Update();

            // 相机旋转输入
            Vector2 cameraInput = _inputManager.GetCameraRotationInput();
            _cameraController?.HandleRotationInput(cameraInput);

            // 获取移动命令
            Vector3 forward = transform.forward;
            MoveCommand command = _inputManager.GetMoveCommand(forward);

            // 记录预测帧
            uint seq = _networkPrediction.GetNextSequence();
            _networkPrediction.RecordPredictedFrame(
                seq,
                _characterController.GetPredictedPosition(),
                _characterController.GetPredictedRotation()
            );

            // 角色更新
            _characterController.Update(command);

            // 动画更新
            _animationController?.Update(_characterController.Data);

            // 相机更新（相机在 LateUpdate 更新）
            _cameraController?.Update();
        }

        private void LateUpdate()
        {
            // 额外的相机插值可以在 LateUpdate 做
        }

        private void FixedUpdate()
        {
            // 固定帧网络同步（10Hz）
            if (_networkBridge.IsConnected)
            {
                _networkBridge.SendPositionSync(
                    _characterController.GetPredictedPosition(),
                    _characterController.GetPredictedRotation(),
                    _characterController.Data.Velocity.magnitude
                );
            }
        }

        private void OnPositionSyncResponse(PositionSyncResponse response)
        {
            // 处理服务端校验结果
            // 实际项目中需要从服务端获取权威位置，这里简化处理
        }

        /// <summary>
        /// 绑定网络客户端（外部调用）
        /// </summary>
        public void BindNetworkClient(KcpNet.KcpClient kcpClient)
        {
            _networkBridge.Initialize(kcpClient);
        }
    }
}
