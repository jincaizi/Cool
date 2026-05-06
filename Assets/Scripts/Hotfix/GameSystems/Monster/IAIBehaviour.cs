namespace Hotfix.GameSystems.Monster
{
    public interface IAIBehaviour
    {
        bool CanEnter(MonsterAIContext ctx);
        void Enter(MonsterAIContext ctx);
        void Update(MonsterAIContext ctx, float deltaTime);
        void Exit(MonsterAIContext ctx);
        MonsterAIState StateType { get; }
    }
}
