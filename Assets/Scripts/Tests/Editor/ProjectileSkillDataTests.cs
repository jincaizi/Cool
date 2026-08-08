using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEngine;
using Definition = Hotfix.GameSystems.Skills.Definition;

namespace GameSys.EditorTests
{
    public class ProjectileSkillDataTests
    {
        [Test]
        public void test_projectile_skill_data_onvalidate_forces_type_projectile()
        {
            // Arrange（模拟 Inspector 里被改错类型）
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            TestHelpers.SetField(data, "_skillType", Definition.SkillType.Combo);

            // Act（OnValidate 为私有方法，反射调用）
            var method = typeof(ProjectileSkillData).GetMethod("OnValidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(method, "OnValidate 必须存在");
            method.Invoke(data, null);

            // Assert（与 Combo/Charged/Channeled 同款约束：类型强制正确）
            Assert.AreEqual(Definition.SkillType.Projectile, data.SkillType);
        }

        [Test]
        public void test_projectile_skill_data_defaults()
        {
            // Arrange
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();

            // Assert（默认值：胸前发射位、命中爆炸 1.5m、射程 15m）
            Assert.AreEqual(1.5f, data.ExplosionRadius, 0.001f);
            Assert.AreEqual(new Vector3(0f, 1.2f, 0.5f), data.SpawnOffset);
            Assert.AreEqual(15f, data.ProjectileRange, 0.001f);
        }
    }
}
