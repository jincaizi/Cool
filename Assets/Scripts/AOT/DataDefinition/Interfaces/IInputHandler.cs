using UnityEngine;

namespace AOT.DataDefinition.Interfaces
{
    /// <summary>
    /// 输入处理器接口
    /// </summary>
    public interface IInputHandler
    {
        /// <summary>
        /// 移动输入
        /// </summary>
        Vector2 MoveInput { get; }

        /// <summary>
        /// 是否按下跳跃
        /// </summary>
        bool IsJumpPressed { get; }

        /// <summary>
        /// 是否按下攻击
        /// </summary>
        bool IsAttackPressed { get; }

        /// <summary>
        /// 是否按下交互
        /// </summary>
        bool IsInteractPressed { get; }

        /// <summary>
        /// 是否按下冲刺
        /// </summary>
        bool IsSprintPressed { get; }

        /// <summary>
        /// 初始化
        /// </summary>
        void Initialize();

        /// <summary>
        /// 更新输入
        /// </summary>
        void UpdateInput();

        /// <summary>
        /// 销毁
        /// </summary>
        void Destroy();
    }
}
