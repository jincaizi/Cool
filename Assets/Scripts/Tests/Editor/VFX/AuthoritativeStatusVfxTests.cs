using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.VFX;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class AuthoritativeStatusVfxTests
    {
        private readonly List<StatusEffectAppliedEvent> _applied = new List<StatusEffectAppliedEvent>();
        private readonly List<StatusEffectRemovedEvent> _removed = new List<StatusEffectRemovedEvent>();
        /// <summary>应用/移除的到达顺序（"A:类型" / "R:类型"）——换根时断言 Removed 先于 Applied</summary>
        private readonly List<string> _sequence = new List<string>();

        private GameObject _targetGo;
        private AuthoritativeStatusVfx _vfx;

        private GameObject _renderGo;
        private MeshRenderer _renderer;
        private Material _material;
        private GameObject _systemGo;
        private StatusVFXSystem _system;

        private static readonly MethodInfo AppliedMethod = typeof(StatusVFXSystem).GetMethod(
            "OnApplied", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RemovedMethod = typeof(StatusVFXSystem).GetMethod(
            "OnRemoved", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo UpdateMethod = typeof(StatusVFXSystem).GetMethod(
            "Update", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            _applied.Clear();
            _removed.Clear();
            _sequence.Clear();
            _targetGo = new GameObject("StatusTarget");
            _vfx = new AuthoritativeStatusVfx();
            EventBus.Subscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Subscribe<StatusEffectRemovedEvent>(OnRemoved);

            // 染色目标（Standard 材质带 _Color）与系统实例
            _renderGo = new GameObject("TintTarget");
            _renderer = _renderGo.AddComponent<MeshRenderer>();
            _material = new Material(Shader.Find("Standard"));
            _renderer.sharedMaterial = _material;
            _systemGo = new GameObject("StatusVFX");
            _system = _systemGo.AddComponent<StatusVFXSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.Unsubscribe<StatusEffectAppliedEvent>(OnApplied);
            EventBus.Unsubscribe<StatusEffectRemovedEvent>(OnRemoved);
            Object.DestroyImmediate(_systemGo);
            Object.DestroyImmediate(_targetGo);
            Object.DestroyImmediate(_renderGo);
            Object.DestroyImmediate(_material);
        }

        private void OnApplied(StatusEffectAppliedEvent e)
        {
            _applied.Add(e);
            _sequence.Add("A:" + e.EffectType);
        }

        private void OnRemoved(StatusEffectRemovedEvent e)
        {
            _removed.Add(e);
            _sequence.Add("R:" + e.EffectType);
        }

        private static IList<StatusProjection> Projection(params (int EffectId, int GroupId)[] statuses)
        {
            var list = new List<StatusProjection>();
            foreach (var (effectId, groupId) in statuses)
                list.Add(new StatusProjection { EffectId = effectId, GroupId = groupId });
            return list;
        }

        private void ApplyStatusToRenderTarget(EffectType type)
        {
            AppliedMethod.Invoke(_system, new object[]
            {
                new StatusEffectAppliedEvent
                {
                    EntityId = _renderGo.GetInstanceID(),
                    Target = _renderGo.transform,
                    EffectType = type,
                    EffectId = type.ToString(),
                    Duration = 2f,
                    Stacks = 1,
                }
            });
        }

        private void RemoveStatusFromRenderTarget(EffectType type)
        {
            RemovedMethod.Invoke(_system, new object[]
            {
                new StatusEffectRemovedEvent
                {
                    EntityId = _renderGo.GetInstanceID(),
                    Target = _renderGo.transform,
                    EffectType = type,
                    EffectId = type.ToString(),
                }
            });
        }

        [Test]
        public void test_authoritative_status_vfx_applies_each_group_once_and_ignores_repeated_projection()
        {
            // Arrange
            var statuses = Projection((101, 1), (201, 2));

            // Act
            _vfx.Apply(7, _targetGo.transform, statuses);

            // Assert
            Assert.AreEqual(2, _applied.Count, "两个组应各应用一次");
            Assert.AreEqual(0, _removed.Count);
            Assert.AreEqual(7, _applied[0].EntityId);
            Assert.AreEqual(EffectType.Freeze, _applied[0].EffectType);
            Assert.AreEqual(EffectType.Burn, _applied[1].EffectType);

            // Act：同一投影重复到达
            _vfx.Apply(7, _targetGo.transform, Projection((101, 1), (201, 2)));

            // Assert
            Assert.AreEqual(2, _applied.Count, "重复投影不得重复应用（每帧投影）");
            Assert.AreEqual(0, _removed.Count);
        }

        [Test]
        public void test_authoritative_status_vfx_maps_four_elements_to_effect_types()
        {
            // Arrange & Act
            _vfx.Apply(9, _targetGo.transform, Projection((101, 1), (201, 2), (301, 3), (401, 4)));

            // Assert
            var types = new List<EffectType>();
            foreach (var e in _applied) types.Add(e.EffectType);
            CollectionAssert.AreEquivalent(
                new[] { EffectType.Freeze, EffectType.Burn, EffectType.Stun, EffectType.Vulnerable },
                types);
            Assert.AreEqual(EffectType.Freeze, AuthoritativeStatusVfx.Map(101));
            Assert.AreEqual(EffectType.Burn, AuthoritativeStatusVfx.Map(201));
            Assert.AreEqual(EffectType.Stun, AuthoritativeStatusVfx.Map(301));
            Assert.AreEqual(EffectType.Vulnerable, AuthoritativeStatusVfx.Map(401));
            Assert.AreEqual(EffectType.Debuff, AuthoritativeStatusVfx.Map(999));
        }

        [Test]
        public void test_authoritative_status_vfx_removes_group_once_when_projection_loses_it()
        {
            // Arrange
            _vfx.Apply(7, _targetGo.transform, Projection((101, 1), (201, 2)));

            // Act：火消失，冰保留
            _vfx.Apply(7, _targetGo.transform, Projection((101, 1)));

            // Assert
            Assert.AreEqual(1, _removed.Count);
            Assert.AreEqual(EffectType.Burn, _removed[0].EffectType);

            // Act：全部清空
            _vfx.Apply(7, _targetGo.transform, Projection());

            // Assert
            Assert.AreEqual(2, _removed.Count);
            Assert.AreEqual(EffectType.Freeze, _removed[1].EffectType);
        }

        [Test]
        public void test_authoritative_status_vfx_replaces_same_group_with_one_remove_and_one_apply()
        {
            // Arrange
            _vfx.Apply(7, _targetGo.transform, Projection((101, 1)));

            // Act：同组换成雷
            _vfx.Apply(7, _targetGo.transform, Projection((301, 1)));

            // Assert
            Assert.AreEqual(1, _removed.Count);
            Assert.AreEqual(EffectType.Freeze, _removed[0].EffectType);
            Assert.AreEqual(2, _applied.Count);
            Assert.AreEqual(EffectType.Stun, _applied[1].EffectType);
        }

        [Test]
        public void test_authoritative_status_vfx_keeps_entities_isolated_on_remove()
        {
            // Arrange
            var otherGo = new GameObject("OtherStatusTarget");
            try
            {
                _vfx.Apply(7, _targetGo.transform, Projection((101, 1)));
                _vfx.Apply(8, otherGo.transform, Projection((201, 2)));
                Assert.AreEqual(2, _applied.Count);

                // Act：只移除实体 7
                _vfx.Remove(7);

                // Assert
                Assert.AreEqual(1, _removed.Count);
                Assert.AreEqual(7, _removed[0].EntityId);
                Assert.AreEqual(EffectType.Freeze, _removed[0].EffectType);

                // Act：重复移除幂等
                _vfx.Remove(7);
                Assert.AreEqual(1, _removed.Count);

                // 实体 8 仍保持应用状态：再收到同一投影不重复应用也不移除
                _vfx.Apply(8, otherGo.transform, Projection((201, 2)));
                Assert.AreEqual(2, _applied.Count);
                Assert.AreEqual(1, _removed.Count);
            }
            finally
            {
                Object.DestroyImmediate(otherGo);
            }
        }

        [Test]
        public void test_authoritative_status_vfx_replays_all_groups_when_target_changes()
        {
            // Arrange
            var oldTarget = new GameObject("OldBody");
            var newTarget = new GameObject("NewBody");
            try
            {
                _vfx.Apply(7, oldTarget.transform, Projection((101, 1), (201, 2)));
                Assert.AreEqual(2, _applied.Count);
                _applied.Clear();
                _removed.Clear();
                _sequence.Clear();

                // Act：同一实体换渲染根，状态集合不变（Proxy→Full / 本地角色重建）
                _vfx.Apply(7, newTarget.transform, Projection((101, 1), (201, 2)));

                // Assert：旧根全部先 Removed，新根再全部 Applied
                Assert.AreEqual(2, _removed.Count, "旧根的两个组都要移除");
                Assert.AreEqual(oldTarget.transform, _removed[0].Target);
                Assert.AreEqual(oldTarget.transform, _removed[1].Target);
                CollectionAssert.AreEquivalent(
                    new[] { EffectType.Freeze, EffectType.Burn },
                    new[] { _removed[0].EffectType, _removed[1].EffectType });

                Assert.AreEqual(2, _applied.Count, "新根要重新应用两个组");
                Assert.AreEqual(newTarget.transform, _applied[0].Target);
                Assert.AreEqual(newTarget.transform, _applied[1].Target);
                Assert.AreEqual(4, _sequence.Count);
                StringAssert.StartsWith("R:", _sequence[0], "旧根 Removed 必须先于新根 Applied");
                StringAssert.StartsWith("R:", _sequence[1]);
                StringAssert.StartsWith("A:", _sequence[2]);
                StringAssert.StartsWith("A:", _sequence[3]);

                // Act：target 不变时重复投影不重复发
                _vfx.Apply(7, newTarget.transform, Projection((101, 1), (201, 2)));

                // Assert
                Assert.AreEqual(2, _applied.Count, "target 相同的重复投影不得重复应用");
                Assert.AreEqual(2, _removed.Count);
            }
            finally
            {
                Object.DestroyImmediate(oldTarget);
                Object.DestroyImmediate(newTarget);
            }
        }

        [Test]
        public void test_remote_entity_registry_replays_statuses_when_tier_changes_render_root()
        {
            // Arrange：观察者在场 ⇒ 先建 Proxy；基线投影带状态
            var root = new GameObject("StatusTierRoot");
            var prefab = new GameObject("StatusTierAvatar");
            prefab.AddComponent<Animator>();
            var registry = new RemoteEntityRegistry(root.transform,
                new[] { new AppearanceEntry { Id = 0, Full = prefab } });
            registry.ObserverPositionProvider = () => Vector3.zero;
            registry.TierPolicy.FullDistance = 300f;
            registry.TierPolicy.MaxFull = 20;
            var events = new List<(GameObject Target, IList<StatusProjection> Statuses)>();
            registry.StatusesChanged += (_, target, statuses) => events.Add((target, statuses));
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 77,
                    FieldMask = (ushort)(EntityFieldMask.Position
                        | EntityFieldMask.Kind | EntityFieldMask.Combat),
                    X = 10f, Y = 0f, Z = 0f,
                    Kind = (byte)EntityKind.PlayerBot,
                    Combat = new CombatProjection
                    {
                        LifeId = 1,
                        CombatVersion = 1,
                        Hp = 100f,
                        MaxHp = 100f,
                        Statuses = Projection((101, 1)),
                    },
                }, serverTicks: 1_000_000L);

                var view = registry.GetView(77);
                Assert.IsNotNull(view);
                Assert.AreEqual(RemoteTier.Proxy, view!.Tier, "观察者在场 ⇒ 先建 Proxy");
                Assert.AreEqual(1, events.Count, "基线投影发一次状态事件");
                Assert.AreSame(view.ProxyRoot, events[0].Target, "首次应用目标是 Proxy 根");

                // Act：近距离升 Full（换渲染根）
                registry.UpdateRender(nowServerTicks: 1_000_000L, renderDelayTicks: 0L);

                // Assert：用当前 Statuses 对新根重放一次
                Assert.AreEqual(RemoteTier.Full, view.Tier, "近距离应升 Full");
                Assert.AreEqual(2, events.Count, "换根后必须重放一次 StatusesChanged");
                Assert.AreSame(view.FullRoot, events[1].Target, "重放目标是新的 Full 根");
                Assert.AreEqual(1, events[1].Statuses.Count);
                Assert.AreEqual(101, events[1].Statuses[0].EffectId);
                Assert.AreEqual(1, events[1].Statuses[0].GroupId);
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void test_status_vfx_system_tints_stun_purple_and_clears_on_remove()
        {
            // Arrange
            ApplyStatusToRenderTarget(EffectType.Stun);
            var tints = (IDictionary)TestHelpers.GetField(_system, "_tints");
            Assert.AreEqual(1, tints.Count, "雷状态应建立染色条目");

            // Act
            UpdateMethod.Invoke(_system, null);

            // Assert
            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);
            var tinted = block.GetColor("_Color");
            Assert.Greater(tinted.b, tinted.g, "雷紫：蓝分量应高于绿");
            Assert.Greater(tinted.b, tinted.r);

            // Act：移除
            RemoveStatusFromRenderTarget(EffectType.Stun);

            // Assert
            Assert.AreEqual(0, tints.Count, "移除后条目应清理");
            var cleared = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(cleared);
            Assert.IsTrue(cleared.isEmpty, "移除后渲染器不应再带染色 property block");
        }

        [Test]
        public void test_status_vfx_system_tints_vulnerable_cyan_and_clears_on_remove()
        {
            // Arrange
            ApplyStatusToRenderTarget(EffectType.Vulnerable);
            var tints = (IDictionary)TestHelpers.GetField(_system, "_tints");
            Assert.AreEqual(1, tints.Count, "风状态应建立染色条目");

            // Act
            UpdateMethod.Invoke(_system, null);

            // Assert
            var block = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(block);
            var tinted = block.GetColor("_Color");
            Assert.Greater(tinted.g, tinted.b, "风青：绿分量应高于蓝");
            Assert.Greater(tinted.g, tinted.r);

            // Act：移除
            RemoveStatusFromRenderTarget(EffectType.Vulnerable);

            // Assert
            Assert.AreEqual(0, tints.Count, "移除后条目应清理");
            var cleared = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(cleared);
            Assert.IsTrue(cleared.isEmpty, "移除后渲染器不应再带染色 property block");
        }
    }
}
