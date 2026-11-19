using System.Collections.Generic;
using System.Reflection;
using Cool.Protocol;
using ProtoBuf;
using Xunit;

namespace Cool.Protocol.Tests;

public class SwordQiEchoProtocolTests
{
    [Fact]
    public void State_RoundTripsWithContactOrderAndTiming()
    {
        var source = new SwordQiEchoState
        {
            Phase = SwordQiEchoPhase.Started,
            CasterId = 7,
            CastId = 42,
            RequestId = 9,
            EchoBindingId = 3,
            StartTick = 100,
            EndTick = 130,
            ServerTimestamp = 123456789L,
            CooldownTicks = 180,
            OriginX = 1f,
            OriginY = 0.3f,
            OriginZ = -2f,
            Rotation = 90f,
            Radius = 12f,
            SwordCount = 4,
            OvershootMeters = 0.75f,
            Contacts = new List<SwordQiEchoContactMessage>
            {
                new SwordQiEchoContactMessage
                {
                    TargetId = 10, SwordIndex = 0, OffsetTicks = 6,
                    X = 1f, Y = 0f, Z = 2f
                },
                new SwordQiEchoContactMessage
                {
                    TargetId = 11, SwordIndex = 1, OffsetTicks = 24,
                    X = -1f, Y = 0f, Z = 3f
                },
            },
        };

        var bytes = MessageCodec.EncodeToArray(MessageId.SwordQiEchoState, source);
        var (id, message) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.SwordQiEchoState, id);
        var got = Assert.IsType<SwordQiEchoState>(message);
        Assert.Equal(SwordQiEchoPhase.Started, got.Phase);
        Assert.Equal(7L, got.CasterId);
        Assert.Equal(42UL, got.CastId);
        Assert.Equal(9UL, got.RequestId);
        Assert.Equal(3UL, got.EchoBindingId);
        Assert.Equal(100u, got.StartTick);
        Assert.Equal(130u, got.EndTick);
        Assert.Equal(123456789L, got.ServerTimestamp);
        Assert.Equal(180u, got.CooldownTicks);
        Assert.Equal(12f, got.Radius, 4);
        Assert.Equal(4, got.SwordCount);
        Assert.Equal(0.75f, got.OvershootMeters, 4);
        Assert.Equal(2, got.Contacts.Count);
        Assert.Equal(10L, got.Contacts[0].TargetId);
        Assert.Equal(6u, got.Contacts[0].OffsetTicks);
        Assert.Equal(11L, got.Contacts[1].TargetId);
        Assert.Equal(24u, got.Contacts[1].OffsetTicks);
    }

    [Fact]
    public void DamageResults_ShareCastIdButKeepDistinctTargets()
    {
        var first = RoundtripDamage(10L, 42UL);
        var second = RoundtripDamage(11L, 42UL);

        Assert.Equal(42UL, first.EchoCastId);
        Assert.Equal(42UL, second.EchoCastId);
        Assert.Equal(10L, first.Hits[0].TargetPlayerId);
        Assert.Equal(11L, second.Hits[0].TargetPlayerId);
    }

    private static DamageResult RoundtripDamage(long targetId, ulong castId)
    {
        var source = new DamageResult
        {
            SourcePlayerId = 7,
            SkillId = 20006,
            EchoCastId = castId,
            Hits = new List<HitEntry>
            {
                new HitEntry
                {
                    TargetPlayerId = targetId,
                    Damage = 100f,
                    TargetRemainingHp = 900f,
                },
            },
        };

        var bytes = MessageCodec.EncodeToArray(MessageId.DamageResult, source);
        return Assert.IsType<DamageResult>(MessageCodec.Decode(bytes).message);
    }

    [Fact]
    public void LegacyCombat_WithoutEchoFields_ReadsDefaults()
    {
        var request = new CastRequest
        {
            SkillId = 20001,
            Shape = new HitShape { ShapeType = 0, Range = 3.5f, Angle = 120f },
        };
        var decodedRequest = Assert.IsType<CastRequest>(
            MessageCodec.Decode(MessageCodec.EncodeToArray(MessageId.CastRequest, request)).message);
        Assert.Equal(0UL, decodedRequest.EchoRequestId);
        Assert.Equal(0UL, decodedRequest.EchoBindingId);

        var damage = new DamageResult
        {
            SourcePlayerId = 7,
            SkillId = 10001,
            Hits = new List<HitEntry>
            {
                new HitEntry { TargetPlayerId = 10, Damage = 50f, TargetRemainingHp = 50f },
            },
        };
        var decodedDamage = Assert.IsType<DamageResult>(
            MessageCodec.Decode(MessageCodec.EncodeToArray(MessageId.DamageResult, damage)).message);
        Assert.Equal(0UL, decodedDamage.EchoCastId);

        Assert.Equal(0u, new LoginRequest().SupportedFeatures);
        Assert.Equal(0u, new LoginResponse().EnabledFeatures);
    }

    [Fact]
    public void EchoTagsAndFeatureBit_ArePinned()
    {
        Assert.Equal(305, (int)MessageId.SwordQiEchoState);
        Assert.Equal(1u << 2, ProtocolFeatures.SwordQiEcho);
        Assert.Equal(8, GetProtoTag(typeof(CastRequest), "EchoRequestId"));
        Assert.Equal(9, GetProtoTag(typeof(CastRequest), "EchoBindingId"));
        Assert.Equal(9, GetProtoTag(typeof(DamageResult), "EchoCastId"));

        var contacts = typeof(SwordQiEchoState).GetProperty("Contacts");
        Assert.NotNull(contacts);
        Assert.Equal(typeof(IList<SwordQiEchoContactMessage>), contacts!.PropertyType);
    }

    private static int GetProtoTag(System.Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName);
        Assert.NotNull(property);
        var attribute = property!.GetCustomAttribute<ProtoMemberAttribute>();
        Assert.NotNull(attribute);
        return attribute!.Tag;
    }
}
