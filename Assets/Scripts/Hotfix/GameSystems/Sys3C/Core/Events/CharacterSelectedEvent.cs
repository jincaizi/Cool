using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.Core.Events
{
    /// <summary>
    /// 角色选择确认事件（选人面板 → 玩家生成器）
    /// </summary>
    public struct CharacterSelectedEvent : IEvent
    {
        public CharacterGender Gender;

        public CharacterSelectedEvent(CharacterGender gender)
        {
            Gender = gender;
        }
    }
}
