using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 伤害事件
    /// </summary>
    public struct DamageEvent : IEvent
    {
        public int SourceId;
        public int TargetId;
        public float Damage;
        public bool IsCritical;
        public Vector3 HitDirection;
        public float KnockbackForce;
        public float LaunchForce;
        public float StunDuration;

        public DamageEvent(int sourceId, int targetId, float damage, bool isCritical = false)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Damage = damage;
            IsCritical = isCritical;
            HitDirection = Vector3.back;
            KnockbackForce = 0f;
            LaunchForce = 0f;
            StunDuration = 0f;
        }
    }

    /// <summary>
    /// 受击事件
    /// </summary>
    public struct HitReceivedEvent : IEvent
    {
        public float KnockbackForce;
        public bool HasSuperArmor;

        public HitReceivedEvent(float knockbackForce = 0f, bool hasSuperArmor = false)
        {
            KnockbackForce = knockbackForce;
            HasSuperArmor = hasSuperArmor;
        }
    }
}
