using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    public class StaminaSkillAssetTests
    {
        private const string Dir = "Assets/PreRes/SkillsCfg/";

        private static T Load<T>(string name) where T : SkillData
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(Dir + name + ".asset");
            Assert.IsNotNull(asset, name + " 未找到");
            return asset;
        }

        [Test]
        public void test_skill_assets_stamina_drain_values()
        {
            Assert.AreEqual(20f, Load<SpinSkillData>("Spin_SkillR").StaminaDrainPerSecond, 0.001f);
            Assert.AreEqual(15f, Load<ChargedSkillData>("ChargeFire_SkillR").StaminaDrainPerSecond, 0.001f);
            Assert.AreEqual(15f, Load<ProjectileSkillData>("Fireball_SkillQ").StaminaDrainPerSecond, 0.001f);
            Assert.AreEqual(0f, Load<InstantSkillData>("Instant_SkillQ").StaminaDrainPerSecond, 0.001f);
            Assert.AreEqual(0f, Load<InstantSkillData>("Meteor_Ultimate").StaminaDrainPerSecond, 0.001f);
        }
    }
}
