using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SwordQiEchoSkillTests
    {
        private GameObject _ownerGo;

        [SetUp]
        public void SetUp()
        {
            _ownerGo = new GameObject("EchoOwner");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_ownerGo);
        }

        private static SwordQiEchoSkillData CreateEchoData()
        {
            var data = ScriptableObject.CreateInstance<SwordQiEchoSkillData>();
            TestHelpers.SetField(data, "_skillId", (int)SkillID.SwordQiEcho);
            TestHelpers.SetField(data, "_cooldown", 12f);
            TestHelpers.SetField(data, "_skillType", SkillType.Instant);
            TestHelpers.SetField(data, "_canBeInterruptedByMovement", false);
            return data;
        }

        private static InstantSkillData CreateInstantData()
        {
            var data = ScriptableObject.CreateInstance<InstantSkillData>();
            TestHelpers.SetField(data, "_skillId", (int)SkillID.SkillQ);
            TestHelpers.SetField(data, "_skillType", SkillType.Instant);
            return data;
        }

        [Test]
        public void CooldownManager_StartCooldown_WithElapsed_PreservesTotalAndRemaining()
        {
            var manager = new CooldownManager();
            manager.StartCooldown((int)SkillID.SwordQiEcho, 12f, 2f);

            var info = manager.GetCooldownInfo((int)SkillID.SwordQiEcho);

            Assert.IsTrue(info.IsOnCooldown);
            Assert.AreEqual(12f, info.Total, 0.001f);
            Assert.AreEqual(10f, info.Remaining, 0.05f);
        }

        [Test]
        public void SkillStateMachine_StartEcho_IgnoresClipLength()
        {
            var data = CreateEchoData();
            var stateMachine = new SkillStateMachine(data);
            double now = 0d;

            Assert.IsTrue(stateMachine.TryStart());
            Assert.IsTrue(stateMachine.StartEcho(2d, () => now));

            stateMachine.Update(0.5f);
            Assert.AreEqual(SkillSubState.Execution, stateMachine.CurrentState);

            now = 1.99d;
            stateMachine.Update(0.1f);
            Assert.AreEqual(SkillSubState.Execution, stateMachine.CurrentState);

            now = 2d;
            stateMachine.Update(0.1f);
            Assert.AreEqual(SkillSubState.Completed, stateMachine.CurrentState);
        }

        [Test]
        public void SkillCoordinator_StartOfflineEcho_AllowsMovementAndRotation()
        {
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            var data = CreateEchoData();
            coordinator.RegisterSkill(data);
            double now = 0d;

            Assert.IsTrue(coordinator.StartOfflineEcho(data, 2d, () => now));
            Assert.AreEqual(SkillSubState.Execution, coordinator.CurrentSubState);
            Assert.IsTrue(coordinator.CanMove());
            Assert.IsTrue(coordinator.CanRotate());
            Assert.IsTrue(coordinator.IsAnimationCompletionIgnored());

            now = 2d;
            coordinator.Update(0.1f);
            Assert.IsNull(coordinator.CurrentSkill, "到点完成后协调器应清理执行器");
        }

        [Test]
        public void PendingRequest_BlocksOtherCastsUntilDecisionOrTimeout()
        {
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            var echo = CreateEchoData();
            coordinator.RegisterSkill(echo);
            coordinator.RegisterSkill(CreateInstantData());
            coordinator.OnEchoRequested += _ => { }; // 模拟联网宿主已订阅

            coordinator.HandleInput(SkillInput.SkillToPosition((int)SkillID.SwordQiEcho, Vector3.zero));
            Assert.IsTrue(coordinator.IsEchoRequestPending);

            coordinator.HandleInput(SkillInput.SkillToPosition((int)SkillID.SkillQ, Vector3.zero));
            Assert.IsNull(coordinator.CurrentSkill, "待确认期间不得启动其他主动施法");

            coordinator.CancelEchoRequest();
            Assert.IsFalse(coordinator.IsEchoRequestPending);
        }

        [Test]
        public void LateAcceptedStart_ReconcilesConflictingLocalSkill()
        {
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            var echo = CreateEchoData();
            coordinator.RegisterSkill(echo);
            coordinator.RegisterSkill(CreateInstantData());
            double now = 0d;

            coordinator.HandleInput(SkillInput.SkillToPosition((int)SkillID.SkillQ, Vector3.zero));
            Assert.IsNotNull(coordinator.CurrentSkill);
            Assert.IsInstanceOf<InstantSkillData>(coordinator.CurrentSkill.Data);

            Assert.IsTrue(coordinator.CommitEchoRequest(echo, 2d, 12f, 0f, () => now));
            Assert.IsInstanceOf<SwordQiEchoSkillData>(coordinator.CurrentSkill.Data);
            Assert.AreEqual(SkillSubState.Execution, coordinator.CurrentSubState);
        }
    }
}
