using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum EntityType
    {
        Player = 0,
        Monster = 1,
    }

    public interface IEntityRegistry
    {
        void Register(IDamageable entity, EntityType type);
        void Unregister(IDamageable entity);
        IReadOnlyList<IDamageable> FindNearby(Vector3 center, float radius, EntityType type);
    }
}
