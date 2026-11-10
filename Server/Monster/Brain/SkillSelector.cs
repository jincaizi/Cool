using System;
using Cool.SyncCore;
using KcpServer.Config;

namespace KcpServer.Monster
{
    public readonly struct SelectedSkill
    {
        public readonly MonsterSkillConfig Config;
        public readonly SkillEntry Entry;

        public SelectedSkill(MonsterSkillConfig config, SkillEntry entry)
        {
            Config = config;
            Entry = entry;
        }
    }

    /// <summary>
    /// 多技能选择（P2）。冷却只认 CombatRules；距离/血量/优先级来自模板 skills。
    /// 排序：priority 降序 → skillId 升序（确定性）。
    /// </summary>
    public static class SkillSelector
    {
        private const float Epsilon = 0.001f;

        private static bool HpAllowed(MonsterSkillConfig skill, PlayerState self)
        {
            float ratio = self.MaxHp > 0f ? self.Hp / self.MaxHp : 0f;
            return ratio <= skill.hpBelow + 0.0001f && ratio >= skill.hpAbove - 0.0001f;
        }

        /// <summary>就绪且距离/血量满足的最高优先级技能；无则 null。</summary>
        public static SelectedSkill? Select(MonsterState monster, PlayerState self, float distance, uint tick)
        {
            SelectedSkill? best = null;
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                float maxRange = skill.MaxRangeOf(entry);
                if (distance < skill.minRange - Epsilon || distance > maxRange + Epsilon) continue;
                if (!self.IsCastReady(skill.skillId, tick, CombatRules.CooldownTicks(entry))) continue;

                if (best == null
                    || skill.priority > best.Value.Config.priority
                    || (skill.priority == best.Value.Config.priority && skill.skillId < best.Value.Config.skillId))
                {
                    best = new SelectedSkill(skill, entry);
                }
            }
            return best;
        }

        /// <summary>当前血量条件下所有可用技能的最大 maxRange——停滞检测用“已经能打”的半径。
        /// 只过滤血量，不看冷却（冷却中仍然在射程内，不应被误判为卡死）。</summary>
        public static float MaxRangeFor(MonsterState monster, PlayerState self)
        {
            float max = 0f;
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                max = MathF.Max(max, skill.MaxRangeOf(entry));
            }
            return max;
        }

        public static SelectedSkill? SelectPotential(MonsterState monster, PlayerState self, uint tick)
        {
            SelectedSkill? best=null;
            foreach(var skill in monster.Config.skills) {
                if(!CombatRules.TryGet(skill.skillId,out var entry) || !HpAllowed(skill,self) || !self.IsCastReady(skill.skillId,tick,CombatRules.CooldownTicks(entry))) continue;
                if(best==null || skill.priority>best.Value.Config.priority || skill.priority==best.Value.Config.priority && skill.skillId<best.Value.Config.skillId) best=new SelectedSkill(skill,entry);
            }
            return best;
        }

        /// <summary>距离是否落在任一（血量条件满足的）技能区间；忽略冷却——用于 Chase 停步。
        /// 与 Select 分开：目标在射程内但技能冷却时，怪物应站定等待而不是继续贴近。</summary>
        public static bool IsWithinAnySkillRange(MonsterState monster, PlayerState self, float distance)
        {
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                float maxRange = skill.MaxRangeOf(entry);
                if (distance >= skill.minRange - Epsilon && distance <= maxRange + Epsilon) return true;
            }
            return false;
        }

        /// <summary>目标太近、所有满足血量条件的技能 minRange 都够不到 → 需要后撤。</summary>
        public static bool NeedsReposition(MonsterState monster, PlayerState self, float distance)
        {
            bool anyTooClose = false;
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                float maxRange = skill.MaxRangeOf(entry);
                if (distance >= skill.minRange - Epsilon && distance <= maxRange + Epsilon) return false;
                if (distance < skill.minRange - Epsilon) anyTooClose = true;
            }
            return anyTooClose;
        }

        /// <summary>后撤目标距离：所有“太近”技能 minRange 的最大值（一次退到都满足）。</summary>
        public static float RepositionDistance(MonsterState monster, PlayerState self, float distance)
        {
            float desired = 0f;
            foreach (var skill in monster.Config.skills)
            {
                if (!CombatRules.TryGet(skill.skillId, out var entry)) continue;
                if (!HpAllowed(skill, self)) continue;
                float maxRange = skill.MaxRangeOf(entry);
                if (distance <= maxRange + Epsilon && distance < skill.minRange - Epsilon)
                    desired = MathF.Max(desired, skill.minRange);
            }
            return desired;
        }
    }
}
