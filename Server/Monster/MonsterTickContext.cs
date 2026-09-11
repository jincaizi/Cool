namespace KcpServer.Monster
{
    /// <summary>行为树 tick 上下文——一个 tick 内共享（状态/实体/模块/移动系统/节拍）</summary>
    public sealed class MonsterTickContext
    {
        public MonsterState State { get; }
        public PlayerState Entity { get; }
        public MonsterModule Module { get; }
        public SimpleMoveSystem Mover { get; }
        public float DeltaTime { get; }
        public uint Tick { get; }

        public MonsterTickContext(MonsterState state, PlayerState entity, MonsterModule module,
            SimpleMoveSystem mover, float deltaTime, uint tick)
        {
            State = state;
            Entity = entity;
            Module = module;
            Mover = mover;
            DeltaTime = deltaTime;
            Tick = tick;
        }
    }
}
