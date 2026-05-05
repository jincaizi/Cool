using UnityEngine;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    public interface IDamageable
    {
        void TakeDamage(DamageData damageData, Vector3 hitDirection);
        bool IsAlive { get; }
        Transform Transform { get; }
    }
}
