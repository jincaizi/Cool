using System.Collections.Generic;
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

        [Test]
        public void test_pending_action_from_old_life_is_not_replayed()
        {
            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 12, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 3, Revision = 1, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Windup
                }
            });
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 12, Kind = (byte)EntityKind.Npc, TemplateId = 1,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead),
                X = 0f, Z = 0f, Dead = false, LifeId = 2
            }, 1);

            var view = _registry.GetView(12);
            Assert.IsNotNull(view);
            Assert.AreEqual(0UL, view!.ActionInstanceId,
                "a cached action from an old life must not be replayed");
        }

        [Test]
        public void test_older_instance_does_not_override_newer()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 13, Kind = (byte)EntityKind.Npc, TemplateId = 1,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead),
                X = 0f, Z = 0f, Dead = false, LifeId = 1
            }, 1);

            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 13, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 10, Revision = 1, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Windup
                }
            });
            _registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = 13, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 9, Revision = 5, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Cancelled
                }
            });

            var view = _registry.GetView(13);
            Assert.IsNotNull(view);
            Assert.AreEqual(10UL, view!.ActionInstanceId,
                "an older action instance must not cancel the newer one");
        }

        [Test]
        public void test_newer_instance_stops_the_previous_attack()
        {
            using var fixture = new NpcAnimatorFixture(_root.transform);
            var registry = fixture.Registry;
            var view = fixture.View;

            registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = NpcAnimatorFixture.EntityId, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 20, Revision = 1, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Active
                }
            });

            // CastBroadcast（同 tick 先到）→ 真实排程，到点由 UpdateRender 的 drain 起手。
            registry.ScheduleAttack(NpcAnimatorFixture.EntityId, 100, 50001);

            registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = NpcAnimatorFixture.EntityId, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 21, Revision = 1, Kind = NpcActionKind.HitReaction,
                    Phase = NpcActionPhase.Active
                }
            });

            registry.UpdateRender(200, 0);

            Assert.AreEqual(21UL, view.ActionInstanceId);
            Assert.AreEqual(NpcEntityAnimator.StateHit,
                fixture.Animator.GetInteger("MonsterState"),
                "an interrupting action must clear the superseded instance's scheduled attack");
        }

        [Test]
        public void test_skill_windup_after_cast_broadcast_keeps_the_scheduled_attack()
        {
            using var fixture = new NpcAnimatorFixture(_root.transform);
            var registry = fixture.Registry;
            var view = fixture.View;

            registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = NpcAnimatorFixture.EntityId, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 20, Revision = 1, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Active
                }
            });

            view.NpcAnimator!.PlayAttack(0);   // CastBroadcast → NpcAttackSchedule 起手
            Assert.AreEqual(NpcEntityAnimator.StateAttacking,
                fixture.Animator.GetInteger("MonsterState"));

            // 同一技能的 NpcActionEvent 下一 tick 才到：普通 Windup 不得清掉已排的攻击。
            registry.ApplyNpcActionEvent(new NpcActionEvent
            {
                EntityId = NpcAnimatorFixture.EntityId, LifeId = 1,
                Action = new NpcActionSnapshot
                {
                    InstanceId = 21, Revision = 1, Kind = NpcActionKind.Skill,
                    Phase = NpcActionPhase.Windup
                }
            });

            Assert.AreEqual(21UL, view.ActionInstanceId);
            Assert.AreEqual(NpcEntityAnimator.StateAttacking,
                fixture.Animator.GetInteger("MonsterState"),
                "a plain skill windup must not clear the attack scheduled by CastBroadcast");
        }

        /// <summary>
        /// 带真 Animator/NpcEntityAnimator 的 NPC 视图夹具（EditMode 下 AddComponent 不跑 Awake，
        /// 必须显式 ResolveAnimator）。每个用例一份临时 controller，Dispose 时整体清理。
        /// </summary>
        private sealed class NpcAnimatorFixture : System.IDisposable
        {
            public const long EntityId = 14;
            private const string ControllerPath = "Assets/Tests_TempNpcActionSupersede.controller";

            public readonly RemoteEntityRegistry Registry;
            public readonly RemoteEntityView View;
            public readonly Animator Animator;
            private readonly GameObject _prefab;

            public NpcAnimatorFixture(Transform root)
            {
                var controller = UnityEditor.Animations.AnimatorController
                    .CreateAnimatorControllerAtPath(ControllerPath);
                controller.AddParameter("MonsterState", AnimatorControllerParameterType.Int);
                controller.AddParameter("AttackVariant", AnimatorControllerParameterType.Int);
                controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);

                _prefab = new GameObject("NpcActionSupersedePrefab");
                var prefabAnimator = _prefab.AddComponent<Animator>();
                prefabAnimator.runtimeAnimatorController = controller;

                Registry = new RemoteEntityRegistry(root,
                    npcPrefabs: new Dictionary<int, GameObject> { [1] = _prefab });
                // 刻意不带 Position 位：避免 UpdateRender 用插值样本里的 Idle 状态覆盖
                // 用例写入的 MonsterState（supersede 用例只验证排程 drain 是否被清）。
                Registry.ApplySnapshot(new EntitySnapshot
                {
                    PlayerId = EntityId, Kind = (byte)EntityKind.Npc, TemplateId = 1,
                    FieldMask = (ushort)EntityFieldMask.Dead,
                    X = 0f, Z = 0f, Dead = false, LifeId = 1
                }, 1);

                View = Registry.GetView(EntityId)!;
                View.NpcAnimator!.ResolveAnimator();
                Animator = View.NpcAnimator.GetComponentInChildren<Animator>();
            }

            public void Dispose()
            {
                Registry.Clear();
                Object.DestroyImmediate(_prefab);
                UnityEditor.AssetDatabase.DeleteAsset(ControllerPath);
            }
        }
    }
}
