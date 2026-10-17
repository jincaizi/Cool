using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Stamina;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillCoordinatorStaminaTests
    {
        private GameObject _ownerGo;

        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { }
            protected override float GetCurrentTime() => Now;
        }

        private static void InjectMachine(SkillExecutor executor, FakeTimeMachine machine)
        {
            var oldMachine = TestHelpers.GetField(executor, "_stateMachine");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            string[] events =
            {
                "_onStateChanged", "_onHitboxFrame", "_onHitConfirm",
                "_onSkillCompleted", "_onSkillInterrupted"
            };
            foreach (var name in events)
            {
                var field = typeof(SkillStateMachine).GetField(name, flags);
                Assert.IsNotNull(field, name);
                field.SetValue(machine, field.GetValue(oldMachine));
            }
            TestHelpers.SetField(executor, "_stateMachine", machine);
        }

        private ChargedSkillData CreateChargedData(float drain = 15f)
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            TestHelpers.SetField(data, "_skillId", 20004);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            TestHelpers.SetField(data, "_staminaDrainPerSecond", drain);
            TestHelpers.SetField(data, "_skillType", SkillType.Charged);
            return data;
        }

        private SpinSkillData CreateSpinData(float drain = 20f)
        {
            var data = ScriptableObject.CreateInstance<SpinSkillData>();
            TestHelpers.SetField(data, "_skillId", 20002);
            TestHelpers.SetField(data, "_minDuration", 1f);
            TestHelpers.SetField(data, "_maxDuration", 3f);
            TestHelpers.SetField(data, "_staminaDrainPerSecond", drain);
            TestHelpers.SetField(data, "_skillType", SkillType.Spin);
            return data;
        }

        private ChanneledSkillData CreateChanneledData(float drain = 20f)
        {
            var data = ScriptableObject.CreateInstance<ChanneledSkillData>();
            TestHelpers.SetField(data, "_skillId", 20005);
            TestHelpers.SetField(data, "_castTime", 0.2f);
            TestHelpers.SetField(data, "_channelDuration", 2f);
            TestHelpers.SetField(data, "_staminaDrainPerSecond", drain);
            TestHelpers.SetField(data, "_skillType", SkillType.Channeled);
            return data;
        }

        private InstantSkillData CreateInstantData()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", 20001);
            TestHelpers.SetField(data, "_skillType", SkillType.Instant);
            return data;
        }

        private (SkillCoordinator, StaminaSystem, SkillExecutor, FakeTimeMachine)
            CreateWithExecutor(SkillData data)
        {
            _ownerGo = new GameObject("StaminaSkillOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            coordinator.RegisterSkill(data);

            var stamina = new StaminaSystem(StaminaConfig.Default);
            coordinator.SetStamina(stamina);

            var executor = new SkillExecutor(owner, data);
            var machine = new FakeTimeMachine(data);
            InjectMachine(executor, machine);
            Assert.IsTrue(executor.TryStart());
            TestHelpers.SetField(coordinator, "_currentSkill", executor);
            return (coordinator, stamina, executor, machine);
        }

        private (SkillCoordinator, StaminaSystem, SkillExecutor, FakeTimeMachine) CreateAtCharging()
        {
            var tuple = CreateWithExecutor(CreateChargedData());
            tuple.Item4.Now = 0.15f;
            tuple.Item3.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, tuple.Item3.CurrentSubState);
            return tuple;
        }

        [TearDown]
        public void TearDown()
        {
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
        }

        [Test]
        public void test_skill_charged_drains_only_during_charging()
        {
            var (coordinator, stamina, _, _) = CreateAtCharging();
            float before = stamina.Current;

            coordinator.Update(0.1f);

            Assert.AreEqual(before - 15f * 0.1f, stamina.Current, 0.01f);
        }

        [Test]
        public void test_skill_casting_does_not_drain()
        {
            var (coordinator, stamina, executor, machine) = CreateWithExecutor(CreateChargedData());
            machine.Now = 0.05f;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Casting, executor.CurrentSubState);

            float before = stamina.Current;
            coordinator.Update(0.1f);

            Assert.AreEqual(before, stamina.Current, 0.001f);
        }

        [Test]
        public void test_skill_execution_recovery_skill_drain_is_zero()
        {
            var (coordinator, stamina, executor, machine) = CreateAtCharging();
            machine.Now = 2.2f;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);

            float before = stamina.Current;
            coordinator.Update(0.1f);

            Assert.AreEqual(before, stamina.Current, 0.001f);
        }

        [Test]
        public void test_skill_charged_at_zero_stamina_auto_releases_and_reports_cast()
        {
            var (coordinator, stamina, executor, _) = CreateAtCharging();
            stamina.TryConsume(99f); // 剩 1
            int released = 0;
            coordinator.OnChargeReleased += (_, _, _) => released++;

            coordinator.Update(0.1f);

            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);
            Assert.AreEqual(1, released, "力竭自动释放也必须上报一次");
        }

        [Test]
        public void test_skill_spin_at_zero_stamina_interrupts_despite_super_armor()
        {
            var (coordinator, stamina, executor, _) = CreateWithExecutor(CreateSpinData());
            Assert.AreEqual(SkillSubState.Spinning, executor.CurrentSubState);
            stamina.TryConsume(99f);

            coordinator.Update(0.1f);

            Assert.AreEqual(SkillSubState.Cancelled, executor.CurrentSubState,
                "旋转霸体不得阻止力竭打断");
        }

        [Test]
        public void test_skill_channeled_at_zero_stamina_interrupts()
        {
            var (coordinator, stamina, executor, machine) = CreateWithExecutor(CreateChanneledData());
            machine.Now = 0.3f;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Channeling, executor.CurrentSubState);
            stamina.TryConsume(99f);

            coordinator.Update(0.1f);

            Assert.AreEqual(SkillSubState.Cancelled, executor.CurrentSubState);
        }

        [Test]
        public void test_skill_combo_zero_drain_activates_while_exhausted()
        {
            _ownerGo = new GameObject("StaminaSkillOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            coordinator.RegisterSkill(CreateInstantData());
            var stamina = new StaminaSystem(StaminaConfig.Default);
            coordinator.SetStamina(stamina);
            stamina.TryConsume(100f);

            coordinator.HandleInput(SkillInput.SkillToPosition(20001, Vector3.forward));

            Assert.IsNotNull(coordinator.CurrentSkill, "0 消耗技能在力竭时仍可施放");
        }

        [Test]
        public void test_skill_sustained_with_drain_cannot_activate_while_exhausted()
        {
            _ownerGo = new GameObject("StaminaSkillOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            coordinator.RegisterSkill(CreateChargedData());
            var stamina = new StaminaSystem(StaminaConfig.Default);
            coordinator.SetStamina(stamina);
            stamina.TryConsume(100f);

            coordinator.HandleInput(SkillInput.SkillToPosition(20004, Vector3.forward));

            Assert.IsNull(coordinator.CurrentSkill, "有消耗的持续技能在力竭时必须拒绝激活");
        }
    }
}
