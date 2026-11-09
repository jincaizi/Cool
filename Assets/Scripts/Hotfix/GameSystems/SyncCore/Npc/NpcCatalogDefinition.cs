using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    public enum NpcWindupArmorOverride : byte { Inherit = 0, Interruptible = 1, Armor = 2 }
    [Serializable]
    public sealed class NpcCatalogDefinition
    {
        public int SchemaVersion = 1;
        public string ContentHash = "";
        public List<NpcTemplateDefinition> Templates = new List<NpcTemplateDefinition>();
        public List<NpcSkillDefinition> Skills = new List<NpcSkillDefinition>();
        public List<NpcPresentationDefinition> Presentations = new List<NpcPresentationDefinition>();
    }

    [Serializable]
    public sealed class NpcTemplateDefinition
    {
        public int TemplateId;
        public string Name = "";
        public bool Placeholder;
        public string PrefabKey = "";
        public float Hp = 100;
        public float MoveSpeed = 2;
        public bool IsBoss;
        public NpcCollisionDefinition Collision = new NpcCollisionDefinition();
        public NpcAiDefinition Ai = new NpcAiDefinition();
        public List<NpcSkillBinding> Skills = new List<NpcSkillBinding>();
    }

    [Serializable]
    public sealed class NpcCollisionDefinition
    {
        public float CenterX, CenterY = 0.5f, CenterZ;
        public float SizeX = 1, SizeY = 1, SizeZ = 1;
        public float BodyRadius = 0.5f, NavRadius = 0.5f;
        public string MeasurementBasis = "NetworkEntityRoot unit scale; candidate requires calibration";
        public bool Approved;
    }

    [Serializable]
    public sealed class NpcAiDefinition
    {
        public bool DefaultWindupArmor;
        public float DetectionRadius = 8, VisionAngle = 120, AttackRange = 1.5f;
        public float PatrolRadius = 5, LeashRadius = 23;
        public uint ThinkIntervalTicks = 3, HitReactionTicks = 5, HitRepeatWindowTicks = 8;
        public uint NoTargetTicks = 45, PerceptionGraceTicks = 30, EvadeTimeoutTicks = 300;
        public uint DeathDisplayTicks = 30, RespawnTicks = 450, NavigationStuckTicks = 90;
        public float HomeArrivalDistance = 0.5f;
    }

    [Serializable]
    public sealed class NpcSkillBinding
    {
        public int SkillId, Priority;
        public float MinRange, PreferredMinRange, PreferredMaxRange, MaxRange;
        public float MinHpRatio, MaxHpRatio = 1;
    }

    [Serializable]
    public sealed class NpcSkillDefinition
    {
        public int SkillId, ShapeType;
        public float MaxRange, AreaRadius, Angle;
        public float BaseDamage, CriticalRate;
        public uint CooldownTicks = 15, WindupTicks = 6, RecoveryTicks = 5;
        public List<uint> ActiveOffsets = new List<uint> { 0 };
        public NpcWindupArmorOverride ArmorOverride;
        public bool RequiresTarget = true;
        public bool CanTurnDuringWindup, CanMoveDuringWindup, CanTurnDuringActive, CanMoveDuringActive;
        public int PresentationId, PresentationVariant;
    }

    [Serializable]
    public sealed class NpcPresentationDefinition
    {
        public int PresentationId, SemanticVersion = 1, AnimatorLayer, Variant;
        public string StatePath = "Base Layer.Attack01";
        public float WindupStart, WindupEnd = 0.42f;
        public float ActiveStart = 0.42f, ActiveEnd = 0.5f;
        public float RecoveryStart = 0.5f, RecoveryEnd = 1;
    }
}
