using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 技能激活事件
    /// </summary>
    public struct SkillActivatedEvent : IEvent
    {
        public int SkillId;
        public string SkillName;

        public SkillActivatedEvent(int skillId, string skillName)
        {
            SkillId = skillId;
            SkillName = skillName;
        }
    }

    /// <summary>
    /// 技能完成事件
    /// </summary>
    public struct SkillCompletedEvent : IEvent
    {
        public int SkillId;
        public bool WasInterrupted;

        public SkillCompletedEvent(int skillId, bool wasInterrupted = false)
        {
            SkillId = skillId;
            WasInterrupted = wasInterrupted;
        }
    }

    /// <summary>
    /// 技能被打断事件
    /// </summary>
    public struct SkillInterruptedEvent : IEvent
    {
        public int SkillId;
        public InterruptionSource Source;

        public SkillInterruptedEvent(int skillId, InterruptionSource source)
        {
            SkillId = skillId;
            Source = source;
        }
    }
}