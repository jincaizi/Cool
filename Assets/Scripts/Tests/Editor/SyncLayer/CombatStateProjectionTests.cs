using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class CombatStateProjectionTests
    {
        private GameObject _root = null!;
        private RemoteEntityRegistry _registry = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("CombatStateProjectionTests");
            _registry = new RemoteEntityRegistry(_root.transform);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void test_projection_applies_higher_combat_version_and_ignores_older()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 7, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health),
                X = 0f, Z = 0f, Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 1, LifeId = 1
            }, 1);

            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 7,
                Projection = new CombatProjection
                {
                    CombatVersion = 3, LifeId = 1, Hp = 500f, MaxHp = 720f,
                    Dead = false, IsDefending = true,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                    }
                }
            });

            var view = _registry.GetView(7);
            Assert.IsNotNull(view);
            Assert.AreEqual(500f, view!.Hp);
            Assert.IsTrue(view.IsDefending);
            Assert.AreEqual(42L, view.Equipment!.WeaponInstanceId);

            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 7,
                Projection = new CombatProjection
                {
                    CombatVersion = 2, LifeId = 1, Hp = 1f, MaxHp = 720f
                }
            });

            Assert.AreEqual(500f, view.Hp, "older combat version must not overwrite");
        }

        [Test]
        public void test_empty_status_list_clears_existing_statuses()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 9, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health),
                X = 0f, Z = 0f, Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 1, LifeId = 1
            }, 1);

            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 9,
                Projection = new CombatProjection
                {
                    CombatVersion = 2, LifeId = 1,
                    Statuses = new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 1, GroupId = 1 }
                    }
                }
            });
            Assert.AreEqual(1, _registry.GetView(9)!.Statuses.Count);

            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 9,
                Projection = new CombatProjection
                {
                    CombatVersion = 3, LifeId = 1,
                    Statuses = new List<StatusProjection>()
                }
            });
            Assert.AreEqual(0, _registry.GetView(9)!.Statuses.Count,
                "an empty status list clears the previous statuses");
        }

        [Test]
        public void test_pending_update_replays_after_baseline_snapshot()
        {
            // 服务端在基线快照之前就发出更新：视图未建 → 先入缓存
            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 20,
                Projection = new CombatProjection
                {
                    CombatVersion = 3, LifeId = 1, Hp = 500f, MaxHp = 720f,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                    }
                }
            });

            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 20, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health),
                X = 0f, Z = 0f, Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 1, LifeId = 1
            }, 1);

            var view = _registry.GetView(20);
            Assert.IsNotNull(view);
            Assert.AreEqual(500f, view!.Hp, "cached projection must override the baseline hp");
            Assert.AreEqual(3u, view.CombatVersion);
            Assert.AreEqual(42L, view.Equipment!.WeaponInstanceId);
        }

        [Test]
        public void test_baseline_snapshot_applies_projection()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 8, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health | EntityFieldMask.Combat),
                X = 0f, Z = 0f, Dead = false, Hp = 300f, MaxHp = 720f,
                CombatVersion = 5, LifeId = 2,
                Combat = new CombatProjection
                {
                    CombatVersion = 5, LifeId = 2, Hp = 300f, MaxHp = 720f,
                    Dead = false, IsDefending = true
                }
            }, 1);

            var view = _registry.GetView(8);
            Assert.IsNotNull(view);
            Assert.AreEqual(5u, view!.CombatVersion);
            Assert.IsTrue(view.IsDefending);
        }

        [Test]
        public void test_older_life_id_projection_is_ignored()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 50, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health),
                X = 0f, Z = 0f, Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 5, LifeId = 2
            }, 1);

            // 上一世代（LifeId=1）迟到的投影：即使版本更高也不得覆盖当前世代
            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 50,
                Projection = new CombatProjection
                {
                    CombatVersion = 99, LifeId = 1, Hp = 1f, MaxHp = 720f, Dead = true
                }
            });

            var view = _registry.GetView(50);
            Assert.IsNotNull(view);
            Assert.AreEqual(720f, view!.Hp, "older life id must not overwrite hp");
            Assert.IsFalse(view.Dead);
            Assert.AreEqual(5u, view.CombatVersion);
        }

        [Test]
        public void test_newer_life_id_projection_waits_for_matching_baseline()
        {
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 51, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health),
                X = 0f, Z = 0f, Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 2, LifeId = 1
            }, 1);
            var view = _registry.GetView(51)!;

            // 新世代（LifeId=2）的投影先到：视图仍属上一辈子 → 先缓存，不应用
            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 51,
                Projection = new CombatProjection
                {
                    CombatVersion = 6, LifeId = 2, Hp = 500f, MaxHp = 800f,
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 77, TemplateId = 90002, ElementId = 2
                    }
                }
            });
            Assert.AreEqual(720f, view.Hp, "newer life id must wait for its baseline");
            Assert.AreEqual(1u, view.LifeId);
            Assert.AreEqual(2u, view.CombatVersion);

            // 该世代的 Health 基线到达（版本 7）：基线状态应用，缓存里较旧的投影被丢弃
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 51, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health),
                X = 0f, Z = 0f, Dead = false, Hp = 600f, MaxHp = 800f,
                CombatVersion = 7, LifeId = 2
            }, 2);

            Assert.AreEqual(2u, view.LifeId);
            Assert.AreEqual(600f, view.Hp, "matching baseline hp applies");
            Assert.AreEqual(7u, view.CombatVersion, "cached older projection is discarded");

            // 同世代的更高版本投影正常应用
            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 51,
                Projection = new CombatProjection
                {
                    CombatVersion = 8, LifeId = 2, Hp = 450f, MaxHp = 800f, IsDefending = true
                }
            });
            Assert.AreEqual(450f, view.Hp);
            Assert.AreEqual(8u, view.CombatVersion);
            Assert.IsTrue(view.IsDefending);
        }

        [Test]
        public void test_active_cast_projection_raises_applied_event()
        {
            var received = new List<(long EntityId, ulong CastId)>();
            _registry.ActiveCastApplied += (entityId, activeCast) =>
                received.Add((entityId, activeCast.CastId));

            // 基线路径：AOI 快照的 Combat.ActiveCast
            _registry.ApplySnapshot(new EntitySnapshot
            {
                PlayerId = 30, Kind = (byte)EntityKind.Player,
                FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Dead
                    | EntityFieldMask.Health | EntityFieldMask.Combat),
                X = 0f, Z = 0f, Dead = false, Hp = 720f, MaxHp = 720f,
                CombatVersion = 2, LifeId = 1,
                Combat = new CombatProjection
                {
                    CombatVersion = 2, LifeId = 1, Hp = 720f, MaxHp = 720f,
                    ActiveCast = new ActiveCastProjection { CastId = 501, SkillId = 10002, EndTick = 40 }
                }
            }, 1);

            // 增量路径：CombatStateUpdate
            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 30,
                Projection = new CombatProjection
                {
                    CombatVersion = 3, LifeId = 1, Hp = 700f, MaxHp = 720f,
                    ActiveCast = new ActiveCastProjection
                    {
                        CastId = 502, SkillId = 10002, Phase = NpcActionPhase.Active, EndTick = 50
                    }
                }
            });

            // 不带 ActiveCast 的投影不触发
            _registry.ApplyCombatStateUpdate(new CombatStateUpdate
            {
                EntityId = 30,
                Projection = new CombatProjection { CombatVersion = 4, LifeId = 1, Hp = 680f, MaxHp = 720f }
            });

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(30L, received[0].EntityId);
            Assert.AreEqual(501UL, received[0].CastId);
            Assert.AreEqual(502UL, received[1].CastId);
        }
    }
}
