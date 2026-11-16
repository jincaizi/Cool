using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class CombatCancelMessagesTests
{
    [Fact]
    public void CastCancel_Roundtrips()
    {
        var decoded = (CastCancelBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CastCancelBroadcast,
            new CastCancelBroadcast
            {
                CastId = 42,
                CasterLifeId = 3,
                CasterPlayerId = 7,
                EffectiveTick = 77,
                Reason = NpcActionCancelReason.Hit
            })).message;

        Assert.Equal(42UL, decoded.CastId);
        Assert.Equal(3u, decoded.CasterLifeId);
        Assert.Equal(7L, decoded.CasterPlayerId);
        Assert.Equal(77u, decoded.EffectiveTick);
        Assert.Equal(NpcActionCancelReason.Hit, decoded.Reason);
    }

    [Fact]
    public void CancelCastRequest_Carries_Optional_CastId()
    {
        var decoded = (CancelCastRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CancelCastRequest,
            new CancelCastRequest { SkillId = 20002, CastId = 99 })).message;

        Assert.Equal(20002, decoded.SkillId);
        Assert.Equal(99UL, decoded.CastId);
    }

    [Fact]
    public void NpcActionEvent_Carries_LifeId()
    {
        var decoded = (NpcActionEvent)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.NpcActionEvent,
            new NpcActionEvent
            {
                EntityId = 7,
                LifeId = 5,
                Action = new NpcActionSnapshot { InstanceId = 3, Revision = 2 },
                ServerTimestamp = 123
            })).message;

        Assert.Equal(7L, decoded.EntityId);
        Assert.Equal(5u, decoded.LifeId);
        Assert.Equal(3UL, decoded.Action.InstanceId);
        Assert.Equal(2u, decoded.Action.Revision);
    }
}
