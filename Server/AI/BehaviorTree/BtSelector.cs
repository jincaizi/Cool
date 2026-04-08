using System.Collections.Generic;
using KcpServer.AI.Core;

namespace KcpServer.AI.BehaviorTree
{
    public class BtSelector : BtNode
    {
        private readonly List<BtNode> _children = new();

        public BtSelector(params BtNode[] children)
        {
            _children.AddRange(children);
        }

        public override BtStatus Tick(AiComponent ai)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ai);
                if (status == BtStatus.Success)
                    return BtStatus.Success;
                if (status == BtStatus.Running)
                    return BtStatus.Running;
            }
            return BtStatus.Failure;
        }
    }
}
