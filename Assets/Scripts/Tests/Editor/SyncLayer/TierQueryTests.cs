using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>`?full=` / `?fulldist=`（design §4 诊断读数与旋钮）。规则与 ServerAddressQuery 同源：
    /// 每种输入都有确定行为；畸形一律**忽略**（现场多打一个字符的代价不能是"打不开游戏"）。</summary>
    public class TierQueryTests
    {
        [Test]
        public void Full_Overrides_The_Budget()
        {
            Assert.IsTrue(TierQuery.TryFull("?full=12", out int budget));
            Assert.AreEqual(12, budget);
        }

        [Test]
        public void Full_Zero_Is_Valid_And_Means_All_Proxy()
        {
            // ?full=0 = 回到"纯胶囊"条件：AOI 验证去噪 + 模型太贵时的回滚开关
            Assert.IsTrue(TierQuery.TryFull("?full=0", out int budget));
            Assert.AreEqual(0, budget, "0 是合法值，不是畸形");
        }

        [Test]
        public void FullDistance_Overrides_The_Threshold()
        {
            Assert.IsTrue(TierQuery.TryFullDistance("?fulldist=25.5", out float distance));
            Assert.AreEqual(25.5f, distance, 0.001f);
        }

        [Test]
        public void Missing_Or_Malformed_Params_Are_Ignored()
        {
            Assert.IsFalse(TierQuery.TryFull(null, out _));
            Assert.IsFalse(TierQuery.TryFull("?host=1.2.3.4", out _));
            Assert.IsFalse(TierQuery.TryFull("?full=abc", out _));
            Assert.IsFalse(TierQuery.TryFull("?full=-3", out _));
            Assert.IsFalse(TierQuery.TryFullDistance("?fulldist=abc", out _));
            Assert.IsFalse(TierQuery.TryFullDistance("?fulldist=-1", out _));
        }

        [Test]
        public void Absurdly_Large_Values_Are_Rejected()
        {
            // 上限存在是为了挡住"?full=999999"这种把整屏实体全塞进 Full 的输入
            Assert.IsFalse(TierQuery.TryFull("?full=999999", out _));
            Assert.IsFalse(TierQuery.TryFullDistance("?fulldist=99999", out _));
        }
    }
}
