namespace KcpServer.Monster
{
    public enum MovementStatus { Moving, Arrived, PendingBudget, TemporarilyBlocked, Unreachable }
    public readonly struct MovementResult
    {
        public readonly MovementStatus Status;
        public readonly string FailReason;
        public readonly uint LastProgressTick;
        public readonly int ValidQueries;
        public readonly uint GoalRevision;
        public MovementResult(MovementStatus status,string reason,uint progress,int queries,uint revision)
        {Status=status;FailReason=reason;LastProgressTick=progress;ValidQueries=queries;GoalRevision=revision;}
    }
}
