namespace KcpServer.Monster
{
    /// <summary>序列节点：依序执行子节点，首个非 Success（Running/Failure）即返回；全成功返回 Success</summary>
    public sealed class BtSequence : BtNode
    {
        private readonly BtNode[] _children;

        public BtSequence(params BtNode[] children) => _children = children;

        public override BtStatus Tick(MonsterTickContext ctx)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ctx);
                if (status != BtStatus.Success) return status;
            }
            return BtStatus.Success;
        }
    }
}
