using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class ClientEquipmentStateTests
    {
        [Test]
        public void Applies_Newer_Revision_And_Ignores_Stale()
        {
            var state = new ClientEquipmentState();
            Assert.IsTrue(state.TryApply(new EquipWeaponConfirm
            {
                Accepted = true,
                EquipmentRevision = 2,
                WeaponInstanceId = 42,
                TemplateId = 90002,
                ElementId = 1
            }));
            Assert.AreEqual(2u, state.Revision);
            Assert.AreEqual(1, state.ElementId);
            Assert.AreEqual(42L, state.WeaponInstanceId);

            Assert.IsFalse(state.TryApply(new EquipWeaponConfirm
            {
                Accepted = true,
                EquipmentRevision = 1,
                WeaponInstanceId = 43,
                TemplateId = 90003,
                ElementId = 2
            }));
            Assert.AreEqual(42L, state.WeaponInstanceId);
        }

        [Test]
        public void Baseline_Initializes_Revision_LifeId_And_Weapon()
        {
            var state = new ClientEquipmentState();
            state.InitializeFromBaseline(new WeaponInventoryBaseline
            {
                LifeId = 3,
                EquipmentRevision = 4,
                WeaponInstanceId = 42,
                TemplateId = 90002,
                ElementId = 1
            });

            Assert.AreEqual(4u, state.Revision);
            Assert.AreEqual(3u, state.LifeId);
            Assert.AreEqual(42L, state.WeaponInstanceId);
            Assert.AreEqual(90002, state.TemplateId);
            Assert.AreEqual(1, state.ElementId);
            Assert.AreEqual(1f, state.AttackSpeedMultiplier, 0.001f);
        }

        [Test]
        public void Rejected_Newer_Summary_Restores_Authoritative_Equipment()
        {
            var state = new ClientEquipmentState();
            state.TryApply(new EquipWeaponConfirm
            {
                Accepted = true, EquipmentRevision = 2, WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
            });

            bool changed = state.TryApply(new EquipWeaponConfirm
            {
                Accepted = false,
                RejectReason = "stale-revision",
                EquipmentRevision = 5,
                WeaponInstanceId = 43,
                TemplateId = 90003,
                ElementId = 2
            });

            Assert.IsTrue(changed, "拒绝携带的较新摘要也必须恢复当前装备");
            Assert.AreEqual(5u, state.Revision);
            Assert.AreEqual(43L, state.WeaponInstanceId);
            Assert.AreEqual(3, state.TemplateId - 90000);
            Assert.AreEqual(2, state.ElementId);
        }

        [Test]
        public void Reset_Clears_Login_State()
        {
            var state = new ClientEquipmentState();
            state.InitializeFromBaseline(new WeaponInventoryBaseline
            {
                LifeId = 3, EquipmentRevision = 4, WeaponInstanceId = 42,
                TemplateId = 90002, ElementId = 1
            });

            state.Reset();

            Assert.AreEqual(0u, state.Revision);
            Assert.AreEqual(1u, state.LifeId);
            Assert.AreEqual(0L, state.WeaponInstanceId);
            Assert.IsFalse(state.HasEquipment);
        }
    }
}
