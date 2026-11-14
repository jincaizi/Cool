using Cool.Protocol;
using Hotfix.GameSystems.Bag.Core;
using Hotfix.GameSystems.Bag.Data;
using Hotfix.GameSystems.Bag.Runtime;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.Bag
{
    public class BagWeaponInventoryTests
    {
        [SetUp]
        public void SetUp() => BagManager.Instance.Clear();

        [TearDown]
        public void TearDown() => BagManager.Instance.Clear();

        [Test]
        public void ApplyServerWeapons_Uses_Authoritative_Instance_And_Template_Ids()
        {
            BagManager.Instance.ApplyServerWeapons(new[]
            {
                new WeaponInstanceSummary { InstanceId = 42, TemplateId = 90002, ElementId = 1 },
                new WeaponInstanceSummary { InstanceId = 43, TemplateId = 90003, ElementId = 2 }
            });

            var first = BagManager.Instance.GetItem(0);
            var second = BagManager.Instance.GetItem(1);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.AreEqual(42L, first!.InstanceId);
            Assert.AreEqual(90002, first.TemplateId);
            Assert.AreEqual(ItemType.Equipment, first.Template.Type);
            Assert.AreEqual(EquipmentSlot.Weapon, first.Template.EquipSlot);
            Assert.AreEqual(43L, second!.InstanceId);
            Assert.AreEqual(90003, second.TemplateId);
        }

        [Test]
        public void RequestEquip_Raises_Clicked_Item()
        {
            var item = new ItemData { InstanceId = 42, TemplateId = 90002, Count = 1 };
            ItemData captured = null;
            void Handler(ItemData clicked) => captured = clicked;

            BagManager.Instance.OnEquipRequested += Handler;
            try
            {
                BagManager.Instance.RequestEquip(item);
            }
            finally
            {
                BagManager.Instance.OnEquipRequested -= Handler;
            }

            Assert.AreSame(item, captured);
        }
    }
}
