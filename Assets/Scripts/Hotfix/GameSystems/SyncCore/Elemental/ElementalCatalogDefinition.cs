using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>属性剑气共享目录：服务端与客户端读取同一份作者数据（纯 C# DTO）。</summary>
    [Serializable]
    public sealed class ElementalCatalogDefinition
    {
        public int SchemaVersion = 1;
        public string ContentHash = "";
        public int DefaultWeaponTemplateId;
        public List<int> StartingWeaponTemplateIds = new List<int>();
        public List<WeaponDefinition> Weapons = new List<WeaponDefinition>();
        public List<ElementDefinition> Elements = new List<ElementDefinition>();
        public List<StatusEffectDefinition> Statuses = new List<StatusEffectDefinition>();
        public List<AttackTimingDefinition> AttackTimings = new List<AttackTimingDefinition>();
        public ProjectileGeometryDefinition SwordQi = new ProjectileGeometryDefinition();
        public List<DisplacementDefinition> Displacements = new List<DisplacementDefinition>();
        public JumpSettings Jump = new JumpSettings();
        public PvpCombatSettings Pvp = new PvpCombatSettings();
    }

    [Serializable]
    public sealed class WeaponDefinition
    {
        public int TemplateId;
        public string WeaponViewKey = "";
        public int ElementId;
        public float BaseAttack = 1f;
        public float AttackSpeedMultiplier = 1f;
        public float SwordQiDamageMultiplier = 1f;
    }

    [Serializable]
    public sealed class ElementDefinition
    {
        public int ElementId;
        public string SwordQiViewKey = "";
        public List<int> StatusEffectIds = new List<int>();
    }

    [Serializable]
    public sealed class StatusEffectDefinition
    {
        public int EffectId;
        public int EffectKind; // 1=Slow 2=Burn 3=Paralyze 4=Vulnerable
        public int StackGroupId;
        public float DurationSeconds;
        public float IntervalSeconds;
        public float DamagePerTick;
        public float SlowRatio;
        public float IncomingDamageMultiplier = 1f;
        public string ViewKey = "";
    }

    [Serializable]
    public sealed class AttackTimingDefinition
    {
        public int SkillId;
        public float WindupSeconds;
        public float ActiveSeconds;
        public float RecoverySeconds;
        public float ChainWindowSeconds;
        public float CooldownSeconds;
    }

    [Serializable]
    public sealed class ProjectileGeometryDefinition
    {
        public float Length = 12f;
        public float HalfWidth = 2f;
        public float Speed = 18f;
        public int Stages = 3;
        public float TotalFlightSeconds = 12f / 18f;
        public float SwordQiDamageCoefficient = 1f;
    }

    [Serializable]
    public sealed class PvpCombatSettings
    {
        public float PlayerMaxHp = 720f;
        public float WindVulnerabilityMultiplier = 1.28f;
        public float ControlImmunitySeconds = 0.5f;
        public float SameGroupVulnerabilityNoRefreshSeconds = 4f;
        public float ThunderDurationSeconds = 0.8f;
    }

    [Serializable]
    public sealed class DisplacementDefinition
    {
        public int SkillId;
        public float DashDistance;
        public float DashDuration;
        public float StartupSeconds = 0.05f;
        public float RecoverySeconds = 0.05f;
    }

    [Serializable]
    public sealed class JumpSettings
    {
        public float JumpForce = 10.95f;
        public float Gravity = -30f;
        public float MaxRiseSeconds = 0.5f;
    }
}
