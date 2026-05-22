using System;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public interface ITargetStatsProvider
    {
        float MPPercent { get; }
        int CurrentMP { get; }
        int MaxMP { get; }
        BuffInfo[] ActiveBuffs { get; }
        event Action<float, int, int> OnMPChanged;
        event Action<BuffInfo[]> OnBuffsChanged;
    }
}
