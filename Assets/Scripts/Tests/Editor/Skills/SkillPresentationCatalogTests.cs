using System.Linq;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillPresentationCatalogTests
    {
        private static readonly int[] SyncedPlayerSkills =
            { 10001, 10002, 20001, 20002, 20003, 20004, 20006, 30001 };

        [Test]
        public void Catalog_Contains_All_Eight_Synced_Player_Skills()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(
                "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset");
            Assert.IsNotNull(catalog, "SkillPresentationCatalog.asset 未创建");

            foreach (var id in SyncedPlayerSkills)
            {
                Assert.IsTrue(catalog!.TryGet(id, out var data), "目录缺少技能 " + id);
                Assert.IsFalse(string.IsNullOrEmpty(data!.AnimatorTrigger), "技能 " + id + " 缺 AnimatorTrigger");
            }
            Assert.AreEqual(SyncedPlayerSkills.Length, catalog!.Skills.Count,
                "目录只应收录 8 个同步玩家技能（怪物技能不在客户端目录）");
        }

        [Test]
        public void EchoFireballAndChargeFireCatalogEntriesUseGeneratedTextures()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(
                "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset");
            Assert.IsTrue(catalog.TryGet(20006, out var echoEntry));
            var echo = (SwordQiEchoSkillData)echoEntry;
            Assert.AreEqual("SkillIconSwordQiEcho", echo.Icon.name);
            Assert.AreEqual("SwordQiEchoHead", echo.SwordVfx.GetComponentInChildren<MeshRenderer>()
                .sharedMaterial.mainTexture.name);
            Assert.AreEqual("SwordQiEchoTrail", echo.SwordVfx.GetComponentInChildren<TrailRenderer>()
                .sharedMaterial.mainTexture.name);
            Assert.AreEqual("SwordQiEchoContact", echo.ContactVfx.GetComponent<ParticleSystemRenderer>()
                .sharedMaterial.mainTexture.name);

            Assert.IsTrue(catalog.TryGet(20004, out var fireEntry));
            var fire = (ChargedSkillData)fireEntry;
            Assert.AreEqual("SkillIconChargeFire", fire.Icon.name);
            Assert.AreEqual(Cool.SyncCore.CombatRules.ChargeBlastRadius, fire.Shape.AreaRadius);
            Assert.AreEqual(Cool.SyncCore.CombatRules.ChargeBlastMaxScale, fire.GetAreaScaleForCharge(1f));
            Assert.AreEqual(Cool.SyncCore.CombatRules.ChargeBlastBasePushSpeed, fire.Effect.KnockbackForce);
            Assert.AreEqual("ChargeFireGatherRibbon", fire.ChargeVfx.GetComponent<ParticleSystemRenderer>()
                .sharedMaterial.mainTexture.name);

            var shock = fire.Presentation.ReleaseVFX.transform.Find("GroundRing")
                .GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual(ParticleSystemRenderMode.HorizontalBillboard, shock.renderMode);
            Assert.AreEqual("ChargeBlastShockRing", shock.sharedMaterial.mainTexture.name);

            Assert.IsTrue(catalog.TryGet(20003, out var fireballEntry));
            var release = ((ProjectileSkillData)fireballEntry).Presentation.ReleaseVFX.transform;
            var flame = release.Find("Flame").GetComponent<ParticleSystem>();
            Assert.IsTrue(flame.main.playOnAwake);
            var atlas = flame.textureSheetAnimation;
            Assert.AreEqual(16, atlas.spriteCount);
            Assert.AreEqual("ChargeFireBurstFlipbook_00", atlas.GetSprite(0).name);
            Assert.AreEqual("ChargeFireBurstFlipbook_15", atlas.GetSprite(15).name);
            var ring = release.Find("GroundRing").GetComponent<ParticleSystemRenderer>();
            Assert.IsTrue(ring.GetComponent<ParticleSystem>().main.playOnAwake);
            Assert.AreEqual(ParticleSystemRenderMode.HorizontalBillboard, ring.renderMode);
            Assert.AreEqual("ChargeFireGroundRing", ring.sharedMaterial.mainTexture.name);
            var embers = release.Find("Embers").GetComponent<ParticleSystem>();
            Assert.IsTrue(embers.main.playOnAwake);
            Assert.AreEqual(4, embers.textureSheetAnimation.spriteCount);
        }

        [Test]
        public void MeteorPrefabUsesWarningFlightAndImpactTextures()
        {
            var config = AssetDatabase.LoadAssetAtPath<MeteorConfig>(
                "Assets/PreRes/SkillsCfg/MeteorConfig_Fire.asset");
            var strike = config.Prefab.transform;
            Assert.AreEqual("MeteorWarningRing",
                strike.Find("WarnDecal").GetComponent<MeshRenderer>().sharedMaterial.mainTexture.name);
            Assert.AreEqual("MeteorFallBody",
                strike.Find("Falling/Body").GetComponent<ParticleSystemRenderer>().sharedMaterial.mainTexture.name);
            Assert.AreEqual("MeteorFallTrail",
                strike.Find("Falling/Wake").GetComponent<MeshRenderer>().sharedMaterial.mainTexture.name);
            var atlas = config.ImpactVFX.transform.Find("Impact").GetComponent<ParticleSystem>().textureSheetAnimation;
            Assert.AreEqual(16, atlas.spriteCount);
            Assert.AreEqual("MeteorImpactFlipbook_00", atlas.GetSprite(0).name);
            Assert.AreEqual("MeteorImpactFlipbook_15", atlas.GetSprite(15).name);
        }
    }
}
