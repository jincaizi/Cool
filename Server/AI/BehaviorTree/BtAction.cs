using KcpServer.AI.Core;

namespace KcpServer.AI.BehaviorTree
{
    public delegate BtStatus ActionDelegate(AiComponent ai);

    public class BtAction : BtNode
    {
        private readonly ActionDelegate _action;

        public BtAction(ActionDelegate action)
        {
            _action = action;
        }

        public override BtStatus Tick(AiComponent ai)
        {
            return _action(ai);
        }
    }
}
