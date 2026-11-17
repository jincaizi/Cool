namespace KcpServer
{
    /// <summary>一次已确认施法的权威技能位移窗口（服务端主循环独占）。</summary>
    public sealed class DisplacementWindow
    {
        public ulong CastId;
        public int SkillId;
        public uint StartTick;
        public uint EndTick;
        public float TotalDistance;
        public float Speed;
        public float GrantedDistance;
    }
}
