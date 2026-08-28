using System;
using System.Collections.Generic;
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class WorldSnapshotTests
{
    [Fact]
    public void WorldSnapshot_Roundtrip_Preserves_All()
    {
        var source = new WorldSnapshot
        {
            Tick = 1234,
            ServerTimestamp = 987654321L,
            Entities = new List<EntitySnapshot>
            {
                new EntitySnapshot
                {
                    PlayerId = 7,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.ActionState),
                    X = 1.5f, Y = 0.25f, Z = -3.75f,
                    Rotation = 90f, Speed = 0f,
                    ActionState = 3,
                    Tick = 1234
                },
                new EntitySnapshot
                {
                    PlayerId = 9,
                    FieldMask = (ushort)EntityFieldMask.Position,
                    X = 10f, Y = 2f, Z = -1f,
                    Rotation = 0f, Speed = 4f,
                    ActionState = 0,
                    Tick = 1234
                }
            }
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var snap = Assert.IsType<WorldSnapshot>(decoded);
        Assert.Equal(source.Tick, snap.Tick);
        Assert.Equal(source.ServerTimestamp, snap.ServerTimestamp);
        Assert.Equal(2, snap.Entities.Count);

        var first = snap.Entities[0];
        Assert.Equal(7L, first.PlayerId);
        // 两侧统一 cast 为 ushort（xUnit 泛型推断要求同类型）
        Assert.Equal((ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.ActionState), first.FieldMask);
        Assert.Equal(1.5f, first.X, 4);
        Assert.Equal(0.25f, first.Y, 4);
        Assert.Equal(-3.75f, first.Z, 4);
        Assert.Equal(90f, first.Rotation, 4);
        Assert.Equal(0f, first.Speed, 4);
        Assert.Equal(1234U, first.Tick);
        Assert.Equal(3, first.ActionState);

        var second = snap.Entities[1];
        Assert.Equal(9L, second.PlayerId);
        Assert.Equal((ushort)EntityFieldMask.Position, second.FieldMask);
        Assert.Equal(10f, second.X, 4);
    }

    [Fact]
    public void ClockSync_Request_Response_Roundtrip()
    {
        var request = new ClockSyncRequest { ClientTimestamp = 111L };
        var encodedReq = MessageCodec.EncodeToArray(MessageId.ClockSyncRequest, request);
        var (reqId, reqDecoded) = MessageCodec.Decode(encodedReq);
        Assert.Equal(MessageId.ClockSyncRequest, reqId);
        Assert.Equal(111L, Assert.IsType<ClockSyncRequest>(reqDecoded).ClientTimestamp);

        var response = new ClockSyncResponse { ClientTimestamp = 111L, ServerTimestamp = 222L };
        var encodedResp = MessageCodec.EncodeToArray(MessageId.ClockSyncResponse, response);
        var (respId, respDecoded) = MessageCodec.Decode(encodedResp);
        Assert.Equal(MessageId.ClockSyncResponse, respId);
        var typed = Assert.IsType<ClockSyncResponse>(respDecoded);
        Assert.Equal(111L, typed.ClientTimestamp);
        Assert.Equal(222L, typed.ServerTimestamp);
    }

    [Fact]
    public void LoginResponse_AddedFields_Roundtrip()
    {
        var source = new LoginResponse
        {
            Success = true,
            Message = "ok",
            PlayerId = 42,
            SessionToken = "t0ken",
            ZoneId = "Global",
            ServerTimestamp = 1L,
            ServerTickRateMs = 67,
            SpawnX = 3f, SpawnY = 1f, SpawnZ = -2f,
            SpawnRotation = 45f
        };

        var encoded = MessageCodec.EncodeToArray(MessageId.LoginResponse, source);
        var (_, decoded) = MessageCodec.Decode(encoded);

        var resp = Assert.IsType<LoginResponse>(decoded);
        Assert.Equal(67L, resp.ServerTickRateMs);
        Assert.Equal(3f, resp.SpawnX, 4);
        Assert.Equal(1f, resp.SpawnY, 4);
        Assert.Equal(-2f, resp.SpawnZ, 4);
        Assert.Equal(45f, resp.SpawnRotation, 4);
    }
}
