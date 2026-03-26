using System;
using System.Buffers;
using System.IO;
using ProtoBuf;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;

namespace KcpNet
{
    /// <summary>
    /// 消息标志位
    /// </summary>
    [Flags]
    public enum MessageFlags : byte
    {
        /// <summary>
        /// 无标志
        /// </summary>
        None = 0,

        /// <summary>
        /// 加密标志
        /// </summary>
        Encrypted = 1 << 0,

        /// <summary>
        /// 压缩标志
        /// </summary>
        Compressed = 1 << 1,

        /// <summary>
        /// 可靠传输标志
        /// </summary>
        Reliable = 1 << 2,

        /// <summary>
        /// 高优先级标志
        /// </summary>
        HighPriority = 1 << 3,

        /// <summary>
        /// 保留标志4
        /// </summary>
        Reserved4 = 1 << 4,

        /// <summary>
        /// 保留标志5
        /// </summary>
        Reserved5 = 1 << 5,

        /// <summary>
        /// 保留标志6
        /// </summary>
        Reserved6 = 1 << 6,

        /// <summary>
        /// 保留标志7
        /// </summary>
        Reserved7 = 1 << 7
    }

    /// <summary>
    /// 消息编解码器
    /// </summary>
    public static class MessageCodec
    {
        private const int HeaderSize = 5; // flag(1) + messageId(2) + length(2)

        /// <summary>
        /// 编码消息
        /// </summary>
        /// <param name="messageId">消息ID</param>
        /// <param name="message">消息对象</param>
        /// <param name="writer">缓冲区写入器</param>
        /// <param name="flags">消息标志</param>
        /// <param name="encryptionKey">加密密钥（可选）</param>
        /// <param name="compress">是否压缩</param>
        /// <returns>编码后的数据长度</returns>
        public static int Encode(MessageId messageId, object message, IBufferWriter<byte> writer,
            MessageFlags flags = MessageFlags.None, byte[]? encryptionKey = null, bool compress = false)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            // 序列化消息
            byte[] payload;
            using (var memoryStream = new MemoryStream())
            {
                Serializer.NonGeneric.Serialize(memoryStream, message);
                payload = memoryStream.ToArray();
            }

            // 压缩（如果启用）
            if (compress && payload.Length > 0)
            {
                payload = Compress(payload);
                flags |= MessageFlags.Compressed;
            }

            // 加密（如果启用）
            if ((flags & MessageFlags.Encrypted) != 0 && encryptionKey != null && encryptionKey.Length > 0)
            {
                payload = Encrypt(payload, encryptionKey);
            }

            // 写入头部
            var headerSpan = writer.GetSpan(HeaderSize);
            headerSpan[0] = (byte)flags;
            BitConverter.TryWriteBytes(headerSpan.Slice(1), (ushort)messageId);
            BitConverter.TryWriteBytes(headerSpan.Slice(3), (ushort)payload.Length);
            writer.Advance(HeaderSize);

            // 写入负载
            var payloadSpan = writer.GetSpan(payload.Length);
            payload.CopyTo(payloadSpan);
            writer.Advance(payload.Length);

            return HeaderSize + payload.Length;
        }

        /// <summary>
        /// 解码消息
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <param name="encryptionKey">解密密钥（可选）</param>
        /// <returns>消息ID和消息对象</returns>
        public static (MessageId messageId, IMessage message) Decode(byte[] data, byte[]? encryptionKey = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < HeaderSize) throw new ArgumentException($"Data too short: {data.Length} < {HeaderSize}");

            // 解析头部
            var flags = (MessageFlags)data[0];
            var messageId = (MessageId)BitConverter.ToUInt16(data, 1);
            var length = BitConverter.ToUInt16(data, 3);

            if (data.Length < HeaderSize + length)
                throw new ArgumentException($"Data length mismatch: {data.Length} < {HeaderSize + length}");

            // 提取负载
            var payload = new byte[length];
            Array.Copy(data, HeaderSize, payload, 0, length);

            // 解密（如果启用）
            if ((flags & MessageFlags.Encrypted) != 0 && encryptionKey != null && encryptionKey.Length > 0)
            {
                payload = Decrypt(payload, encryptionKey);
            }

            // 解压缩（如果启用）
            if ((flags & MessageFlags.Compressed) != 0)
            {
                payload = Decompress(payload);
            }

            // 反序列化消息
            var messageType = MessageTypeRegistry.GetMessageType(messageId);
            if (messageType == null)
                throw new InvalidOperationException($"Unknown message ID: {messageId}");

