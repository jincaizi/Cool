
using AOT.Core.GameEventDispatcher;
using AOT.Core.InputManager;
using AOT.DataDefinition.Constants;
using AOT.DataDefinition.Interfaces;
using UnityEngine;

namespace AOT.Bridge
{
    /// <summary>
    /// 输入桥接类，供热更新层调用AOT层功能
    /// </summary>
    public static class Bridge_Input
    {
        private static IInputHandler? _inputHandler;
        private static InputManager? _inputManager;

        /// <summary>
        /// 设置输入管理器
        /// </summary>
        public static void SetInputManager(InputManager manager)
        {
            _inputManager = manager;
        }

        /// <summary>
        /// 设置输入处理器实例
        /// </summary>
        public static void SetHandler(IInputHandler handler)
        {
            _inputHandler = handler;
        }

        /// <summary>
        /// 获取输入处理器
        /// </summary>
        public static IInputHandler? GetHandler()
        {
            return _inputHandler;
        }

        /// <summary>
        /// 获取移动输入
        /// </summary>
        public static Vector2 GetMoveInput()
        {
            if (_inputHandler != null)
            {
                return _inputHandler.MoveInput;
            }
            if (_inputManager != null)
            {
                return _inputManager.MoveInput;
            }
            return Vector2.zero;
        }

        /// <summary>
        /// 是否按下跳跃
        /// </summary>
        public static bool IsJumpPressed()
        {
            if (_inputHandler != null)
            {
                return _inputHandler.IsJumpPressed;
            }
            if (_inputManager != null)
            {
                return _inputManager.JumpInput;
            }
            return false;
        }

        /// <summary>
        /// 是否按下攻击
        /// </summary>
        public static bool IsAttackPressed()
        {
            if (_inputHandler != null)
            {
                return _inputHandler.IsAttackPressed;
            }
            if (_inputManager != null)
            {
                return _inputManager.AttackInput;
            }
            return false;
        }

        /// <summary>
        /// 是否按下交互
        /// </summary>
        public static bool IsInteractPressed()
        {
            if (_inputHandler != null)
            {
                return _inputHandler.IsInteractPressed;
            }
            if (_inputManager != null)
            {
                return _inputManager.InteractInput;
            }
            return false;
        }

        /// <summary>
        /// 是否按下冲刺
        /// </summary>
        public static bool IsSprintPressed()
        {
            if (_inputHandler != null)
            {
                return _inputHandler.IsSprintPressed;
            }
            if (_inputManager != null)
            {
                return _inputManager.SprintInput;
            }
            return false;
        }

        /// <summary>
        /// 派发移动事件
        /// </summary>
        public static void DispatchMove(Vector2 input)
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.Move, input);
        }

        /// <summary>
        /// 派发跳跃事件
        /// </summary>
        public static void DispatchJump()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.Jump, null);
        }

        /// <summary>
        /// 派发跳跃取消事件
        /// </summary>
        public static void DispatchJumpCancelled()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.JumpCancelled, null);
        }

        /// <summary>
        /// 派发攻击事件
        /// </summary>
        public static void DispatchAttack()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.Attack, null);
        }

        /// <summary>
        /// 派发攻击取消事件
        /// </summary>
        public static void DispatchAttackCancelled()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.AttackCancelled, null);
        }

        /// <summary>
        /// 派发交互事件
        /// </summary>
        public static void DispatchInteract()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.Interact, null);
        }

        /// <summary>
        /// 派发冲刺事件
        /// </summary>
        public static void DispatchSprint()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.Sprint, null);
        }

        /// <summary>
        /// 派发冲刺取消事件
        /// </summary>
        public static void DispatchSprintCancelled()
        {
            GameEventDispatcher.Instance.Dispatch(EventKeys.SprintCancelled, null);
        }
    }
}
