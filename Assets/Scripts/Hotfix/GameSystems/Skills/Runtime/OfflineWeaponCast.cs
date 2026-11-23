using System.Collections.Generic;
using Cool.SyncCore;
using Hotfix.GameSystems.Monster;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>同一单机普攻的挥击与剑气共用冻结的武器数值和每目标附加记录。</summary>
    internal sealed class OfflineWeaponCast
    {
        private readonly float _meleeDamageScale;
        private readonly float _swordQiDamageScale;
        private readonly ElementDefinition _element;
        private HashSet<(int TargetId, uint Life)> _appliedTargets;

        internal WeaponCastSnapshot Weapon { get; }
        internal int SkillId { get; }
        internal int SourceId { get; }
        internal ulong CastId { get; }

        internal OfflineWeaponCast(WeaponDefinition weapon, ElementalCatalogDefinition catalog,
            int skillId, int sourceId, ulong castId)
        {
            Weapon = new WeaponCastSnapshot(0, weapon.TemplateId, weapon.ElementId, 0,
                weapon.BaseAttack, weapon.AttackSpeedMultiplier, weapon.SwordQiDamageMultiplier);
            SkillId = skillId;
            SourceId = sourceId;
            CastId = castId;

            var defaultWeapon = catalog.Weapons.Find(row => row.TemplateId == catalog.DefaultWeaponTemplateId);
            float defaultAttack = defaultWeapon?.BaseAttack ?? 1f;
            _meleeDamageScale = defaultAttack > 0f ? Weapon.BaseAttack / defaultAttack : 1f;
            _swordQiDamageScale = _meleeDamageScale * catalog.SwordQi.SwordQiDamageCoefficient
                * Weapon.SwordQiDamageMultiplier;
            if (skillId == 10002)
                _element = ElementalCatalogDefaults.FindElement(Weapon.ElementId);
        }

        internal float DamageScale(bool swordQi) => swordQi ? _swordQiDamageScale : _meleeDamageScale;

        internal void Hit(IDamageable target, DamageBlock damage, Vector3 direction)
        {
            if (!(damage.CalculatedDamage > 0f) || !target.IsAlive
                || NetworkModeService.Load() == NetworkMode.Online)
                return;

            if (target is MonsterEntity monster)
                monster.TakeOfflineWeaponDamage(damage, direction, this);
            else
                target.TakeDamage(damage, direction);
        }

        internal ElementDefinition ClaimElement(int targetId, uint life)
        {
            if (_element == null) return null;
            _appliedTargets ??= new HashSet<(int, uint)>();
            return _appliedTargets.Add((targetId, life)) ? _element : null;
        }
    }
}
