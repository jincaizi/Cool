using System;
using AOT.Core.GameEventDispatcher;
using AOT.Bridge;
using AOT.DataDefinition.Constants;
using AOT.DataDefinition.Interfaces;
using UnityEngine;

namespace Hotfix.GameSystems
{
    /// <summary>
    /// 输入处理器，实现IInputHandler接口
    /// 监听新输入系统，转换为游戏逻辑事件
    /// </summary>
    public class InputHandler : IInputHandler
    {
        private Vector2 _moveInput;
        private bool _jumpPressed;
        private bool _attackPressed;
        private bool _interactPressed;
        private bool _sprintPressed;
        private bool _initialized;

        public Vector2 MoveInput => _moveInput;
        public bool IsJumpPressed => _jumpPressed;
        public bool IsAttackPressed => _attackPressed;
        public bool IsInteractPressed => _interactPressed;
        public bool IsSprintPressed => _sprintPressed;

        /// <summary>
        /// 初始化输入处理器
        /// </summary>
        public void Initialize()
        {
            Bridge_Input.SetHandler(this);
            RegisterInputCallbacks();
            _initialized = true;
        }

        /// <summary>
        /// 注册输入回调
        /// </summary>
        private void RegisterInputCallbacks()
        {
            GameEventDispatcher.Instance.Register(EventKeys.Move, OnMove);
            GameEventDispatcher.Instance.Register(EventKeys.Jump, OnJump);
            GameEventDispatcher.Instance.Register(EventKeys.JumpCancelled, OnJumpCancelled);
            GameEventDispatcher.Instance.Register(EventKeys.Attack, OnAttack);
            GameEventDispatcher.Instance.Register(EventKeys.AttackCancelled, OnAttackCancelled);
            GameEventDispatcher.Instance.Register(EventKeys.Interact, OnInteract);
            GameEventDispatcher.Instance.Register(EventKeys.Sprint, OnSprint);
            GameEventDispatcher.Instance.Register(EventKeys.SprintCancelled, OnSprintCancelled);
        }

        /// <summary>
        /// 更新输入
        /// </summary>
        public void UpdateInput()
        {
            if (!_initialized) return;

            _moveInput = Bridge_Input.GetMoveInput();
            _jumpPressed = Bridge_Input.IsJumpPressed();
            _attackPressed = Bridge_Input.IsAttackPressed();
            _interactPressed = Bridge_Input.IsInteractPressed();
            _sprintPressed = Bridge_Input.IsSprintPressed();
        }

        private void OnMove(object data)
        {
            if (data is Vector2 input)
            {
                _moveInput = input;
            }
        }

        private void OnJump(object _)
        {
            _jumpPressed = true;
            Bridge_Player.DispatchJumped();
        }

        private void OnJumpCancelled(object _)
        {
            _jumpPressed = false;
        }

        private void OnAttack(object _)
        {
            _attackPressed = true;
            Bridge_Player.DispatchAttacked();
        }

        private void OnAttackCancelled(object _)
        {
            _attackPressed = false;
        }

        private void OnInteract(object _)
        {
            _interactPressed = true;
            Bridge_Player.DispatchAttacked();
            _interactPressed = false;
        }

        private void OnSprint(object _)
        {
            _sprintPressed = true;
        }

        private void OnSprintCancelled(object _)
        {
            _sprintPressed = false;
        }

        /// <summary>
        /// 重置输入状态
        /// </summary>
        public void ResetInput()
        {
            _moveInput = Vector2.zero;
            _jumpPressed = false;
            _attackPressed = false;
            _interactPressed = false;
            _sprintPressed = false;
        }

        /// <summary>
        /// 销毁
        /// </summary>
        public void Destroy()
        {
            _initialized = false;

            GameEventDispatcher.Instance.Unregister(EventKeys.Move, OnMove);
            GameEventDispatcher.Instance.Unregister(EventKeys.Jump, OnJump);
            GameEventDispatcher.Instance.Unregister(EventKeys.JumpCancelled, OnJumpCancelled);
            GameEventDispatcher.Instance.Unregister(EventKeys.Attack, OnAttack);
            GameEventDispatcher.Instance.Unregister(EventKeys.AttackCancelled, OnAttackCancelled);
            GameEventDispatcher.Instance.Unregister(EventKeys.Interact, OnInteract);
            GameEventDispatcher.Instance.Unregister(EventKeys.Sprint, OnSprint);
            GameEventDispatcher.Instance.Unregister(EventKeys.SprintCancelled, OnSprintCancelled);

            Bridge_Input.SetHandler(null!);
        }
    }
}
