namespace KcpServer.Monster
{
    /// <summary>行为树节点（旧 Server/AI 移植——tick 驱动替代 Task.Run 循环，R2 合规）</summary>
    public abstract class BtNode
    {
        public abstract BtStatus Tick(MonsterTickContext ctx);
    }
}
