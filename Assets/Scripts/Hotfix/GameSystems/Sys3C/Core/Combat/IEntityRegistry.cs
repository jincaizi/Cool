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
        void Register(Transform entity, EntityType type);
        void Unregister(Transform entity);
        IReadOnlyList<Transform> FindNearby(Vector3 center, float radius, EntityType type);
    }
}
