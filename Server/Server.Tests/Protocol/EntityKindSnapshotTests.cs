using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

/// <summary>P5 T1：实体种类/模板字段线上编解码保真（只加不删——旧字段不受影响）</summary>
public class EntityKindSnapshotTests
{
    [Fact]
    public void WorldSnapshot_Roundtrip_Preserves_Npc_Kind_TemplateId_ActionState()
    {
        var snapshot = new WorldSnapshot
        {
            Tick = 7,
            ServerTimestamp = 1_700_000_000_000_0000L,
            Entities =
            {
                new EntitySnapshot
                {
                    PlayerId = 2_000_001,
                    FieldMask = (ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation
                        | EntityFieldMask.Kind | EntityFieldMask.TemplateId | EntityFieldMask.ActionState),
                    X = 12f, Y = 0f, Z = 3f, Rotation = 90f, Speed = 3.5f,
                    Kind = (byte)EntityKind.Npc,
                    TemplateId = 2,
                    ActionState = (byte)NpcAnimationState.Run,
                    Tick = 7
                }
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable);
        var (id, decoded) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.WorldSnapshot, id);
        var entity = Assert.Single(Assert.IsType<WorldSnapshot>(decoded).Entities);
        Assert.Equal(2_000_001L, entity.PlayerId);
        Assert.Equal((byte)EntityKind.Npc, entity.Kind);
        Assert.Equal(2, entity.TemplateId);
        Assert.Equal((byte)NpcAnimationState.Run, entity.ActionState);
    }

    [Fact]
    public void Player_Snapshot_Defaults_Kind_Zero_After_Roundtrip()
    {
        // 玩家快照不设 Kind → protobuf 默认 0（旧客户端解码兼容——只加不删语义）
        var snapshot = new WorldSnapshot
        {
            Tick = 1,
            Entities = { new EntitySnapshot { PlayerId = 9, FieldMask = (ushort)EntityFieldMask.Position, X = 1f, Z = 2f } }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.WorldSnapshot, snapshot, MessageFlags.Reliable);
        var (_, decoded) = MessageCodec.Decode(bytes);
        var entity = Assert.Single(Assert.IsType<WorldSnapshot>(decoded).Entities);
        Assert.Equal(0, entity.Kind);
        Assert.Equal(0, entity.TemplateId);
    }
}
