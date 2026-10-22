// Assets/Scripts/Tests/Editor/Sys3C/SprintCommandTests.cs
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 冲刺命令层：力竭锁存 + 冲刺时方向归一化。
    /// 既有门禁用例 test_input_sprint_gate_respects_allowed（PlayMode）保持不动。
    /// </summary>
    public class SprintCommandTests
    {
        private GameObject _go;
        private InputManager _input;

        private sealed class FakeMoveAdapter : IInputAdapter
        {
            private readonly Vector3 _move;

            public FakeMoveAdapter(Vector3 move) => _move = move;

            public string AdapterName => "Fake";
            public Vector3 GetMoveInput() => _move;
            public Vector2 GetCameraRotationInput() => Vector2.zero;
            public bool HasMoveInput() => _move.sqrMagnitude > 0f;
            public bool HasCameraRotationInput() => false;
        }

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("SprintCommandTests");
            _input = _go.AddComponent<InputManager>();
            _input.SetAdapter(new FakeMoveAdapter(Vector3.forward));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        [Test]
        public void test_sprint_gate_requires_release_after_exhaustion()
        {
            // Arrange
            _input.SetSprintHeld(true);
            _input.SprintAllowed = true;
            Assert.IsTrue(_input.GetMoveCommand(Vector3.forward).IsSprint);

            // Act & Assert：力竭当帧退出
            _input.SprintAllowed = false;
            Assert.IsFalse(_input.GetMoveCommand(Vector3.forward).IsSprint);

            // 精力恢复但手指/键仍按住 ⇒ 不得自动再冲刺（锁存）
            _input.SprintAllowed = true;
            Assert.IsFalse(_input.GetMoveCommand(Vector3.forward).IsSprint,
                "力竭后仍按住不得自动恢复冲刺");

            // 释放一次清锁存
            _input.SetSprintHeld(false);
            _input.GetMoveCommand(Vector3.forward);

            // 再按 ⇒ 可以冲刺
            _input.SetSprintHeld(true);
            Assert.IsTrue(_input.GetMoveCommand(Vector3.forward).IsSprint,
                "释放再按后应恢复冲刺");
        }

        [Test]
        public void test_sprint_command_normalizes_move_direction()
        {
            // Arrange：半推（0.5）但已触发冲刺
            _input.SetAdapter(new FakeMoveAdapter(new Vector3(0.5f, 0f, 0f)));
            _input.SetSprintHeld(true);
            _input.SprintAllowed = true;

            // Act
            var command = _input.GetMoveCommand(Vector3.forward);

            // Assert
            Assert.IsTrue(command.IsSprint);
            Assert.AreEqual(1f, command.MoveDir.magnitude, 0.001f,
                "冲刺时方向必须归一化，否则半推冲刺比满推走路还慢");
        }

        [Test]
        public void test_walk_command_keeps_analog_magnitude()
        {
            // Arrange：半推、不冲刺
            _input.SetAdapter(new FakeMoveAdapter(new Vector3(0.5f, 0f, 0f)));
            _input.SetSprintHeld(false);
            _input.SprintAllowed = true;

            // Act
            var command = _input.GetMoveCommand(Vector3.forward);

            // Assert
            Assert.IsFalse(command.IsSprint);
            Assert.AreEqual(0.5f, command.MoveDir.magnitude, 0.001f,
                "非冲刺必须保留模拟量，不能被顺手归一化");
        }
    }
}
