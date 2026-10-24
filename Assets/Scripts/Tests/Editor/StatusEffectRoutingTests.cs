using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.EditorTests
{
    public class StatusEffectRoutingTests
    {
        [SetUp]
        public void SetUp()
        {
            // 一次性告警标记是静态的，跨用例/跨运行会残留，这里重置以保证可重复
            typeof(SkillExecutor)
                .GetField("_warnedMissingStatusManager", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, false);
        }

        private static void ApplyEffects(SkillExecutor executor, IEffectTarget target)
        {
            var method = typeof(SkillExecutor).GetMethod(
                "ApplyEffects", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "SkillExecutor.ApplyEffects 不存在");
            method.Invoke(executor, new object[] { target });
        }

        [Test]
        public void test_duration_effect_is_skipped_when_manager_missing()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            var block = new EffectBlock();
            var effect = new TestHelpers.CountingEffect(duration: 5f);
            TestHelpers.SetField(block, "_applyEffects", new EffectData[] { effect });
            TestHelpers.SetField(data, "_effect", block);

            var owner = new TestHelpers.FakeTarget(null);
            var executor = new SkillExecutor(owner, data);

            LogAssert.Expect(LogType.Error, "[SkillExecutor] StatusEffectManager 未挂载，带持续时间的效果已跳过");
            ApplyEffects(executor, owner);

            Assert.AreEqual(0, effect.ApplyCount);

            Object.DestroyImmediate(data);
        }

        [Test]
        public void test_instant_effect_is_applied_without_manager()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            var block = new EffectBlock();
            var effect = new TestHelpers.CountingEffect(duration: 0f);
            TestHelpers.SetField(block, "_applyEffects", new EffectData[] { effect });
            TestHelpers.SetField(data, "_effect", block);

            var owner = new TestHelpers.FakeTarget(null);
            var executor = new SkillExecutor(owner, data);

            ApplyEffects(executor, owner);

            Assert.AreEqual(1, effect.ApplyCount);

            Object.DestroyImmediate(data);
        }
    }
}
