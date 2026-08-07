using UnityEngine;

namespace Hotfix.GameSystems.Skills.Events
{
    /// <summary>
    /// 剑气生成事件（网络预留：SkillNetworkSync 将来订阅后转发给其他客户端）。
    /// 本次无订阅者——仅承载生成快照。
    /// </summary>
    public class SwordQiSpawnedEvent : IEvent
    {
        public int CasterId;
        public int SkillId;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Speed;
        public float Range;
    }
}
