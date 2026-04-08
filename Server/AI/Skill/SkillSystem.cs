using System.Collections.Generic;

namespace KcpServer.AI.Skill
{
    public class SkillSystem
    {
        private readonly List<SkillData> _skills = new();
        private readonly Dictionary<string, float> _cooldowns = new();

        public SkillSystem(IEnumerable<SkillData> skills)
        {
            foreach (var skill in skills)
            {
                _skills.Add(skill);
                _cooldowns[skill.SkillName] = 0f;
            }
        }

        public void Update(float deltaTime)
        {
            foreach (var key in _cooldowns.Keys)
            {
                _cooldowns[key] = System.Math.Max(0, _cooldowns[key] - deltaTime);
            }
        }

        public bool CanCast(string skillName)
        {
            return _cooldowns.TryGetValue(skillName, out var cd) && cd <= 0;
        }

        public float? CastSkill(string skillName)
        {
            if (!CanCast(skillName)) return null;

            var skill = _skills.Find(s => s.SkillName == skillName);
            if (skill == null) return null;

            _cooldowns[skillName] = skill.Cooldown;
            return skill.Damage;
        }

        public float GetDamage(string skillName)
        {
            var skill = _skills.Find(s => s.SkillName == skillName);
            return skill?.Damage ?? 0f;
        }
    }
}