            using (var memoryStream = new MemoryStream(payload))
            {
                var message = Serializer.NonGeneric.Deserialize(messageType, memoryStream) as IMessage;
                if (message == null)
                    throw new InvalidOperationException($"Failed to deserialize message type: {messageType.Name}");

                return (messageId, message);
            }
        }

        /// <summary>
        /// 尝试解码消息
        /// </summary>
        /// <param name="data">原始数据</param>
        /// <param name="result">解码结果</param>
        /// <param name="encryptionKey">解密密钥（可选）</param>
        /// <returns>是否解码成功</returns>
        public static bool TryDecode(byte[] data, out (MessageId messageId, IMessage message) result, byte[]? encryptionKey = null)
        {
            result = default;
            try
            {
                result = Decode(data, encryptionKey);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 编码消息（使用ArrayPool分配缓冲区）
        /// </summary>
        public static byte[] EncodeToArray(MessageId messageId, object message,
            MessageFlags flags = MessageFlags.None, byte[]? encryptionKey = null, bool compress = false)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            Encode(messageId, message, bufferWriter, flags, encryptionKey, compress);
            return bufferWriter.WrittenMemory.ToArray();
        }

        // ========== 加密解密 ==========

        private static byte[] Encrypt(byte[] data, byte[] key)
        {
            // 简单XOR加密（示例）
            // 实际项目中应使用AES等安全加密算法
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            }
            return result;
        }

        private static byte[] Decrypt(byte[] data, byte[] key)
        {
            // XOR解密是对称的
            return Encrypt(data, key);
        }

        // ========== 压缩解压 ==========

        private static byte[] Compress(byte[] data)
        {
            // 简单压缩示例（实际应使用GZip或Deflate）
            // 这里返回原数据，实际项目需要实现真实压缩
            return data;
        }

        private static byte[] Decompress(byte[] data)
        {
            // 简单解压示例
            return data;
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
            // 注册基础消息类型
            RegisterMessageType(MessageId.LoginRequest, typeof(LoginRequest));
            RegisterMessageType(MessageId.LoginResponse, typeof(LoginResponse));
            RegisterMessageType(MessageId.Heartbeat, typeof(Heartbeat));
            RegisterMessageType(MessageId.Kick, typeof(Kick));
            RegisterMessageType(MessageId.DisconnectRequest, typeof(DisconnectRequest));
            RegisterMessageType(MessageId.ShutdownNotification, typeof(ShutdownNotification));
            RegisterMessageType(MessageId.PositionSyncRequest, typeof(PositionSyncRequest));
            RegisterMessageType(MessageId.PositionSyncResponse, typeof(PositionSyncResponse));
            RegisterMessageType(MessageId.ChatMessage, typeof(ChatMessage));
            RegisterMessageType(MessageId.BroadcastMessage, typeof(BroadcastMessage));
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
        /// 注册消息类型
        /// </summary>
        public static void RegisterMessageType<T>(MessageId messageId) where T : IMessage
        {
            RegisterMessageType(messageId, typeof(T));
        }

        /// <summary>
        /// 获取消息类型
        /// </summary>
        public static Type? GetMessageType(MessageId messageId)
        {
            ushort id = (ushort)messageId;
            _messageIdToType.TryGetValue(id, out var type);
            return type;
        }

        /// <summary>
        /// 获取消息ID
        /// </summary>
        public static MessageId? GetMessageId(Type messageType)
        {
            if (_typeToMessageId.TryGetValue(messageType, out var messageId))
            {
                return (MessageId)messageId;
            }
            return null;
        }

        /// <summary>
        /// 获取消息ID
        /// </summary>
        public static MessageId? GetMessageId<T>() where T : IMessage
        {
            return GetMessageId(typeof(T));
        }

        /// <summary>
        /// 检查消息类型是否已注册
        /// </summary>
        public static bool IsRegistered(MessageId messageId)
        {
            ushort id = (ushort)messageId;
            return _messageIdToType.ContainsKey(id);
        }

        /// <summary>
        /// 检查消息类型是否已注册
        /// </summary>
        public static bool IsRegistered(Type messageType)
        {
            return _typeToMessageId.ContainsKey(messageType);
        }

        /// <summary>
        /// 获取所有已注册的消息ID
        /// </summary>
        public static IEnumerable<MessageId> GetAllMessageIds()
        {
            return _messageIdToType.Keys.Select(id => (MessageId)id);
        }
    }

    /// <summary>
    /// ArrayBufferWriter实现（.NET Standard 2.0兼容）
    /// </summary>
    public sealed class ArrayBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer;
        private int _written;

        /// <summary>
        /// 初始化ArrayBufferWriter
        /// </summary>
        public ArrayBufferWriter(int initialCapacity = 1024)
        {
            _buffer = new byte[initialCapacity];
            _written = 0;
        }

        /// <summary>
        /// 已写入的数据
        /// </summary>
        public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

        /// <summary>
        /// 已写入的数据
        /// </summary>
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

        /// <summary>
        /// 已写入的字节数
        /// </summary>
        public int WrittenCount => _written;

        /// <summary>
        /// 缓冲区容量
        /// </summary>
        public int Capacity => _buffer.Length;

        /// <summary>
        /// 剩余容量
        /// </summary>
        public int FreeCapacity => _buffer.Length - _written;

        /// <inheritdoc/>
        public void Advance(int count)
        {
            if (count < 0) throw new ArgumentException(nameof(count));
            if (_written + count > _buffer.Length) throw new InvalidOperationException("Cannot advance beyond buffer capacity");
            _written += count;
        }

        /// <inheritdoc/>
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsMemory(_written);
        }

        /// <inheritdoc/>
        public Span<byte> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsSpan(_written);
        }

        /// <summary>
        /// 清空缓冲区
        /// </summary>
        public void Clear()
        {
            _written = 0;
        }

        /// <summary>
        /// 转换为字节数组
        /// </summary>
        public byte[] ToArray()
        {
            var result = new byte[_written];
            Array.Copy(_buffer, 0, result, 0, _written);
            return result;
        }

        private void CheckAndResizeBuffer(int sizeHint)
        {
            if (sizeHint < 0) throw new ArgumentException(nameof(sizeHint));

            var sizeHintOrMinimum = sizeHint > 0 ? sizeHint : 256;
            if (sizeHintOrMinimum <= FreeCapacity) return;

            var currentLength = _buffer.Length;
            var growBy = Math.Max(sizeHintOrMinimum, currentLength);
            var newSize = currentLength + growBy;

            if ((uint)newSize > int.MaxValue)
            {
                newSize = currentLength + sizeHintOrMinimum;
                if ((uint)newSize > int.MaxValue)
                    throw new OutOfMemoryException($"Cannot allocate buffer of size {newSize}");
            }

            Array.Resize(ref _buffer, newSize);
        }
    }
}