using System.Collections.Generic;
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class NpcActionProtocolTests
{
    [Fact]
    public void Event_Codec_Roundtrip_Preserves_Action_And_Clone_Is_Independent()
    {
        var action = new NpcActionSnapshot { InstanceId = ulong.MaxValue, Revision = 4,
            Kind = NpcActionKind.Skill, Phase = NpcActionPhase.Windup, EffectiveTick = 0,
            SkillId = 50001, EndTick = 12, AimYaw = 33, PresentationVariant = 2 };
        var decoded = (NpcActionEvent)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.NpcActionEvent, new NpcActionEvent { EntityId = 8, Action = action, ServerTimestamp = 123 })).message;
        Assert.Equal(action.InstanceId, decoded.Action.InstanceId);
        Assert.Equal(action.Phase, decoded.Action.Phase);
        Assert.Equal(33, decoded.Action.AimYaw);
        var copy = action.Clone(); copy.Revision++;
        Assert.Equal(4u, action.Revision);
    }

    [Fact]
    public void World_Roundtrip_Preserves_Batch_History_Exit_And_Anchor()
    {
        var world = new WorldSnapshot { SnapshotSequence = 8, PartIndex = 1, PartCount = 3,
            TickAnchorServerTimestamp = 999, NpcExits = { new NpcVisibilityExit { EntityId = 7, ExitTick = 20 } },
            Entities = { new EntitySnapshot { PlayerId = 8, FieldMask = (ushort)EntityFieldMask.NpcAction,
                NpcAction = new NpcActionSnapshot { InstanceId = 4 },
                NpcActionHistory = { new NpcActionSnapshot { InstanceId = 3 } } } } };
        var copy = (WorldSnapshot)MessageCodec.Decode(MessageCodec.EncodeToArray(MessageId.WorldSnapshot, world)).message;
        Assert.Equal(8ul, copy.SnapshotSequence); Assert.Equal(999, copy.TickAnchorServerTimestamp);
        Assert.Equal(20u, copy.NpcExits[0].ExitTick);
        Assert.Equal(3ul, copy.Entities[0].NpcActionHistory[0].InstanceId);
        Assert.Equal(4ul, copy.Entities[0].NpcAction!.InstanceId);
        Assert.Equal(typeof(IList<NpcActionSnapshot>), typeof(EntitySnapshot).GetProperty("NpcActionHistory")!.PropertyType);
    }

    [Fact]
    public void Legacy_Defaults_And_Handshake_Are_Additive()
    {
        var legacy = (WorldSnapshot)MessageCodec.Decode(MessageCodec.EncodeToArray(MessageId.WorldSnapshot, new WorldSnapshot())).message;
        Assert.Empty(legacy.NpcExits); Assert.Equal(0ul, legacy.SnapshotSequence);
        Assert.Null(new EntitySnapshot().NpcAction);
        var request = (LoginRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(MessageId.LoginRequest,
            new LoginRequest { SupportedFeatures = ProtocolFeatures.NpcActionTimelineV1, NpcContentHash = "abc" })).message;
        Assert.Equal(1u, request.SupportedFeatures); Assert.Equal("abc", request.NpcContentHash);
        var response = (LoginResponse)MessageCodec.Decode(MessageCodec.EncodeToArray(MessageId.LoginResponse,
            new LoginResponse { EnabledFeatures = 1, NpcContentHash = "abc" })).message;
        Assert.Equal("abc", response.NpcContentHash);
        var cast = (CastBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(MessageId.CastBroadcast,
            new CastBroadcast { NpcAction = new NpcActionSnapshot { InstanceId = 12 } })).message;
        Assert.Equal(12ul, cast.NpcAction!.InstanceId);
    }

    [Fact]
    public void Snapshot_Tags_And_All_Scalar_Values_Roundtrip()
    {
        var source = new NpcActionSnapshot { InstanceId = 42, Revision = 7, Kind = NpcActionKind.Skill,
            Phase = NpcActionPhase.Cancelled, StartTick = uint.MaxValue, PhaseStartTick = 2, EndTick = 3,
            SkillId = 50006, TargetId = long.MaxValue, PresentationId = 50006, CancelReason = NpcActionCancelReason.Hit,
            ActiveStartTick = 4, ActiveEndTick = 5, RecoveryStartTick = 6, AimX = 1.25f, AimY = -2.5f,
            AimZ = 3.75f, AimYaw = 270, PresentationVariant = 2, EffectiveTick = 8 };
        var decoded = (NpcActionEvent)MessageCodec.Decode(MessageCodec.EncodeToArray(MessageId.NpcActionEvent,
            new NpcActionEvent { Action = source })).message;
        var properties = typeof(NpcActionSnapshot).GetProperties();
        Assert.Equal(20, properties.Length);
        var tags = new HashSet<int>();
        foreach (var property in properties)
        {
            Assert.Equal(property.GetValue(source), property.GetValue(decoded.Action));
            tags.Add(((ProtoBuf.ProtoMemberAttribute)System.Attribute.GetCustomAttribute(property,
                typeof(ProtoBuf.ProtoMemberAttribute))!).Tag);
        }
        for (int i = 1; i <= 20; i++) Assert.Contains(i, tags);
        Assert.Equal(typeof(byte), System.Enum.GetUnderlyingType(typeof(NpcActionKind)));
        Assert.Equal(typeof(byte), System.Enum.GetUnderlyingType(typeof(NpcActionPhase)));
        Assert.Equal(typeof(byte), System.Enum.GetUnderlyingType(typeof(NpcActionCancelReason)));
        Assert.Equal(2004, (int)MessageId.NpcActionEvent);
    }
}
