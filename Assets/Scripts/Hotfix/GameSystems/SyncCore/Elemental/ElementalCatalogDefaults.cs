using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>首版默认表与稳定 ID。作者数据源为 Server/Config/elemental_catalog.json。</summary>
    public static class ElementalCatalogDefaults
    {
        public const int DefaultSwordTemplateId = 90001;
        public const int IceSwordTemplateId = 90002;
        public const int FireSwordTemplateId = 90003;
        public const int ThunderSwordTemplateId = 90004;
        public const int WindSwordTemplateId = 90005;

        private static readonly ElementalCatalogDefinition Shared = Build();

        public static ElementalCatalogDefinition Create() => Build();

        public static DisplacementDefinition? FindDisplacement(int skillId)
        {
            foreach (var d in Shared.Displacements)
                if (d.SkillId == skillId) return d;
            return null;
        }

        public static JumpSettings Jump => Shared.Jump;

        private static ElementalCatalogDefinition Build() => new ElementalCatalogDefinition
        {
            DefaultWeaponTemplateId = DefaultSwordTemplateId,
            StartingWeaponTemplateIds = new List<int>
            {
                DefaultSwordTemplateId, IceSwordTemplateId, FireSwordTemplateId,
                ThunderSwordTemplateId, WindSwordTemplateId
            },
            Weapons = new List<WeaponDefinition>
            {
                new WeaponDefinition { TemplateId = DefaultSwordTemplateId, WeaponViewKey = "weapon/default", ElementId = 0 },
                new WeaponDefinition { TemplateId = IceSwordTemplateId, WeaponViewKey = "weapon/ice", ElementId = 1 },
                new WeaponDefinition { TemplateId = FireSwordTemplateId, WeaponViewKey = "weapon/fire", ElementId = 2 },
                new WeaponDefinition { TemplateId = ThunderSwordTemplateId, WeaponViewKey = "weapon/thunder", ElementId = 3 },
                new WeaponDefinition { TemplateId = WindSwordTemplateId, WeaponViewKey = "weapon/wind", ElementId = 4 },
            },
            Elements = new List<ElementDefinition>
            {
                new ElementDefinition { ElementId = 1, SwordQiViewKey = "swordqi/ice", StatusEffectIds = { 101 } },
                new ElementDefinition { ElementId = 2, SwordQiViewKey = "swordqi/fire", StatusEffectIds = { 201 } },
                new ElementDefinition { ElementId = 3, SwordQiViewKey = "swordqi/thunder", StatusEffectIds = { 301 } },
                new ElementDefinition { ElementId = 4, SwordQiViewKey = "swordqi/wind", StatusEffectIds = { 401 } },
            },
            Statuses = new List<StatusEffectDefinition>
            {
                new StatusEffectDefinition { EffectId = 101, EffectKind = 1, StackGroupId = 1, DurationSeconds = 2f, SlowRatio = 0.3f },
                new StatusEffectDefinition { EffectId = 201, EffectKind = 2, StackGroupId = 2, DurationSeconds = 3f, IntervalSeconds = 1f, DamagePerTick = 8f },
                new StatusEffectDefinition { EffectId = 301, EffectKind = 3, StackGroupId = 3, DurationSeconds = 0.8f },
                new StatusEffectDefinition { EffectId = 401, EffectKind = 4, StackGroupId = 4, DurationSeconds = 1.5f, IncomingDamageMultiplier = 2f },
            },
            AttackTimings = new List<AttackTimingDefinition>
            {
                new AttackTimingDefinition { SkillId = 10001, WindupSeconds = 0.30f, ActiveSeconds = 0.10f, RecoverySeconds = 0.20f, ChainWindowSeconds = 0.25f, CooldownSeconds = 0f },
                new AttackTimingDefinition { SkillId = 10002, WindupSeconds = 0.35f, ActiveSeconds = 0.10f, RecoverySeconds = 0.25f, ChainWindowSeconds = 0.30f, CooldownSeconds = 0.5f },
            },
            SwordQi = new ProjectileGeometryDefinition { Length = 12f, HalfWidth = 2f, Speed = 18f, Stages = 3, TotalFlightSeconds = 12f / 18f, SwordQiDamageCoefficient = 1f },
            Displacements = new List<DisplacementDefinition>
            {
                new DisplacementDefinition
                {
                    SkillId = 20001, DashDistance = 2f, DashDuration = 0.3f,
                    StartupSeconds = 0.05f, RecoverySeconds = 0.05f
                },
            },
            Jump = new JumpSettings { JumpForce = 10.95f, Gravity = -30f, MaxRiseSeconds = 0.5f },
            Pvp = new PvpCombatSettings()
        };
    }
}
