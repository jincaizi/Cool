using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface IHitDetector
    {
        bool CheckHit(IAttackHitbox hitbox, IDamageable target, out Vector3 hitDirection);
    }
}
