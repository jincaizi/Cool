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
    }
}
