using UnityEngine;

namespace AOT.DataDefinition.Interfaces
{
    /// <summary>
    /// 玩家控制器接口
    /// </summary>
    public interface IPlayerController
    {
        /// <summary>
        /// 当前玩家状态
        /// </summary>
        PlayerState CurrentState { get; }

        /// <summary>
        /// 是否正在移动
        /// </summary>
        bool IsMoving { get; }

        /// <summary>
        /// 是否在地面上
        /// </summary>
        bool IsGrounded { get; }

        /// <summary>
        /// 移动速度
        /// </summary>
        float MoveSpeed { get; set; }

        /// <summary>
        /// 角色Transform
        /// </summary>
        Transform CharacterTransform { get; }

        /// <summary>
        /// 设置移动输入
        /// </summary>
        void SetMoveInput(Vector2 input);

        /// <summary>
        /// 触发跳跃
        /// </summary>
        void Jump();

        /// <summary>
        /// 触发攻击
        /// </summary>
        void Attack();

        /// <summary>
        /// 停止攻击
        /// </summary>
        void StopAttack();

        /// <summary>
        /// 服务器位置同步回调（客户端预测校正）
        /// </summary>
        void OnServerPositionUpdate(Vector3 serverPosition, Quaternion serverRotation);

        /// <summary>
        /// 初始化
        /// </summary>
        void Initialize();

        /// <summary>
        /// 销毁
        /// </summary>
        void Destroy();
    }

    /// <summary>
    /// 玩家状态枚举
    /// </summary>
    public enum PlayerState
    {
        /// <summary>
        /// 空闲
        /// </summary>
        Idle = 0,

        /// <summary>
        /// 行走
        /// </summary>
        Walk = 1,

        /// <summary>
        /// 奔跑
        /// </summary>
        Run = 2,

        /// <summary>
        /// 跳跃
        /// </summary>
        Jump = 3,

        /// <summary>
        /// 掉落
        /// </summary>
        Fall = 4,

        /// <summary>
        /// 攻击
        /// </summary>
        Attack = 5,

        /// <summary>
        /// 受击
        /// </summary>
        Hit = 6,

        /// <summary>
        /// 死亡
        /// </summary>
        Dead = 7
    }
}
