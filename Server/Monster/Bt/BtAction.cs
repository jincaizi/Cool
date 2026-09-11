using System;

namespace KcpServer.Monster
{
    /// <summary>动作节点：执行动作函数并透传其状态</summary>
    public sealed class BtAction : BtNode
    {
        private readonly Func<MonsterTickContext, BtStatus> _action;

        public BtAction(Func<MonsterTickContext, BtStatus> action) => _action = action;

        public override BtStatus Tick(MonsterTickContext ctx) => _action(ctx);
    }
}
