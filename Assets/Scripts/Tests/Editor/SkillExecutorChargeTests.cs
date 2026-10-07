using System.Collections.Generic;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SkillExecutorChargeTests
    {
        private readonly List<GameObject> _gos = new List<GameObject>();
        private GameObject _ownerGo;
        private int _releasedCount;
        private bool _lastEventFullCharge;

        [SetUp]
        public void SetUp()
        {
            _releasedCount = 0;
            _lastEventFullCharge = false;
            EventBus.Subscribe<SkillReleasedEvent>(OnReleased);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<SkillReleasedEvent>(OnReleased);
            if (_ownerGo != null) Object.DestroyImmediate(_ownerGo);
            foreach (var go in _gos) if (go != null) Object.DestroyImmediate(go);
            _gos.Clear();
        }

        private void OnReleased(SkillReleasedEvent e) { _releasedCount++; _lastEventFullCharge = e.IsFullCharge; }

        /// <summary>
        /// 物理命中假目标：执行器 AOE 检测走 Physics.OverlapSphere（碰撞体 + IEffectTarget 组件），
        /// 因此必须挂真实 Collider 与 MonoBehaviour 组件（不同于剑气测试的 PhysicsRegistry 假目标）
        /// </summary>
        private class FakeHitTarget : MonoBehaviour, IEffectTarget, IDamageable
        {
            public bool IsAlive => true;
            public int HitCount { get; private set; }
            public DamageBlock LastDamage;
            public Transform Transform => transform;
            public IEffectStats Stats => null;
            public IShieldSystem ShieldSystem => null;
            public IPhysicsSystem PhysicsSystem => null;
            public IStatusController StatusController => null;
            public void Heal(float amount) { }
            public void TakeDamage(DamageBlock d, Vector3 dir) { HitCount++; LastDamage = d; }
            public void TakeDamage(DamageBlock d, Vector3 dir, HitContact c) { TakeDamage(d, dir); }
        }

        /// <summary>假时间状态机（同 Task 1，SkillExecutor 测试用反射注入替换内部机器）</summary>
        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { Now = 0f; }
            protected override float GetCurrentTime() => Now;
        }

        /// <summary>
        /// 用假时钟机器替换执行器内部机器（readonly 字段反射可写）。
        /// 注意：执行器构造时已把判定帧/状态回调订阅到"旧机器"上，替换后新机器无人订阅，
        /// 必须把旧机器的全部事件委托拷贝过来，判定帧与状态变更才会到达执行器。
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

        private static void RaiseHitboxFrame(SkillExecutor executor, int frameIndex)
        {
            var stateMachine = TestHelpers.GetField(executor, "_stateMachine");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            // 私有基类字段：从子类类型 GetField 查不到（Mono 语义），须用声明类型；
            // 注入 FakeTimeMachine 后 GetType() 是子类，这里固定用 SkillStateMachine
            var eventField = typeof(SkillStateMachine).GetField("_onHitboxFrame", flags);
            Assert.IsNotNull(eventField);
            var del = (System.Action<int>)eventField.GetValue(stateMachine);
            del?.Invoke(frameIndex);
        }

        /// <summary>充电技能数据：AOE 圆 2m，命中帧 {0f}（Execution 进入即炸），伤害 100</summary>
        private ChargedSkillData CreateChargedData()
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            // SerializedObject 对 CreateInstance 对象无法重建"原为 null 的引用字段"的子属性树
            // （_shape 反射赋值后 _shape/_targetType 仍解析不到），统一走反射（同 Task 1 测试模式）
            var shape = new ShapeBlock();
            TestHelpers.SetField(shape, "_targetType", TargetType.AOE_Circle);
            TestHelpers.SetField(shape, "_areaRadius", 2f);
            TestHelpers.SetField(shape, "_hitboxTimings", new float[] { 0f });
            TestHelpers.SetField(data, "_shape", shape);
            TestHelpers.SetField(data, "_minChargeTime", 0.3f);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            // OnValidate 在编辑器 CreateInstance 下不自动触发，显式设置（打断矩阵按 SkillType 查表）
            TestHelpers.SetField(data, "_skillType", SkillType.Charged);
            TestHelpers.SetField(data, "_damage", DamageBlock.CreateDefault(100f, 0f));
            return data;
        }

        private SkillExecutor CreateExecutor(ChargedSkillData data)
        {
            _ownerGo = new GameObject("ChargeOwner");
            _ownerGo.transform.position = Vector3.zero;
            var owner = new TestHelpers.FakeTarget(_ownerGo.transform);
            var executor = new SkillExecutor(owner, data);
            InjectMachine(executor, new FakeTimeMachine(data));
            return executor;
        }

        private static void DriveToCharging(SkillExecutor executor, FakeTimeMachine machine, float at = 0.15f)
        {
            executor.TryStart();
            machine.Now = at;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
        }

        /// <summary>物理命中假目标：带 SphereCollider 与 FakeHitTarget 组件（AOE 物理检测用）</summary>
        private FakeHitTarget NewTarget(string name, Vector3 position)
        {
            var go = new GameObject(name);
            _gos.Add(go);
            go.transform.position = position;
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.5f;
            return go.AddComponent<FakeHitTarget>();
        }

        [Test]
        public void test_charge_blast_no_damage_while_charging()
        {
            // Arrange
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            var target = NewTarget("Target", new Vector3(1.5f, 0f, 0f));

            // Act（蓄力中判定帧 ×3——无守卫时第 3 帧必然结算伤害）
            RaiseHitboxFrame(executor, 0);
            RaiseHitboxFrame(executor, 0);
            RaiseHitboxFrame(executor, 0);

            // Assert（蓄力期不结算伤害）
            Assert.AreEqual(0, target.HitCount, "蓄力期间不得造成伤害");
        }

        [Test]
        public void test_charge_blast_damage_scales_by_curve_at_release()
        {
            // Arrange（默认曲线 Linear(0,1,1,2)，进度 0.5 → 1.5x）
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            machine.Now = 1.15f; // 蓄力 1.0s → 进度 0.5
            machine.Release();   // 松开释放（输入链路在 T3 接线，此处直接驱动状态机）
            executor.Update(0.01f); // 步进一帧（此时无目标，不结算）
            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);
            var target = NewTarget("Target", new Vector3(1.5f, 0f, 0f));

            // Act（Execution 判定帧 ×1：P6 ② 后状态机保证每个判定窗口只触发一次，单帧即结算）
            RaiseHitboxFrame(executor, 0);

            // Assert（100 × curve(0.5)=1.5 → 150）
            Assert.AreEqual(1, target.HitCount);
            Assert.AreEqual(150f, target.LastDamage.CalculatedDamage, 0.001f);
        }

        [Test]
        public void test_charge_blast_full_charge_marks_was_full_charge()
        {
            // Arrange
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            machine.Now = 2.15f; // 蓄力 ≥ max → 自动释放，进度 1
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);

            // Assert（满蓄力标志写入，不再是死字段恒 false）
            Assert.IsTrue((bool)TestHelpers.GetField(executor, "_wasFullCharge"), "满蓄力应标记 _wasFullCharge");
            Assert.IsTrue(_lastEventFullCharge, "释放事件应携带 IsFullCharge = true");
        }

        [Test]
        public void test_charge_blast_aoe_radius_scales_with_charge()
        {
            // Arrange（基础半径 2m，满蓄力 AreaScale(1)=1.5 → 3m；目标 2.6m：未缩放够不到，缩放后命中）
            var data = CreateChargedData();
            TestHelpers.SetField(data, "_chargeAreaCurve", AnimationCurve.Linear(0f, 1f, 1f, 1.5f));
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            machine.Now = 2.15f; // 满蓄力
            executor.Update(0.01f);
            var target = NewTarget("Target", new Vector3(2.6f, 0f, 0f));

            // Act（满蓄力 → 半径 2×1.5=3m；单帧判定即结算）
            RaiseHitboxFrame(executor, 0);

            // Assert（缩放后半径 3m + 碰撞体半径 0.5m → 触及 2.6m 目标）
            Assert.AreEqual(1, target.HitCount, "满蓄力半径放大后 2.6m 处目标应命中");
        }

        [Test]
        public void test_charge_blast_interrupt_emits_skill_released_event()
        {
            // Arrange
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);

            // Act（蓄力中受击打断）
            Assert.IsTrue(executor.TryInterrupt(InterruptionSource.DamageTaken));

            // Assert（打断必须补发释放事件，VFX 才能熄灭）
            Assert.AreEqual(1, _releasedCount, "蓄力打断应补发 SkillReleasedEvent");
        }

        [Test]
        public void test_charge_progress_for_chargeable_projectile()
        {
            // Arrange（可蓄力投射物 max 2s：蓄力进度按 ElapsedTime/2 计算）
            var data = ScriptableObject.CreateInstance<ProjectileSkillData>();
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            TestHelpers.SetField(data, "_skillId", 20003);
            _ownerGo = new GameObject("FireballOwner");
            _ownerGo.transform.position = Vector3.zero;
            var executor = new SkillExecutor(new TestHelpers.FakeTarget(_ownerGo.transform), data);
            var machine = new FakeTimeMachine(data);
            InjectMachine(executor, machine);
            executor.TryStart();
            machine.Now = 0.15f; // Casting 0.1s → 进入 Charging
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);

            // Act（蓄力 1.0s）
            machine.Now = 1.15f;
            executor.Update(0.01f);

            // Assert（进度 1.0/2.0 = 0.5）
            Assert.AreEqual(0.5f, executor.GetChargeProgress(), 0.001f);
        }

        [Test]
        public void test_charge_blast_execution_hitbox_settles_at_30fps()
        {
            // Arrange（30fps 帧步长 0.0333s：固定 0.05s 窗口仅容纳 2 帧，连续 3 帧检测才能结算伤害）
            var data = CreateChargedData();
            var executor = CreateExecutor(data);
            var machine = (FakeTimeMachine)TestHelpers.GetField(executor, "_stateMachine");
            DriveToCharging(executor, machine, 0.15f);
            var target = NewTarget("Target", new Vector3(1.5f, 0f, 0f)); // 目标提前就位：Release 当帧（elapsed=0）即计入连续检测
            machine.Now = 0.45f;
            machine.Release();      // 松开释放 → Execution（蓄力 0.3s，进度 0.15）
            executor.Update(0.01f); // elapsed=0 触发判定帧 → 检测 1
            Assert.AreEqual(SkillSubState.Execution, executor.CurrentSubState);

            // Act（30fps 帧步长推进：固定窗口下第 3 帧落在 0.05s 之外 → 连续计数重置永不结算；
            //      帧率自适应窗口（max(0.05, 3×deltaTime)=0.1）容纳 3 帧 → 伤害结算）
            machine.Now = 0.4833f; executor.Update(0.0333f); // 帧 2（elapsed 0.0333）→ 检测 2
            machine.Now = 0.5166f; executor.Update(0.0333f); // 帧 3（elapsed 0.0666）→ 检测 3 → 结算伤害

            // Assert（30fps 下 3 帧连续命中 → 伤害必须结算）
            Assert.AreEqual(1, target.HitCount, "30fps 下判定窗口须容纳 3 帧，伤害必须结算");
        }
    }
}
