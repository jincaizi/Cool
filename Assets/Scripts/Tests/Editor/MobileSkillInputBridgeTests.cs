using System.Collections.Generic;
using System.Reflection;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.UI;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class MobileSkillInputBridgeTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private GameObject _root;
        private MobileActionButtons _buttons;
        private InputManager _input;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("MobileSkillInputBridgeTests");
            _created.Add(_root);
            _buttons = _root.AddComponent<MobileActionButtons>();
            _input = _root.AddComponent<InputManager>();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();
            // 失焦用例会置位静态阻断门；不清会污染同域内后续输入测试。
            InputBlockGate.ClearAll();
        }

        [Test]
        public void test_mobile_action_buttons_consume_one_shots_but_keep_holds()
        {
            _buttons.OnSkillQDown();
            _buttons.OnSkillRDown();
            _buttons.OnMeteorDown();
            _buttons.OnFireballDown();
            _buttons.OnChargeBlastDown();

            _buttons.ConsumeSkillPresses();

            Assert.IsFalse(_buttons.SkillQPressed);
            Assert.IsFalse(_buttons.SkillRPressed);
            Assert.IsFalse(_buttons.MeteorPressed);
            Assert.IsFalse(_buttons.FireballPressed);
            Assert.IsFalse(_buttons.ChargeBlastPressed);
            Assert.IsTrue(_buttons.FireballHeld, "消费按下沿不能清除火球按住状态");
            Assert.IsTrue(_buttons.ChargeBlastHeld, "消费按下沿不能清除炎爆按住状态");
        }

        [Test]
        public void test_clear_combat_ui_input_releases_attack_and_charge_holds()
        {
            _buttons.OnAttackDown();
            _buttons.OnFireballDown();
            _buttons.OnChargeBlastDown();
            _input.SetAttackHeld(true);
            _input.SetSkillFireballHeldFromUI(true);
            _input.SetSkillChargeBlastHeldFromUI(true);
            _input.Update();

            _buttons.ClearCombatInput();
            MobileInputBinder.ClearCombatUiInput(_input);
            _input.Update();

            Assert.IsFalse(_buttons.AttackHeld);
            Assert.IsFalse(_buttons.FireballHeld);
            Assert.IsFalse(_buttons.ChargeBlastHeld);
            Assert.IsFalse(_input.IsAttackHeld());
            Assert.IsFalse(_input.IsSkillFireballHeld());
            Assert.IsFalse(_input.IsSkillChargeBlastHeld());
        }

        [Test]
        public void test_binder_disable_then_reenable_restores_bound_entry()
        {
            // Arrange（模拟已绑定的 HUD 与常驻角色）
            var binder = CreateBinder();
            var (entry, input) = CreateEntry("BinderRebindEntry");
            TestHelpers.SetField(binder, "_inputManager", input);
            TestHelpers.SetField(binder, "_boundEntry", entry);
            Assert.AreSame(entry, binder.BoundEntry, "前置：应已绑定本地角色");

            // Act（禁用：必须同时释放两个引用，并清掉旧 InputManager 的触摸状态）
            _buttons.OnAttackDown();
            _buttons.OnFireballDown();
            Invoke(binder, "LateUpdate");
            input.Update();
            Assert.IsTrue(input.IsAttackHeld(), "前置：普攻 held 已汇入旧 InputManager");

            Invoke(binder, "OnDisable");

            // Assert
            Assert.IsNull(binder.BoundEntry, "禁用必须释放 HUD 绑定");
            input.Update();
            Assert.IsFalse(input.IsAttackHeld(), "禁用必须清掉旧 InputManager 的普攻 held");
            Assert.IsFalse(input.IsSkillFireballHeld(), "禁用必须清掉旧 InputManager 的火球 held");

            // Act & Assert（重新启用：Update 必须重新发现并恢复绑定）
            Invoke(binder, "Update");
            Assert.AreSame(entry, binder.BoundEntry, "重新启用后 Update 必须重新绑定本地角色");
        }

        [Test]
        public void test_unconsumed_skill_press_is_dropped_on_next_late_update()
        {
            // Arrange（桥接一次按下；消费者因死亡/防御本帧没有读取）
            var binder = CreateBinder();
            TestHelpers.SetField(binder, "_inputManager", _input);
            _buttons.OnSkillQDown();
            _buttons.OnSkillRDown();
            _buttons.OnFireballDown();
            _buttons.OnChargeBlastDown();
            _buttons.OnMeteorDown();
            Invoke(binder, "LateUpdate");

            // Act（下一帧按钮已无按下沿；未被消费的锁存必须被 false 覆盖）
            Invoke(binder, "LateUpdate");

            // Assert
            Assert.IsFalse(_input.IsSkill2Pressed(), "未被消费的 Q 锁存必须在下一帧丢弃");
            Assert.IsFalse(_input.IsSkill3Pressed(), "未被消费的 R 锁存必须在下一帧丢弃");
            Assert.IsFalse(_input.IsSkillFireballPressed(), "未被消费的火球锁存必须在下一帧丢弃");
            Assert.IsFalse(_input.IsSkillChargeBlastPressed(), "未被消费的炎爆锁存必须在下一帧丢弃");
            Assert.IsFalse(_input.IsSkillMeteorPressed(), "未被消费的陨石锁存必须在下一帧丢弃");
        }

        [Test]
        public void test_focus_loss_cancels_attack_hold_and_clears_heavy_ready()
        {
            // Arrange
            var binder = CreateBinder();
            var (entry, input) = CreateEntry("FocusLossEntry");
            TestHelpers.SetField(binder, "_inputManager", input);
            TestHelpers.SetField(binder, "_boundEntry", entry);
            input.SetAttackHeld(true);
            input.Update();
            Assert.IsTrue(input.IsAttackHeld(), "前置：普攻应处于按住状态");
            TestHelpers.SetField(entry, "_canFireHeavy", true);

            // Act
            Invoke(binder, "OnApplicationFocus", false);

            // Assert
            Assert.IsFalse(entry.CanFireHeavy, "失焦必须清除剑气就绪，避免金边残留");
            input.Update();
            Assert.IsFalse(input.IsAttackHeld(), "失焦必须取消普攻按住");
            Assert.Less(input.GetAttackReleaseDuration(), 0f,
                "失焦不得合成一次释放时长（否则会补放一次重击）");
        }

        [Test]
        public void test_death_clears_heavy_ready()
        {
            // Arrange
            var (entry, input) = CreateEntry("DeathEntry");
            TestHelpers.SetField(entry, "_inputManager", input);
            TestHelpers.SetField(entry, "_canFireHeavy", true);
            TestHelpers.SetField(entry, "_isDead", true);

            // Act
            Invoke(entry, "HandleInput");

            // Assert
            Assert.IsFalse(entry.CanFireHeavy, "死亡必须清除剑气就绪");
        }

        [Test]
        public void test_binder_forwards_fireball_aim_and_release()
        {
            // Arrange（binder 已绑定 InputManager 与瞄准摇杆）
            var binder = CreateBinder();
            var aim = new GameObject("Aim").AddComponent<MobileSkillAimJoystick>();
            _created.Add(aim.gameObject);
            TestHelpers.SetField(binder, "_fireballAim", aim);
            TestHelpers.SetField(binder, "_inputManager", _input);

            // 直接驱动纯状态，绕过 UGUI 指针
            TestHelpers.SetField(aim, "_state",
                new AimGestureState(100f, 0.1f, 1.4f, 1.2f));
            var state = (AimGestureState)TestHelpers.GetField(aim, "_state");
            state.Press(Vector2.zero);
            state.Drag(new Vector2(150f, 0f));
            TestHelpers.SetField(aim, "_releaseKind", AimReleaseKind.Cancel);

            // Act（帧末汇入）
            Invoke(binder, "LateUpdate");

            // Assert
            Assert.IsTrue(_input.IsSkillFireballAimActive());
            Assert.IsTrue(_input.IsSkillFireballCancelArmed());
            Assert.IsTrue(_input.ConsumeSkillFireballRelease(out bool canceled));
            Assert.IsTrue(canceled);
        }

        private MobileInputBinder CreateBinder()
        {
            var go = new GameObject("MobileInputBinder");
            _created.Add(go);
            var binder = go.AddComponent<MobileInputBinder>();
            binder.TouchOverride = TouchOverride.ForceTouch;
            TestHelpers.SetField(binder, "_buttons", _buttons);
            return binder;
        }

        private (Sys3CEntry entry, InputManager input) CreateEntry(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            var entry = go.AddComponent<Sys3CEntry>();
            var input = go.AddComponent<InputManager>();
            return (entry, input);
        }

        private static void Invoke(object target, string methodName, params object[] args)
        {
            foreach (var method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (method.Name != methodName || method.GetParameters().Length != args.Length) continue;
                method.Invoke(target, args);
                return;
            }
            Assert.Fail("缺少方法 " + methodName);
        }
    }
}
