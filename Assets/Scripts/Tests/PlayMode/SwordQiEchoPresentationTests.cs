using System.Collections.Generic;
using Cool.SyncCore;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 剑气回荡表现层：接触时刻、四道剑气、回池与迟到开始。
    /// 使用可注入时钟的 Tick，避免依赖真实帧率。
    /// </summary>
    public class SwordQiEchoPresentationTests
    {
        private GameObject _root;
        private GameObject _owner;
        private SwordQiEchoPresenter _presenter;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("EchoPresentationRoot");
            _owner = new GameObject("EchoPresentationOwner");
            _presenter = _root.AddComponent<SwordQiEchoPresenter>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            Object.DestroyImmediate(_owner);
        }

        [Test]
        public void OneTarget_FourTrailsWithDistinctPasses()
        {
            var contacts = new List<SwordQiEchoContact>
            {
                new SwordQiEchoContact(10L, 0, 6, new Vec3(0f, 0f, 2f)),
            };

            _presenter.Play(_owner.transform, contacts, 2f, 0d, () => 0d);
            Assert.AreEqual(4, _presenter.ActiveSwordCount);

            _presenter.Tick(0.4d);
            Assert.Less(Vector3.Distance(_presenter.SwordPosition(0),
                new Vector3(0f, 1.1f, 2f)), 0.01f);

            _presenter.Tick(2.1d);
            Assert.IsFalse(_presenter.IsPlaying);
            Assert.AreEqual(0, _presenter.ActiveSwordCount);
        }

        [Test]
        public void EightTargets_AllScheduledContactsReachedBeforeEnd()
        {
            var contacts = new List<SwordQiEchoContact>();
            for (int i = 0; i < 8; i++)
            {
                contacts.Add(new SwordQiEchoContact(i + 1L, i % 4,
                    i < 4 ? 6u : 24u, new Vec3(i + 1f, 0f, 2f)));
            }

            _presenter.Play(_owner.transform, contacts, 2f, 0d, () => 0d);
            _presenter.Tick(0.4d);
            _presenter.Tick(1.6d);

            for (int sword = 0; sword < 4; sword++)
            {
                Assert.Less(Vector3.Distance(_presenter.SwordPosition(sword),
                    new Vector3(sword + 5f, 1.1f, 2f)), 0.01f);
            }
        }

        [Test]
        public void LateStartEndDetach_AlwaysReturnsTrailsToPool()
        {
            var contacts = new List<SwordQiEchoContact>
            {
                new SwordQiEchoContact(10L, 0, 24, new Vec3(0f, 0f, 2f)),
            };

            _presenter.Play(_owner.transform, contacts, 2f, elapsedSeconds: 0.8d,
                now: () => 0.8d);
            Assert.AreEqual(4, _presenter.ActiveSwordCount);

            _presenter.Tick(0.9d);
            _presenter.Stop();
            Assert.AreEqual(0, _presenter.ActiveSwordCount);

            _presenter.Play(_owner.transform, contacts, 2f, 0d, () => 0d);
            Assert.AreEqual(4, _presenter.ActiveSwordCount);
            _presenter.Stop();
        }
    }
}
