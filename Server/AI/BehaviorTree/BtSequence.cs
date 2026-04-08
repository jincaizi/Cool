using System.Collections.Generic;

namespace KcpServer.AI.BehaviorTree
{
    public class BtSequence : BtNode
    {
        private readonly List<BtNode> _children = new();

        public BtSequence(params BtNode[] children)
        {
            _children.AddRange(children);
        }

        public override BtStatus Tick(AiComponent ai)
        {
            foreach (var child in _children)
            {
                var status = child.Tick(ai);
                if (status == BtStatus.Failure)
                    return BtStatus.Failure;
                if (status == BtStatus.Running)
                    return BtStatus.Running;
            }
            return BtStatus.Success;
        }
    }
}
