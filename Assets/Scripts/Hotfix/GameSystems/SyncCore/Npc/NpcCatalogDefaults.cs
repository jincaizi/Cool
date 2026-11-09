using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>Migration authoring seed only. Production must load and validate published content.</summary>
    public static class NpcCatalogDefaults
    {
        public static NpcCatalogDefinition Create()
        {
            var catalog = new NpcCatalogDefinition();
            catalog.Skills.Add(Skill(50001, 0, 1.5f, 0, 120, 15, 10));
            catalog.Skills.Add(Skill(50002, 0, 2, 0, 120, 15, 15));
            catalog.Skills.Add(Skill(50003, 0, 2.2f, 0, 120, 23, 30));
            catalog.Skills.Add(Skill(50004, 0, 2.5f, 0, 120, 30, 20));
            catalog.Skills.Add(Skill(50005, 0, 6, 0, 60, 60, 25));
            catalog.Skills.Add(Skill(50006, 1, 0, 4, 0, 75, 40));
            foreach (var skill in catalog.Skills)
                catalog.Presentations.Add(new NpcPresentationDefinition { PresentationId = skill.PresentationId,
                    StatePath = skill.SkillId == 50005 || skill.SkillId == 50006 ? "Base Layer.Attack02" : "Base Layer.Attack01" });
            catalog.Templates.Add(Template(1, "Slime", 100, 2, 0.5f, 0.5f, 8, 120, 5, 23, Bind(50001, 1.5f)));
            catalog.Templates.Add(Template(2, "Wolf", 150, 3.5f, 0.5f, 0.5f, 15, 90, 10, 35, Bind(50002, 2)));
            catalog.Templates[1].Placeholder = true;
            catalog.Templates.Add(Template(3, "DogKnight", 300, 2.8f, 0.88f, 0.88f, 18, 120, 6, 34, Bind(50003, 2.2f)));
            catalog.Templates[2].Skills.Add(new NpcSkillBinding { SkillId = 50005, Priority = 10,
                MinRange = 2, PreferredMinRange = 2.5f, PreferredMaxRange = 5, MaxRange = 6, MaxHpRatio = 0.5f });
            catalog.Templates.Add(Template(4, "SpiderBoss", 1500, 3.2f, 1.74f, 1, 25, 140, 12, 47, Bind(50004, 2.5f)));
            catalog.Templates[3].IsBoss = true; catalog.Templates[3].Ai.DefaultWindupArmor = true;
            catalog.Templates[3].Skills.Add(new NpcSkillBinding { SkillId = 50006, Priority = 10,
                PreferredMinRange = 0, PreferredMaxRange = 3, MaxRange = 4, MaxHpRatio = 0.5f });
            return catalog;
        }

        private static NpcSkillDefinition Skill(int id, int shape, float range, float radius, float angle, uint cooldown, float damage)
            => new NpcSkillDefinition { SkillId = id, ShapeType = shape, MaxRange = range, AreaRadius = radius,
                Angle = angle, CooldownTicks = cooldown, BaseDamage = damage, PresentationId = id };

        private static NpcSkillBinding Bind(int id, float range) => new NpcSkillBinding {
            SkillId = id, PreferredMinRange = range * 0.4f, PreferredMaxRange = range * 0.8f, MaxRange = range };

        private static NpcTemplateDefinition Template(int id, string name, float hp, float speed, float body, float nav,
            float detection, float vision, float patrol, float leash, NpcSkillBinding skill)
            => new NpcTemplateDefinition { TemplateId = id, Name = name, Hp = hp, MoveSpeed = speed,
                PrefabKey = "NPC/Network/" + name,
                Collision = new NpcCollisionDefinition { BodyRadius = body, NavRadius = nav, SizeX = body * 2, SizeZ = body * 2 },
                Ai = new NpcAiDefinition { DetectionRadius = detection, VisionAngle = vision,
                    AttackRange = skill.MaxRange, PatrolRadius = patrol, LeashRadius = leash },
                Skills = new List<NpcSkillBinding> { skill } };
    }
}
