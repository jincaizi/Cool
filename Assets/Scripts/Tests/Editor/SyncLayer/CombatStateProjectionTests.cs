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
    }
}
