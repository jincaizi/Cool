using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillDataMeteorTests
    {
        [Test]
        public void test_skill_data_meteor_defaults_to_null()
        {
            // Arrange
            var data = ScriptableObject.CreateInstance<InstantSkillData>();

            // Assert（默认不发射陨石，与 SwordQi 同款语义）
            Assert.IsNull(data.Meteor);
        }

        [Test]
        public void test_skill_data_meteor_returns_assigned_config()
        {
            // Arrange
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_meteor", cfg);

            // Act & Assert
            Assert.AreSame(cfg, data.Meteor);
        }

        [Test]
        public void test_meteor_config_defaults()
        {
            // Arrange
            var cfg = ScriptableObject.CreateInstance<MeteorConfig>();

            // Assert（数值草稿默认值）
            Assert.AreEqual(0.8f, cfg.WarnDuration, 0.001f);
            Assert.AreEqual(4f, cfg.ExplosionRadius, 0.001f);
            Assert.AreEqual(1f, cfg.DamagePercent, 0.001f);
        }
    }
}
