using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

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

        [Test]
        public void SharedRelease_PutsEarlyAndLateClientsOnTheSamePath_ThenDissolves()
        {
            var lateRoot = new GameObject("Late echo client");
            try
            {
                var late = lateRoot.AddComponent<SwordQiEchoPresenter>();
                double clock = 100d;
                _presenter.Prepare(_owner.transform, 4, 5d, now: () => clock);
                _presenter.Tick(100.9d);
                for (int i = 0; i < 4; i++)
                    Assert.That(_presenter.SwordPosition(i).y, Is.InRange(0.58f, 0.66f));
                var state = new SwordQiEchoState
                {
                    Phase = SwordQiEchoPhase.Started,
                    SwordCount = 4, StartTick = 15, EndTick = 45,
                    OrbitStartedTimestamp = 100 * TimeSpan.TicksPerSecond,
                    ServerTimestamp = 101 * TimeSpan.TicksPerSecond,
                    Contacts = new List<SwordQiEchoContactMessage>
                    {
                        new SwordQiEchoContactMessage { TargetId = 10, SwordIndex = 2, OffsetTicks = 6, Z = 8f },
                        new SwordQiEchoContactMessage { TargetId = 11, SwordIndex = 2, OffsetTicks = 24, X = 8f, Z = 4f },
                    },
                };
                clock = 101.08d;
                _presenter.Play(_owner.transform, state, clock - 101d, () => clock);
                _presenter.Tick(101.41d);
                clock = 101.85d;
                late.Play(_owner.transform, state, clock - 101d, () => clock);
                _presenter.Tick(clock);
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(Vector3.Distance(_presenter.SwordPosition(i), late.SwordPosition(i)), Is.LessThan(0.001f));
                    Assert.That(Quaternion.Angle(_root.transform.GetChild(i).rotation,
                        lateRoot.transform.GetChild(i).rotation), Is.LessThan(0.1f));
                }
                Assert.That(_presenter.SwordPosition(0).magnitude, Is.LessThan(2.2f));

                _owner.transform.position = new Vector3(4f, 0f, -2f);
                _presenter.Tick(103.13d);
                var orbit = _presenter.SwordPosition(2) - _owner.transform.position;
                Assert.That(new Vector2(orbit.x, orbit.z).magnitude, Is.EqualTo(1.5f).Within(0.05f));
                Assert.That(orbit.y, Is.InRange(0.58f, 0.66f));
                _presenter.Tick(103.60d);
                var collapsed = _presenter.SwordPosition(2) - _owner.transform.position;
                Assert.That(new Vector2(collapsed.x, collapsed.z).magnitude, Is.LessThan(0.8f));
                var block = new MaterialPropertyBlock();
                _root.transform.GetChild(2).GetComponent<TrailRenderer>().GetPropertyBlock(block);
                Assert.That(block.GetColor("_Color").a, Is.LessThan(0.6f));
                _presenter.Tick(103.90d);
                Assert.Zero(_presenter.ActiveSwordCount);
            }
            finally { Object.DestroyImmediate(lateRoot); }
        }
    }
}
