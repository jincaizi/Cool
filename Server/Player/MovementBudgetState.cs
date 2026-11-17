namespace KcpServer
{
    /// <summary>每玩家位置预算游标（主循环独占）。</summary>
    public sealed class MovementBudgetState
    {
        public uint LastBudgetTick;
        public uint LastJumpSeq;
        public bool JumpActive;
        public long JumpStartTick;
        public float JumpStartY;
        public float JumpHorizontalSpeed;
        public float JumpGrantedDistance;

        public void Reset()
        {
            LastBudgetTick = 0;
            LastJumpSeq = 0;
            JumpActive = false;
            JumpStartTick = 0;
            JumpStartY = 0f;
            JumpHorizontalSpeed = 0f;
            JumpGrantedDistance = 0f;
        }
    }
}
