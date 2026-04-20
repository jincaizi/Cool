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

        /// <summary>
        /// 是否冲刺
        /// </summary>
        public bool IsSprint;
    }

    /// <summary>
    /// 角色状态
    /// </summary>
    public enum CharacterState
    {
        Idle = 0,
        BattleIdle = 1,
        Move = 2,
        Run = 3,
        JumpStart = 4,
        JumpAir = 5,
        JumpEnd = 6,
        Death = 7
    }

    /// <summary>
    /// 跳跃动画阶段（驱动 JumpStart → JumpAir → JumpEnd）
    /// </summary>
    public enum JumpPhase
    {
        None = 0,
        Start = 1,
        Air = 2,
        End = 3
    }

    /// <summary>
    /// 攻击连击阶段（0 = 未攻击，1-4 = 攻击索引）
    /// </summary>
    public enum AttackPhase
    {
        None = 0,
        Attack1 = 1,
        Attack2 = 2,
        Attack3 = 3,
        Attack4 = 4
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

        /// <summary>
        /// 当前跳跃阶段（Start/Air/End）
        /// </summary>
        public JumpPhase JumpPhase;

        /// <summary>
        /// 当前攻击阶段（None 或 Attack1-4）
        /// </summary>
        public AttackPhase AttackPhase;

        /// <summary>
        /// 是否处于战斗模式（任意攻击时触发）
        /// </summary>
        public bool IsBattle;

        /// <summary>
        /// 连击窗口是否激活——下一击输入可触发连击
        /// </summary>
        public bool ComboWindowActive;

        /// <summary>
        /// 是否冲刺
        /// </summary>
        public bool IsSprint;
    }
}
