using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>P0 连通性验收载体：回环请求（KCP over UDP 真 socket 往返）</summary>
    [ProtoContract]
    public class EchoRequest : IMessage
    {
        /// <summary>客户端发起时刻的服务器时钟换算值（P0 阶段允许填本地墙钟）</summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }
    }

    /// <summary>P0 连通性验收载体：回环响应</summary>
    [ProtoContract]
    public class EchoResponse : IMessage
    {
        /// <summary>原样回传请求中的 ClientTimestamp</summary>
        [ProtoMember(1)]
        public long ClientTimestamp { get; set; }

        /// <summary>响应生成时刻的服务器时钟</summary>
        [ProtoMember(2)]
        public long ServerTimestamp { get; set; }
    }
}
