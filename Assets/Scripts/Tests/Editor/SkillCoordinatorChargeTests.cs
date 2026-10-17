using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameSys.EditorTests
{
    public class SkillCoordinatorChargeTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;

        /// <summary>录制 CastRequest 的假客户端（同 CombatNetworkBridgeTests.RecordingClient 范式）</summary>
        private sealed class RecordingClient : INetworkClient
        {
            public readonly List<(MessageId id, IMessage msg)> Sent = new List<(MessageId, IMessage)>();
            public bool IsConnected => true;
            public Task<bool> ConnectAsync(string host, int port) => Task.FromResult(true);
            public Task DisconnectAsync(string reason) => Task.CompletedTask;
            public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
                => Sent.Add((messageId, message));
            public event Action<MessageId, IMessage> MessageReceived;
            public event Action<string> Disconnected;
            public event Action<Exception> Error;
            public void Dispose() { }
        }

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
            // OnValidate 在编辑器 CreateInstance 下不自动触发——打断矩阵按 SkillType 查表
            TestHelpers.SetField(data, "_skillType", SkillType.Charged);
            return data;
        }

        /// <summary>可蓄力投射物（20003 火球）：MaxChargeTime &gt; 0 ⇒ 状态机同样走 Casting→Charging→Execution</summary>
        private ProjectileSkillData CreateChargeableProjectileData()
        {
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            TestHelpers.SetField(data, "_skillId", 20003);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            TestHelpers.SetField(data, "_skillType", SkillType.Projectile);
            return data;
        }

        /// <summary>
        /// 把协调器按 ClientNetworkHost 的方式接到真实 <see cref="CombatNetworkBridge"/>（录制客户端），
        /// 这样"一次施法恰好一条 CastRequest / 取消不发"可以直接按发送条数断言。
        /// </summary>
        private (SkillCoordinator Coordinator, RecordingClient Client, FakeTimeMachine Machine)
            CreateCoordinatorWithBridge(SkillData data)
        {
            _ownerGo = new GameObject("ChargeOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            coordinator.RegisterSkill(data);

            var executor = new SkillExecutor(owner, data);
            var machine = new FakeTimeMachine(data);
            InjectMachine(executor, machine);
            executor.TryStart();
            machine.Now = 0.15f; // Casting 0.1s → Charging
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
            TestHelpers.SetField(coordinator, "_currentSkill", executor);

            var client = new RecordingClient();
            var bridge = new CombatNetworkBridge(client, () => 1L,
                new RemoteEntityRegistry(_ownerGo.transform));
            coordinator.OnSkillActivated += d =>
                SkillCastReporter.ReportActivation(bridge, d, Vector3.zero, _ownerGo.transform);
            coordinator.OnChargeReleased += (d, p, pt) =>
                SkillCastReporter.ReportChargeRelease(bridge, d, p, pt);
            return (coordinator, client, machine);
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

        // ====================================================================================
        // 审查修复轮：可蓄力技能的上报时机
        //   "一次施法恰好一条 CastRequest / 取消不发"——接真实 CombatNetworkBridge 按条数断言。
        // ====================================================================================

        [Test]
        public void test_charge_release_reports_exactly_once_with_actual_progress()
        {
            // Arrange（蓄力 0.5s / 上限 2s = 0.25）
            var data = CreateChargedData();
            var (coordinator, client, machine) = CreateCoordinatorWithBridge(data);
            int released = 0;
            float releasedProgress = -1f;
            coordinator.OnChargeReleased += (d, p, pt) =>
            {
                released++;
                releasedProgress = p;
            };

            // Act
            machine.Now = 0.65f;
            coordinator.Update(0.01f); // 刷新 ElapsedTime
            Assert.IsTrue(coordinator.TryReleaseCharge(20002), "蓄力中的当前技能应被释放");

            // Assert
            Assert.AreEqual(1, released, "一次施法只允许上报一次");
            Assert.AreEqual(0.25f, releasedProgress, 0.001f, "上报的必须是释放瞬间的实际进度");
            Assert.AreEqual(1, client.Sent.Count, "释放时应恰好发送一条 CastRequest");
            Assert.AreEqual(MessageId.CastRequest, client.Sent[0].id);

            // 已释放后再松手不得重复上报
            Assert.IsFalse(coordinator.TryReleaseCharge(20002));
            Assert.AreEqual(1, released);
            Assert.AreEqual(1, client.Sent.Count, "同一施法不得发出第二条 CastRequest");
        }

        [Test]
        public void test_full_charge_auto_release_reports_exactly_once_with_progress_one()
        {
            // Arrange（按住不放：状态机在 MaxChargeTime 处自动 Charging → Execution，
            // 不经过 TryReleaseCharge——这条路径同样必须上报，否则满蓄力施法服务端永远收不到）
            var data = CreateChargedData();
            var (coordinator, client, machine) = CreateCoordinatorWithBridge(data);
            int released = 0;
            float releasedProgress = -1f;
            coordinator.OnChargeReleased += (d, p, pt) =>
            {
                released++;
                releasedProgress = p;
            };

            // Act
            machine.Now = 2.15f; // 蓄力 2.0s == MaxChargeTime
            coordinator.Update(0.01f);

            // Assert
            Assert.AreEqual(SkillSubState.Execution, coordinator.CurrentSubState);
            Assert.AreEqual(1, released, "满蓄力自动释放也必须上报一次");
            Assert.AreEqual(1f, releasedProgress, 0.001f, "自动释放的进度按满计");
            Assert.AreEqual(1, client.Sent.Count);

            coordinator.Update(0.01f); // 后续帧不得重复上报
            Assert.AreEqual(1, released);
            Assert.AreEqual(1, client.Sent.Count);
        }

        [Test]
        public void test_charge_cancel_does_not_report_release()
        {
            // Arrange
            var data = CreateChargedData();
            var (coordinator, client, machine) = CreateCoordinatorWithBridge(data);
            int released = 0;
            coordinator.OnChargeReleased += (d, p, pt) => released++;

            // Act（蓄力中被打断 = 取消蓄力）
            coordinator.InterruptCurrentSkill(InterruptionSource.Stun);

            // Assert（注入式装置未把执行器登记进 _activeExecutors，打断后停在 Cancelled 而非被清理）
            Assert.AreEqual(SkillSubState.Cancelled, coordinator.CurrentSubState, "打断应使蓄力进入取消态");
            Assert.AreEqual(0, released, "取消蓄力不得上报释放");
            Assert.AreEqual(0, client.Sent.Count, "取消蓄力不得发送 CastRequest");
        }

        [Test]
        public void test_chargeable_projectile_activation_sends_no_cast_request()
        {
            // Arrange（20003 火球资产 MaxChargeTime=2 ⇒ 与 ChargedSkillData 同样蓄力：
            // 激活时发 CastRequest 会让服务端按蓄力起始位置/时刻判定，取消还会产生幽灵伤害）
            var data = CreateChargeableProjectileData();
            _ownerGo = new GameObject("ChargeOwner");
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var coordinator = new SkillCoordinator(owner);
            coordinator.RegisterSkill(data);
            var client = new RecordingClient();
            var bridge = new CombatNetworkBridge(client, () => 1L,
                new RemoteEntityRegistry(_ownerGo.transform));
            coordinator.OnSkillActivated += d =>
                SkillCastReporter.ReportActivation(bridge, d, Vector3.zero, _ownerGo.transform);

            // Act
            coordinator.HandleInput(SkillInput.SkillToPosition(20003, Vector3.forward * 5f));

            // Assert
            Assert.AreEqual(SkillSubState.Casting, coordinator.CurrentSubState,
                "可蓄力投射物激活后应进入蓄力阶段（Casting → Charging）");
            Assert.AreEqual(0, client.Sent.Count,
                "可蓄力投射物激活时不得发 CastRequest（改为释放时上报）");
        }

        [Test]
        public void test_chargeable_projectile_release_sends_exactly_one_cast_request()
        {
            // Arrange
            var data = CreateChargeableProjectileData();
            var (coordinator, client, machine) = CreateCoordinatorWithBridge(data);
            machine.Now = 0.65f; // 进度 0.25
            coordinator.Update(0.01f);

            // Act
            Assert.IsTrue(coordinator.TryReleaseCharge(20003), "火球松手应释放");

            // Assert
            Assert.AreEqual(1, client.Sent.Count, "释放时应恰好一条 CastRequest");
            var cast = (CastRequest)client.Sent[0].msg;
            Assert.AreEqual(20003, cast.SkillId);
            Assert.AreEqual(0f, cast.ImpactZ, 0.001f, "火球是线形判定（沿朝向），不传落点");
            Assert.AreEqual(0.25f, cast.ChargeProgress, 0.001f, "释放进度随包上线（服务端 20003 不缩放）");
        }

        [Test]
        public void test_chargeable_projectile_cancel_sends_no_cast_request()
        {
            // Arrange
            var data = CreateChargeableProjectileData();
            var (coordinator, client, machine) = CreateCoordinatorWithBridge(data);

            // Act（蓄力中取消）
            coordinator.InterruptCurrentSkill(InterruptionSource.Stun);

            // Assert
            Assert.AreEqual(SkillSubState.Cancelled, coordinator.CurrentSubState);
            Assert.AreEqual(0, client.Sent.Count,
                "取消蓄力不得发送 CastRequest（否则服务端会产生幽灵伤害并进入冷却）");
        }
    }
}
