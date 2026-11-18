using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>权威战斗投影组装（Plan 02：HP/装备/活动施法/控制；Plan 04：状态）。</summary>
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
                Statuses = entity.Statuses == null
                    ? new List<StatusProjection>()
                    : entity.Statuses.Runtime.Active.Select(s => new StatusProjection
                    {
                        EffectId = s.EffectId,
                        GroupId = s.GroupId,
                        EndTick = s.EndTick,
                        SourceId = s.SourceId,
                        SlowRatio = s.Definition.SlowRatio,
                        DamagePerTick = s.Definition.DamagePerTick,
                        IntervalTicks = StatusRuntime.SecondsToTicks(s.Definition.IntervalSeconds),
                        IncomingDamageMultiplier = s.Definition.IncomingDamageMultiplier
                    }).ToList(),
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
