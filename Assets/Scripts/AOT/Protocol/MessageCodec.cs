using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;

namespace Cool.Protocol
{
    /// <summary>消息标志位（wire 格式第 1 字节）</summary>
    [Flags]
    public enum MessageFlags : byte
    {
        None = 0,
        Reliable = 1 << 2,
        Encrypted = 1 << 0,
        Compressed = 1 << 1,
        HighPriority = 1 << 3,

        Reserved4 = 1 << 4,
        Reserved5 = 1 << 5,
        Reserved6 = 1 << 6,
        Reserved7 = 1 << 7
    }

    /// <summary>
    /// 消息编解码器 —— 全仓库唯一一份（P0-T5/T7 已收编两侧旧实现；flag 位语义以本文件为准）（规则层 v1：
    /// header[flags(1)|messageId(2 LE)|length(2 LE)] + protobuf 负载）
    /// </summary>
    public static class MessageCodec
    {
        private const int HeaderSize = ProtocolConstants.HeaderSize;

        /// <summary>编码消息到缓冲区</summary>
        public static int Encode(MessageId messageId, object message, IBufferWriter<byte> writer,
            MessageFlags flags = MessageFlags.None, byte[]? encryptionKey = null, bool compress = false)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (writer == null) throw new ArgumentNullException(nameof(writer));

            if ((flags & MessageFlags.Encrypted) != 0 && (encryptionKey == null || encryptionKey.Length == 0))
                throw new ArgumentException("MessageFlags.Encrypted requires a non-empty encryptionKey", nameof(encryptionKey));

            if (!MessageTypeRegistry.IsRegistered(messageId))
                throw new ArgumentException($"Message ID not registered: {(ushort)messageId}", nameof(messageId));

            byte[] payload;
            using (var memoryStream = new MemoryStream())
            {
                Serializer.NonGeneric.Serialize(memoryStream, message);
                payload = memoryStream.ToArray();
            }

            if (compress && payload.Length > 0)
            {
                payload = Compress(payload);
                flags |= MessageFlags.Compressed;
            }

            if ((flags & MessageFlags.Encrypted) != 0 && encryptionKey != null && encryptionKey.Length > 0)
            {
                payload = Encrypt(payload, encryptionKey);
            }

            if (payload.Length > ushort.MaxValue)
                throw new ArgumentException($"Message payload too large: {payload.Length} bytes (max {ushort.MaxValue})", nameof(message));

            var headerSpan = writer.GetSpan(HeaderSize);
            headerSpan[0] = (byte)flags;
            BitConverter.TryWriteBytes(headerSpan.Slice(1), (ushort)messageId);
            BitConverter.TryWriteBytes(headerSpan.Slice(3), (ushort)payload.Length);
            writer.Advance(HeaderSize);

            var payloadSpan = writer.GetSpan(payload.Length);
            payload.CopyTo(payloadSpan);
            writer.Advance(payload.Length);

            return HeaderSize + payload.Length;
        }

        /// <summary>解码消息。调用方每次只应传入单帧完整字节（多帧缓冲须先分帧；本方法不消费尾随字节）</summary>
        public static (MessageId messageId, IMessage message) Decode(byte[] data, byte[]? encryptionKey = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < HeaderSize) throw new ArgumentException($"Data too short: {data.Length} < {HeaderSize}");

            var flags = (MessageFlags)data[0];
            var messageId = (MessageId)BitConverter.ToUInt16(data, 1);
            var length = BitConverter.ToUInt16(data, 3);

            if (data.Length < HeaderSize + length)
                throw new ArgumentException($"Data length mismatch: {data.Length} < {HeaderSize + length}");

            var payload = new byte[length];
            Array.Copy(data, HeaderSize, payload, 0, length);

            if ((flags & MessageFlags.Encrypted) != 0 && encryptionKey != null && encryptionKey.Length > 0)
            {
                payload = Decrypt(payload, encryptionKey);
            }

            if ((flags & MessageFlags.Compressed) != 0)
            {
                payload = Decompress(payload);
            }

            var messageType = MessageTypeRegistry.GetMessageType(messageId);
            if (messageType == null)
                throw new InvalidOperationException($"Unknown message ID: {(ushort)messageId}");

