using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>
    /// 复活通知（服务器 → 被复活者**本人**）。
    ///
    /// 为什么需要它：本地玩家**不在自己的 AOI 快照里**
    /// （`ClientNetworkManager` 应用快照时 `if (entity.PlayerId == LocalPlayerId) continue;`），
    /// 所以快照里的 `Dead` 只能让**观察者**知道别人的死活，本人收不到任何通知。
    /// 而客户端在联网态**有意短路**本地复活（`Sys3CEntry.EvaluateLocalDeath` 的注释写明：
    /// 免得"客户端站起来了、服务端还当他死了"）——没有这条消息，被复活者会永远停在死亡画面。
    /// </summary>
    [ProtoContract]
    public class RespawnNotice : IMessage
    {
        /// <summary>复活后的血量（**服务端权威**，客户端直接采信，不自算）</summary>
        [ProtoMember(1)]
        public float Hp { get; set; }

        /// <summary>服务器 tick</summary>
        [ProtoMember(2)]
        public uint Tick { get; set; }
    }
}
