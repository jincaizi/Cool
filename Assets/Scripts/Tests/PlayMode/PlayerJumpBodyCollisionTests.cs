using System.Collections;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Sys3CController = Hotfix.GameSystems.Sys3C.Character.CharacterController;

namespace GameSys.PlayModeTests
{
    /// <summary>
    /// B5：贴怪起跳复现。真实 CharacterController + 实心 layer 10 盒体（NpcBody 同层）。
    /// 记录请求/消费接地值与 CollisionFlags，区分输入门控、接地闪烁、竖直位移被挡。
    /// </summary>
    public class PlayerJumpBodyCollisionTests
    {
        private GameObject _player;
        private GameObject _ground;
        private GameObject _body;
        private Sys3CEntry _entry;
        private Sys3CController _cc;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(20f, 1f, 20f);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "测试用玩家 Prefab 缺失");
            _player = Object.Instantiate(prefab, new Vector3(0f, 2f, 0f), Quaternion.identity);
            _player.layer = 9; // NpcBody 层矩阵只放行本地玩家层 9
            _entry = _player.GetComponent<Sys3CEntry>();
            Assert.IsNotNull(_entry);
            yield return null;

            _entry.enabled = false; // 停掉输入驱动，测试手动步进 CharacterController
            _cc = _entry.CoreController;
            Assert.IsNotNull(_cc);
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null) Object.Destroy(_player);
            if (_body != null) Object.Destroy(_body);
            if (_ground != null) Object.Destroy(_ground);
        }

        private static MoveCommand Forward() => new MoveCommand
        {
            MoveDir = Vector3.forward,
            Rotation = Quaternion.identity,
            IsSprint = false
        };

        private static MoveCommand Idle() => new MoveCommand
        {
            MoveDir = Vector3.zero,
            Rotation = Quaternion.identity
        };

        private IEnumerator SettleOnGround()
        {
            for (int i = 0; i < 180 && !_cc.Data.IsGrounded; i++)
            {
                _cc.Update(Idle());
                yield return null;
            }
            Assert.IsTrue(_cc.Data.IsGrounded, "前置：角色应落到测试地面");
        }

        private IEnumerator StepIdle(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _cc.Update(Idle());
                yield return null;
            }
        }

        private void CreateBody(Vector3 position)
        {
            _body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _body.name = "NpcBody";
            _body.layer = 10; // NpcBody：只与本地玩家层 9 碰撞
            _body.transform.position = position;
            _body.transform.localScale = new Vector3(1f, 2f, 1f);
        }

        private string Probe()
        {
            var uc = _player.GetComponent<UnityEngine.CharacterController>();
            return "requests=" + _cc.JumpRequests
                   + " rejectedUngrounded=" + _cc.JumpRequestsRejectedUngrounded
                   + " consumeGrounded=" + _cc.LastConsumeGrounded
                   + " consumeBodySupport=" + _cc.LastConsumeBodySupport
                   + " grounded=" + _cc.Data.IsGrounded
                   + " flags=" + _cc.LastMoveFlags
                   + " ppos=" + _player.transform.position
                   + " body=" + (_body != null ? _body.transform.position : Vector3.zero)
                   + " bodyLayer=" + (_body != null ? _body.layer : -1)
                   + " center=" + uc.center + " h=" + uc.height
                   + " r=" + uc.radius + " skin=" + uc.skinWidth;
        }

        [UnityTest]
        public IEnumerator test_jump_consumes_on_flat_ground()
        {
            yield return SettleOnGround();
            float y0 = _player.transform.position.y;

            _cc.RequestJump();
            _cc.Update(Forward());
            yield return StepIdle(10);

            Assert.AreEqual(1, _cc.JumpsConsumed, "空场起跳必须被消费：" + Probe());
            Assert.Greater(_player.transform.position.y, y0 + 0.05f, "起跳后 Y 必须上升");
        }

        [UnityTest]
        public IEnumerator test_jump_consumes_when_pressed_against_solid_body()
        {
            yield return SettleOnGround();
            CreateBody(new Vector3(0f, 1f, 1.5f)); // 近面 z=1.0；玩家从 z=0 向前压

            for (int i = 0; i < 40; i++)
            {
                _cc.Update(Forward());
                yield return null;
            }
            float y0 = _player.transform.position.y;

            _cc.RequestJump();
            _cc.Update(Forward());
            yield return StepIdle(10);

            Assert.AreEqual(1, _cc.JumpsConsumed, "贴怪起跳未消费：" + Probe());
            Assert.Greater(_player.transform.position.y, y0 + 0.05f,
                "起跳被消费但 Y 未上升（竖直位移被挡）：" + Probe());
        }

        [UnityTest]
        public IEnumerator test_jump_consumes_when_body_overlaps_player()
        {
            yield return SettleOnGround();
            CreateBody(_player.transform.position + Vector3.up); // 盒体与玩家胶囊交叠

            for (int i = 0; i < 10; i++)
            {
                _cc.Update(Forward());
                yield return null;
            }
            float y0 = _player.transform.position.y;

            _cc.RequestJump();
            _cc.Update(Forward());
            yield return StepIdle(10);

            Assert.AreEqual(1, _cc.JumpsConsumed, "交叠状态起跳未消费：" + Probe());
            Assert.Greater(_player.transform.position.y, y0 + 0.05f,
                "交叠状态起跳 Y 未上升：" + Probe());
        }
    }
}
