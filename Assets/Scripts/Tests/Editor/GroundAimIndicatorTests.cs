using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GameSys.EditorTests
{
    public class GroundAimIndicatorTests
    {
        private const string PrefabPath = "Assets/PreRes/Skills/FireballAimIndicator.prefab";
        private const string FireballPath = "Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset";

        [Test]
        public void test_indicator_prefab_has_no_collider_and_no_shadow_cast()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "占位地面圈 prefab 缺失");
            Assert.IsNull(prefab.GetComponentInChildren<Collider>(), "地面圈不得带 Collider");
            Assert.IsNotNull(prefab.GetComponent<GroundAimIndicator>(), "缺少 GroundAimIndicator");
            var renderer = prefab.GetComponentInChildren<Renderer>();
            Assert.IsNotNull(renderer);
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
        }

        [Test]
        public void test_fireball_asset_references_aim_indicator_prefab()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(FireballPath);
            Assert.IsNotNull(asset);
            Assert.IsNotNull(asset.AimIndicatorPrefab, "火球资产必须引用占位地面圈");
        }
    }
}
