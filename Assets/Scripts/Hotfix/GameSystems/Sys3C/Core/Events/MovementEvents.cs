using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Skills.Events;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 跳跃事件
    /// </summary>
    public struct JumpEvent : IEvent
    {
        public JumpPhase Phase;

        public JumpEvent(JumpPhase phase)
        {
            Phase = phase;
        }

        public static JumpEvent Start => new JumpEvent(JumpPhase.Start);
        public static JumpEvent Air => new JumpEvent(JumpPhase.Air);
        public static JumpEvent End => new JumpEvent(JumpPhase.End);
    }

    /// <summary>
    /// 落地事件
    /// </summary>
    public struct LandEvent : IEvent
    {
        public float FallDistance;

        public LandEvent(float fallDistance)
        {
            FallDistance = fallDistance;
        }
    }
}