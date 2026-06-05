namespace Hotfix.GameSystems.Monster
{
    // Terminal state. Once entered, never transitions out.
    // Death animation plays once. OnUpdate is a no-op.
    // The death sequence (loot, destroy) is handled by MonsterEntity coroutine.
    public class DeathState : AIStateBase
    {
        public override MonsterAIState StateType => MonsterAIState.Death;

        public override void OnEnter(AIContext ctx)
        {
            ctx.Animator.SetTrigger(MonsterAnimHashes.Death);
            ctx.Movement.Stop();
        }
    }
}
