using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 本人战斗投影的分流（`IsLocalProjection` 是 internal，测试程序集有 InternalsVisibleTo）：
    /// 本人投影只喂本地 Sys3CEntry（控制/HP），**不得**进远程 registry 的未知实体缓存。
    /// </summary>
    public class ClientLocalControlRoutingTests
    {
        [Test]
        public void Local_Projection_Is_Not_Forwarded_To_Remote_Registry()
        {
            Assert.IsTrue(ClientNetworkHost.IsLocalProjection(7, 7));
            Assert.IsFalse(ClientNetworkHost.IsLocalProjection(7, 8));
        }
    }
}
