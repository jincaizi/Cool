using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(menuName = "Game/Skills/Skill Presentation Catalog",
        fileName = "SkillPresentationCatalog")]
    public sealed class SkillPresentationCatalog : ScriptableObject
    {
        [SerializeField] private SkillData[] _skills = Array.Empty<SkillData>();

        public IReadOnlyList<SkillData> Skills => _skills;

        public bool TryGet(int skillId, out SkillData data)
        {
            foreach (var skill in _skills)
            {
                if (skill != null && skill.SkillId == skillId)
                {
                    data = skill;
                    return true;
                }
            }
            data = null!;
            return false;
        }
    }
}
