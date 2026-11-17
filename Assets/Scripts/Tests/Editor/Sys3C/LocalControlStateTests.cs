using Hotfix.GameSystems.Sys3C.Character;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.Sys3C
{
    public class LocalControlStateTests
    {
        [Test]
        public void Apply_Gates_By_Version_And_Reports_Lock_Edge()
        {
            var state = new LocalControlState();

            Assert.IsTrue(state.Apply(0.7f, true, 2), "首次从无锁到锁必须报告边沿");
            Assert.AreEqual(2u, state.Version);
            Assert.AreEqual(0.7f, state.SpeedScale, 0.0001f);
            Assert.IsTrue(state.ActionLock);

            Assert.IsFalse(state.Apply(1f, false, 1), "旧版本必须忽略");
            Assert.AreEqual(2u, state.Version);
            Assert.IsTrue(state.ActionLock);

            Assert.IsFalse(state.Apply(1f, false, 3), "解除锁不是“变为锁”的边沿");
            Assert.AreEqual(3u, state.Version);
            Assert.IsFalse(state.ActionLock);
        }

        [Test]
        public void Apply_Clamps_Invalid_Scale()
        {
            var state = new LocalControlState();
            state.Apply(float.NaN, false, 1);
            Assert.AreEqual(0f, state.SpeedScale, 0.0001f);
        }
    }
}
