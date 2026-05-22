using System;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface IPlayerStatsProvider
    {
        string Name { get; }
        int Level { get; }
        Sprite Portrait { get; }
        float HPPercent { get; }
        int CurrentHP { get; }
        int MaxHP { get; }
        float MPPercent { get; }
        int CurrentMP { get; }
        int MaxMP { get; }
        BuffInfo[] ActiveBuffs { get; }
        event Action<float, int, int> OnHPChanged;
        event Action<float, int, int> OnMPChanged;
        event Action<BuffInfo[]> OnBuffsChanged;
    }
}
