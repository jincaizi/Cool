using System;
using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>共享目录的纯函数校验：服务端构建、客户端加载与测试共用。</summary>
    public static class ElementalCatalogValidator
    {
        public static List<string> Validate(ElementalCatalogDefinition catalog)
        {
            var errors = new List<string>();
            if (catalog == null) { errors.Add("catalog missing"); return errors; }
            if (catalog.SchemaVersion != 1) errors.Add("unsupported SchemaVersion");

            var weaponIds = new HashSet<int>();
            foreach (var w in catalog.Weapons)
            {
                if (w == null) { errors.Add("weapon missing"); continue; }
                if (w.TemplateId <= 0 || !weaponIds.Add(w.TemplateId))
                    errors.Add("weapon duplicate/invalid id");
                if (string.IsNullOrWhiteSpace(w.WeaponViewKey))
                    errors.Add("weapon view key missing");
                if (!Positive(w.BaseAttack) || !InRange(w.AttackSpeedMultiplier, 0.25f, 4f)
                    || !Positive(w.SwordQiDamageMultiplier))
                    errors.Add("weapon stats out of range");
            }

            if (!weaponIds.Contains(catalog.DefaultWeaponTemplateId))
                errors.Add("default weapon missing");

            var startingIds = new HashSet<int>();
            foreach (int templateId in catalog.StartingWeaponTemplateIds)
            {
                if (!startingIds.Add(templateId))
                    errors.Add("starting weapon duplicate");
                if (!weaponIds.Contains(templateId))
                    errors.Add("starting weapon unresolved");
            }
            if (!startingIds.Contains(catalog.DefaultWeaponTemplateId))
                errors.Add("default weapon not in starting list");

            var elementIds = new HashSet<int>();
            foreach (var e in catalog.Elements)
            {
                if (e == null) { errors.Add("element missing"); continue; }
                if (e.ElementId <= 0 || !elementIds.Add(e.ElementId))
                    errors.Add("element duplicate/invalid id");
                if (string.IsNullOrWhiteSpace(e.SwordQiViewKey))
                    errors.Add("element view key missing");
            }

            var effectIds = new HashSet<int>();
            foreach (var s in catalog.Statuses)
            {
                if (s == null) { errors.Add("status missing"); continue; }
                if (s.EffectId <= 0 || !effectIds.Add(s.EffectId))
                    errors.Add("status duplicate/invalid id");
                if (s.EffectKind < 1 || s.EffectKind > 4)
                    errors.Add("status kind out of range");
                if (s.StackGroupId <= 0) errors.Add("status group invalid");
                if (!Positive(s.DurationSeconds) || s.DurationSeconds > 60f)
                    errors.Add("status duration out of range");
                if (s.EffectKind == 2 && (!Positive(s.IntervalSeconds) || s.IntervalSeconds > s.DurationSeconds))
                    errors.Add("status interval out of range");
                if (s.EffectKind == 1 && !InRange(s.SlowRatio, 0.01f, 0.99f))
                    errors.Add("status slow ratio out of range");
                if (s.EffectKind == 4 && (!Positive(s.IncomingDamageMultiplier) || s.IncomingDamageMultiplier < 1f))
                    errors.Add("status vulnerability out of range");
            }

            foreach (var e in catalog.Elements)
            {
                if (e == null) continue;
                foreach (int effectId in e.StatusEffectIds)
                    if (!effectIds.Contains(effectId)) errors.Add("element status unresolved");
            }

            foreach (var w in catalog.Weapons)
            {
                if (w == null || w.ElementId == 0) continue;
                if (!elementIds.Contains(w.ElementId)) errors.Add("weapon element unresolved");
            }

            var timingIds = new HashSet<int>();
            foreach (var t in catalog.AttackTimings)
            {
                if (t == null) { errors.Add("timing missing"); continue; }
                if (t.SkillId <= 0 || !timingIds.Add(t.SkillId))
                    errors.Add("timing duplicate/invalid id");
                if (!Nonnegative(t.WindupSeconds, t.ActiveSeconds, t.RecoverySeconds, t.ChainWindowSeconds, t.CooldownSeconds))
                    errors.Add("timing out of range");
            }

            var g = catalog.SwordQi;
            if (g == null || !Positive(g.Length) || !Positive(g.Speed) || g.Stages < 1)
                errors.Add("sword qi geometry out of range");
            else if (!Positive(g.HalfWidth) || !Positive(g.SwordQiDamageCoefficient)
                || !Positive(g.TotalFlightSeconds)
                || Math.Abs(g.Length / g.Speed - g.TotalFlightSeconds) > 0.001f)
                errors.Add("sword qi flight mismatch");

            var displacementIds = new HashSet<int>();
            foreach (var d in catalog.Displacements)
            {
                if (d == null) { errors.Add("displacement missing"); continue; }
                if (d.SkillId <= 0 || !displacementIds.Add(d.SkillId))
                    errors.Add("displacement duplicate/invalid id");
                if (!Positive(d.DashDistance) || !Positive(d.DashDuration)
                    || !Nonnegative(d.StartupSeconds, d.RecoverySeconds)
                    || d.DashDuration <= d.StartupSeconds + d.RecoverySeconds)
                {
                    errors.Add("displacement timing out of range");
                    continue;
                }
                float active = d.DashDuration - d.StartupSeconds - d.RecoverySeconds;
                float speed = d.DashDistance / active;
                if (!Positive(speed) || speed > 60f)
                    errors.Add("displacement speed out of range");
            }

            var jump = catalog.Jump;
            if (jump == null || !Positive(jump.JumpForce) || !(jump.Gravity < 0f)
                || !Positive(jump.MaxRiseSeconds)
                || jump.MaxRiseSeconds < jump.JumpForce / -jump.Gravity)
                errors.Add("jump settings out of range");

            var p = catalog.Pvp;
            if (p == null || !Positive(p.PlayerMaxHp) || !Positive(p.WindVulnerabilityMultiplier)
                || !Positive(p.ControlImmunitySeconds) || !Positive(p.SameGroupVulnerabilityNoRefreshSeconds)
                || !Positive(p.ThunderDurationSeconds))
                errors.Add("pvp settings out of range");

            return errors;
        }

        private static bool Positive(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && v > 0f;
        private static bool Nonnegative(params float[] values)
        {
            foreach (float v in values)
                if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return false;
            return true;
        }
        private static bool InRange(float v, float min, float max)
            => !float.IsNaN(v) && !float.IsInfinity(v) && v >= min && v <= max;
    }
}
