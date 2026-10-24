using System.Collections;
using System.Reflection;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class StatusVFXSystemTests
    {
        private GameObject _targetGo;
        private GameObject _systemGo;
        private Material _material;
        private StatusVFXSystem _system;

        [SetUp]
        public void SetUp()
        {
            _targetGo = new GameObject("TintTarget");
            var renderer = _targetGo.AddComponent<MeshRenderer>();
            _material = new Material(Shader.Find("Standard"));
            renderer.sharedMaterial = _material;

            _systemGo = new GameObject("StatusVFX");
            _system = _systemGo.AddComponent<StatusVFXSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_systemGo);
            Object.DestroyImmediate(_targetGo);
            Object.DestroyImmediate(_material);
        }

        [Test]
        public void test_status_vfx_adds_and_removes_freeze_tint()
        {
            var applied = typeof(StatusVFXSystem).GetMethod(
                "OnApplied", BindingFlags.NonPublic | BindingFlags.Instance);
            var removed = typeof(StatusVFXSystem).GetMethod(
                "OnRemoved", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(applied);
            Assert.IsNotNull(removed);

            applied.Invoke(_system, new object[]
            {
                new StatusEffectAppliedEvent
                {
                    EntityId = _targetGo.GetInstanceID(),
                    Target = _targetGo.transform,
                    EffectType = EffectType.Freeze,
                    EffectId = "freeze",
                    Duration = 2f,
                    Stacks = 1,
                }
            });

            var tints = (IDictionary)TestHelpers.GetField(_system, "_tints");
            Assert.AreEqual(1, tints.Count);

            removed.Invoke(_system, new object[]
            {
                new StatusEffectRemovedEvent
                {
                    EntityId = _targetGo.GetInstanceID(),
                    Target = _targetGo.transform,
                    EffectType = EffectType.Freeze,
                    EffectId = "freeze",
                }
            });

            Assert.AreEqual(0, tints.Count);
        }

        [Test]
        public void test_status_vfx_removes_entry_after_target_destroyed()
        {
            var applied = typeof(StatusVFXSystem).GetMethod(
                "OnApplied", BindingFlags.NonPublic | BindingFlags.Instance);
            var update = typeof(StatusVFXSystem).GetMethod(
                "Update", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(applied);
            Assert.IsNotNull(update);

            applied.Invoke(_system, new object[]
            {
                new StatusEffectAppliedEvent
                {
                    EntityId = _targetGo.GetInstanceID(),
                    Target = _targetGo.transform,
                    EffectType = EffectType.Burn,
                    EffectId = "burn",
                    Duration = 3f,
                    Stacks = 1,
                }
            });

            var tints = (IDictionary)TestHelpers.GetField(_system, "_tints");
            Assert.AreEqual(1, tints.Count);

            // 目标销毁后 Renderer 一并销毁，Update 不应再对其 SetPropertyBlock
            Object.DestroyImmediate(_targetGo);
            _targetGo = null;

            Assert.DoesNotThrow(() => update.Invoke(_system, null));
            Assert.AreEqual(0, tints.Count);
        }

        [Test]
        public void test_status_vfx_removed_after_target_destroyed_does_not_throw()
        {
            var applied = typeof(StatusVFXSystem).GetMethod(
                "OnApplied", BindingFlags.NonPublic | BindingFlags.Instance);
            var removed = typeof(StatusVFXSystem).GetMethod(
                "OnRemoved", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(applied);
            Assert.IsNotNull(removed);

            int entityId = _targetGo.GetInstanceID();
            applied.Invoke(_system, new object[]
            {
                new StatusEffectAppliedEvent
                {
                    EntityId = entityId,
                    Target = _targetGo.transform,
                    EffectType = EffectType.Freeze,
                    EffectId = "freeze",
                    Duration = 2f,
                    Stacks = 1,
                }
            });

            var tints = (IDictionary)TestHelpers.GetField(_system, "_tints");
            Assert.AreEqual(1, tints.Count);

            // 目标销毁后 Removed 事件先于 Update 清理到达，不应再对已销毁 Renderer 染色
            Object.DestroyImmediate(_targetGo);
            _targetGo = null;

            Assert.DoesNotThrow(() => removed.Invoke(_system, new object[]
            {
                new StatusEffectRemovedEvent
                {
                    EntityId = entityId,
                    Target = null,
                    EffectType = EffectType.Freeze,
                    EffectId = "freeze",
                }
            }));
            Assert.AreEqual(0, tints.Count);
        }
    }
}
