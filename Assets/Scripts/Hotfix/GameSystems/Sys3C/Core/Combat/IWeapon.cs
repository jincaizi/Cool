using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum WeaponType
    {
        Melee = 0,
        Ranged = 1,
    }

    public interface IWeapon
    {
        WeaponType WeaponType { get; }
        bool CanAttack();
        List<IDamageable> Attack(Vector3 forward, LayerMask targetMask);
        WeaponConfig Config { get; }
    }
}
