using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// P3：AOI 下视野外实体自然停流——registry 按缺席超时销毁视图（清插值缓冲）。
    /// 1.2s 无任何快照样本即删（PlayerLeaveRoom 仍是即时删除路径）。
    /// </summary>
    public class RemoteEntityRegistryTests
    {
        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("RegistryTestRoot");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private static EntitySnapshot Snap(long id, float x, float z)
        {
            return new EntitySnapshot
            {
                PlayerId = id,
                FieldMask = (ushort)EntityFieldMask.Position,
                X = x, Y = 0, Z = z,
                Rotation = 0, Tick = 1
            };
        }

        [Test]
        public void Entity_Not_Expired_While_Receiving_Snapshots()
        {
            _registry.ApplySnapshot(Snap(1, 0, 0), serverTicks: 1_000_000L);

            // 持续收快照永不超时（每次推进后刷新样本时间）
            for (int i = 0; i < 100; i++)
            {
                _registry.ApplySnapshot(Snap(1, i, 0), serverTicks: 2_000_000L + i * 100_000L);
                _registry.UpdateExpiry(nowServerTicks: 2_000_000L + i * 100_000L);
            }
            Assert.AreEqual(1, _registry.Count);
        }

        [Test]
        public void Entity_Expired_After_1_2s_Without_Snapshots()
        {
            _registry.ApplySnapshot(Snap(1, 0, 0), serverTicks: 1_000_000L);
            Assert.AreEqual(1, _registry.Count);

            // 服务器时钟推进 1.3s，无新快照
            _registry.UpdateExpiry(nowServerTicks: 1_000_000L + 13_000_000L);

            Assert.AreEqual(0, _registry.Count);
            Assert.IsNull(_registry.GetView(1));
        }

        [Test]
        public void Multi_Entity_Independent_Expiry()
        {
            _registry.ApplySnapshot(Snap(1, 0, 0), serverTicks: 1_000_000L);
            _registry.ApplySnapshot(Snap(2, 10, 0), serverTicks: 1_000_000L);
            _registry.ApplySnapshot(Snap(3, 20, 0), serverTicks: 1_000_000L);

            // 实体 2 持续刷新到最后时刻（19M），1/3 停流于 1M
            _registry.ApplySnapshot(Snap(2, 11, 0), serverTicks: 19_000_000L);
            _registry.UpdateExpiry(nowServerTicks: 20_000_000L); // 1/3 差 19M ticks = 1.9s > 1.2s 删；2 差 1M = 0.1s 保留

            Assert.AreEqual(1, _registry.Count);
            Assert.IsNotNull(_registry.GetView(2));
        }

        [Test]
        public void Npc_Snapshot_Creates_Monster_View_With_Template()
        {
            var root = new GameObject("NpcRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 2_000_001,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                        | EntityFieldMask.Kind | EntityFieldMask.TemplateId | EntityFieldMask.ActionState),
                    X = 12f, Z = 3f, Rotation = 90f,
                    Kind = (byte)EntityKind.Npc,
                    TemplateId = 2,
                    ActionState = (byte)NpcAnimationState.Run
                }, serverTicks: 1_000L);

                var view = registry.GetView(2_000_001);
                Assert.IsNotNull(view);
                Assert.AreEqual((byte)EntityKind.Npc, view!.Kind);
                Assert.AreEqual(2, view.TemplateId);
                Assert.AreEqual((byte)NpcAnimationState.Run, view.ActionState);
                Assert.IsTrue(view.GameObject.name.Contains("Monster"), $"view name '{view.GameObject.name}' should mark monster");
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Player_Snapshot_Still_Creates_Player_View()
        {
            var root = new GameObject("PlayerRoot");
            var registry = new RemoteEntityRegistry(root.transform);
            try
            {
                registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = 5,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Kind),
                    X = 1f, Z = 2f,
                    Kind = (byte)EntityKind.Player
                }, serverTicks: 1_000L);

                var view = registry.GetView(5);
                Assert.IsNotNull(view);
                Assert.AreEqual((byte)EntityKind.Player, view!.Kind);
                Assert.IsTrue(view.GameObject.name.Contains("RemotePlayer"));
            }
            finally
            {
                registry.Clear();
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Npc_View_Keeps_Sampled_State_Instead_Of_Arrival_State()
        {
            // 采样路径：视图的 ActionState 由 UpdateRender 在 renderAt 处采出
            //（ApplySnapshot 仍写 view.ActionState 作为"最后已知值"，供测试与观察用）
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 2_000_001,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Kind
                    | EntityFieldMask.ActionState | EntityFieldMask.TemplateId),
                X = 0f, Z = 0f, Kind = (byte)EntityKind.Npc, TemplateId = 2,
                ActionState = (byte)NpcAnimationState.Run, Tick = 1
            }, serverTicks: 1_000_000L);

            Assert.IsTrue(_registry.TrySampleNpcState(2_000_001, renderAt: 1_000_000L,
                out var state, out var dead));
            Assert.AreEqual((byte)NpcAnimationState.Run, state);
            Assert.IsFalse(dead);
        }
    }
}
