using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface IAttackShape
    {
        IReadOnlyList<IDamageable> Resolve(
            Vector3 origin, Vector3 forward, LayerMask targetMask);

        void ResolveNonAlloc(
            Vector3 origin, Vector3 forward, LayerMask targetMask,
            List<IDamageable> results);
    }
}
