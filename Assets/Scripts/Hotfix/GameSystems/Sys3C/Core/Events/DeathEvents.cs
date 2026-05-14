using Hotfix.GameSystems.Skills.Events;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 死亡事件
    /// </summary>
    public class DeathEvent : IEvent
    {
        public long EntityId { get; set; }
        public int KillerId { get; set; }
        public string KillerName { get; set; }

        public DeathEvent() { }

        public DeathEvent(long entityId)
        {
            EntityId = entityId;
        }
    }

    /// <summary>
    /// 复活事件
    /// </summary>
    public class ResurrectEvent : IEvent
    {
        public long EntityId { get; set; }
        public int ResurrectType { get; set; } // 0=普通复活 1=队友复活

        public ResurrectEvent() { }

        public ResurrectEvent(long entityId, int type = 0)
        {
            EntityId = entityId;
            ResurrectType = type;
        }
    }
}