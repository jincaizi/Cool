using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface ITargetable
    {
        string DisplayName { get; }
        int Level { get; }
        Sprite Portrait { get; }
        float HPPercent { get; }
        int CurrentHP { get; }
        int MaxHP { get; }
        Vector3 WorldPosition { get; }
        event Action<float, int, int> OnHPChanged;
        event Action OnDeath;
    }
}
