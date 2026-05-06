namespace Hotfix.GameSystems.Monster
{
    public class TauntBehaviour : IAIBehaviour
    {
        public MonsterAIState StateType => MonsterAIState.Taunt;

        public bool CanEnter(MonsterAIContext ctx)
        {
            return ctx.Config.EnableTaunt
                && !ctx.AttackHitTarget
                && UnityEngine.Random.value < ctx.Config.TauntChance;
        }

        public void Enter(MonsterAIContext ctx)
        {
            ctx.Movement.Stop();
            ctx.Animator.SetTrigger("Taunt");
        }

        public void Update(MonsterAIContext ctx, float deltaTime) { }

        public void Exit(MonsterAIContext ctx)
        {
            ctx.Movement.Resume();
        }
    }
}
