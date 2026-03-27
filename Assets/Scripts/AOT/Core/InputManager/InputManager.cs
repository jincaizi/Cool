using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AOT.Core.InputManager
{
    /// <summary>
    /// 输入管理器，封装新输入系统，提供统一输入事件
    /// </summary>
    public sealed class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        private PlayerInput? _playerInput;
        private InputAction? _moveAction;
        private InputAction? _jumpAction;
        private InputAction? _attackAction;
        private InputAction? _interactAction;
        private InputAction? _sprintAction;

        public event Action<Vector2>? OnMove;
        public event Action? OnJump;
        public event Action? OnJumpCancelled;
        public event Action? OnAttack;
        public event Action? OnAttackCancelled;
        public event Action? OnInteract;
        public event Action? OnSprint;
        public event Action? OnSprintCancelled;

        public Vector2 MoveInput { get; private set; }
        public bool JumpInput { get; private set; }
        public bool AttackInput { get; private set; }
        public bool InteractInput { get; private set; }
        public bool SprintInput { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            EnableInput();
        }

        private void OnDisable()
        {
            DisableInput();
        }

        /// <summary>
        /// 初始化输入系统
        /// </summary>
        /// <param name="inputAsset">输入资产</param>
        public void Initialize(InputActionAsset? inputAsset)
        {
            if (inputAsset == null)
            {
                Debug.LogWarning("[InputManager] InputActionAsset is null, creating default actions");
                CreateDefaultActions();
                return;
            }

            _moveAction = inputAsset.FindAction("Move");
            _jumpAction = inputAsset.FindAction("Jump");
            _attackAction = inputAsset.FindAction("Attack");
            _interactAction = inputAsset.FindAction("Interact");
            _sprintAction = inputAsset.FindAction("Sprint");

            BindActions();
        }

        /// <summary>
        /// 创建默认输入动作
        /// </summary>
        private void CreateDefaultActions()
        {
            var map = new InputActionMap("Gameplay");

            _moveAction = map.AddAction("Move", InputActionType.Value, "<Keyboard>/w");
            _moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _jumpAction = map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            _attackAction = map.AddAction("Attack", InputActionType.Button, "<Mouse>/leftButton");
            _interactAction = map.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            _sprintAction = map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");

            BindActions();
        }

        /// <summary>
        /// 绑定输入动作回调
        /// </summary>
        private void BindActions()
        {
            if (_moveAction != null)
            {
                _moveAction.performed += OnMovePerformed;
                _moveAction.canceled += OnMoveCanceled;
            }

            if (_jumpAction != null)
            {
                _jumpAction.performed += OnJumpPerformed;
                _jumpAction.canceled += OnJumpCanceled;
            }

            if (_attackAction != null)
            {
                _attackAction.performed += OnAttackPerformed;
                _attackAction.canceled += OnAttackCanceled;
            }

            if (_interactAction != null)
            {
                _interactAction.performed += OnInteractPerformed;
            }

            if (_sprintAction != null)
            {
                _sprintAction.performed += OnSprintPerformed;
                _sprintAction.canceled += OnSprintCanceled;
            }
        }

        /// <summary>
        /// 启用输入
        /// </summary>
        public void EnableInput()
        {
            _moveAction?.Enable();
            _jumpAction?.Enable();
            _attackAction?.Enable();
            _interactAction?.Enable();
            _sprintAction?.Enable();
        }

        /// <summary>
        /// 禁用输入
        /// </summary>
        public void DisableInput()
        {
            _moveAction?.Disable();
            _jumpAction?.Disable();
            _attackAction?.Disable();
            _interactAction?.Disable();
            _sprintAction?.Disable();
        }

        private void OnMovePerformed(InputAction.CallbackContext ctx)
        {
            MoveInput = ctx.ReadValue<Vector2>();
            OnMove?.Invoke(MoveInput);
        }

        private void OnMoveCanceled(InputAction.CallbackContext ctx)
        {
            MoveInput = Vector2.zero;
            OnMove?.Invoke(Vector2.zero);
        }

        private void OnJumpPerformed(InputAction.CallbackContext ctx)
        {
            JumpInput = true;
            OnJump?.Invoke();
        }

        private void OnJumpCanceled(InputAction.CallbackContext ctx)
        {
            JumpInput = false;
            OnJumpCancelled?.Invoke();
        }

        private void OnAttackPerformed(InputAction.CallbackContext ctx)
        {
            AttackInput = true;
            OnAttack?.Invoke();
        }

        private void OnAttackCanceled(InputAction.CallbackContext ctx)
        {
            AttackInput = false;
            OnAttackCancelled?.Invoke();
        }

        private void OnInteractPerformed(InputAction.CallbackContext ctx)
        {
            InteractInput = true;
            OnInteract?.Invoke();
            InteractInput = false;
        }

        private void OnSprintPerformed(InputAction.CallbackContext ctx)
        {
            SprintInput = true;
            OnSprint?.Invoke();
        }

        private void OnSprintCanceled(InputAction.CallbackContext ctx)
        {
            SprintInput = false;
            OnSprintCancelled?.Invoke();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
