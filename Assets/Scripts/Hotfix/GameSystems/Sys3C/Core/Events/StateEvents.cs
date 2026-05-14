using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Skills.Events;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 状态变化事件
    /// </summary>
    public struct StateChangedEvent : IEvent
    {
        public LayerType Layer;
        public string PreviousState;
        public string CurrentState;

        public StateChangedEvent(LayerType layer, string previous, string current)
        {
            Layer = layer;
            PreviousState = previous;
            CurrentState = current;
        }
    }

    /// <summary>
    /// 层锁定事件
    /// </summary>
    public struct LayerLockedEvent : IEvent
    {
        public LayerType Layer;
        public bool IsLocked;

        public LayerLockedEvent(LayerType layer, bool isLocked)
        {
            Layer = layer;
            IsLocked = isLocked;
        }
    }

    /// <summary>
    /// 层解锁事件
    /// </summary>
    public struct LayerUnlockedEvent : IEvent
    {
        public LayerType Layer;

        public LayerUnlockedEvent(LayerType layer)
        {
            Layer = layer;
        }
    }
}