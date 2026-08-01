using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillStateMachineSpinTests
    {
        /// <summary>
        /// 假时间状态机：override GetCurrentTime() 注入可控时钟
        /// </summary>
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        private readonly System.Collections.Generic.List<int> _ticks = new System.Collections.Generic.List<int>();
        private int _completedCount;

        private SpinSkillData CreateSpinData(float min, float max, float tick, AnimationClip startClip = null)
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_minDuration", min);
            TestHelpers.SetField(data, "_maxDuration", max);
            TestHelpers.SetField(data, "_tickInterval", tick);
            TestHelpers.SetField(data, "_castClip", startClip);
            return data;
        }

        private static AnimationClip CreateClip(float length)
        {
            var clip = new AnimationClip();
            clip.SetCurve("", typeof(Transform), "m_LocalPosition.x",
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(length, length)));
            return clip;
        }

        private FakeTimeMachine CreateMachine(SpinSkillData data)
        {
            var machine = new FakeTimeMachine(data);
            machine.OnHitboxFrame += i => _ticks.Add(i);
            machine.OnSkillCompleted += () => _completedCount++;
            return machine;
        }

        [TearDown]
        public void TearDown()
        {
            _ticks.Clear();
            _completedCount = 0;
        }

        [Test]
        public void TryStart_SpinSkill_TransitionsToSpinning()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            Assert.IsTrue(machine.TryStart());
            Assert.AreEqual(SkillSubState.Spinning, machine.CurrentState);
        }

        [Test]
        public void Update_TicksAtFixedInterval()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            machine.TryStart();

            machine.Now = 0.19f;
            machine.Update(0.01f);
            Assert.AreEqual(0, _ticks.Count, "第一个tick前不触发");

            machine.Now = 0.2f;
            machine.Update(0.01f);
            machine.Now = 0.4f;
            machine.Update(0.01f);
            machine.Now = 0.61f;
            machine.Update(0.01f);

            Assert.AreEqual(new[] { 0, 1, 2 }, _ticks.ToArray(), "tick 按 tickInterval 间隔触发且序号递增");
        }

        [Test]
        public void Update_FirstTickDelayedByCastClipLength()
        {
            var clip = CreateClip(1f);
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f, clip));
            machine.TryStart();

            machine.Now = 1.0f;
            machine.Update(0.01f);
            Assert.AreEqual(0, _ticks.Count, "起手动画结束时刻不触发");

            machine.Now = 1.2f;
            machine.Update(0.01f);
            Assert.AreEqual(new[] { 0 }, _ticks.ToArray(), "第一个tick在 startClip.length + tickInterval");
        }

        [Test]
        public void Update_ReachingMaxDuration_AutoCompletes()
        {
            var machine = CreateMachine(CreateSpinData(1f, 1f, 0.2f));
            machine.TryStart();

            machine.Now = 0.99f;
            machine.Update(0.01f);
            Assert.AreEqual(SkillSubState.Spinning, machine.CurrentState);

            machine.Now = 1.0f;
            machine.Update(0.01f);
            Assert.AreEqual(SkillSubState.Completed, machine.CurrentState);
            Assert.AreEqual(1, _completedCount, "自动完成只触发一次 OnSkillCompleted");
        }

        [Test]
        public void Cancel_BeforeMinWindow_Rejected()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            machine.TryStart();

            machine.Now = 0.5f;
            machine.Update(0.01f);

            Assert.IsFalse(machine.Cancel(), "min 窗口前不可取消");
            Assert.AreEqual(SkillSubState.Spinning, machine.CurrentState);
            Assert.AreEqual(0, _completedCount);
        }

        [Test]
        public void Cancel_InsideWindow_Completes()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            machine.TryStart();

            machine.Now = 1.2f;
            machine.Update(0.01f);

            Assert.IsTrue(machine.Cancel());
            Assert.AreEqual(SkillSubState.Completed, machine.CurrentState);
            Assert.AreEqual(1, _completedCount, "取消走正常完成语义");
        }

        [Test]
        public void Cancel_AfterCompletion_IsNoOp()
        {
            var machine = CreateMachine(CreateSpinData(1f, 1f, 0.2f));
            machine.TryStart();

            machine.Now = 1f;
            machine.Update(0.01f);
            Assert.AreEqual(SkillSubState.Completed, machine.CurrentState);

            Assert.IsFalse(machine.Cancel(), "已完成状态下取消无效（幂等）");
            Assert.AreEqual(1, _completedCount, "不重复触发完成事件");
        }

        [Test]
        public void CanCancel_ReflectsCancelWindow()
        {
            var machine = CreateMachine(CreateSpinData(1f, 5f, 0.2f));
            machine.TryStart();

            machine.Now = 0.5f;
            machine.Update(0.01f);
            Assert.IsFalse(machine.CanCancel());

            machine.Now = 1.2f;
            machine.Update(0.01f);
            Assert.IsTrue(machine.CanCancel());
        }
    }
}