            using (var memoryStream = new MemoryStream(payload))
            {
                var message = Serializer.NonGeneric.Deserialize(messageType, memoryStream) as IMessage;
                if (message == null)
                    throw new InvalidOperationException($"Failed to deserialize message type: {messageType.Name}");

                return (messageId, message);
            }
        }

        /// <summary>尝试解码（失败返回 false 不抛异常）——需要知道失败原因时用下面的重载</summary>
        public static bool TryDecode(byte[] data, out (MessageId messageId, IMessage message) result, byte[]? encryptionKey = null)
            => TryDecode(data, out result, out _, encryptionKey);

        /// <summary>
        /// 尝试解码，并给出失败原因（error 非 null 即失败）。
        ///
        /// 留痕机制：本程序集同时被 Unity 客户端与 .NET 服务端编译，两侧日志设施不同（且不得
        /// 引用 UnityEngine），所以这里不打日志，只把异常交给调用方——调用方是唯一知道该往哪记
        /// 的一方（客户端 → Unity 控制台，服务端 → ILogger）。
        ///
        /// NotSupportedException 必须留在捕获列表里：IL2CPP 的 icall
        /// RuntimeParameterInfo::GetTypeModifiers 未实现，protobuf-net 的 tuple 探测会抛它。
        /// 它曾穿透此处、直接炸在接收回调上，症状是 WorldSnapshot 恒解不出来（列表恒空，
        /// 界面上什么都看不见）——失败必须留下原因，否则和"没收到"无从区分。
        /// </summary>
        public static bool TryDecode(byte[] data, out (MessageId messageId, IMessage message) result,
            out Exception? error, byte[]? encryptionKey = null)
        {
            result = default;
            error = null;
            try
            {
                result = Decode(data, encryptionKey);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException
                                    || ex is InvalidOperationException
                                    || ex is System.IO.EndOfStreamException
                                    || ex is ProtoBuf.ProtoException
                                    || ex is NotSupportedException)
            {
                error = ex;
                return false;
            }
        }

        /// <summary>便捷编码到字节数组</summary>
        public static byte[] EncodeToArray(MessageId messageId, object message,
            MessageFlags flags = MessageFlags.None, byte[]? encryptionKey = null, bool compress = false)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            Encode(messageId, message, bufferWriter, flags, encryptionKey, compress);
            return bufferWriter.WrittenMemory.ToArray();
        }

        // ========== 加密解密（XOR 占位实现，沿袭现状；正式加密另行任务） ==========

        private static byte[] Encrypt(byte[] data, byte[] key)
        {
            var result = new byte[data.Length];
            for (int i = 0; i < data.Length; i++)
            {
                result[i] = (byte)(data[i] ^ key[i % key.Length]);
            }
            return result;
        }

        private static byte[] Decrypt(byte[] data, byte[] key) => Encrypt(data, key);

        // ========== 压缩解压（直通占位实现，沿袭现状） ==========

        private static byte[] Compress(byte[] data) => data;

        private static byte[] Decompress(byte[] data) => data;
    }

    /// <summary>消息类型注册表（id ↔ Type 双向映射）</summary>
    public static class MessageTypeRegistry
    {
        private static readonly Dictionary<ushort, Type> _messageIdToType = new Dictionary<ushort, Type>();
        private static readonly Dictionary<Type, ushort> _typeToMessageId = new Dictionary<Type, ushort>();

        static MessageTypeRegistry()
        {
            RegisterMessageType(MessageId.LoginRequest, typeof(LoginRequest));
            RegisterMessageType(MessageId.LoginResponse, typeof(LoginResponse));
            RegisterMessageType(MessageId.Heartbeat, typeof(Heartbeat));
            RegisterMessageType(MessageId.Kick, typeof(Kick));
            RegisterMessageType(MessageId.DisconnectRequest, typeof(DisconnectRequest));
            RegisterMessageType(MessageId.ShutdownNotification, typeof(ShutdownNotification));
            RegisterMessageType(MessageId.EchoRequest, typeof(EchoRequest));
            RegisterMessageType(MessageId.EchoResponse, typeof(EchoResponse));
            RegisterMessageType(MessageId.ClockSyncRequest, typeof(ClockSyncRequest));
            RegisterMessageType(MessageId.ClockSyncResponse, typeof(ClockSyncResponse));
            RegisterMessageType(MessageId.PositionSyncRequest, typeof(PositionSyncRequest));
            RegisterMessageType(MessageId.PositionSyncResponse, typeof(PositionSyncResponse));
            RegisterMessageType(MessageId.WorldSnapshot, typeof(WorldSnapshot));
            RegisterMessageType(MessageId.CastRequest, typeof(CastRequest));
            RegisterMessageType(MessageId.CastBroadcast, typeof(CastBroadcast));
            RegisterMessageType(MessageId.DamageResult, typeof(DamageResult));
            RegisterMessageType(MessageId.RespawnNotice, typeof(RespawnNotice));
            RegisterMessageType(MessageId.ChatMessage, typeof(ChatMessage));
            RegisterMessageType(MessageId.BroadcastMessage, typeof(BroadcastMessage));
            RegisterMessageType(MessageId.RemotePlayerSync, typeof(RemotePlayerSync));
            RegisterMessageType(MessageId.PlayerEnterRoom, typeof(PlayerEnterRoom));
            RegisterMessageType(MessageId.PlayerLeaveRoom, typeof(PlayerLeaveRoom));
            RegisterMessageType(MessageId.RoomSync, typeof(RoomSync));
            RegisterMessageType(MessageId.NpcSpawn, typeof(NpcSpawn));
            RegisterMessageType(MessageId.NpcDespawn, typeof(NpcDespawn));
            RegisterMessageType(MessageId.NpcPosSync, typeof(NpcPosSync));
            RegisterMessageType(MessageId.NpcAnimSync, typeof(NpcAnimSync));
        }

        /// <summary>注册消息类型（重复注册同一 id 覆盖旧值）</summary>
        public static void RegisterMessageType(MessageId messageId, Type messageType)
        {
            if (!typeof(IMessage).IsAssignableFrom(messageType))
                throw new ArgumentException($"Type must implement IMessage: {messageType.Name}");

            ushort id = (ushort)messageId;
            _messageIdToType[id] = messageType;
            _typeToMessageId[messageType] = id;
        }

        public static void RegisterMessageType<T>(MessageId messageId) where T : IMessage
            => RegisterMessageType(messageId, typeof(T));

        public static Type? GetMessageType(MessageId messageId)
            => _messageIdToType.TryGetValue((ushort)messageId, out var type) ? type : null;

        public static MessageId? GetMessageId(Type messageType)
            => _typeToMessageId.TryGetValue(messageType, out var messageId) ? (MessageId)messageId : null;

        public static MessageId? GetMessageId<T>() where T : IMessage
            => GetMessageId(typeof(T));

        public static bool IsRegistered(MessageId messageId)
            => _messageIdToType.ContainsKey((ushort)messageId);

        public static bool IsRegistered(Type messageType)
            => _typeToMessageId.ContainsKey(messageType);

        public static IEnumerable<MessageId> GetAllMessageIds()
            => _messageIdToType.Keys.Select(id => (MessageId)id);
    }

    /// <summary>ArrayBufferWriter 实现（非泛型 IBufferWriter 实现，沿袭 Unity 侧现状）</summary>
    public sealed class ArrayBufferWriter : IBufferWriter<byte>
    {
        private byte[] _buffer;
        private int _written;

        public ArrayBufferWriter(int initialCapacity = 1024)
        {
            _buffer = new byte[initialCapacity];
            _written = 0;
        }

        public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);
        public int WrittenCount => _written;
        public int Capacity => _buffer.Length;
        public int FreeCapacity => _buffer.Length - _written;

        public void Advance(int count)
        {
            if (count < 0) throw new ArgumentException(nameof(count));
            if (_written + count > _buffer.Length) throw new InvalidOperationException("Cannot advance beyond buffer capacity");
            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            CheckAndResizeBuffer(sizeHint);
            return _buffer.AsSpan(_written);
        }

        public void Clear() => _written = 0;

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
