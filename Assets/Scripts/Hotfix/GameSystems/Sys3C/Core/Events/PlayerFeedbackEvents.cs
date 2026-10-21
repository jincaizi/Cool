using Hotfix.GameSystems.Skills.Events;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>实际回血发生时发出（本地玩家）。表现层据此播绿色粒子。</summary>
    public struct PlayerHealedEvent : IEvent
    {
        public int EntityId;
        public float Amount;

        public PlayerHealedEvent(int entityId, float amount)
        {
            EntityId = entityId;
            Amount = amount;
        }
    }

    /// <summary>本地玩家被玩家/bot 来源命中（非格挡、非致死）时发出。UI 层据此触发红色闪屏。</summary>
    public struct LocalPlayerHitByPlayerEvent : IEvent
    {
        public int EntityId;

        public LocalPlayerHitByPlayerEvent(int entityId)
        {
            EntityId = entityId;
        }
    }
}
