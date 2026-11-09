using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Cool.SyncCore
{
    /// <summary>Versioned canonical little-endian UTF8 stream. No platform binaries, paths or export timestamps.</summary>
    public static class NpcCatalogHasher
    {
        public static string Compute(NpcCatalogDefinition catalog)
        {
            var errors = NpcCatalogValidator.Validate(catalog);
            if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors), nameof(catalog));
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write("Cool.NpcCatalog.Semantic.v1"); w.Write(catalog.SchemaVersion);
                var templates = new List<NpcTemplateDefinition>(catalog.Templates);
                templates.Sort((a, b) => a.TemplateId.CompareTo(b.TemplateId)); w.Write(templates.Count);
                foreach (var t in templates)
                {
                    w.Write(t.TemplateId); w.Write(t.Name); w.Write(t.Placeholder); w.Write(t.PrefabKey);
                    F(w, t.Hp, t.MoveSpeed); w.Write(t.IsBoss);
                    var c = t.Collision;
                    F(w, c.CenterX, c.CenterY, c.CenterZ, c.SizeX, c.SizeY, c.SizeZ, c.BodyRadius, c.NavRadius);
                    w.Write(c.MeasurementBasis); w.Write(c.Approved);
                    var a = t.Ai; w.Write(a.DefaultWindupArmor);
                    F(w, a.DetectionRadius, a.VisionAngle, a.AttackRange, a.PatrolRadius, a.LeashRadius);
                    w.Write(a.ThinkIntervalTicks); w.Write(a.HitReactionTicks); w.Write(a.HitRepeatWindowTicks);
                    w.Write(a.NoTargetTicks); w.Write(a.PerceptionGraceTicks); w.Write(a.EvadeTimeoutTicks);
                    w.Write(a.DeathDisplayTicks); w.Write(a.RespawnTicks); w.Write(a.NavigationStuckTicks); F(w, a.HomeArrivalDistance);
                    var bindings = new List<NpcSkillBinding>(t.Skills); bindings.Sort((x, y) => x.SkillId.CompareTo(y.SkillId));
                    w.Write(bindings.Count);
                    foreach (var b in bindings) { w.Write(b.SkillId); w.Write(b.Priority);
                        F(w, b.MinRange, b.PreferredMinRange, b.PreferredMaxRange, b.MaxRange, b.MinHpRatio, b.MaxHpRatio); }
                }
                var skills = new List<NpcSkillDefinition>(catalog.Skills); skills.Sort((a, b) => a.SkillId.CompareTo(b.SkillId));
                w.Write(skills.Count);
                foreach (var s in skills)
                {
                    w.Write(s.SkillId); w.Write(s.ShapeType); F(w, s.MaxRange, s.AreaRadius, s.Angle, s.BaseDamage, s.CriticalRate);
                    w.Write(s.CooldownTicks); w.Write(s.WindupTicks); w.Write(s.RecoveryTicks);
                    w.Write(s.ActiveOffsets.Count); foreach (var offset in s.ActiveOffsets) w.Write(offset);
                    w.Write((byte)s.ArmorOverride); w.Write(s.RequiresTarget);
                    w.Write(s.CanTurnDuringWindup); w.Write(s.CanMoveDuringWindup); w.Write(s.CanTurnDuringActive); w.Write(s.CanMoveDuringActive);
                    w.Write(s.PresentationId); w.Write(s.PresentationVariant);
                }
                var presentations = new List<NpcPresentationDefinition>(catalog.Presentations);
                presentations.Sort((a, b) => a.PresentationId != b.PresentationId ? a.PresentationId.CompareTo(b.PresentationId) : a.Variant.CompareTo(b.Variant));
                w.Write(presentations.Count);
                foreach (var p in presentations) { w.Write(p.PresentationId); w.Write(p.SemanticVersion); w.Write(p.AnimatorLayer);
                    w.Write(p.Variant); w.Write(p.StatePath); F(w, p.WindupStart, p.WindupEnd, p.ActiveStart, p.ActiveEnd, p.RecoveryStart, p.RecoveryEnd); }
                w.Flush();
                using (var sha = SHA256.Create())
                {
                    var bytes = sha.ComputeHash(stream.ToArray()); var result = new StringBuilder(64);
                    foreach (byte b in bytes) result.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    return result.ToString();
                }
            }
        }

        // +0 and -0 are semantically identical. BinaryWriter specifies little-endian primitive encoding.
        private static void F(BinaryWriter w, params float[] values)
        { foreach (float v in values) w.Write(v == 0 ? 0f : v); }
    }
}
