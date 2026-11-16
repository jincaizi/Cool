using System.Collections.Generic;
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class CombatProjectionMessagesTests
{
    [Fact]
    public void CombatStateUpdate_Roundtrips_All_Plan02_Fields()
    {
        var decoded = (CombatStateUpdate)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CombatStateUpdate,
            new CombatStateUpdate
            {
                EntityId = 7,
                ServerTimestamp = 100,
                Projection = new CombatProjection
                {
                    CombatVersion = 4,
                    LifeId = 2,
                    Hp = 720f,
                    MaxHp = 720f,
                    Dead = false,
                    IsDefending = true,
                    Statuses = new List<StatusProjection>
                    {
                        new StatusProjection { EffectId = 1, GroupId = 1, EndTick = 30 }
                    },
                    Equipment = new EquipmentSummary
                    {
                        WeaponInstanceId = 42, TemplateId = 90002, ElementId = 1
                    },
                    Control = new ControlSummary
                    {
                        SpeedScale = 0.7f, ActionLock = false, ControlVersion = 3
                    },
                    ActiveCast = new ActiveCastProjection
                    {
                        CastId = 9, SkillId = 10002,
                        Phase = NpcActionPhase.Windup, EndTick = 44
                    }
                }
            })).message;

        Assert.Equal(7L, decoded.EntityId);
        Assert.Equal(4u, decoded.Projection.CombatVersion);
        Assert.Equal(2u, decoded.Projection.LifeId);
        Assert.Equal(720f, decoded.Projection.Hp);
        Assert.Single(decoded.Projection.Statuses);
        Assert.Equal(42L, decoded.Projection.Equipment!.WeaponInstanceId);
        Assert.Equal(0.7f, decoded.Projection.Control!.SpeedScale);
        Assert.Equal(9UL, decoded.Projection.ActiveCast!.CastId);
    }

    [Fact]
    public void EntitySnapshot_Carries_CombatVersion_And_LifeId()
    {
        var decoded = ((WorldSnapshot)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.WorldSnapshot,
            new WorldSnapshot
            {
                Entities = new List<EntitySnapshot>
                {
                    new EntitySnapshot
                    {
                        PlayerId = 1, FieldMask = (ushort)EntityFieldMask.Health,
                        CombatVersion = 9, LifeId = 3
                    }
                }
            })).message).Entities[0];

        Assert.Equal(9u, decoded.CombatVersion);
        Assert.Equal(3u, decoded.LifeId);
    }
}
