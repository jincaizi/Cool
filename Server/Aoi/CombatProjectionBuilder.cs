using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>权威战斗投影组装（Plan 02：HP/装备/活动施法；状态与控制留待后续任务）。</summary>
    public static class CombatProjectionBuilder
    {
        public static CombatProjection Build(PlayerState entity, ElementalCatalogDefinition catalog)
        {
            var weapon = catalog.Weapons.Find(w => w.TemplateId == entity.EquippedWeaponTemplateId);
            return new CombatProjection
            {
                CombatVersion = entity.CombatVersion,
                LifeId = entity.LifeId,
                Hp = entity.Hp,
                MaxHp = entity.MaxHp,
                Dead = !entity.IsAlive,
                IsDefending = entity.IsDefending,
                Statuses = new List<StatusProjection>(),
                Equipment = entity.EquippedWeaponInstanceId != 0
                    ? new EquipmentSummary
                    {
                        WeaponInstanceId = entity.EquippedWeaponInstanceId,
                        TemplateId = entity.EquippedWeaponTemplateId,
                        ElementId = weapon?.ElementId ?? 0
                    }
                    : null,
                Control = new ControlSummary
                {
                    SpeedScale = entity.ControlSpeedScale,
                    ActionLock = entity.ControlActionLock,
                    ControlVersion = entity.ControlVersion
                },
                ActiveCast = entity.ActiveCast
            };
        }
    }
}
