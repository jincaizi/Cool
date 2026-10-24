using Hotfix.GameSystems.Skills.Effect;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Events
{
    public struct StatusEffectAppliedEvent : IEvent
    {
        public int EntityId;
        public Transform Target;
        public EffectType EffectType;
        public string EffectId;
        public float Duration;
        public int Stacks;
    }

    public struct StatusEffectRemovedEvent : IEvent
    {
        public int EntityId;
        public Transform Target;
        public EffectType EffectType;
        public string EffectId;
    }
}
