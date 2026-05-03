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

        public DamageEvent(int sourceId, int targetId, float damage, bool isCritical = false)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Damage = damage;
            IsCritical = isCritical;
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

    /// <summary>
    /// 死亡事件
    /// </summary>
    public struct DeathEvent : IEvent
    {
        public int EntityId;
        public int KillerId;

        public DeathEvent(int entityId, int killerId = 0)
        {
            EntityId = entityId;
            KillerId = killerId;
        }
    }
}