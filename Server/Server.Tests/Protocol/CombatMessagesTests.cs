using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class CombatMessagesTests
{
    [Fact]
    public void CastRequest_Roundtrip_Preserves_Shape_And_Target()
    {
        var request = new CastRequest
        {
            SkillId = 20001,
            TargetPlayerId = 7,
            Shape = new HitShape
            {
                ShapeType = 0, Range = 3.5f, Angle = 120f,
                InnerRadius = 0.8f, InnerAngle = 90f
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.CastRequest, request, MessageFlags.Reliable);
        var (id, decoded) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.CastRequest, id);
        var cast = Assert.IsType<CastRequest>(decoded);
        Assert.Equal(20001, cast.SkillId);
        Assert.Equal(7L, cast.TargetPlayerId);
        Assert.Equal(0, cast.Shape.ShapeType);
        Assert.Equal(3.5f, cast.Shape.Range, 4);
        Assert.Equal(120f, cast.Shape.Angle, 4);
        Assert.Equal(0.8f, cast.Shape.InnerRadius, 4);
        Assert.Equal(90f, cast.Shape.InnerAngle, 4);
    }

    [Fact]
    public void DamageResult_Roundtrip_Preserves_Hits()
    {
        var result = new DamageResult
        {
            SourcePlayerId = 1,
            SkillId = 20001,
            ServerTimestamp = 1_700_000_000_000_0000L,
            Hits =
            {
                new HitEntry
                {
                    TargetPlayerId = 2, Damage = 120f, IsCritical = false,
                    TargetRemainingHp = 0f, HitX = 1f, HitZ = 2f
                },
                new HitEntry
                {
                    TargetPlayerId = 3, Damage = 180f, IsCritical = true,
                    TargetRemainingHp = 42f, HitX = 1f, HitZ = 2f
                }
            }
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.DamageResult, result, MessageFlags.Reliable);
        var (id, decoded) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.DamageResult, id);
        var damage = Assert.IsType<DamageResult>(decoded);
        Assert.Equal(2, damage.Hits.Count);
        Assert.Equal(2L, damage.Hits[0].TargetPlayerId);
        Assert.Equal(120f, damage.Hits[0].Damage, 4);
        Assert.False(damage.Hits[0].IsCritical);
        Assert.Equal(0f, damage.Hits[0].TargetRemainingHp, 4);
        Assert.Equal(3L, damage.Hits[1].TargetPlayerId);
        Assert.Equal(180f, damage.Hits[1].Damage, 4);
        Assert.True(damage.Hits[1].IsCritical);
        Assert.Equal(42f, damage.Hits[1].TargetRemainingHp, 4);
    }

    [Fact]
    public void CastBroadcast_Roundtrip_Preserves_Caster_And_Shape()
    {
        var broadcast = new CastBroadcast
        {
            SkillId = 20002, CasterPlayerId = 3, TargetPlayerId = 0,
            CasterX = 10f, CasterY = 1f, CasterZ = 20f, CasterRotation = 90f,
            Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f },
            ServerTimestamp = 1_700_000_000_000_0000L
        };
        var bytes = MessageCodec.EncodeToArray(MessageId.CastBroadcast, broadcast, MessageFlags.Reliable);
        var (id, decoded) = MessageCodec.Decode(bytes);

        Assert.Equal(MessageId.CastBroadcast, id);
        var cast = Assert.IsType<CastBroadcast>(decoded);
        Assert.Equal(20002, cast.SkillId);
        Assert.Equal(3L, cast.CasterPlayerId);
        Assert.Equal(10f, cast.CasterX, 4);
        Assert.Equal(90f, cast.CasterRotation, 4);
        Assert.Equal(1, cast.Shape.ShapeType);
        Assert.Equal(2.5f, cast.Shape.AreaRadius, 4);
    }
}
