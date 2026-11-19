using System.Collections.Generic;
using Cool.SyncCore;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SwordQiEchoPresenterTests
    {
        private GameObject _root;
        private GameObject _owner;
        private SwordQiEchoPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("EchoPresenterRoot");
            _owner = new GameObject("EchoOwner");
            _owner.transform.position = Vector3.zero;
            _presenter = _root.AddComponent<SwordQiEchoPresenter>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_owner);
        }

        [Test]
        public void OneTarget_FourSwords_FirstPassAtContact()
        {
            var contacts = new List<SwordQiEchoContact>
            {
                new SwordQiEchoContact(10L, swordIndex: 0, offsetTicks: 6,
                    position: new Vec3(0f, 0f, 2f)),
            };

            _presenter.Play(_owner.transform, contacts, durationSeconds: 2f,
                elapsedSeconds: 0d, now: () => 0d);

            Assert.AreEqual(4, _presenter.ActiveSwordCount, "单目标仍应生成 4 道剑气");
            _presenter.Tick(0.2d);
            Assert.Greater(Vector3.Distance(_presenter.SwordPosition(0),
                new Vector3(0f, 1.1f, 2f)), 0.2f, "接触前不得提前穿过目标");

            _presenter.Tick(0.4d); // 6 tick / 15Hz
            Assert.Less(Vector3.Distance(_presenter.SwordPosition(0),
                new Vector3(0f, 1.1f, 2f)), 0.01f, "首次穿过必须落在排定接触时刻");

            _presenter.Tick(0.5d);
            Assert.Less(Vector3.Distance(_presenter.SwordPosition(0),
                new Vector3(0f, 1.1f, 2f)), 0.01f, "接触后短暂保持穿透点");

            _presenter.Tick(2.1d);
            Assert.IsFalse(_presenter.IsPlaying);
            Assert.AreEqual(0, _presenter.ActiveSwordCount, "到期后全部回池");
        }

        [Test]
        public void PlayAfterStop_ReusesPoolAndKeepsFourSwords()
        {
            var contacts = new List<SwordQiEchoContact>
            {
                new SwordQiEchoContact(10L, 0, 6, new Vec3(0f, 0f, 2f)),
            };

            _presenter.Play(_owner.transform, contacts, 2f, 0d, () => 0d);
            Assert.AreEqual(4, _presenter.ActiveSwordCount);
            _presenter.Stop();
            Assert.AreEqual(0, _presenter.ActiveSwordCount);

            _presenter.Play(_owner.transform, contacts, 2f, 0d, () => 0d);
            Assert.AreEqual(4, _presenter.ActiveSwordCount);
            _presenter.Stop();
        }

        [Test]
        public void EightTargets_AllScheduledContactsReached()
        {
            var contacts = new List<SwordQiEchoContact>();
            for (int i = 0; i < 8; i++)
            {
                uint offset = i < 4 ? 6u : 24u;
                contacts.Add(new SwordQiEchoContact(i + 1L, i % 4, offset,
                    new Vec3(i + 1f, 0f, 2f)));
            }

            _presenter.Play(_owner.transform, contacts, 2f, 0d, () => 0d);
            _presenter.Tick(0.4d); // 第一轮
            _presenter.Tick(1.6d); // 第二轮 24 tick / 15Hz

            for (int sword = 0; sword < 4; sword++)
            {
                var expected = new Vector3(sword + 5f, 1.1f, 2f);
                Assert.Less(Vector3.Distance(_presenter.SwordPosition(sword), expected), 0.01f,
                    "第二轮接触点应按剑气编号到达");
            }
        }
    }
}
