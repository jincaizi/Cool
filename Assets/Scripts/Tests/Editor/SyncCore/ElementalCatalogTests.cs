using Cool.SyncCore;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;

namespace Hotfix.GameSystems.Tests.SyncCore
{
    public class ElementalCatalogTests
    {
        [Test]
        public void Default_Catalog_Has_Default_And_Elemental_Weapons()
        {
            var catalog = ElementalCatalogDefaults.Create();
            Assert.IsEmpty(ElementalCatalogValidator.Validate(catalog));
            Assert.IsNotNull(catalog.Weapons.Find(w => w.TemplateId == ElementalCatalogDefaults.DefaultSwordTemplateId));
            Assert.IsNotNull(catalog.Weapons.Find(w => w.TemplateId == ElementalCatalogDefaults.IceSwordTemplateId));
        }

        [Test]
        public void SwordQi_Length_Over_Speed_Matches_Flight_Time()
        {
            var g = ElementalCatalogDefaults.Create().SwordQi;
            Assert.AreEqual(g.Length / g.Speed, g.TotalFlightSeconds, 0.001f);
        }

        [Test]
        public void Heavy_SwordQi_Asset_Matches_Shared_Defaults()
        {
            var config = AssetDatabase.LoadAssetAtPath<SwordQiConfig>(
                "Assets/PreRes/SkillsCfg/SwordQi_Heavy.asset");
            Assert.IsNotNull(config);

            var g = ElementalCatalogDefaults.Create().SwordQi;
            Assert.AreEqual(g.Length, config.Range, 0.001f);
            Assert.AreEqual(g.Speed, config.Speed, 0.001f);
            Assert.AreEqual(g.HalfWidth, config.DetectRadius, 0.001f);
            Assert.AreEqual(g.SwordQiDamageCoefficient, config.DamagePercent, 0.001f);
        }

        [Test]
        public void CombatRules_10002_Uses_Shared_Default_Speed()
        {
            Assert.IsTrue(CombatRules.TryGet(10002, out var entry));
            var spec = entry.Projectile;
            Assert.IsNotNull(spec);
            var g = ElementalCatalogDefaults.Create().SwordQi;
            Assert.AreEqual(g.Speed, spec.Speed, 0.001f);
            Assert.AreEqual(g.Length / g.Speed, spec.TotalFlightSeconds, 0.001f);
        }
    }
}
