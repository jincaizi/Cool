using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class NpcActionConsumptionTests
    {
        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("NpcActionConsumptionTests");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void test_npc_action_applies_to_existing_view_and_ignores_old_revision()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 9, Kind = (byte)EntityKind.Npc, TemplateId = 1,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead),
                X = 1f, Z = 1f, Dead = false, LifeId = 1
            }, 1);

            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 9, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 5, Revision = 2, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Windup
                }
            });

            var view = _registry.GetView(9);
            Assert.IsNotNull(view);
            Assert.AreEqual(5UL, view!.ActionInstanceId);
            Assert.AreEqual(2u, view.ActionRevision);

            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 9, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 5, Revision = 1, Phase = NpcActionPhase.Active
                }
            });

            Assert.AreEqual(2u, view.ActionRevision, "older revision must not overwrite");
        }

        [Test]
        public void test_npc_action_before_view_is_buffered_and_replayed()
        {
            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 10, LifeId = 4,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 8, Revision = 3, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Windup
                }
            });
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 10, Kind = (byte)EntityKind.Npc, TemplateId = 1,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead),
                X = 0f, Z = 0f, Dead = false, LifeId = 4
            }, 1);

            var view = _registry.GetView(10);
            Assert.IsNotNull(view);
            Assert.AreEqual(8UL, view!.ActionInstanceId);
            Assert.AreEqual(3u, view.ActionRevision);
        }

        [Test]
        public void test_baseline_snapshot_applies_npc_action()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 11, Kind = (byte)EntityKind.Npc, TemplateId = 1,
                FieldMask = (ushort)(EntityFieldMask.Position
                    | EntityFieldMask.Dead | EntityFieldMask.NpcAction),
                X = 0f, Z = 0f, Dead = false, LifeId = 2,
                NpcAction = new NpcActionSnapshot
                {
                    InstanceId = 7, Revision = 1, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Windup
                }
            }, 1);

            var view = _registry.GetView(11);
            Assert.IsNotNull(view);
            Assert.AreEqual(7UL, view!.ActionInstanceId);
            Assert.AreEqual(1u, view.ActionRevision);
        }
    }
}
