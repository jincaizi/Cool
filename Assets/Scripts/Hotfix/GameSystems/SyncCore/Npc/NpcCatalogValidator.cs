using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    public static class NpcCatalogValidator
    {
        public static List<string> Validate(NpcCatalogDefinition catalog)
        {
            var errors = new List<string>();
            if (catalog == null) { errors.Add("catalog missing"); return errors; }
            if (catalog.SchemaVersion != 1) errors.Add("unsupported SchemaVersion");
            if (catalog.Templates == null || catalog.Skills == null || catalog.Presentations == null)
            { errors.Add("catalog lists missing"); return errors; }
            var skills = new Dictionary<int, NpcSkillDefinition>();
            var presentations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in catalog.Presentations)
            {
                if (p == null) { errors.Add("presentation missing"); continue; }
                if (p.PresentationId <= 0 || !presentations.Add(Key(p.PresentationId, p.Variant))) errors.Add("duplicate/invalid presentation id");
                if (p.SemanticVersion < 1 || p.AnimatorLayer < 0 || p.Variant < 0 || string.IsNullOrWhiteSpace(p.StatePath)) errors.Add("invalid presentation state");
                if (!Mapping(p.WindupStart, p.WindupEnd) || !Mapping(p.ActiveStart, p.ActiveEnd)
                    || !Mapping(p.RecoveryStart, p.RecoveryEnd)) errors.Add("invalid presentation mapping");
            }
            foreach (var s in catalog.Skills)
            {
                if (s == null) { errors.Add("skill missing"); continue; }
                string label = "skill " + s.SkillId;
                if (s.SkillId <= 0 || skills.ContainsKey(s.SkillId)) errors.Add(label + " duplicate/invalid id");
                else skills.Add(s.SkillId, s);
                if (s.ShapeType < 0 || s.ShapeType > 3 || !Nonnegative(s.MaxRange, s.AreaRadius, s.Angle, s.BaseDamage)
                    || s.Angle > 360 || !Ratio(s.CriticalRate)) errors.Add(label + " invalid geometry/damage");
                if (!Duration(s.WindupTicks) || !Duration(s.RecoveryTicks) || s.CooldownTicks >= 0x80000000u) errors.Add(label + " invalid duration");
                if (s.ActiveOffsets == null || s.ActiveOffsets.Count == 0) errors.Add(label + " missing active offsets");
                else
                {
                    if (s.ActiveOffsets[0] != 0) errors.Add(label + " first offset must be zero");
                    for (int i = 1; i < s.ActiveOffsets.Count; i++)
                        if (s.ActiveOffsets[i] <= s.ActiveOffsets[i - 1]) errors.Add(label + " offsets not strictly increasing");
                    ulong total = (ulong)s.WindupTicks + s.ActiveOffsets[s.ActiveOffsets.Count - 1] + 1 + s.RecoveryTicks;
                    if (total >= 0x80000000ul) errors.Add(label + " total window too large");
                }
                if ((byte)s.ArmorOverride > 2) errors.Add(label + " invalid armor override");
                if (!presentations.Contains(Key(s.PresentationId, s.PresentationVariant))) errors.Add(label + " unknown presentation/variant");
            }
            var templates = new HashSet<int>();
            foreach (var t in catalog.Templates)
            {
                if (t == null) { errors.Add("template missing"); continue; }
                string label = "template " + t.TemplateId;
                if (t.TemplateId <= 0 || !templates.Add(t.TemplateId)) errors.Add(label + " duplicate/invalid id");
                if (string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.PrefabKey)
                    || !Positive(t.Hp) || !Nonnegative(t.MoveSpeed)) errors.Add(label + " invalid identity/stats");
                var c = t.Collision;
                if (c == null || !Finite(c.CenterX, c.CenterY, c.CenterZ) || !Positive(c.SizeX) || !Positive(c.SizeY)
                    || !Positive(c.SizeZ) || !Positive(c.BodyRadius) || !Positive(c.NavRadius) || string.IsNullOrWhiteSpace(c.MeasurementBasis)) errors.Add(label + " invalid collision");
                var a = t.Ai;
                if (a == null || !Nonnegative(a.DetectionRadius, a.VisionAngle, a.AttackRange, a.PatrolRadius, a.LeashRadius, a.HomeArrivalDistance)
                    || a.VisionAngle > 360 || !Duration(a.ThinkIntervalTicks) || !Duration(a.HitReactionTicks)
                    || !Duration(a.HitRepeatWindowTicks) || !Duration(a.NoTargetTicks) || !Duration(a.PerceptionGraceTicks)
                    || !Duration(a.EvadeTimeoutTicks) || !Duration(a.DeathDisplayTicks) || !Duration(a.RespawnTicks)
                    || !Duration(a.NavigationStuckTicks)) errors.Add(label + " invalid ai");
                if (t.Skills == null || t.Skills.Count == 0) { errors.Add(label + " missing skills"); continue; }
                var bindings = new HashSet<int>();
                foreach (var b in t.Skills)
                {
                    if (b == null) { errors.Add(label + " missing binding"); continue; }
                    if (!bindings.Add(b.SkillId) || !skills.ContainsKey(b.SkillId)) errors.Add(label + " duplicate/unknown skill");
                    if (!Nonnegative(b.MinRange, b.PreferredMinRange, b.PreferredMaxRange, b.MaxRange)
                        || b.MinRange > b.PreferredMinRange || b.PreferredMinRange > b.PreferredMaxRange || b.PreferredMaxRange > b.MaxRange
                        || !Ratio(b.MinHpRatio) || !Ratio(b.MaxHpRatio) || b.MinHpRatio > b.MaxHpRatio) errors.Add(label + " invalid skill conditions");
                    if (skills.TryGetValue(b.SkillId, out var definition)
                        && b.MaxRange > Math.Max(definition.MaxRange, definition.AreaRadius)) errors.Add(label + " range exceeds geometry");
                }
            }
            return errors;
        }

        private static string Key(int id, int variant) => id + ":" + variant;
        private static bool Duration(uint value) => value > 0 && value < 0x80000000u;
        private static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;
        private static bool Ratio(float value) => !float.IsNaN(value) && value >= 0 && value <= 1;
        private static bool Mapping(float start, float end) => Ratio(start) && Ratio(end) && start < end;
        private static bool Finite(params float[] values)
        { foreach (float v in values) if (float.IsNaN(v) || float.IsInfinity(v)) return false; return true; }
        private static bool Nonnegative(params float[] values)
        { foreach (float v in values) if (float.IsNaN(v) || float.IsInfinity(v) || v < 0) return false; return true; }
    }
}
