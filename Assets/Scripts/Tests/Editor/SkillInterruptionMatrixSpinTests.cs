using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillInterruptionMatrixSpinTests
    {
        private static SpinSkillData CreateSpinData()
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_skillId", 20002);
            return data;
        }

        [Test]
        public void SpinSkill_Stun_IsInterruptible()
        {
            var matrix = new SkillInterruptionMatrix();
            Assert.IsTrue(matrix.CanBeInterrupted(CreateSpinData(), InterruptionSource.Stun), "眩晕/死亡可打断旋转");
        }

        [Test]
        public void SpinSkill_DamageTaken_IsNotInterruptible()
        {
            var matrix = new SkillInterruptionMatrix();
            Assert.IsFalse(matrix.CanBeInterrupted(CreateSpinData(), InterruptionSource.DamageTaken), "受击不打断（霸体）");
        }

        [Test]
        public void SpinSkill_AnotherSkill_IsNotInterruptible()
        {
            var matrix = new SkillInterruptionMatrix();
            Assert.IsFalse(matrix.CanBeInterrupted(CreateSpinData(), InterruptionSource.AnotherSkill), "旋转期间不可被其他技能打断");
        }
    }
}
