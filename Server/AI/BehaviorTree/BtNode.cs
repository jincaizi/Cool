using KcpServer.AI.Core;

namespace KcpServer.AI.BehaviorTree
{
    public abstract class BtNode
    {
        public abstract BtStatus Tick(AiComponent ai);
    }
}
