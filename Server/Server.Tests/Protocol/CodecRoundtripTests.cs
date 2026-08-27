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
    }

    [Fact]
    public void Roundtrip_PositionSyncRequest_PreservesFields()
    {
        var source = new PositionSyncRequest
        {
            X = 1.5f, Y = 0.25f, Z = -3.75f,
            Rotation = 90f, Speed = 4.2f,
            Timestamp = 555L, Sequence = 7U
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.PositionSyncRequest, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var sync = Assert.IsType<PositionSyncRequest>(decoded);
        Assert.Equal(source.X, sync.X, 4);
        Assert.Equal(source.Y, sync.Y, 4);
        Assert.Equal(source.Z, sync.Z, 4);
        Assert.Equal(source.Sequence, sync.Sequence);
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
        Assert.ThrowsAny<Exception>(() => MessageCodec.Decode(bytes));
    }
}
