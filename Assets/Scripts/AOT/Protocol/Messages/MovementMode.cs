namespace Cool.Protocol
{
    /// <summary>位置上报的水平运动模式（只用于服务端预算；防御仍走 IsDefending）。</summary>
    public enum LocomotionMode
    {
        Walk = 0,
        Sprint = 1
    }

    /// <summary>跳跃阶段（None=0 让旧客户端缺省不产生事件）。</summary>
    public enum PlayerJumpPhase
    {
        None = 0,
        Start = 1,
        Air = 2,
        End = 3
    }
}
