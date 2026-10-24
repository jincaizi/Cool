using UnityEngine;
using Hotfix.GameSystems.Skills.Events;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>格挡成功（纯表现事件，不参与结算）。</summary>
    public struct BlockSucceededEvent : IEvent
    {
        public int EntityId;          // 防御者实例 ID（飘字去重用）
        public Vector3 HitPosition;   // 火花位置：盾面/接触点
        public Vector3 TextPosition;  // 飘字位置：头顶

        public BlockSucceededEvent(int entityId, Vector3 hitPosition, Vector3 textPosition)
        {
            EntityId = entityId;
            HitPosition = hitPosition;
            TextPosition = textPosition;
        }
    }
}
