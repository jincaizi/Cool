using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    public class BlockStaminaTests
    {
        private GameObject _player;
        private Sys3CEntry _entry;
        private Hotfix.GameSystems.Sys3C.Character.CharacterController _cc;
        private NetworkMode _previousMode;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab, "缺少 Assets/Resources/PlayerEntryForTest.prefab");
            _player = Object.Instantiate(prefab);
            yield return null;

            _entry = _player.GetComponent<Sys3CEntry>();
            _cc = _entry.CoreController;
            Assert.IsNotNull(_entry.Stamina);
            Assert.IsNotNull(_cc);
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_player != null) Object.DestroyImmediate(_player);
        }

        private void SetData(System.Func<CharacterData, CharacterData> mutate)
        {
            var field = typeof(Hotfix.GameSystems.Sys3C.Character.CharacterController)
                .GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(_cc, mutate((CharacterData)field.GetValue(_cc)));
        }

        private void SetStamina(float value)
        {
            _entry.Stamina.Refill();
            _entry.Stamina.TryConsume(_entry.Stamina.Max - value);
        }

        private void EnterDefend()
        {
            SetData(d => { d.IsGrounded = true; return d; });
            Assert.IsTrue(_cc.TryEnterDefend(), "前置失败：应能进入防御");
        }

        [Test]
        public void test_block_stamina_online_blocked_hit_consumes_fifteen()
        {
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);
            Assert.AreEqual(35f, _entry.Stamina.Current, 0.01f);
            Assert.IsFalse(_entry.Stamina.IsExhausted);
        }

        [Test]
        public void test_block_stamina_block_cost_exhausts_and_forces_shield_down()
        {
            EnterDefend();
            SetStamina(15f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);

            Assert.AreEqual(0f, _entry.Stamina.Current, 0.01f);
            Assert.IsTrue(_entry.Stamina.IsExhausted);
            Assert.IsFalse(_cc.IsDefending, "力竭必须强制收盾");
            Assert.IsFalse(_entry.Stamina.CanStartConsuming);
        }

        [Test]
        public void test_block_stamina_non_blocked_hit_consumes_nothing()
        {
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: false);
            Assert.AreEqual(50f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_two_blocked_hits_consume_thirty()
        {
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);
            _entry.ApplyServerDamage(10f, Vector3.zero, 80f, isBlocked: true);
            Assert.AreEqual(20f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_charges_even_when_local_shield_already_released()
        {
            _cc.TryExitDefend();
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);
            Assert.AreEqual(35f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_charges_while_stunned()
        {
            _cc.StatusControllerAdapter.AddStun(1f);
            SetStamina(50f);
            _entry.ApplyServerDamage(10f, Vector3.zero, 90f, isBlocked: true);
            Assert.AreEqual(35f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_lethal_block_still_charges_before_death()
        {
            NetworkModeService.Save(NetworkMode.Online);
            SetStamina(15f);
            float staminaAtDeath = -1f;
            _entry.OnServerDeath += () => staminaAtDeath = _entry.Stamina.Current;

            _entry.ApplyServerDamage(200f, Vector3.zero, 0f, isBlocked: true);

            Assert.AreEqual(0f, staminaAtDeath, 0.01f,
                "致死格挡也必须先扣精，再走死亡回调");
        }

        [Test]
        public void test_block_stamina_offline_block_consumes_fifteen()
        {
            EnterDefend();
            SetStamina(50f);
            ((IDamageable)_entry).TakeDamage(
                DamageBlock.CreateDefault(50f, 0f), -_entry.transform.forward);
            Assert.AreEqual(35f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_block_stamina_offline_repeated_blocks_never_break_shield()
        {
            EnterDefend();
            SetStamina(100f);
            for (int i = 0; i < 4; i++)
                ((IDamageable)_entry).TakeDamage(
                    DamageBlock.CreateDefault(50f, 0f), -_entry.transform.forward);

            Assert.IsTrue(_cc.IsDefending, "耐久删除后，连续格挡不应强制收盾");
            Assert.AreEqual(40f, _entry.Stamina.Current, 0.01f);
        }

        [Test]
        public void test_defend_is_cleared_on_death()
        {
            EnterDefend();
            _entry.ApplyServerDamage(200f, Vector3.zero, 0f);
            Assert.IsFalse(_cc.IsDefending, "死亡必须收盾");
        }

        [Test]
        public void test_defend_is_cleared_on_respawn()
        {
            EnterDefend();
            _entry.ApplyServerRespawn(100f);
            Assert.IsFalse(_cc.IsDefending, "复活必须收盾");
        }
    }
}
