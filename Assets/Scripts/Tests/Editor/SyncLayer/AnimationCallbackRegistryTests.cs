using System;
using Hotfix.GameSystems.Sys3C.Animation.StateBehaviours;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class AnimationCallbackRegistryTests
    {
        private GameObject _a = null!;
        private GameObject _b = null!;
        private Animator _animA = null!;
        private Animator _animB = null!;

        [SetUp]
        public void SetUp()
        {
            AnimationCallbackRegistry.ClearForTests();
            _a = new GameObject("AnimA");
            _b = new GameObject("AnimB");
            _animA = _a.AddComponent<Animator>();
            _animB = _b.AddComponent<Animator>();
        }

        [TearDown]
        public void TearDown()
        {
            AnimationCallbackRegistry.ClearForTests();
            UnityEngine.Object.DestroyImmediate(_a);
            UnityEngine.Object.DestroyImmediate(_b);
        }

        [Test]
        public void Register_Stores_Attack_And_Hit_Callbacks()
        {
            string attack = null;
            string hit = null;
            AnimationCallbackRegistry.Register(_animA, s => attack = s, s => hit = s);

            Assert.IsTrue(AnimationCallbackRegistry.TryGet(_animA, out var entry));
            entry.Attack?.Invoke("Attack1");
            entry.Hit?.Invoke("Hit");

            Assert.AreEqual("Attack1", attack);
            Assert.AreEqual("Hit", hit);
        }

        [Test]
        public void Unregistered_Animator_Does_Not_Invoke_Local_Callbacks()
        {
            int localCalls = 0;
            AnimationCallbackRegistry.Register(_animA, _ => localCalls++, _ => localCalls++);

            Assert.IsFalse(AnimationCallbackRegistry.TryGet(_animB, out _),
                "未注册的远程 Animator 必须查不到条目——否则远程动画会调用本地 FSM");
            Assert.AreEqual(0, localCalls);
        }

        [Test]
        public void Register_Resets_Hit_Flags_For_The_Same_Animator()
        {
            AnimationCallbackRegistry.Register(_animA, null, null);
            Assert.IsTrue(AnimationCallbackRegistry.TryGet(_animA, out var entry));
            entry.HasTriggeredHitComplete = true;
            entry.LastNormalizedTime = 0.95f;

            AnimationCallbackRegistry.Register(_animA, null, null); // 重装配/角色重建

            Assert.IsTrue(AnimationCallbackRegistry.TryGet(_animA, out var reset));
            Assert.IsFalse(reset.HasTriggeredHitComplete);
            Assert.AreEqual(0f, reset.LastNormalizedTime);
        }

        [Test]
        public void Unregister_Is_Idempotent()
        {
            AnimationCallbackRegistry.Register(_animA, null, null);
            AnimationCallbackRegistry.Unregister(_animA);
            AnimationCallbackRegistry.Unregister(_animA);
            Assert.IsFalse(AnimationCallbackRegistry.TryGet(_animA, out _));
        }

        [Test]
        public void ClearForTests_Removes_All_Entries()
        {
            AnimationCallbackRegistry.Register(_animA, null, null);
            AnimationCallbackRegistry.Register(_animB, null, null);
            AnimationCallbackRegistry.ClearForTests();
            Assert.IsFalse(AnimationCallbackRegistry.TryGet(_animA, out _));
            Assert.IsFalse(AnimationCallbackRegistry.TryGet(_animB, out _));
        }
    }
}
