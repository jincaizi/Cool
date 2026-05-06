namespace Hotfix.GameSystems.Monster
{
    public class AlertBehaviour : IAIBehaviour
    {
        public MonsterAIState StateType => MonsterAIState.Alert;

        public bool CanEnter(MonsterAIContext ctx)
        {
            if (ctx.Target == null) return false;
            float dist = UnityEngine.Vector3.Distance(
                ctx.Self.position, ctx.Target.position);
            return dist > ctx.Config.DetectRange && dist < ctx.Config.AlertRange;
        }

        public void Enter(MonsterAIContext ctx)
        {
            ctx.Animator.SetTrigger("SenseSomething");
        }

        public void Update(MonsterAIContext ctx, float deltaTime) { }

        public void Exit(MonsterAIContext ctx) { }
    }
}
