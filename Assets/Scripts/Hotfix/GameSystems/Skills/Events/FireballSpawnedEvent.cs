using UnityEngine;

namespace Hotfix.GameSystems.Skills.Events
{
    /// <summary>
    /// 火球生成事件（网络预留：与 SwordQiSpawnedEvent 一致，仅承载生成快照，当前无订阅者）。
    /// </summary>
    public class FireballSpawnedEvent : IEvent
    {
        public int CasterId;
        public int SkillId;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Speed;
        public float Range;
    }
}
