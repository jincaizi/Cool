using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using ProtoBuf;
using Xunit;

namespace Cool.Protocol.Tests;

public class CodecRoundtripTests
{
    [Fact]
    public void Roundtrip_EchoResponse_PreservesFields()
    {
        var source = new EchoResponse
        {
            ClientTimestamp = 123456789L,
            ServerTimestamp = 987654321L
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.EchoResponse, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var response = Assert.IsType<EchoResponse>(decoded);
        Assert.Equal(source.ClientTimestamp, response.ClientTimestamp);
        Assert.Equal(source.ServerTimestamp, response.ServerTimestamp);
    }

    [Fact]
    public void Roundtrip_LoginRequest_MergedFields()
    {
        var source = new LoginRequest
        {
            Username = "jincaizi",
            PasswordHash = "md5_hash_placeholder",
            ZoneId = "Global",
            ClientVersion = "1.0.0",
            DeviceInfo = "PC",
            ProtocolVersion = ProtocolConstants.ProtocolVersion
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.LoginRequest, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var request = Assert.IsType<LoginRequest>(decoded);
        Assert.Equal(source.Username, request.Username);
        Assert.Equal(source.ZoneId, request.ZoneId);
        Assert.Equal(source.DeviceInfo, request.DeviceInfo);
        Assert.Equal(source.ProtocolVersion, request.ProtocolVersion);
        Assert.Equal(source.PasswordHash, request.PasswordHash);
        Assert.Equal(source.ClientVersion, request.ClientVersion);
    }

    [Fact]
    public void Roundtrip_PositionSyncRequest_PreservesFields()
    {
        var source = new PositionSyncRequest
        {
            X = 1.5f, Y = 0.25f, Z = -3.75f,
            Rotation = 90f, Speed = 4.2f,
            IsDefending = true,     // 格挡减伤服务端化的输入侧（tag 8）
            Timestamp = 555L, Sequence = 7U
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.PositionSyncRequest, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var sync = Assert.IsType<PositionSyncRequest>(decoded);
        Assert.Equal(source.X, sync.X, 4);
        Assert.Equal(source.Y, sync.Y, 4);
        Assert.Equal(source.Z, sync.Z, 4);
        Assert.Equal(source.Sequence, sync.Sequence);
        Assert.Equal(source.Rotation, sync.Rotation, 4);
        Assert.Equal(source.Speed, sync.Speed, 4);
        Assert.Equal(source.Timestamp, sync.Timestamp);
        Assert.True(sync.IsDefending, "IsDefending 未上线——服务端永远看到 false，格挡减伤整条链静默失效");
    }

    [Fact]
    public void HeaderLayout_MatchesRuleLayerV1()
    {
        // flags(1) + messageId(2, LE) + length(2, LE) + protobuf payload
        var source = new EchoRequest { ClientTimestamp = 42L };
        var bytes = MessageCodec.EncodeToArray(MessageId.EchoRequest, source, MessageFlags.Reliable);

        Assert.Equal((byte)MessageFlags.Reliable, bytes[0]);
        Assert.Equal((ushort)MessageId.EchoRequest, BitConverter.ToUInt16(bytes, 1));
        int payloadLength = BitConverter.ToUInt16(bytes, 3);
        Assert.Equal(bytes.Length - ProtocolConstants.HeaderSize, payloadLength);
    }

    [Fact]
    public void Decode_UnregisteredId_Throws()
    {
        var bytes = new byte[ProtocolConstants.HeaderSize]; // 全零头 = 无效消息
        Assert.Throws<InvalidOperationException>(() => MessageCodec.Decode(bytes));
    }

    [Fact]
    public void Encode_PayloadOverUShort_Throws()
    {
        var big = new BroadcastMessage { Content = new string('x', 70000) };
        Assert.Throws<ArgumentException>(() =>
            MessageCodec.EncodeToArray(MessageId.BroadcastMessage, big));
    }

    [Fact]
    public void Encode_EncryptedFlagWithoutKey_Throws()
    {
        var msg = new EchoRequest { ClientTimestamp = 1L };
        Assert.Throws<ArgumentException>(() =>
            MessageCodec.EncodeToArray(MessageId.EchoRequest, msg, MessageFlags.Encrypted));
    }

    [Fact]
    public void Encode_UnregisteredMessageId_Throws()
    {
        var msg = new EchoRequest { ClientTimestamp = 1L };
        Assert.Throws<ArgumentException>(() =>
            MessageCodec.EncodeToArray((MessageId)9999, msg));
    }

    [Fact]
    public void Decode_TooShortData_Throws()
    {
        Assert.Throws<ArgumentException>(() => MessageCodec.Decode(new byte[ProtocolConstants.HeaderSize - 1]));
    }

    [Fact]
    public void Decode_LengthMismatch_Throws()
    {
        // 头部声明 10 字节负载，实际负载 0 字节
        var data = new byte[ProtocolConstants.HeaderSize];
        BitConverter.GetBytes((ushort)10).CopyTo(data, 3);
        Assert.Throws<ArgumentException>(() => MessageCodec.Decode(data));
    }

    [Fact]
    public void TryDecode_Malformed_ReturnsFalse()
    {
        Assert.False(MessageCodec.TryDecode(new byte[2], out _));
    }

    [Fact]
    public void Roundtrip_CompressTrue_PassthroughSurvives()
    {
        var source = new EchoResponse { ClientTimestamp = 7L, ServerTimestamp = 8L };
        var encoded = MessageCodec.EncodeToArray(MessageId.EchoResponse, source, MessageFlags.None, compress: true);
        var (_, decoded) = MessageCodec.Decode(encoded);
        var response = Assert.IsType<EchoResponse>(decoded);
        Assert.Equal(7L, response.ClientTimestamp);
        Assert.Equal(8L, response.ServerTimestamp);
        // compress 标志位已被置上（直通压缩沿袭现状，P1 换真实实现时本测试守门）
        Assert.Equal(MessageFlags.Compressed, (MessageFlags)encoded[0] & MessageFlags.Compressed);
    }

    [Fact]
    public void Roundtrip_EncryptedWithKey_Survives()
    {
        var key = new byte[] { 1, 2, 3, 4 };
        var source = new EchoRequest { ClientTimestamp = 99L };
        var encoded = MessageCodec.EncodeToArray(MessageId.EchoRequest, source, MessageFlags.Encrypted, key);
        var (_, decoded) = MessageCodec.Decode(encoded, key);
        Assert.Equal(99L, Assert.IsType<EchoRequest>(decoded).ClientTimestamp);
    }

    [Fact]
    public void Roundtrip_WorldSnapshot_Preserves_Health_And_Defending()
    {
        var source = new WorldSnapshot
        {
            Tick = 7u,
            ServerTimestamp = 123L,
            Entities =
            {
                new EntitySnapshot
                {
                    PlayerId = 2L,
                    FieldMask = (ushort)(EntityFieldMask.Health | EntityFieldMask.Defending),
                    Hp = 42.5f,
                    MaxHp = 100f,
                    IsDefending = true,
                    Tick = 7u
                }
            }
        };

        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, source);
        var (_, decoded) = MessageCodec.Decode(bytes);
        var snapshot = Assert.IsType<WorldSnapshot>(decoded);
        var entity = Assert.Single(snapshot.Entities);
        Assert.Equal(42.5f, entity.Hp, 4);
        Assert.Equal(100f, entity.MaxHp, 4);
        Assert.True(entity.IsDefending);
    }

    [Fact]
    public void Roundtrip_CastBroadcast_Preserves_Impact_And_Charge()
    {
        var source = new CastBroadcast
        {
            SkillId = 20004, CasterPlayerId = 3L,
            ImpactX = 1f, ImpactY = 0f, ImpactZ = 9f,
            ChargeProgress = 0.75f,
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2f }
        };

        var bytes = MessageCodec.EncodeToArray(MessageId.CastBroadcast, source);
        var (_, decoded) = MessageCodec.Decode(bytes);
        var cast = Assert.IsType<CastBroadcast>(decoded);
        Assert.Equal(1f, cast.ImpactX, 4);
        Assert.Equal(9f, cast.ImpactZ, 4);
        Assert.Equal(0.75f, cast.ChargeProgress, 4);
    }
}
