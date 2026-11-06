using System.Collections.Generic;
using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Stamina;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 技能栏显示层回归测试：没有本地角色时全部复位、换绑后读取新角色、
    /// 禁用再启用后恢复绑定、旧角色销毁后不残留冷却、角色重建不继承触摸状态。
    /// </summary>
    public class MobileSkillBarPresenterTests
    {
        private static readonly SkillID[] AllSkillIds =
        {
            SkillID.SkillQ, SkillID.SkillR, SkillID.Fireball, SkillID.ChargeBlast, SkillID.Ultimate
        };

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<ScriptableObject> _createdAssets = new List<ScriptableObject>();
        private readonly Dictionary<SkillID, MobileSkillButton> _skillButtons = new Dictionary<SkillID, MobileSkillButton>();
        private readonly Dictionary<SkillID, Image> _cooldowns = new Dictionary<SkillID, Image>();
        private readonly Dictionary<SkillID, Image> _charges = new Dictionary<SkillID, Image>();
        private readonly Dictionary<SkillID, TMP_Text> _countdowns = new Dictionary<SkillID, TMP_Text>();

        private MobileInputBinder _binder;
        private MobileActionButtons _buttons;
        private MobileSkillBarPresenter _presenter;
        private AttackHeavyReadyView _readyView;
        private Image _readyIcon;
        private GameObject _goldBorder;

        [SetUp]
        public void SetUp()
        {
            _binder = CreateComponent<MobileInputBinder>("Binder");
            _binder.TouchOverride = TouchOverride.ForceTouch;
            _buttons = CreateComponent<MobileActionButtons>("Buttons");
            TestHelpers.SetField(_binder, "_buttons", _buttons);

            _readyIcon = CreateComponent<Image>("ReadyIcon");
            _goldBorder = NewGameObject("GoldBorder");
            _readyView = CreateComponent<AttackHeavyReadyView>("ReadyView");
            TestHelpers.SetField(_readyView, "_icon", _readyIcon);
            TestHelpers.SetField(_readyView, "_goldBorder", _goldBorder);

            _presenter = CreateComponent<MobileSkillBarPresenter>("Presenter");
            var skillButtons = new List<MobileSkillButton>();
            foreach (var skillId in AllSkillIds)
            {
                var button = CreateComponent<MobileSkillButton>("Skill" + skillId);
                TestHelpers.SetField(button, "_skillId", skillId);
                TestHelpers.SetField(button, "_buttons", _buttons);

                var cooldown = CreateComponent<Image>("Cooldown" + skillId);
                var charge = CreateComponent<Image>("Charge" + skillId);
                var countdown = CreateComponent<TextMeshProUGUI>("Countdown" + skillId);
                TestHelpers.SetField(button, "_cooldownFill", cooldown);
                TestHelpers.SetField(button, "_chargeFill", charge);
                TestHelpers.SetField(button, "_countdownText", countdown);

                _skillButtons[skillId] = button;
                _cooldowns[skillId] = cooldown;
                _charges[skillId] = charge;
                _countdowns[skillId] = countdown;
                skillButtons.Add(button);
            }

            TestHelpers.SetField(_presenter, "_inputBinder", _binder);
            TestHelpers.SetField(_presenter, "_skillButtons", skillButtons.ToArray());
            TestHelpers.SetField(_presenter, "_attackReadyView", _readyView);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
            for (int i = _createdAssets.Count - 1; i >= 0; i--)
            {
                if (_createdAssets[i] != null) Object.DestroyImmediate(_createdAssets[i]);
            }
            _createdAssets.Clear();
        }

        [Test]
        public void test_no_local_entry_resets_cooldown_charge_and_attack_ready()
        {
            // Arrange
            var entry = CreateEntry("LocalEntry");
            TestHelpers.SetField(entry, "_canFireHeavy", true);
            StartCooldown(CoordinatorOf(entry), SkillID.SkillQ, 10f);
            Bind(entry);

            Refresh();
            Assert.IsTrue(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "前置：绑定时应显示 Q 冷却");
            Assert.IsTrue(_goldBorder.activeSelf, "前置：剑气就绪时应显示金边");
            _skillButtons[SkillID.SkillQ].RenderCharge(0.5f);
            Assert.IsTrue(_charges[SkillID.SkillQ].gameObject.activeSelf, "前置：蓄力环应可见");

            // Act（本地角色丢失）
            TestHelpers.SetField(_binder, "_boundEntry", null);
            TestHelpers.SetField(_binder, "_inputManager", null);
            Refresh();

            // Assert
            Assert.IsFalse(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "无本地角色时冷却遮罩必须复位");
            Assert.IsFalse(_countdowns[SkillID.SkillQ].gameObject.activeSelf, "无本地角色时倒计时必须复位");
            Assert.IsFalse(_charges[SkillID.SkillQ].gameObject.activeSelf, "无本地角色时蓄力环必须复位");
            Assert.IsFalse(_goldBorder.activeSelf, "无本地角色时普攻金边必须复位");
            Assert.AreEqual(Color.white, _readyIcon.color, "无本地角色时普攻图标必须恢复默认色");
        }

        [Test]
        public void test_binding_change_reads_new_character()
        {
            // Arrange
            var first = CreateEntry("First");
            var second = CreateEntry("Second");
            StartCooldown(CoordinatorOf(first), SkillID.SkillQ, 10f);
            Bind(first);
            Refresh();
            Assert.IsTrue(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "前置：旧角色 Q 冷却应可见");

            // Act
            StartCooldown(CoordinatorOf(second), SkillID.Ultimate, 10f);
            Bind(second);
            Refresh();

            // Assert
            Assert.IsFalse(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "换绑后不得继续显示旧角色冷却");
            Assert.IsTrue(_cooldowns[SkillID.Ultimate].gameObject.activeSelf, "换绑后必须读取新角色冷却");
        }

        [Test]
        public void test_charge_ring_reads_current_charging_skill_progress()
        {
            // Arrange（真实 Charging 状态：Casting 0.1s → Charging，蓄力 0.5s/上限 2s）
            var entry = CreateEntry("ChargingEntry");
            var coordinator = CreateChargingCoordinator(entry.gameObject, SkillID.ChargeBlast);
            TestHelpers.SetField(entry, "_skillCoordinator", coordinator);
            Bind(entry);

            // Act
            Refresh();

            // Assert
            Assert.AreEqual(SkillSubState.Charging, coordinator.CurrentSubState);
            Assert.IsTrue(_charges[SkillID.ChargeBlast].gameObject.activeSelf, "当前蓄力技能必须显示蓄力环");
            Assert.AreEqual(coordinator.CurrentSkill.GetChargeProgress(),
                _charges[SkillID.ChargeBlast].fillAmount, 0.0001f,
                "蓄力环必须读取当前技能的 GetChargeProgress()");
            Assert.IsFalse(_charges[SkillID.Fireball].gameObject.activeSelf, "非当前技能不得显示蓄力环");
            Assert.IsFalse(_charges[SkillID.SkillQ].gameObject.activeSelf, "非蓄力技能不得显示蓄力环");
        }

        [Test]
        public void test_hold_to_aim_charging_hides_charge_ring()
        {
            // Arrange（火球 HoldToAim、Charging）
            var entry = CreateEntry("AimEntry");
            var data = CreateAsset<ProjectileSkillData>();
            TestHelpers.SetField(data, "_skillId", (int)SkillID.Fireball);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            TestHelpers.SetField(data, "_holdToAim", true);
            TestHelpers.SetField(data, "_skillType", SkillType.Projectile);

            var coordinator = new SkillCoordinator(new TestHelpers.FakeTarget(entry.gameObject.transform));
            coordinator.RegisterSkill(data);
            var executor = new SkillExecutor(new TestHelpers.FakeTarget(entry.gameObject.transform), data);
            var machine = new FakeTimeMachine(data);
            InjectMachine(executor, machine);
            executor.TryStart();
            machine.Now = 0.15f;
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
            TestHelpers.SetField(coordinator, "_currentSkill", executor);
            TestHelpers.SetField(entry, "_skillCoordinator", coordinator);
            Bind(entry);

            machine.Now = 0.65f; // 蓄力 0.5s / 上限 2s = 0.25
            executor.Update(0.01f);
            Assert.Greater(executor.GetChargeProgress(), 0f, "前置：按住瞄准期间蓄力进度必须大于 0");

            // Act
            Refresh();

            // Assert
            Assert.IsFalse(_charges[SkillID.Fireball].gameObject.activeSelf,
                "按住瞄准期间不得显示蓄力环");
        }

        [Test]
        public void test_gesture_survives_casting_then_resets_after_charging_ends()
        {
            // Arrange（按下 → Casting，按住瞄准期间逐帧刷新）
            var entry = CreateEntry("AimLifecycle");
            TestHelpers.SetField(entry, "_stamina", new StaminaSystem(StaminaConfig.Default));
            var data = CreateAsset<ProjectileSkillData>();
            TestHelpers.SetField(data, "_skillId", (int)SkillID.Fireball);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            TestHelpers.SetField(data, "_holdToAim", true);
            TestHelpers.SetField(data, "_skillType", SkillType.Projectile);

            var coordinator = new SkillCoordinator(new TestHelpers.FakeTarget(entry.gameObject.transform));
            coordinator.RegisterSkill(data);
            var executor = new SkillExecutor(new TestHelpers.FakeTarget(entry.gameObject.transform), data);
            var machine = new FakeTimeMachine(data);
            InjectMachine(executor, machine);
            executor.TryStart();
            machine.Now = 0.05f; // 仍在 Casting（0.1s 后才进 Charging）
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Casting, executor.CurrentSubState, "前置：应仍在 Casting");
            TestHelpers.SetField(coordinator, "_currentSkill", executor);
            TestHelpers.SetField(entry, "_skillCoordinator", coordinator);
            Bind(entry);

            var aim = CreateAimJoystick();
            TestHelpers.SetField(_presenter, "_fireballAim", aim);
            aim.OnPointerDown(new PointerEventData(null));
            Assert.IsTrue(aim.IsGestureActive, "前置：按下后手势必须激活");

            // Act（Casting 阶段逐帧刷新）
            for (int i = 0; i < 3; i++) Refresh();

            // Assert（按下帧与 Casting 不得复位手势）
            Assert.IsTrue(aim.IsGestureActive, "按下帧 / Casting 阶段不得复位手势");
            Assert.IsTrue(aim.Interactable, "无冷却且有精力时应可交互");

            // Act（进入 Charging）
            machine.Now = 0.15f;
            executor.Update(0.01f);
            Refresh();

            // Assert（Charging 期间手势保留）
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
            Assert.IsTrue(aim.IsGestureActive, "Charging 期间手势必须保留");

            // Act（技能结束）
            executor.ForceComplete();
            Assert.AreNotEqual(SkillSubState.Charging, executor.CurrentSubState);
            Refresh();

            // Assert（离开 Charging 的边沿复位，且不合成 Cancel）
            Assert.IsFalse(aim.IsGestureActive, "离开 Charging 后必须复位手势");
            Assert.AreEqual(AimReleaseKind.None, aim.ConsumeReleaseKind(),
                "复位不得合成 Cancel 释放");
        }

        [Test]
        public void test_fireball_aim_interactable_follows_cooldown_stamina_and_reset()
        {
            // Arrange
            var entry = CreateEntry("AimGate");
            TestHelpers.SetField(entry, "_stamina", new StaminaSystem(StaminaConfig.Default));
            Bind(entry);
            var aim = CreateAimJoystick();
            TestHelpers.SetField(_presenter, "_fireballAim", aim);

            // Act + Assert（无冷却、有精力）
            Refresh();
            Assert.IsTrue(aim.Interactable, "无冷却且精力充足时必须可施放");

            // Act + Assert（冷却中）
            StartCooldown(CoordinatorOf(entry), SkillID.Fireball, 10f);
            Refresh();
            Assert.IsFalse(aim.Interactable, "火球冷却中不得施放");

            // Arrange（精力耗尽）
            var exhausted = CreateEntry("AimGateExhausted");
            TestHelpers.SetField(exhausted, "_stamina", new StaminaSystem(StaminaConfig.Default));
            exhausted.Stamina.TryConsume(exhausted.Stamina.Max);
            Bind(exhausted);

            // Act + Assert（精力不足）
            Refresh();
            Assert.IsFalse(aim.Interactable, "精力耗尽时不得施放");

            // Act + Assert（无本地角色时复位为不可施放）
            TestHelpers.SetField(_binder, "_boundEntry", null);
            TestHelpers.SetField(_binder, "_inputManager", null);
            Refresh();
            Assert.IsFalse(aim.Interactable, "无本地角色时摇杆必须不可交互");
        }

        [Test]
        public void test_binder_disable_then_enable_restores_presenter_binding()
        {
            // Arrange
            var entry = CreateEntry("LocalEntry");
            StartCooldown(CoordinatorOf(entry), SkillID.SkillQ, 10f);
            Bind(entry);
            Refresh();
            Assert.IsTrue(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "前置：Q 冷却应可见");

            // Act（禁用后技能栏复位）
            Invoke(_binder, "OnDisable");
            Refresh();
            Assert.IsFalse(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "Binder 禁用后不得继续显示冷却");

            // Assert（重新启用后恢复同一角色绑定）
            Invoke(_binder, "Update");
            Assert.AreSame(entry, _binder.BoundEntry, "Binder 重新启用后必须恢复绑定");
            Refresh();
            Assert.IsTrue(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "恢复绑定后必须继续显示真实冷却");
        }

        [Test]
        public void test_destroyed_character_does_not_keep_stale_cooldown()
        {
            // Arrange
            var oldEntry = CreateEntry("OldEntry");
            StartCooldown(CoordinatorOf(oldEntry), SkillID.SkillQ, 10f);
            Bind(oldEntry);
            Refresh();
            Assert.IsTrue(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "前置：旧角色 Q 冷却应可见");

            // Act（旧角色销毁，Binder 重新发现新角色）
            var newEntry = CreateEntry("NewEntry");
            Object.DestroyImmediate(oldEntry.gameObject);
            Invoke(_binder, "Update");
            Refresh();

            // Assert
            Assert.AreSame(newEntry, _binder.BoundEntry, "旧角色销毁后必须绑定新角色");
            Assert.IsFalse(_cooldowns[SkillID.SkillQ].gameObject.activeSelf, "不得继续显示已销毁角色的冷却");
        }

        [Test]
        public void test_character_rebuild_does_not_pass_held_state_to_new_character()
        {
            // Arrange
            var oldEntry = CreateEntry("OldEntry");
            var newEntry = CreateEntry("NewEntry");
            Bind(oldEntry);
            _buttons.OnAttackDown();
            _buttons.OnSkillQDown();
            _buttons.OnSkillRDown();
            _buttons.OnFireballDown();
            _buttons.OnChargeBlastDown();
            _buttons.OnMeteorDown();

            // Act（旧角色销毁后重建）
            Object.DestroyImmediate(oldEntry.gameObject);
            Invoke(_binder, "Update");
            Invoke(_binder, "LateUpdate");

            // Assert
            var newInput = newEntry.GetComponent<InputManager>();
            newInput.Update();
            Assert.IsFalse(_buttons.AttackHeld, "旧角色的普攻按住不得保留到新角色");
            Assert.IsFalse(_buttons.FireballHeld, "旧角色的火球按住不得保留到新角色");
            Assert.IsFalse(_buttons.ChargeBlastHeld, "旧角色的炎爆按住不得保留到新角色");
            Assert.IsFalse(newInput.IsAttackHeld(), "新角色不得继承旧角色的普攻 held");
            Assert.IsFalse(newInput.IsSkillFireballHeld(), "新角色不得继承旧角色的火球 held");
            Assert.IsFalse(newInput.IsSkillChargeBlastHeld(), "新角色不得继承旧角色的炎爆 held");
            Assert.IsFalse(newInput.IsSkill2Pressed(), "Q 一次性锁存不得跨角色");
            Assert.IsFalse(newInput.IsSkill3Pressed(), "R 一次性锁存不得跨角色");
            Assert.IsFalse(newInput.IsSkillMeteorPressed(), "陨石一次性锁存不得跨角色");
        }

        private class FakeTimeMachine : SkillStateMachine
        {
            public float Now;
            public FakeTimeMachine(SkillData data) : base(data) { }
            protected override float GetCurrentTime() => Now;
        }

        private static void InjectMachine(SkillExecutor executor, FakeTimeMachine machine)
        {
            var oldMachine = TestHelpers.GetField(executor, "_stateMachine");
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            string[] eventFields =
            {
                "_onStateChanged", "_onHitboxFrame", "_onHitConfirm",
                "_onSkillCompleted", "_onSkillInterrupted"
            };
            foreach (var name in eventFields)
            {
                var field = typeof(SkillStateMachine).GetField(name, flags);
                Assert.IsNotNull(field, "缺少状态机事件字段 " + name);
                field.SetValue(machine, field.GetValue(oldMachine));
            }
            TestHelpers.SetField(executor, "_stateMachine", machine);
        }

        /// <summary>构造一个处于 Charging、蓄力进度约 0.25 的真实协调器（同 SkillCoordinatorChargeTests 模式）。</summary>
        private static SkillCoordinator CreateChargingCoordinator(GameObject owner, SkillID skillId)
        {
            var data = ScriptableObject.CreateInstance<ChargedSkillData>();
            TestHelpers.SetField(data, "_skillId", (int)skillId);
            TestHelpers.SetField(data, "_minChargeTime", 0.3f);
            TestHelpers.SetField(data, "_maxChargeTime", 2f);
            TestHelpers.SetField(data, "_skillType", SkillType.Charged);

            var coordinator = new SkillCoordinator(new TestHelpers.FakeTarget(owner.transform));
            coordinator.RegisterSkill(data);

            var executor = new SkillExecutor(new TestHelpers.FakeTarget(owner.transform), data);
            var machine = new FakeTimeMachine(data);
            InjectMachine(executor, machine);
            executor.TryStart();
            machine.Now = 0.15f; // Casting 0.1s → Charging
            executor.Update(0.01f);
            Assert.AreEqual(SkillSubState.Charging, executor.CurrentSubState);
            TestHelpers.SetField(coordinator, "_currentSkill", executor);

            machine.Now = 0.65f; // 蓄力 0.5s / 上限 2s = 0.25
            coordinator.Update(0.01f);
            Assert.Greater(coordinator.CurrentSkill.GetChargeProgress(), 0f);
            return coordinator;
        }

        private Sys3CEntry CreateEntry(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            var entry = go.AddComponent<Sys3CEntry>();
            go.AddComponent<InputManager>();
            TestHelpers.SetField(entry, "_skillCoordinator",
                new SkillCoordinator(new TestHelpers.FakeTarget(go.transform)));
            return entry;
        }

        private void Bind(Sys3CEntry entry)
        {
            TestHelpers.SetField(_binder, "_boundEntry", entry);
            TestHelpers.SetField(_binder, "_inputManager", entry.GetComponent<InputManager>());
        }

        private static SkillCoordinator CoordinatorOf(Sys3CEntry entry)
            => (SkillCoordinator)TestHelpers.GetField(entry, "_skillCoordinator");

        private static void StartCooldown(SkillCoordinator coordinator, SkillID skillId, float duration)
        {
            var manager = (CooldownManager)TestHelpers.GetField(coordinator, "_cooldownManager");
            manager.StartCooldown((int)skillId, duration);
        }

        private void Refresh() => Invoke(_presenter, "Update");

        private T CreateAsset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            _createdAssets.Add(asset);
            return asset;
        }

        private MobileSkillAimJoystick CreateAimJoystick()
        {
            var go = new GameObject("AimJoystick", typeof(RectTransform));
            _created.Add(go);
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            _created.Add(iconGo);
            iconGo.transform.SetParent(go.transform, false);
            var aim = go.AddComponent<MobileSkillAimJoystick>();
            TestHelpers.SetField(aim, "_buttonRect", (RectTransform)go.transform);
            TestHelpers.SetField(aim, "_icon", (RectTransform)iconGo.transform);
            Invoke(aim, "Awake");
            return aim;
        }

        private T CreateComponent<T>(string name) where T : Component
            => NewGameObject(name).AddComponent<T>();

        private GameObject NewGameObject(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        private static void Invoke(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "缺少方法 " + methodName);
            method.Invoke(target, null);
        }
    }
}
