using Cool.SyncCore;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    public class FireballAssetTableTests
    {
        private const string Path = "Assets/PreRes/SkillsCfg/Fireball_SkillQ.asset";

        [Test]
        public void test_fireball_asset_matches_server_projectile_table()
        {
            var asset = AssetDatabase.LoadAssetAtPath<ProjectileSkillData>(Path);
            Assert.IsNotNull(asset);
            Assert.IsTrue(asset.HoldToAim);
            Assert.AreEqual(2f, asset.MinAimDistance, 0.001f);
            Assert.IsFalse(asset.CanMoveWhileCharging);

            Assert.IsTrue(CombatRules.TryGet(20003, out var entry));
            Assert.IsNotNull(entry.Projectile);
            Assert.AreEqual(ProjectileKind.CircleAtPoint, entry.Projectile.Kind);
            Assert.AreEqual(asset.ExplosionRadius, entry.Projectile.Radius, 0.001f);
            Assert.AreEqual(asset.ProjectileRange, entry.Projectile.MaxImpactDistance, 0.001f);
            Assert.AreEqual(asset.ProjectileSpeed, entry.Projectile.Speed, 0.001f);
        }
    }
}
