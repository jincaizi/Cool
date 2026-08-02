using UnityEngine;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Sys3C.Core.Combat;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 玩家受击事件（给浮字/火花系统使用，字段命名与 MonsterTakeDamageEvent 对齐）
    /// </summary>
    public struct PlayerTakeDamageEvent : IEvent
    {
        public int EntityId;
        public Vector3 HitPosition;
        public HitZonePart Part;
        public Vector3 HitDirection;
        public int Damage;
        public Color ElementColor;

        public PlayerTakeDamageEvent(
            int entityId, Vector3 hitPos, HitZonePart part, Vector3 hitDir,
            int damage, Color elementColor)
        {
            EntityId = entityId;
            HitPosition = hitPos;
            Part = part;
            HitDirection = hitDir;
            Damage = damage;
            ElementColor = elementColor;
        }
    }
}
