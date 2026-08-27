namespace Cool.Protocol
{
    /// <summary>
    /// 协议常量 —— 规则层 v1（spec §3 分层冻结；修改本文件任何常量都必须在
    /// docs/superpowers/specs 的对应章节留痕并升 ProtocolVersion）
    /// </summary>
    public static class ProtocolConstants
    {
        /// <summary>协议版本号（登录握手校验用，v1 冻结起点）</summary>
        public const int ProtocolVersion = 1;

        /// <summary>两端必须使用同一版本的 protobuf-net（spec §3 序列化对齐规则）</summary>
        public const string ProtoBufVersion = "3.2.56";

        /// <summary>协议头长度：flags(1) + messageId(2, ushort 小端) + length(2, ushort 小端)</summary>
        public const int HeaderSize = 5;
    }
}
