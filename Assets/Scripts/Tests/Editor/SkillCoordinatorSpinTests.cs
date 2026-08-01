using System.Collections.Generic;
using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillCoordinatorSpinTests
    {
        private const int SpinId = 20002;
        private const int QId = 20001;

        private GameObject _ownerGo;
        private SkillCoordinator _coordinator;
        private SkillInputBuffer _buffer;

        private SpinSkillData CreateSpinData()
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_skillId", SpinId);
            TestHelpers.SetField(data, "_minDuration", 1f);
            TestHelpers.SetField(data, "_maxDuration", 5f);
            TestHelpers.SetField(data, "_tickInterval", 0.2f);
            TestHelpers.SetField(data, "_moveSpeedMultiplier", 0.5f);
            return data;
        }

        private InstantSkillData CreateInstantData()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", QId);
            return data;
        }

        [SetUp]
        public void SetUp()
        {
            _ownerGo = new GameObject("CoordOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            _coordinator = new SkillCoordinator(owner);
            _coordinator.RegisterSkill(CreateSpinData());
            _coordinator.RegisterSkill(CreateInstantData());

            // 反射取出私有输入缓冲，验证入队行为
            var field = typeof(SkillCoordinator).GetField("_inputBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
            _buffer = (SkillInputBuffer)field.GetValue(_coordinator);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_ownerGo);
        }

        [Test]
        public void HandleInput_SameSkillWhileSpinning_IsNotBuffered()
        {
            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));
            Assert.AreEqual(SkillSubState.Spinning, _coordinator.CurrentSubState, "按下后立即进入旋转");

            // 旋转期间（冷却中！）再按R —— 必须走取消特例而非缓冲
            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));

            Assert.AreEqual(0, _buffer.Count, "spin 期间按R永不入缓冲（否则会延迟重施放）");
            Assert.AreEqual(SkillSubState.Spinning, _coordinator.CurrentSubState, "min 窗口内按R不得取消");
        }

        [Test]
        public void HandleInput_OtherSkillWhileSpinning_IsBuffered()
        {
            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));

            _coordinator.HandleInput(SkillInput.SkillToPosition(QId, Vector3.zero));

            Assert.AreEqual(1, _buffer.Count, "其他技能在旋转期间被缓冲（既有行为）");
        }

        [Test]
        public void GetMoveSpeedMultiplier_IsMultiplierWhileSpinning_ElseOne()
        {
            Assert.AreEqual(1f, _coordinator.GetMoveSpeedMultiplier(), 0.0001f, "无技能时倍率为1");

            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));
            Assert.AreEqual(0.5f, _coordinator.GetMoveSpeedMultiplier(), 0.0001f, "旋转时返回配置倍率");
        }

        [Test]
        public void GetMoveSpeedMultiplier_OtherSkillActive_IsOne()
        {
            _coordinator.HandleInput(SkillInput.SkillToPosition(QId, Vector3.zero));
            Assert.AreEqual(1f, _coordinator.GetMoveSpeedMultiplier(), 0.0001f, "其他技能活跃时倍率为1");
        }

        [Test]
        public void ClearInputBuffer_EmptiesBufferedInput()
        {
            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));
            _coordinator.HandleInput(SkillInput.SkillToPosition(QId, Vector3.zero));
            Assert.AreEqual(1, _buffer.Count, "前置：Q 在旋转期间被缓冲");

            _coordinator.ClearInputBuffer();
            Assert.AreEqual(0, _buffer.Count, "ClearInputBuffer 清空缓冲");
        }

        [Test]
        public void IsAnimationCompletionIgnored_WhileSpinning_True()
        {
            _coordinator.HandleInput(SkillInput.SkillToPosition(SpinId, Vector3.zero));
            Assert.IsTrue(_coordinator.IsAnimationCompletionIgnored(), "旋转期间动画完成回调必须被忽略，否则技能被提前清理");
        }

        [Test]
        public void IsAnimationCompletionIgnored_NoSkill_False()
        {
            Assert.IsFalse(_coordinator.IsAnimationCompletionIgnored(), "无技能时不应忽略");
        }

        [Test]
        public void IsAnimationCompletionIgnored_InstantSkillExecution_False()
        {
            _coordinator.HandleInput(SkillInput.SkillToPosition(QId, Vector3.zero));
            Assert.IsFalse(_coordinator.IsAnimationCompletionIgnored(), "瞬发技能执行中不应忽略（动画完成=技能结束）");
        }
    }
}
