using UnityEngine;
using Hotfix.GameSystems.Sys3C.FSM;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 基础移动状态（驱动 Base Layer FSM）
    /// </summary>
    public enum BaseState
    {
        Idle = 0,
        Move = 1,
        Sprint = 2,
        Locomotion = 7,  // 新增：Blend Tree 统一入口状态
        JumpStart = 3,
        JumpAir = 4,
        JumpEnd = 5,
        Death = 6,
        Defend = 8
    }

    /// <summary>
    /// 角色数据（值类型，主线程访问）
    /// </summary>
    public struct CharacterData
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public Vector3 MoveDir;
        public bool IsGrounded;
        public float VerticalVelocity;
        public BaseState BaseState;
        public AttackState AttackState;
        public bool IsSprint;
        public bool IsDead;
        public bool RequestJump;       // 跳跃请求标记
        public bool HasLeftGround;     // 是否已离地（用于受击判定：地面受击 vs 空中受击）
        public bool IsDefending;       // 是否处于防御姿态
        public float MovementSpeed;    // 当前移动速度（0-1，用于 Blend Tree）
        public float MoveMagnitude;    // 移动方向幅度（用于 Blend Tree）
    }

    /// <summary>
    /// 移动命令
    /// </summary>
    public struct MoveCommand
    {
        public Vector3 MoveDir;
        public float Speed;
        public Quaternion Rotation;
        public bool IsSprint;
    }
}
