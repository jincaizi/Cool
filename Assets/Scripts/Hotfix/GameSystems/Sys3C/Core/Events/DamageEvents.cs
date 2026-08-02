using UnityEngine;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Sys3C.Core.Combat;

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

    /// <summary>
    /// 怪物受伤事件（给浮字/火花系统使用）
    /// </summary>
    public struct MonsterTakeDamageEvent : IEvent
    {
        public int EntityId;
        public Vector3 HitPosition;   // 真实接触点（或 FromDirection 重建点）
        public Vector3 HitDirection;
        public int Damage;
        public bool IsCritical;
        public int SkillId;
        public int ComboIndex;
        public HitZonePart Part;
        public Color ElementColor;

        public MonsterTakeDamageEvent(
            int entityId, Vector3 hitPos, Vector3 hitDir,
            int damage, bool isCritical = false,
            int skillId = 0, int comboIndex = 1,
            HitZonePart part = HitZonePart.Torso, Color elementColor = default)
        {
            EntityId = entityId;
            HitPosition = hitPos;
            HitDirection = hitDir;
            Damage = damage;
            IsCritical = isCritical;
            SkillId = skillId;
            ComboIndex = comboIndex;
            Part = part;
            ElementColor = elementColor;
        }
    }

    /// <summary>
    /// 击退事件
    /// </summary>
    public struct KnockbackEvent : IEvent
    {
        public int EntityId;
        public Vector3 Direction;
        public float Force;

        public KnockbackEvent(int entityId, Vector3 direction, float force)
        {
            EntityId = entityId;
            Direction = direction;
            Force = force;
        }
    }

}
