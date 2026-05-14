using UnityEngine;

namespace Hotfix.GameSystems.Skills.Events
{
    public struct SkillChargingStartedEvent : IEvent
    {
        public int SkillId;
    }

    public struct SkillChargeTickEvent : IEvent
    {
        public int SkillId;
        public float Progress;
    }

    public struct SkillReleasedEvent : IEvent
    {
        public int SkillId;
        public bool IsFullCharge;
        public int CasterId;
    }

    public struct SkillHitTargetEvent : IEvent
    {
        public int SkillId;
        public int CasterId;
        public Vector3 HitPosition;
        public bool IsFullCharge;
    }
}
