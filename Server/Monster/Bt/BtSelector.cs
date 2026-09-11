namespace KcpServer.Monster
{
    /// <summary>选择节点：依序尝试子节点，首个非 Failure（Running/Success）即返回；全失败返回 Failure</summary>
    public sealed class BtSelector : BtNode
    {
        private readonly BtNode[] _children;

        public BtSelector(params BtNode[] children) => _children = children;

        public override BtStatus Tick(MonsterTickContext ctx)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ctx);
                if (status != BtStatus.Failure) return status;
            }
            return BtStatus.Failure;
        }
    }
}
