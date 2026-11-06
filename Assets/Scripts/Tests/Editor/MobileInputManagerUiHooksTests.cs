using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Task 6 修复轮：`SetAttackHeld` / `SetJumpPressed` 是"按住=重击、松开=重击释放"
    /// 与"一次跳跃只跳一次"这两条既有语义的唯一新入口，必须与鼠标路径等价。
    /// `InputManager.Update()` 是公开方法，可直接调用（本类不依赖 MonoBehaviour 生命周期）。
    /// </summary>
    public class MobileInputManagerUiHooksTests
    {
        private GameObject _go;
        private InputManager _input;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("T6_InputManagerUiHooks");
            _input = _go.AddComponent<InputManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_ui_attack_hold_yields_exactly_one_press_edge()
        {
            // Arrange（UI 按住 = MobileActionButtons.AttackHeld 经 binder 汇入）
            _input.SetAttackHeld(true);

            // Act & Assert（一次按住只产生**一个**按下沿，持续按住不得重复触发普攻）
            _input.Update();
            Assert.IsTrue(_input.IsAttackJustPressed(), "UI 按下应产生一个按下沿");
            _input.Update();
            Assert.IsFalse(_input.IsAttackJustPressed(), "持续按住不得重复产生按下沿");
            Assert.IsTrue(_input.IsAttackHeld(), "按住期间 IsAttackHeld 应为 true");
        }

        [Test]
        public void test_ui_attack_release_reports_hold_duration()
        {
            // Arrange（按住一帧后松开）
            _input.SetAttackHeld(true);
            _input.Update();

            // Act
            _input.SetAttackHeld(false);
            _input.Update();

            // Assert（松开必须给出 >=0 的按住时长，否则 Sys3CEntry 的重击分支拿不到释放事件）
            Assert.IsFalse(_input.IsAttackHeld(), "松开后不应仍为 held");
            Assert.GreaterOrEqual(_input.GetAttackReleaseDuration(), 0f,
                "UI 松开必须给出按住时长（>= 0）");
        }

        [Test]
        public void test_ui_jump_press_jumps_exactly_once()
        {
            // Arrange（binder 在帧末汇入的一次性按下）
            _input.SetJumpPressed(true);

            // Act & Assert（同一帧内只消费一次）
            Assert.IsTrue(_input.IsJumpPressed(), "UI 跳跃按下应触发一次");
            Assert.IsFalse(_input.IsJumpPressed(), "同帧内不得重复触发");

            // 桥接锁存在下一帧落下（binder 的 ConsumeJump 之后）——不得残留第二次跳跃
            _input.SetJumpPressed(false);
            _input.Update();
            Assert.IsFalse(_input.IsJumpPressed(), "锁存落下后不得再有跳跃");
        }

        [Test]
        public void test_ui_skill_q_r_and_meteor_each_emit_one_press_edge()
        {
            _input.SetSkillQPressedFromUI(true);
            _input.SetSkillRPressedFromUI(true);
            _input.SetSkillMeteorPressedFromUI(true);

            Assert.IsTrue(_input.IsSkill2Pressed(), "Q 应产生一次按下沿");
            Assert.IsFalse(_input.IsSkill2Pressed(), "Q 同一按下不得重复触发");
            Assert.IsTrue(_input.IsSkill3Pressed(), "R 应产生一次按下沿");
            Assert.IsFalse(_input.IsSkill3Pressed(), "R 同一按下不得重复触发");
            Assert.IsTrue(_input.IsSkillMeteorPressed(), "陨石应产生一次按下沿");
            Assert.IsFalse(_input.IsSkillMeteorPressed(), "陨石同一按下不得重复触发");
        }

        [Test]
        public void test_ui_fireball_press_hold_release_matches_keyboard_semantics()
        {
            _input.SetSkillFireballPressedFromUI(true);
            _input.SetSkillFireballHeldFromUI(true);

            Assert.IsTrue(_input.IsSkillFireballPressed(), "火球按下应产生一次边沿");
            Assert.IsFalse(_input.IsSkillFireballPressed(), "火球按下沿不得重复");
            Assert.IsTrue(_input.IsSkillFireballHeld(), "火球按住期间应保持 held");

            _input.SetSkillFireballHeldFromUI(false);
            Assert.IsFalse(_input.IsSkillFireballHeld(), "火球松开后必须清除 held");
        }

        [Test]
        public void test_ui_charge_blast_press_hold_release_matches_keyboard_semantics()
        {
            _input.SetSkillChargeBlastPressedFromUI(true);
            _input.SetSkillChargeBlastHeldFromUI(true);

            Assert.IsTrue(_input.IsSkillChargeBlastPressed(), "炎爆按下应产生一次边沿");
            Assert.IsFalse(_input.IsSkillChargeBlastPressed(), "炎爆按下沿不得重复");
            Assert.IsTrue(_input.IsSkillChargeBlastHeld(), "炎爆按住期间应保持 held");

            _input.SetSkillChargeBlastHeldFromUI(false);
            Assert.IsFalse(_input.IsSkillChargeBlastHeld(), "炎爆松开后必须清除 held");
        }

        [Test]
        public void test_ui_fireball_aim_states_forwarded()
        {
            // Arrange & Act（binder 帧末汇入的瞄准状态）
            _input.SetSkillFireballAimFromUI(true, new Vector2(0.5f, 0f), true);

            // Assert
            Assert.IsTrue(_input.IsSkillFireballAimActive());
            Assert.AreEqual(new Vector2(0.5f, 0f), _input.SkillFireballAim);
            Assert.IsTrue(_input.IsSkillFireballCancelArmed());

            // Act（瞄准结束：状态必须一并复位）
            _input.SetSkillFireballAimFromUI(false, Vector2.zero, false);

            // Assert
            Assert.IsFalse(_input.IsSkillFireballAimActive());
            Assert.AreEqual(Vector2.zero, _input.SkillFireballAim);
            Assert.IsFalse(_input.IsSkillFireballCancelArmed());
        }

        [Test]
        public void test_ui_fireball_release_kind_consumed_once()
        {
            // Arrange
            _input.SetSkillFireballReleaseFromUI(canceled: true);

            // Act & Assert（释放类型只能消费一次）
            Assert.IsTrue(_input.ConsumeSkillFireballRelease(out bool canceled));
            Assert.IsTrue(canceled);
            Assert.IsFalse(_input.ConsumeSkillFireballRelease(out _), "释放类型只能消费一次");
        }

        [Test]
        public void test_ui_fireball_release_latch_not_revived_by_update()
        {
            // Arrange（一次释放锁存并消费）
            _input.SetSkillFireballReleaseFromUI(canceled: true);
            Assert.IsTrue(_input.ConsumeSkillFireballRelease(out bool canceled));
            Assert.IsTrue(canceled);

            // Act（下一帧 Update 会复位消费锁存——已消费的释放不得复活）
            _input.Update();

            // Assert
            Assert.IsFalse(_input.ConsumeSkillFireballRelease(out _),
                "Update 复位消费锁存后，旧释放不得再次可消费");

            // Act（新的按下同样不得复活旧释放）
            _input.SetSkillFireballPressedFromUI(true);

            // Assert
            Assert.IsFalse(_input.ConsumeSkillFireballRelease(out _),
                "新按下不得让旧释放重新可消费");
        }

        [Test]
        public void test_ui_clear_combat_input_resets_aim_state()
        {
            // Arrange
            _input.SetSkillFireballAimFromUI(true, new Vector2(1f, 0f), true);
            _input.SetSkillFireballReleaseFromUI(canceled: true);

            // Act
            MobileInputBinder.ClearCombatUiInput(_input);

            // Assert
            Assert.IsFalse(_input.IsSkillFireballAimActive());
            Assert.AreEqual(Vector2.zero, _input.SkillFireballAim);
            Assert.IsFalse(_input.IsSkillFireballCancelArmed());
            Assert.IsFalse(_input.ConsumeSkillFireballRelease(out _));
        }
    }
}
