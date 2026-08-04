using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 玩家生成完成事件（生成器 → 需要玩家引用的系统，如 HitStopManager）
    /// </summary>
    public struct PlayerSpawnedEvent : IEvent
    {
        public Animator PlayerAnimator;

        public PlayerSpawnedEvent(Animator playerAnimator)
        {
            PlayerAnimator = playerAnimator;
        }
    }
}
