using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Cool.SyncCore
{
    /// <summary>属性剑气目录指纹：稳定排序 + 版本化 little-endian 语义流；排除 ContentHash 自身。</summary>
    public static class ElementalCatalogHasher
    {
        public static string Compute(ElementalCatalogDefinition catalog)
        {
            var errors = ElementalCatalogValidator.Validate(catalog);
            if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors), nameof(catalog));

            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write("Cool.ElementalCatalog.Semantic.v1");
                w.Write(catalog.SchemaVersion);
                w.Write(catalog.DefaultWeaponTemplateId);

                var starting = new List<int>(catalog.StartingWeaponTemplateIds);
                starting.Sort();
                w.Write(starting.Count);
                foreach (int id in starting) w.Write(id);

                var weapons = new List<WeaponDefinition>(catalog.Weapons);
                weapons.Sort((a, b) => a.TemplateId.CompareTo(b.TemplateId));
                w.Write(weapons.Count);
                foreach (var weapon in weapons)
                {
                    w.Write(weapon.TemplateId); w.Write(weapon.WeaponViewKey); w.Write(weapon.ElementId);
                    F(w, weapon.BaseAttack, weapon.AttackSpeedMultiplier, weapon.SwordQiDamageMultiplier);
                }

                var elements = new List<ElementDefinition>(catalog.Elements);
                elements.Sort((a, b) => a.ElementId.CompareTo(b.ElementId));
                w.Write(elements.Count);
                foreach (var element in elements)
                {
                    w.Write(element.ElementId); w.Write(element.SwordQiViewKey);
                    var ids = new List<int>(element.StatusEffectIds); ids.Sort();
                    w.Write(ids.Count); foreach (int id in ids) w.Write(id);
                }

                var statuses = new List<StatusEffectDefinition>(catalog.Statuses);
                statuses.Sort((a, b) => a.EffectId.CompareTo(b.EffectId));
                w.Write(statuses.Count);
                foreach (var status in statuses)
                {
                    w.Write(status.EffectId); w.Write(status.EffectKind); w.Write(status.StackGroupId);
                    F(w, status.DurationSeconds, status.IntervalSeconds, status.DamagePerTick,
                        status.SlowRatio, status.IncomingDamageMultiplier);
                    w.Write(status.ViewKey);
                }

                var timings = new List<AttackTimingDefinition>(catalog.AttackTimings);
                timings.Sort((a, b) => a.SkillId.CompareTo(b.SkillId));
                w.Write(timings.Count);
                foreach (var timing in timings)
                {
                    w.Write(timing.SkillId);
                    F(w, timing.WindupSeconds, timing.ActiveSeconds, timing.RecoverySeconds,
                        timing.ChainWindowSeconds, timing.CooldownSeconds);
                }

                var g = catalog.SwordQi;
                F(w, g.Length, g.HalfWidth, g.Speed);
                w.Write(g.Stages);
                F(w, g.TotalFlightSeconds, g.SwordQiDamageCoefficient);

                var p = catalog.Pvp;
                F(w, p.PlayerMaxHp, p.WindVulnerabilityMultiplier, p.ControlImmunitySeconds,
                    p.SameGroupVulnerabilityNoRefreshSeconds, p.ThunderDurationSeconds);

                w.Flush();
                using (var sha = SHA256.Create())
                {
                    byte[] bytes = sha.ComputeHash(stream.ToArray());
                    var result = new StringBuilder(64);
                    foreach (byte b in bytes)
                        result.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                    return result.ToString();
                }
            }
        }

        // +0 与 -0 语义相同；BinaryWriter 固定 little-endian。
        private static void F(BinaryWriter w, params float[] values)
        {
            foreach (float v in values) w.Write(v == 0 ? 0f : v);
        }
    }
}
