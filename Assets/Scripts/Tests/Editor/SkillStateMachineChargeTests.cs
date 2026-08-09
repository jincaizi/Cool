using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillStateMachineChargeTests
    {
        /// <summary>假时间状态机：override GetCurrentTime() 注入可控时钟（同 SkillStateMachineSpinTests 模式）</summary>
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        private ChargedSkillData CreateChargedData(float min, float max)
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            TestHelpers.SetField(data, "_minChargeTime", min);
            TestHelpers.SetField(data, "_maxChargeTime", max);
            return data;
        }

        /// <summary>投射物数据：maxChargeTime > 0 = 可蓄力投射物（火球蓄力抛投）</summary>
        private static ProjectileSkillData CreateProjectileData(float maxCharge)
        {
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            TestHelpers.SetField(data, "_maxChargeTime", maxCharge);
            return data;
        }

        /// <summary>TryStart 后推进到蓄力阶段（Casting 0.1s 硬编码）</summary>
        private static void AdvanceToCharging(FakeTimeMachine machine)
        {
            machine.TryStart();
            machine.Now = 0.15f;
            machine.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, machine.CurrentState);
        }

        [Test]
        public void test_charge_release_below_min_fires_immediately()
        {
            // Arrange（min 0.3 / max 2.0，蓄力 0.25s < min）
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 2f));
            AdvanceToCharging(machine);
            machine.Now = 0.40f;
            machine.Update(0.01f);

            // Act（未满 min 就松开）
            bool released = machine.Release();

            // Assert（任意时刻释放，立即进 Execution，进度 = 实际蓄力/最大）
            Assert.IsTrue(released, "未满 min 也应立即释放");
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.AreEqual(0.125f, machine.ChargeProgressAtRelease, 0.001f); // 0.25/2.0
        }

        [Test]
        public void test_charge_release_records_partial_progress()
        {
            // Arrange
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 2f));
            AdvanceToCharging(machine);
            machine.Now = 1.15f; // 蓄力 1.0s
            machine.Update(0.01f);

            // Act
            Assert.IsTrue(machine.Release());

            // Assert
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.AreEqual(0.5f, machine.ChargeProgressAtRelease, 0.001f);
        }

        [Test]
        public void test_charge_auto_release_at_max_sets_full_progress()
        {
            // Arrange
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 1f));
            AdvanceToCharging(machine);
            machine.Now = 1.15f; // 蓄力 ≥ max 1.0
            machine.Update(0.01f);

            // Assert（自动释放，进度 = 1）
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.AreEqual(1f, machine.ChargeProgressAtRelease, 0.001f);
        }

        [Test]
        public void test_charge_release_when_not_charging_rejected()
        {
            // Arrange（Ready 态，未开始）
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 2f));

            // Act & Assert
            Assert.IsFalse(machine.Release(), "未开始蓄力时 Release 无效");
            Assert.AreEqual(SkillSubState.Ready, machine.CurrentState);

            // Arrange（Casting 阶段）
            machine.TryStart();

            // Act & Assert
            Assert.IsFalse(machine.Release(), "读条阶段 Release 无效");
        }

        [Test]
        public void test_charge_release_after_release_is_noop()
        {
            // Arrange
            var machine = new FakeTimeMachine(CreateChargedData(0.3f, 2f));
            AdvanceToCharging(machine);
            machine.Now = 0.65f;
            machine.Update(0.01f);
            Assert.IsTrue(machine.Release());
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);

            // Act（再次 Release）
            Assert.IsFalse(machine.Release(), "已释放后 Release 幂等无效");
        }

        [Test]
        public void test_chargeable_projectile_release_fires_immediately()
        {
            // Arrange（可蓄力投射物：max 2s，轻点 1 键 → 蓄力 0.25s 后松开）
            var machine = new FakeTimeMachine(CreateProjectileData(2f));
            AdvanceToCharging(machine);
            machine.Now = 0.40f; // 蓄力 0.25s
            machine.Update(0.01f);

            // Act（松开立即释放）
            bool released = machine.Release();

            // Assert（立即进 Execution，进度 = 实际蓄力/最大）
            Assert.IsTrue(released, "可蓄力投射物松开应立即释放");
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.AreEqual(0.125f, machine.ChargeProgressAtRelease, 0.001f); // 0.25/2.0
        }

        [Test]
        public void test_chargeable_projectile_auto_release_at_max()
        {
            // Arrange（满蓄力 2s 自动抛出）
            var machine = new FakeTimeMachine(CreateProjectileData(2f));
            AdvanceToCharging(machine);
            machine.Now = 2.15f; // 蓄力 ≥ max 2.0
            machine.Update(0.01f);

            // Assert（自动释放，进度 = 1）
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.AreEqual(1f, machine.ChargeProgressAtRelease, 0.001f);
        }

        [Test]
        public void test_non_chargeable_projectile_skips_charge()
        {
            // Arrange（maxChargeTime = 0：非蓄力投射物，按下即发射——保持原瞬时行为）
            var machine = new FakeTimeMachine(CreateProjectileData(0f));

            // Act & Assert（跳过 Casting/Charging，直接 Execution）
            Assert.IsTrue(machine.TryStart());
            Assert.AreEqual(SkillSubState.Execution, machine.CurrentState);
            Assert.IsFalse(machine.Release(), "非蓄力投射物 Release 无效");
        }
    }
}
