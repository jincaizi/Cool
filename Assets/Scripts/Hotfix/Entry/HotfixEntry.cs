using System;
using AOT.Bridge;
using Hotfix.GameSystems;
using UnityEngine;

namespace Hotfix.Entry
{
    /// <summary>
    /// 热更新入口
    /// 初始化3C系统（创建Player、Camera、Input实例）
    /// </summary>
    public class HotfixEntry : MonoBehaviour
    {
        [Header("Player Settings")]
        [SerializeField] private string _playerModelAddress = "Player/Character";
        [SerializeField] private float _playerMoveSpeed = 6f;
        [SerializeField] private float _playerJumpForce = 5f;

        [Header("Camera Settings")]
        [SerializeField] private float _cameraDistance = 5f;
        [SerializeField] private float _cameraSmoothTime = 0.1f;
        [SerializeField] private float _cameraRotationSpeed = 3f;

        private PlayerController? _playerController;
        private ThirdPersonCameraController? _cameraController;
        private InputHandler? _inputHandler;

        private bool _initialized;

        /// <summary>
        /// 是否已初始化
        /// </summary>
        public bool IsInitialized => _initialized;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            Initialize();
        }

        /// <summary>
        /// 初始化3C系统
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                InitializeInput();
                InitializeCamera();
                InitializePlayer();
                RegisterBridges();

                _initialized = true;
                Debug.Log("[HotfixEntry] 3C System initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HotfixEntry] Failed to initialize 3C system: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// 初始化输入系统
        /// </summary>
        private void InitializeInput()
        {
            var inputManager = FindObjectOfType<AOT.Core.InputManager.InputManager>();
            if (inputManager == null)
            {
                var go = new GameObject("InputManager");
                inputManager = go.AddComponent<AOT.Core.InputManager.InputManager>();
            }

            Bridge_Input.SetInputManager(inputManager);

            _inputHandler = new InputHandler();
            _inputHandler.Initialize();

            Debug.Log("[HotfixEntry] Input system initialized");
        }

        /// <summary>
        /// 初始化相机系统
        /// </summary>
        private void InitializeCamera()
        {
            _cameraController = new ThirdPersonCameraController();
            _cameraController.Initialize();
            _cameraController.Distance = _cameraDistance;
            _cameraController.SmoothTime = _cameraSmoothTime;
            _cameraController.SetRotationSpeed(_cameraRotationSpeed);

            Debug.Log("[HotfixEntry] Camera system initialized");
        }

        /// <summary>
        /// 初始化玩家系统
        /// </summary>
        private void InitializePlayer()
        {
            _playerController = new PlayerController();
            _playerController.Initialize();
            _playerController.MoveSpeed = _playerMoveSpeed;

            LoadPlayerModel();

            Debug.Log("[HotfixEntry] Player system initialized");
        }

        /// <summary>
        /// 加载玩家模型
        /// </summary>
        private async void LoadPlayerModel()
        {
            try
            {
                var loader = AOT.Core.ResourceLoader.ResourceLoader.Instance;
                var playerPrefab = await loader.LoadGameObjectAsync(_playerModelAddress);

                if (playerPrefab != null)
                {
                    var playerObj = Instantiate(playerPrefab, Vector3.zero, Quaternion.identity);
                    _playerController?.SetCharacter(playerObj);

                    if (_cameraController != null)
                    {
                        _cameraController.SetTarget(playerObj.transform);
                    }

                    Debug.Log($"[HotfixEntry] Player model loaded: {_playerModelAddress}");
                }
                else
                {
                    CreateDefaultPlayer();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HotfixEntry] Failed to load player model: {ex.Message}, creating default");
                CreateDefaultPlayer();
            }
        }

        /// <summary>
        /// 创建默认玩家
        /// </summary>
        private void CreateDefaultPlayer()
        {
            var defaultPlayer = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            defaultPlayer.name = "DefaultPlayer";
            defaultPlayer.transform.position = Vector3.zero;
            defaultPlayer.transform.localScale = new Vector3(1, 1, 1);

            _playerController?.SetCharacter(defaultPlayer);

            if (_cameraController != null)
            {
                _cameraController.SetTarget(defaultPlayer.transform);
            }

            Debug.Log("[HotfixEntry] Default player created");
        }

        /// <summary>
        /// 注册桥接回调
        /// </summary>
        private void RegisterBridges()
        {
            Bridge_Player.SetController(_playerController!);
            Bridge_Camera.SetHandler(_cameraController!);
            Bridge_Input.SetHandler(_inputHandler!);

            Debug.Log("[HotfixEntry] Bridges registered");
        }

        private void Update()
        {
            if (!_initialized) return;

            _inputHandler?.UpdateInput();
            _playerController?.Update();
            _cameraController?.UpdateCamera();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        private void Cleanup()
        {
            _inputHandler?.Destroy();
            _cameraController?.Destroy();
            _playerController?.Destroy();

            _initialized = false;
            Debug.Log("[HotfixEntry] 3C System cleaned up");
        }

        /// <summary>
        /// 获取玩家控制器
        /// </summary>
        public PlayerController? GetPlayerController()
        {
            return _playerController;
        }

        /// <summary>
        /// 获取相机控制器
        /// </summary>
        public ThirdPersonCameraController? GetCameraController()
        {
            return _cameraController;
        }

        /// <summary>
        /// 获取输入处理器
        /// </summary>
        public InputHandler? GetInputHandler()
        {
            return _inputHandler;
        }
    }
}
