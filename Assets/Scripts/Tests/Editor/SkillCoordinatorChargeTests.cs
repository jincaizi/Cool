using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillCoordinatorChargeTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;

        [TearDown]
        public void TearDown()
        {
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        /// <summary>假时间状态机（同 Task 1/2，反射注入替换执行器内部机器）</summary>
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        /// <summary>
        /// 用假时钟机器替换执行器内部机器（同 Task 2 模式：readonly 字段反射可写，
        /// 并拷贝旧机器的事件委托，判定帧与状态回调才能到达执行器）。
        /// </summary>
        private static void InjectMachine(SkillExecutor executor, FakeTimeMachine machine)
        {
            var oldMachine = TestHelpers.GetField(executor, "_stateMachine");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            string[] eventFields =
            {
                "_onStateChanged", "_onHitboxFrame", "_onHitConfirm",
                "_onSkillCompleted", "_onSkillInterrupted"
            };
            foreach (var name in eventFields)
            {
                var field = typeof(SkillStateMachine).GetField(name, flags);
                Assert.IsNotNull(field, $"Event field {name} not found");
                field.SetValue(machine, field.GetValue(oldMachine));
            }
            TestHelpers.SetField(executor, "_stateMachine", machine);
        }

        /// <summary>蓄力技能数据：仅需 id 与蓄力时长（本任务测试不触发判定帧/打断，无需 shape/damage）</summary>
        private ChargedSkillData CreateChargedData()
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            TestHelpers.SetField(data, "_skillId", 20002);
            TestHelpers.SetField(data, "_minChargeTime", 0.3f);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            return data;
        }

        /// <summary>构造协调器并把"当前技能"置为蓄力中的执行器（反射注入）</summary>
        private SkillCoordinator CreateCoordinatorWithChargingSkill()
        {
            _ownerGo = new GameObject("ChargeOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            var data = CreateChargedData();
            coordinator.RegisterSkill(data);

            var executor = new SkillExecutor(owner, data);
            var machine = new FakeTimeMachine(data);
            InjectMachine(executor, machine);
            executor.TryStart();
            machine.Now = 0.15f; // 蓄力技能 CastTime 0.1s，此帧后进入 Charging
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
            TestHelpers.SetField(coordinator, "_currentSkill", executor);
            return coordinator;
        }

        [Test]
        public void test_try_release_charge_current_charging_skill_releases()
        {
            // Arrange
            var coordinator = CreateCoordinatorWithChargingSkill();

            // Act
            bool released = coordinator.TryReleaseCharge(20002);

            // Assert
            Assert.IsTrue(released, "蓄力中的当前技能应被释放");
            Assert.AreEqual(SkillSubState.Execution, coordinator.CurrentSubState);
        }

        [Test]
        public void test_try_release_charge_wrong_skill_id_ignored()
        {
            // Arrange
            var coordinator = CreateCoordinatorWithChargingSkill();

            // Act
            bool released = coordinator.TryReleaseCharge(9999);

            // Assert
            Assert.IsFalse(released, "非当前技能 id 不得释放");
            Assert.AreEqual(SkillSubState.Charging, coordinator.CurrentSubState);
        }

        [Test]
        public void test_try_release_charge_when_no_skill_active_is_noop()
        {
            // Arrange（无当前技能）
            _ownerGo = new GameObject("ChargeOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);

            // Act & Assert
            Assert.IsFalse(coordinator.TryReleaseCharge(20002), "无技能执行时 Release 无效");
        }
    }
}
