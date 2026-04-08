using KcpServer;

namespace KcpServer.AI
{
    public sealed class AiComponent
    {
        public long InstanceId { get; }
        public int TemplateId { get; }
        public AiBlackboard Blackboard { get; } = new();

        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; }
        public float MoveSpeed { get; set; }
        public float VisionRadius { get; set; }
        public float VisionAngle { get; set; }
        public float AttackRange { get; set; }
        public NpcAnimationState CurrentAnimState { get; set; } = NpcAnimationState.Idle;

        public AiComponent(long instanceId, int templateId)
        {
            InstanceId = instanceId;
            TemplateId = templateId;
        }
    }
}
