using UnityEngine;

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
        JumpStart = 3,
        JumpAir = 4,
        JumpEnd = 5,
        Death = 6
    }

    /// <summary>
    /// 攻击状态（驱动 Attack Layer FSM）
    /// </summary>
    public enum AttackState
    {
        Idle = 0,
        Attack1 = 1,
        Attack2 = 2,
        SkillQ = 3,
        SkillR_Start = 4,  // 新增：技能R起手阶段
        SkillR_Loop = 5    // 新增：技能R持续循环阶段
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
