namespace KcpServer.AI
{
    public class AiBlackboard
    {
        public AlertLevel AlertLevel { get; set; } = AlertLevel.PEACE;
        public long? TargetId { get; set; }
        public Vector3 SpawnPosition { get; set; }
        public Vector3 PatrolCenter { get; set; }
        public float PatrolRadius { get; set; } = 10f;
        public int CurrentPatrolIndex { get; set; }
        public Vector3? LastKnownTargetPosition { get; set; }
    }
}
