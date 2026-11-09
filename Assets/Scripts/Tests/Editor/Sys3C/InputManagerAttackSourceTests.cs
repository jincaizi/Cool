using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// 攻击来源与阻断恢复。
    ///
    /// 产品决定（2026-11-08）：所有平台取消鼠标左键攻击，普攻只认触控 HUD 的普攻按钮。
    /// 阻断解除后，按钮旧按住不得恢复；必须释放再按下。
    /// </summary>
    public class InputManagerAttackSourceTests
    {
        private GameObject _go;
        private InputManager _input;

        [SetUp]
        public void SetUp()
        {
            InputBlockGate.ClearAll();
            _go = new GameObject("InputManagerAttackSourceTests");
            _input = _go.AddComponent<InputManager>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            InputBlockGate.ClearAll();
        }

        [Test]
        public void test_attack_requires_attack_button_state()
        {
            // Arrange & Act：没有按钮按住时，连续帧都不得产生攻击
            for (int i = 0; i < 3; i++)
            {
                _input.Update();
                Assert.IsFalse(_input.IsAttackJustPressed(), "无按钮输入不得产生普攻");
                Assert.IsFalse(_input.IsAttackHeld(), "无按钮输入不得进入攻击按住");
            }

            // Act：只有按钮按下才产生按下沿
            _input.SetAttackHeld(true);
            _input.Update();

            // Assert
            Assert.IsTrue(_input.IsAttackJustPressed(), "普攻按钮必须能产生按下沿");
        }

        [Test]
        public void test_attack_button_hold_release_reports_duration()
        {
            // Arrange
            _input.SetAttackHeld(true);
            _input.Update();
            Assert.IsTrue(_input.IsAttackJustPressed());

            // Act：按住期间不重复触发
            _input.Update();
            Assert.IsFalse(_input.IsAttackJustPressed(), "持续按住不得重复产生按下沿");
            Assert.IsTrue(_input.IsAttackHeld());

            // Act：松开
            _input.SetAttackHeld(false);
            _input.Update();

            // Assert：松开必须给出按住时长（重击语义）
            Assert.IsFalse(_input.IsAttackHeld());
            Assert.GreaterOrEqual(_input.GetAttackReleaseDuration(), 0f);
        }

        [Test]
        public void test_block_cancels_attack_without_release_and_requires_new_press()
        {
            // Arrange：先按下一刀
            _input.SetAttackHeld(true);
            _input.Update();
            Assert.IsTrue(_input.IsAttackJustPressed());

            // Act：遮罩出现后的第一帧（门控先于采样）
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
            _input.Update();

            // Assert：无释放事件
            Assert.IsFalse(_input.IsAttackHeld());
            Assert.AreEqual(-1f, _input.GetAttackReleaseDuration(),
                "阻断不得把按住合成为重击释放");

            // Act：阻断期间按钮仍尝试写入 true（模拟 Binder 滞后一帧）
            _input.SetAttackHeld(true);

            // Assert：阻断期间的写入必须被忽略
            Assert.IsFalse(_input.IsAttackHeld());

            // Act：解除阻断
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, false);
            _input.Update();

            // Assert：旧按住不得恢复
            Assert.IsFalse(_input.IsAttackJustPressed(), "解除后必须等按钮释放再重新按下");

            // Act：释放后重新按下
            _input.SetAttackHeld(false);
            _input.Update();
            _input.SetAttackHeld(true);
            _input.Update();

            // Assert
            Assert.IsTrue(_input.IsAttackJustPressed(), "释放后重新按下必须正常攻击");
        }

        [Test]
        public void test_block_blocks_ui_and_skill_inputs_written_after_block()
        {
            // Arrange：遮罩已存在
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);

            // Act：阻断期间 Binder/按钮仍尝试写入
            _input.SetAttackHeld(true);
            _input.SetJumpPressed(true);
            _input.SetSkillQPressedFromUI(true);
            _input.SetSkillFireballPressedFromUI(true);
            _input.SetSkillFireballHeldFromUI(true);
            _input.SetSkillChargeBlastHeldFromUI(true);
            _input.SetSprintHeld(true);
            _input.SetDefendHeld(true);
            _input.Update();

            // Assert
            Assert.IsFalse(_input.IsAttackHeld());
            Assert.IsFalse(_input.IsJumpPressed());
            Assert.IsFalse(_input.IsSkill2Pressed());
            Assert.IsFalse(_input.IsSkillFireballPressed());
            Assert.IsFalse(_input.IsSkillFireballHeld());
            Assert.IsFalse(_input.IsSkillChargeBlastHeld());
            Assert.IsFalse(_input.IsSprintHeld());
            Assert.IsFalse(_input.IsDefendHeld());
        }

        [Test]
        public void test_block_clears_pending_fireball_release()
        {
            // Arrange：帧末写入的释放锁存尚未消费
            _input.SetSkillFireballReleaseFromUI(canceled: false);
            Assert.IsTrue(_input.HasSkillFireballReleasePending());

            // Act
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
            _input.Update();

            // Assert
            Assert.IsFalse(_input.HasSkillFireballReleasePending());
            Assert.IsFalse(_input.ConsumeSkillFireballRelease(out _),
                "阻断后不得补放蓄力释放");

            // 解锁后旧释放也不能复活
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, false);
            Assert.IsFalse(_input.ConsumeSkillFireballRelease(out _),
                "解除阻断不得让旧释放重新可消费");
        }

        [Test]
        public void test_block_freezes_move_and_look_until_source_released()
        {
            // Arrange
            var adapter = new FakeAdapter(Vector3.right, new Vector2(1f, 0f));
            _input.SetAdapter(adapter);
            Assert.Greater(_input.GetMoveCommand(Vector3.forward).MoveDir.sqrMagnitude, 0f);

            // Act：遮罩出现后的第一帧
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, true);
            _input.Update();

            // Assert：立即归零
            Assert.AreEqual(0f, _input.GetMoveCommand(Vector3.forward).MoveDir.sqrMagnitude);
            Assert.AreEqual(Vector2.zero, _input.GetCameraRotationInput());

            // Act：解除时输入源仍按着
            InputBlockGate.Set(InputBlockGate.Reason.Landscape, false);

            // Assert：必须先归零
            Assert.AreEqual(0f, _input.GetMoveCommand(Vector3.forward).MoveDir.sqrMagnitude,
                "解除后仍按住的移动输入不得立即恢复");
            Assert.AreEqual(Vector2.zero, _input.GetCameraRotationInput(),
                "解除后仍按住的视角输入不得立即恢复");

            // Act：输入源归零（用户松手），再重新推
            adapter.Move = Vector3.zero;
            adapter.Look = Vector2.zero;
            _input.GetMoveCommand(Vector3.forward);
            _input.GetCameraRotationInput();
            adapter.Move = Vector3.right;
            adapter.Look = new Vector2(1f, 0f);

            // Assert
            Assert.Greater(_input.GetMoveCommand(Vector3.forward).MoveDir.sqrMagnitude, 0f,
                "松手后重新推摇杆必须恢复移动");
            Assert.AreEqual(new Vector2(1f, 0f), _input.GetCameraRotationInput(),
                "松手后重新拖动必须恢复视角");
        }

        private sealed class FakeAdapter : IInputAdapter
        {
            public Vector3 Move;
            public Vector2 Look;

            public FakeAdapter(Vector3 move, Vector2 look)
            {
                Move = move;
                Look = look;
            }

            public string AdapterName => "Fake";
            public Vector3 GetMoveInput() => Move;
            public Vector2 GetCameraRotationInput() => Look;
            public bool HasMoveInput() => Move.sqrMagnitude > 0f;
            public bool HasCameraRotationInput() => Look.sqrMagnitude > 0f;
        }
    }
}
