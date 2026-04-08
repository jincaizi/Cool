namespace KcpServer.AI.BehaviorTree
{
    public delegate bool ConditionDelegate(AiComponent ai);

    public class BtCondition : BtNode
    {
        private readonly ConditionDelegate _condition;

        public BtCondition(ConditionDelegate condition)
        {
            _condition = condition;
        }

        public override BtStatus Tick(AiComponent ai)
        {
            return _condition(ai) ? BtStatus.Success : BtStatus.Failure;
        }
    }
}
