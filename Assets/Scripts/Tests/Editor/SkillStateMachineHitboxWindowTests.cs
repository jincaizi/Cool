using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// P6 ②：判定窗口触发次数必须与帧率无关。
    /// 原实现窗口内每帧触发，下游靠 consecutiveRequired=3 抵消——60fps 结算 1 次、
    /// 120fps 结算 2 次（伤害翻倍）。
    /// </summary>
    public class SkillStateMachineHitboxWindowTests
    {
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        /// <summary>一个判定窗口落在 0.3s 的连击技能（须落在下方采样区间 0.15s→0.45s 之内）</summary>
        private static ComboSkillData CreateComboWithSingleWindow()
        {
            var shape = new ShapeBlock();
            TestHelpers.SetField(shape, "_hitboxTimings", new[] { 0.3f });

            var data = ScriptableObject.CreateInstance<ComboSkillData>();
            TestHelpers.SetField(data, "_shape", shape);
            return data;
        }

        /// <summary>以给定帧间隔推进 0.3s，返回判定帧触发次数</summary>
        private static int CountFires(float deltaTime)
        {
            var machine = new FakeTimeMachine(CreateComboWithSingleWindow());
            int fires = 0;
            machine.OnHitboxFrame += _ => fires++;

            machine.TryStart();       // Combo 无 Casting：TryStart 直接进 Execution
            machine.Now = 0.15f;      // 采样起点：0.3s 判定窗口落在随后的推进区间内
            machine.Update(deltaTime);

            float elapsed = 0f;
            while (elapsed < 0.3f)
            {
                machine.Now += deltaTime;
                machine.Update(deltaTime);
                elapsed += deltaTime;
            }
            return fires;
        }

        [Test]
        public void test_single_window_fires_exactly_once_at_60fps()
        {
            Assert.AreEqual(1, CountFires(1f / 60f), "一个判定窗口只应触发一次");
        }

        [Test]
        public void test_single_window_fires_exactly_once_at_120fps()
        {
            Assert.AreEqual(1, CountFires(1f / 120f),
                "窗口内每帧触发会让 120fps 结算两次——伤害翻倍");
        }

        [Test]
        public void test_fire_count_is_frame_rate_independent()
        {
            // Arrange & Act
            int at60 = CountFires(1f / 60f);
            int at120 = CountFires(1f / 120f);
            int at30 = CountFires(1f / 30f);

            // Assert
            Assert.AreEqual(at60, at120, "60fps 与 120fps 的判定次数必须相同");
            Assert.AreEqual(at60, at30, "60fps 与 30fps 的判定次数必须相同");
        }
    }
}
