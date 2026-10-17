using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Stamina;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class StaminaIntegrationTests
    {
        private GameObject _player;
        private Sys3CEntry _entry;
        private Hotfix.GameSystems.Sys3C.Character.CharacterController _cc;
        private InputManager _input;

        private sealed class FakeInputAdapter : IInputAdapter
        {
            public string AdapterName => "Fake";
            public Vector3 GetMoveInput() => Vector3.forward;
            public Vector2 GetCameraRotationInput() => Vector2.zero;
            public bool HasMoveInput() => true;
            public bool HasCameraRotationInput() => false;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab");
            _player = Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            _cc = _entry.CoreController;
            _input = _player.GetComponent<InputManager>();
            Assert.IsNotNull(_entry.Stamina);
            Assert.IsNotNull(_cc);
            Assert.IsNotNull(_input);
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null) Object.DestroyImmediate(_player);
        }

        private void SetData(System.Func<CharacterData, CharacterData> mutate)
        {
            var field = typeof(Hotfix.GameSystems.Sys3C.Character.CharacterController)
                .GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(_cc, mutate((CharacterData)field.GetValue(_cc)));
        }

        private void InvokeUpdateStamina(float deltaTime)
        {
            var method = typeof(Sys3CEntry)
                .GetMethod("UpdateStamina", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "UpdateStamina 不存在");
            method.Invoke(_entry, new object[] { deltaTime });
        }

        [Test]
        public void test_stamina_sprint_drains_when_grounded_and_unlocked()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = true; return d; });
            _cc.LockMovement = false;

            InvokeUpdateStamina(1f); // 内部钳到 0.1s

            Assert.AreEqual(98f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_stamina_sprint_does_not_drain_in_air()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = false; return d; });
            _cc.LockMovement = false;

            InvokeUpdateStamina(1f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_stamina_sprint_does_not_drain_when_locked()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = true; return d; });
            _cc.LockMovement = true;

            InvokeUpdateStamina(1f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_stamina_sprint_does_not_drain_in_hit_layer()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = true; return d; });
            _cc.LockMovement = false;
            _entry.Controller.Coordinator.SetActiveLayer(LayerType.Hit);

            InvokeUpdateStamina(1f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
            _entry.Controller.Coordinator.SetActiveLayer(LayerType.Base);
        }

        [Test]
        public void test_stamina_defend_drains_and_forced_drop()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.IsGrounded = true; return d; });
            Assert.IsTrue(_cc.TryEnterDefend());

            InvokeUpdateStamina(1f);
            Assert.AreEqual(99f, _entry.Stamina.Current, 0.01f, "举盾 10/s × 0.1s");

            _entry.Stamina.TryConsume(98.5f); // 99 → 剩 0.5，下一次 1.0 的消耗扣不动
            InvokeUpdateStamina(1f);

            Assert.IsFalse(_cc.IsDefending, "力竭必须强制收盾");
            Assert.IsTrue(_entry.Stamina.IsExhausted);
        }

        [Test]
        public void test_stamina_dead_does_not_drain()
        {
            _entry.Stamina.Refill();
            SetData(d => { d.BaseState = BaseState.Sprint; d.IsGrounded = true; return d; });
            _cc.LockMovement = false;
            typeof(Sys3CEntry).GetField("_isDead", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_entry, true);

            InvokeUpdateStamina(1f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
            typeof(Sys3CEntry).GetField("_isDead", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_entry, false);
        }

        [Test]
        public void test_stamina_respawn_refills()
        {
            _entry.Stamina.TryConsume(50f);
            Assert.AreEqual(50f, _entry.Stamina.Current, 0.01f);

            _entry.ApplyServerRespawn(100f);

            Assert.AreEqual(100f, _entry.Stamina.Current, 0.01f);
            Assert.IsFalse(_entry.Stamina.IsExhausted);
        }

        [Test]
        public void test_input_sprint_gate_respects_allowed()
        {
            _input.SetAdapter(new FakeInputAdapter());
            _input.SetSprintHeld(true);

            _input.SprintAllowed = true;
            Assert.IsTrue(_input.GetMoveCommand(Vector3.forward).IsSprint);

            _input.SprintAllowed = false;
            Assert.IsFalse(_input.GetMoveCommand(Vector3.forward).IsSprint);
        }
    }
}
