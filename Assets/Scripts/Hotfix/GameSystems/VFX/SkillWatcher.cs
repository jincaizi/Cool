namespace Hotfix.GameSystems.VFX
{
    public static class SkillWatcher
    {
        public static bool Matches(int[] watchIds, int skillId)
        {
            if (watchIds == null || watchIds.Length == 0)
                return skillId == (int)Skills.Definition.SkillID.SkillR;
            foreach (var id in watchIds)
                if (id == skillId) return true;
            return false;
        }
    }
}
