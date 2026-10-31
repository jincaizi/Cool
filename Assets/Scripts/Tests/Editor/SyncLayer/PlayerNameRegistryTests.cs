using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    public class PlayerNameRegistryTests
    {
        [Test]
        public void Set_Stores_And_Notifies_Once_Per_Change()
        {
            var r = new PlayerNameRegistry();
            int changes = 0; r.OnNameChanged += (_, __) => changes++;
            r.Set(3L, "Alice");
            r.Set(3L, "Alice");
            Assert.IsTrue(r.TryGetName(3L, out var name));
            Assert.AreEqual("Alice", name);
            Assert.AreEqual(1, changes);
        }

        [Test]
        public void Remove_And_Clear_Work()
        {
            var r = new PlayerNameRegistry();
            r.Set(1L, "A");
            r.Set(2L, "B");
            r.Remove(1L);
            Assert.IsFalse(r.TryGetName(1L, out _));
            Assert.IsTrue(r.TryGetName(2L, out _));
            r.Clear();
            Assert.IsFalse(r.TryGetName(2L, out _));
        }
    }
}
