using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 任务 11：`T` 键 / 移动端"切换目标"共用同一条一次性按下语义。
    /// `Update` 只重置消费锁存，不得提前清掉 UI 标志 —— 否则 binder 在 LateUpdate
    /// 汇入的按下会活不过下一次 Update 而被静默丢弃。
    /// </summary>
    public class InputManagerTargetSwitchTests
    {
        private GameObject _go;
        private InputManager _input;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("T11_InputManagerTargetSwitch");
            _input = _go.AddComponent<InputManager>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_input_target_switch_ui_flag_produces_one_press_per_consume()
        {
            // Arrange（UI 按钮置位）
            _input.SetTargetSwitchPressedFromUI(true);

            // Act & Assert（同一次按下只消费一次）
            Assert.IsTrue(_input.IsTargetSwitchPressed(), "UI 切换目标按下应触发一次");
            Assert.IsFalse(_input.IsTargetSwitchPressed(), "同一次按下只消费一次");
        }

        [Test]
        public void test_input_target_switch_ui_flag_is_consumed_once_across_update()
        {
            // Arrange（binder 在 LateUpdate 汇入，读取方在下一帧 Update 之后）
            _input.SetTargetSwitchPressedFromUI(true);

            // Act
            _input.Update();

            // Assert（Update 只重置消费锁存，不得清掉 UI 标志；消费仍只有一次）
            Assert.IsTrue(_input.IsTargetSwitchPressed(), "跨 Update 后 UI 按下仍应可读");
            Assert.IsFalse(_input.IsTargetSwitchPressed(), "消费后不得重复触发");
        }
    }
}
