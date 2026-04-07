using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 标准化移动命令
    /// </summary>
    public struct MoveCommand
    {
        /// <summary>
        /// 移动方向（标准化）
        /// </summary>
        public Vector3 MoveDir;

        /// <summary>
        /// 移动速度
        /// </summary>
        public float Speed;

        /// <summary>
        /// 角色朝向（四元数）
        /// </summary>
        public Quaternion Rotation;

        /// <summary>
        /// 时间戳（客户端预测用）
        /// </summary>
        public long Timestamp;

        /// <summary>
        /// 序列号
        /// </summary>
        public uint Sequence;
    }

    /// <summary>
    /// 角色状态
    /// </summary>
    public enum CharacterState
    {
        Idle,
        Running,
        Falling
    }

    /// <summary>
    /// 角色数据（值类型，主线程访问）
    /// </summary>
    public struct CharacterData
    {
        /// <summary>
        /// 世界位置
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// 旋转（四元数）
        /// </summary>
        public Quaternion Rotation;

        /// <summary>
        /// 速度向量
        /// </summary>
        public Vector3 Velocity;

        /// <summary>
        /// 当前状态
        /// </summary>
        public CharacterState State;

        /// <summary>
        /// 是否在地面上
        /// </summary>
        public bool IsGrounded;

        /// <summary>
        /// 垂直速度（用于动画）
        /// </summary>
        public float VerticalVelocity;
    }
}