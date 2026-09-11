using System;

namespace KcpServer.Monster
{
    /// <summary>条件节点：黑板谓词——true=Success，false=Failure</summary>
    public sealed class BtCondition : BtNode
    {
        private readonly Func<MonsterState, bool> _predicate;

        public BtCondition(Func<MonsterState, bool> predicate) => _predicate = predicate;

        public override BtStatus Tick(MonsterTickContext ctx)
            => _predicate(ctx.State) ? BtStatus.Success : BtStatus.Failure;
    }
}
