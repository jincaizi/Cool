using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.FSM;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    /// <summary>
    /// P6 ①：受击反馈接线——受击必须真正进入 HitFSM 并切换活动层，
    /// 受击结束后必须把活动层交还 Base（否则 isAttacking 读数失真）。
    /// FSMManager 构造会无条件调用 Animator.SetLayerWeight 与
    /// CharacterController.SetStateCoordinator，两者都不能为 null，故用真实宿主构造。
    /// </summary>
    public class FSMManagerHitFeedbackTests
    {
        private GameObject _host;
        private FSMManager _fsm;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("FSMTestHost");
            var animator = _host.AddComponent<Animator>();
            var unityController = _host.AddComponent<UnityEngine.CharacterController>();
            var characterController = new Hotfix.GameSystems.Sys3C.Character.CharacterController(
                _host.transform, unityController, 0);
            _fsm = new FSMManager(characterController, animator);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
        }

        [Test]
        public void test_handled_damage_enters_hit_state()
        {
            // Arrange
            Assert.AreEqual(HitState.None, _fsm.HitFSM.CurrentState, "初始应为 None");

            // Act
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 1.5f);

            // Assert
            Assert.AreNotEqual(HitState.None, _fsm.HitFSM.CurrentState,
                "受击后必须进入 HitFSM——曾完全不调用 EnterHit，受击链空转");
        }

        [Test]
        public void test_handled_damage_sets_active_layer_to_hit()
        {
            // Arrange & Act
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 1.5f);

            // Assert
            Assert.AreEqual(LayerType.Hit, _fsm.Coordinator.ActiveLayer,
                "受击必须切换活动层，否则 Hit 层与 Attack 层同时驱动 Animator");
        }

        [Test]
        public void test_knockback_damage_enters_knockback_state()
        {
            // Arrange & Act（有击退 → 应进 Knockback 而非最轻的 Hit）
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 2f);

            // Assert
            Assert.AreEqual(HitState.Knockback, _fsm.HitFSM.CurrentState,
                "有击退力度时应进入 Knockback 态");
        }

        [Test]
        public void test_hit_recovery_returns_layer_to_base()
        {
            // Arrange
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 1.5f);
            Assert.AreEqual(LayerType.Hit, _fsm.Coordinator.ActiveLayer);

            // Act（受击动画播完）
            _fsm.HitFSM.Recover();

            // Assert
            Assert.AreEqual(LayerType.Base, _fsm.Coordinator.ActiveLayer,
                "受击结束必须把活动层交还 Base");
        }

        [Test]
        public void test_hit_recovery_does_not_override_a_layer_that_moved_on()
        {
            // Arrange（受击期间技能激活把层设为 Attack——该路径无受击态门禁，D-P6-20 明确接受）
            _fsm.HandleDamage(sourceId: -1, damage: 10f, hitDirection: Vector3.back, knockbackForce: 1.5f);
            _fsm.Coordinator.SetAttackLayerActive();
            Assert.AreEqual(LayerType.Attack, _fsm.Coordinator.ActiveLayer);

            // Act（受击动画播完）
            _fsm.HitFSM.Recover();

            // Assert（D-P6-21 守卫：层已走掉时不得覆盖，否则攻击可被跳跃取消）
            Assert.AreEqual(LayerType.Attack, _fsm.Coordinator.ActiveLayer,
                "受击结束时若层已切到 Attack，不得打回 Base");
        }
    }
}
