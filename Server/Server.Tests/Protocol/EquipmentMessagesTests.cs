using System.Linq;
using Cool.Protocol;
using Xunit;

namespace KcpServer.Tests;

public class EquipmentMessagesTests
{
    [Fact]
    public void Equip_Request_Roundtrip_Preserves_Fields()
    {
        var decoded = (EquipWeaponRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.EquipWeaponRequest,
            new EquipWeaponRequest { ClientEquipSeq = 7, WeaponInstanceId = 42, KnownEquipmentRevision = 3 })).message;

        Assert.Equal(7u, decoded.ClientEquipSeq);
        Assert.Equal(42L, decoded.WeaponInstanceId);
        Assert.Equal(3u, decoded.KnownEquipmentRevision);
    }

    [Fact]
    public void Equip_Confirm_Roundtrip_Preserves_Reject_Summary()
    {
        var decoded = (EquipWeaponConfirm)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.EquipWeaponConfirm,
            new EquipWeaponConfirm
            {
                ClientEquipSeq = 9,
                Accepted = false,
                RejectReason = "stale-revision",
                EquipmentRevision = 4,
                WeaponInstanceId = 42,
                TemplateId = 90002,
                ElementId = 1
            })).message;

        Assert.Equal(9u, decoded.ClientEquipSeq);
        Assert.False(decoded.Accepted);
        Assert.Equal("stale-revision", decoded.RejectReason);
        Assert.Equal(4u, decoded.EquipmentRevision);
        Assert.Equal(42L, decoded.WeaponInstanceId);
        Assert.Equal(90002, decoded.TemplateId);
        Assert.Equal(1, decoded.ElementId);
    }

    [Fact]
    public void Weapon_Inventory_Baseline_Roundtrip_Empty_And_Full()
    {
        var emptyResponse = (LoginResponse)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.LoginResponse,
            new LoginResponse { Success = true, WeaponInventory = new WeaponInventoryBaseline() })).message;
        Assert.NotNull(emptyResponse.WeaponInventory);
        Assert.Empty(emptyResponse.WeaponInventory!.Weapons);

        var fullResponse = (LoginResponse)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.LoginResponse,
            new LoginResponse
            {
                Success = true,
                WeaponInventory = new WeaponInventoryBaseline
                {
                    LifeId = 2,
                    EquipmentRevision = 3,
                    WeaponInstanceId = 42,
                    TemplateId = 90002,
                    ElementId = 1,
                    Weapons =
                    {
                        new WeaponInstanceSummary { InstanceId = 42, TemplateId = 90002, ElementId = 1 },
                        new WeaponInstanceSummary { InstanceId = 43, TemplateId = 90003, ElementId = 2 }
                    }
                }
            })).message;
        Assert.NotNull(fullResponse.WeaponInventory);
        var full = fullResponse.WeaponInventory!;

        Assert.Equal(2u, full.LifeId);
        Assert.Equal(3u, full.EquipmentRevision);
        Assert.Equal(42L, full.WeaponInstanceId);
        Assert.Equal(2, full.Weapons.Count);
        Assert.Equal(43L, full.Weapons[1].InstanceId);
        Assert.Equal(90003, full.Weapons[1].TemplateId);
        Assert.Equal(2, full.Weapons[1].ElementId);
    }

    [Fact]
    public void Login_Fingerprint_Roundtrip()
    {
        var request = (LoginRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.LoginRequest,
            new LoginRequest
            {
                Username = "p",
                SupportedFeatures = ProtocolFeatures.ElementalSwordQiV1,
                ElementalCatalogFingerprint = "abc123"
            })).message;
        Assert.Equal(ProtocolFeatures.ElementalSwordQiV1, request.SupportedFeatures);
        Assert.Equal("abc123", request.ElementalCatalogFingerprint);

        var response = (LoginResponse)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.LoginResponse,
            new LoginResponse
            {
                Success = true,
                EnabledFeatures = ProtocolFeatures.ElementalSwordQiV1,
                ElementalCatalogFingerprint = "abc123"
            })).message;
        Assert.Equal(ProtocolFeatures.ElementalSwordQiV1, response.EnabledFeatures);
        Assert.Equal("abc123", response.ElementalCatalogFingerprint);
    }

    [Fact]
    public void Cast_Request_Roundtrip_Preserves_Idempotency_Fields()
    {
        var decoded = (CastRequest)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CastRequest,
            new CastRequest
            {
                SkillId = 10002, ClientCastSeq = 12, EquipmentRevision = 4, CasterLifeId = 2
            })).message;

        Assert.Equal(12u, decoded.ClientCastSeq);
        Assert.Equal(4u, decoded.EquipmentRevision);
        Assert.Equal(2u, decoded.CasterLifeId);
    }

    [Fact]
    public void Cast_Broadcast_Roundtrip_Preserves_CastId_And_Speed()
    {
        var decoded = (CastBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CastBroadcast,
            new CastBroadcast
            {
                CastId = 99,
                CasterPlayerId = 1,
                CasterLifeId = 2,
                SkillId = 10002,
                AttackSpeedMultiplier = 1.25f,
                ElementId = 1,
                EquipmentRevision = 4
            })).message;

        Assert.Equal(99ul, decoded.CastId);
        Assert.Equal(2u, decoded.CasterLifeId);
        Assert.Equal(1.25f, decoded.AttackSpeedMultiplier);
        Assert.Equal(1, decoded.ElementId);
        Assert.Equal(4u, decoded.EquipmentRevision);
    }

    [Fact]
    public void Cast_Confirm_Roundtrip_Preserves_All_Fields()
    {
        var decoded = (CastConfirm)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.CastConfirm,
            new CastConfirm
            {
                ClientCastSeq = 12,
                Accepted = true,
                CastId = 99,
                SkillId = 10002,
                CasterLifeId = 2,
                EquipmentRevision = 4,
                AttackSpeedMultiplier = 1.25f,
                AcceptedTick = 321,
                WeaponInstanceId = 42,
                TemplateId = 90002,
                ElementId = 1
            })).message;

        Assert.Equal(12u, decoded.ClientCastSeq);
        Assert.True(decoded.Accepted);
        Assert.Equal(99ul, decoded.CastId);
        Assert.Equal(10002, decoded.SkillId);
        Assert.Equal(2u, decoded.CasterLifeId);
        Assert.Equal(4u, decoded.EquipmentRevision);
        Assert.Equal(1.25f, decoded.AttackSpeedMultiplier);
        Assert.Equal(321u, decoded.AcceptedTick);
        Assert.Equal(42L, decoded.WeaponInstanceId);
        Assert.Equal(90002, decoded.TemplateId);
        Assert.Equal(1, decoded.ElementId);
    }

    [Fact]
    public void Projectile_Launch_Roundtrip_Preserves_Authoritative_Description()
    {
        var decoded = (ProjectileLaunchBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.ProjectileLaunchBroadcast,
            new ProjectileLaunchBroadcast
            {
                CastId = 77,
                CasterPlayerId = 1,
                CasterLifeId = 2,
                CombatSceneId = "room-a",
                LaunchTick = 123,
                OriginX = 1f, OriginY = 2f, OriginZ = 3f,
                Yaw = 45f, Speed = 18f, Range = 12f, ElementId = 1,
                SkillId = 10002, ServerTimestamp = 999
            })).message;

        Assert.Equal(77ul, decoded.CastId);
        Assert.Equal(2u, decoded.CasterLifeId);
        Assert.Equal("room-a", decoded.CombatSceneId);
        Assert.Equal(123u, decoded.LaunchTick);
        Assert.Equal(1f, decoded.OriginX);
        Assert.Equal(3f, decoded.OriginZ);
        Assert.Equal(45f, decoded.Yaw);
        Assert.Equal(18f, decoded.Speed);
        Assert.Equal(12f, decoded.Range);
        Assert.Equal(1, decoded.ElementId);
        Assert.Equal(10002, decoded.SkillId);
        Assert.Equal(999L, decoded.ServerTimestamp);
    }

    [Fact]
    public void Projectile_Stop_Roundtrip_Preserves_CastId_And_Point()
    {
        var decoded = (ProjectileStopBroadcast)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.ProjectileStopBroadcast,
            new ProjectileStopBroadcast
            {
                CastId = 77,
                CasterPlayerId = 1,
                CasterLifeId = 2,
                CombatSceneId = "room-a",
                StopTick = 130,
                Reason = ProjectileStopReason.Blocked,
                StopX = 4f,
                StopZ = 5f
            })).message;

        Assert.Equal(77ul, decoded.CastId);
        Assert.Equal(2u, decoded.CasterLifeId);
        Assert.Equal("room-a", decoded.CombatSceneId);
        Assert.Equal(130u, decoded.StopTick);
        Assert.Equal(ProjectileStopReason.Blocked, decoded.Reason);
        Assert.Equal(4f, decoded.StopX);
        Assert.Equal(5f, decoded.StopZ);
    }

    [Fact]
    public void Hit_Entry_Roundtrip_Preserves_Cast_Identity_Fields()
    {
        var decoded = (DamageResult)MessageCodec.Decode(MessageCodec.EncodeToArray(
            MessageId.DamageResult,
            new DamageResult
            {
                Hits =
                {
                    new HitEntry
                    {
                        TargetPlayerId = 3,
                        CastId = 77,
                        EventSeq = 5,
                        EventId = 9001,
                        DamageKind = DamageKind.Direct,
                        TargetLifeId = 2,
                        CombatVersion = 6
                    }
                }
            })).message;

        var hit = decoded.Hits.Single();
        Assert.Equal(77ul, hit.CastId);
        Assert.Equal(5u, hit.EventSeq);
        Assert.Equal(9001ul, hit.EventId);
        Assert.Equal(DamageKind.Direct, hit.DamageKind);
        Assert.Equal(2u, hit.TargetLifeId);
        Assert.Equal(6u, hit.CombatVersion);
    }

    [Fact]
    public void New_Message_Ids_Are_Registered_Uniquely()
    {
        var ids = new[]
        {
            MessageId.EquipWeaponRequest, MessageId.EquipWeaponConfirm, MessageId.CastConfirm,
            MessageId.ProjectileLaunchBroadcast, MessageId.ProjectileStopBroadcast
        };

        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (var id in ids)
        {
            Assert.True(MessageTypeRegistry.IsRegistered(id), id.ToString());
            var type = MessageTypeRegistry.GetMessageType(id);
            Assert.NotNull(type);
            Assert.Equal(id, MessageTypeRegistry.GetMessageId(type!));
        }
    }

    [Fact]
    public void DamageKind_Values_Are_Stable()
    {
        Assert.Equal(0, (byte)DamageKind.Direct);
        Assert.Equal(1, (byte)DamageKind.Dot);
    }
}
