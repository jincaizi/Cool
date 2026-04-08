using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;

namespace KcpServer
{
    /// <summary>
    /// 消息标志位
    /// </summary>
    [Flags]
    public enum MessageFlags : byte
    {
        None = 0,
        Reliable = 1 << 0,
        Compressed = 1 << 1,
        Encrypted = 1 << 2
    }

    /// <summary>
    /// 消息编解码器 - 使用 protobuf-net 进行序列化
    /// </summary>
    public static class MessageCodec
    {
        private const int HeaderSize = 5; // flags(1) + messageId(2) + length(2)

        /// <summary>
        /// 编码消息
        /// </summary>
        public static byte[] Encode(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            // 使用 protobuf 序列化消息
            byte[] payload;
            using (var memoryStream = new MemoryStream())
            {
                Serializer.Serialize(memoryStream, message);
                payload = memoryStream.ToArray();
            }

            // 构建协议头
            var data = new byte[HeaderSize + payload.Length];
            data[0] = (byte)flags;
            BitConverter.GetBytes((ushort)messageId).CopyTo(data, 1);
            BitConverter.GetBytes((ushort)payload.Length).CopyTo(data, 3);

            // 复制负载数据
            payload.CopyTo(data, HeaderSize);

            return data;
        }

        /// <summary>
        /// 解码消息
        /// </summary>
        public static (MessageId messageId, IMessage message) Decode(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < HeaderSize) throw new ArgumentException($"Data too short: {data.Length} < {HeaderSize}");

            // 解析协议头
            var flags = (MessageFlags)data[0];
            var messageId = (MessageId)BitConverter.ToUInt16(data, 1);
            var length = BitConverter.ToUInt16(data, 3);

            if (data.Length < HeaderSize + length)
                throw new ArgumentException($"Data length mismatch: {data.Length} < {HeaderSize + length}");

            // 提取负载
            var payload = new byte[length];
            Array.Copy(data, HeaderSize, payload, 0, length);

            // 使用 protobuf 反序列化
            var messageType = MessageTypeRegistry.GetMessageType(messageId);
            if (messageType == null)
                throw new InvalidOperationException($"Unknown message ID: {(ushort)messageId}");

            using (var memoryStream = new MemoryStream(payload))
            {
                var message = Serializer.Deserialize(messageType, memoryStream) as IMessage;
                if (message == null)
                    throw new InvalidOperationException($"Failed to deserialize message type: {messageType.Name}");

                return (messageId, message);
            }
        }

        /// <summary>
        /// 尝试解码消息
        /// </summary>
        public static bool TryDecode(byte[] data, out (MessageId messageId, IMessage message) result)
        {
            result = default;
            try
            {
                result = Decode(data);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 消息类型注册表
    /// </summary>
    public static class MessageTypeRegistry
    {
        private static readonly Dictionary<ushort, Type> _messageIdToType = new Dictionary<ushort, Type>();
        private static readonly Dictionary<Type, ushort> _typeToMessageId = new Dictionary<Type, ushort>();

        static MessageTypeRegistry()
        {
            // 注册消息类型
            RegisterMessageType(MessageId.LoginRequest, typeof(LoginRequest));
            RegisterMessageType(MessageId.LoginResponse, typeof(LoginResponse));
            RegisterMessageType(MessageId.Heartbeat, typeof(Heartbeat));
            RegisterMessageType(MessageId.PositionSyncRequest, typeof(PositionSyncRequest));
            RegisterMessageType(MessageId.PositionSyncResponse, typeof(PositionSyncResponse));
            RegisterMessageType(MessageId.RemotePlayerSync, typeof(RemotePlayerSync));
            RegisterMessageType(MessageId.PlayerEnterRoom, typeof(PlayerEnterRoom));
            RegisterMessageType(MessageId.PlayerLeaveRoom, typeof(PlayerLeaveRoom));
            RegisterMessageType(MessageId.RoomSync, typeof(RoomSync));
        }

        /// <summary>
        /// 注册消息类型
        /// </summary>
        public static void RegisterMessageType(MessageId messageId, Type messageType)
        {
            if (!typeof(IMessage).IsAssignableFrom(messageType))
                throw new ArgumentException($"Type must implement IMessage: {messageType.Name}");

            ushort id = (ushort)messageId;
            _messageIdToType[id] = messageType;
            _typeToMessageId[messageType] = id;
        }

        /// <summary>
        /// 获取消息类型
        /// </summary>
        public static Type? GetMessageType(MessageId messageId)
        {
            ushort id = (ushort)messageId;
            return _messageIdToType.TryGetValue(id, out var type) ? type : null;
        }

        /// <summary>
        /// 获取消息ID
        /// </summary>
        public static MessageId? GetMessageId(Type messageType)
        {
            return _typeToMessageId.TryGetValue(messageType, out var messageId) ? (MessageId)messageId : null;
        }

        /// <summary>
        /// 检查消息类型是否已注册
        /// </summary>
        public static bool IsRegistered(MessageId messageId)
        {
            return _messageIdToType.ContainsKey((ushort)messageId);
        }

        /// <summary>
        /// 获取所有已注册的消息ID
        /// </summary>
        public static IEnumerable<MessageId> GetAllMessageIds()
        {
            return _messageIdToType.Keys.Select(id => (MessageId)id);
        }
    }
}