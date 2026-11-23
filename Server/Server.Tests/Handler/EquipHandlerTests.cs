using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

public class EquipHandlerTests
{
    private static EquipHandler CreateHandler()
        => new EquipHandler(ElementalCatalogDefaults.Create());

    [Fact]
    public void Rejects_Not_Owned()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        var confirm = CreateHandler().ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 1, WeaponInstanceId = 99, KnownEquipmentRevision = 0 }, player);
        Assert.False(confirm.Accepted);
        Assert.Equal("not-owned", confirm.RejectReason);
    }

    [Fact]
    public void Accepts_Owned_And_Bumps_Revision()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        var confirm = CreateHandler().ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 1, WeaponInstanceId = 42, KnownEquipmentRevision = 0 }, player);
        Assert.True(confirm.Accepted);
        Assert.Equal(1u, player.EquipmentRevision);
        Assert.Equal(42L, player.EquippedWeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, player.EquippedWeaponTemplateId);
        Assert.Equal(1, confirm.ElementId);
        // I3 顺带：换装必须 bump 战斗投影版本并置脏（CombatStateUpdate 的触发源）
        Assert.Equal(1u, player.CombatVersion);
        Assert.True(player.CombatDirty);
    }

    [Fact]
    public void Rejects_Stale_Revision()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        player.EquipmentRevision = 3;
        var confirm = CreateHandler().ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 1, WeaponInstanceId = 42, KnownEquipmentRevision = 2 }, player);
        Assert.False(confirm.Accepted);
        Assert.Equal("stale-revision", confirm.RejectReason);
    }

    [Fact]
    public void Same_Seq_Replays_Original_Confirm_Without_Bumping_Revision()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        var handler = CreateHandler();
        var request = new EquipWeaponRequest { ClientEquipSeq = 7, WeaponInstanceId = 42, KnownEquipmentRevision = 0 };

        var first = handler.ValidateAndApply(request, player);
        var replay = handler.ValidateAndApply(request, player);

        Assert.True(first.Accepted);
        Assert.True(replay.Accepted);
        Assert.Equal(first.EquipmentRevision, replay.EquipmentRevision);
        Assert.Equal(1u, player.EquipmentRevision);
    }

    [Fact]
    public void Older_Seq_Is_Rejected_And_Carries_Current_Summary()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        player.WeaponInstanceTemplates[43] = ElementalCatalogDefaults.FireSwordTemplateId;
        var handler = CreateHandler();

        Assert.True(handler.ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 5, WeaponInstanceId = 42, KnownEquipmentRevision = 0 },
            player).Accepted);
        var stale = handler.ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 4, WeaponInstanceId = 43, KnownEquipmentRevision = 1 },
            player);

        Assert.False(stale.Accepted);
        Assert.Equal("stale-seq", stale.RejectReason);
        Assert.Equal(1u, stale.EquipmentRevision);
        Assert.Equal(42L, stale.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, stale.TemplateId);
        Assert.Equal(1, stale.ElementId);
    }

    [Fact]
    public void Dead_Equip_Is_Rejected_And_Retry_After_Respawn_Requires_A_New_Seq()
    {
        var player = new PlayerState(1)
        {
            EquippedWeaponInstanceId = 42,
            EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId,
            EquipmentRevision = 2
        };
        player.WeaponInstanceTemplates[43] = ElementalCatalogDefaults.FireSwordTemplateId;
        player.ApplyDamage(100f);
        player.MarkDead(1);
        var handler = CreateHandler();
        var request = new EquipWeaponRequest { ClientEquipSeq = 7, WeaponInstanceId = 43, KnownEquipmentRevision = 2 };

        var rejected = handler.ValidateAndApply(request, player, tick: 2);
        Assert.False(rejected.Accepted);
        Assert.Equal("caster-dead", rejected.RejectReason);
        Assert.Equal(2u, rejected.EquipmentRevision);
        Assert.Equal(42L, rejected.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, rejected.TemplateId);
        Assert.Equal(1, rejected.ElementId);

        Assert.True(player.TryRespawn(123, 66));
        Assert.Same(rejected, handler.ValidateAndApply(request, player, tick: 123));
        request.ClientEquipSeq++;
        Assert.True(handler.ValidateAndApply(request, player, tick: 123).Accepted);
        Assert.Equal(3u, player.EquipmentRevision);
    }

    [Fact]
    public void Reject_Carries_Current_Element()
    {
        var player = new PlayerState(1);
        player.WeaponInstanceTemplates[42] = ElementalCatalogDefaults.IceSwordTemplateId;
        player.EquippedWeaponInstanceId = 42;
        player.EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId;
        player.EquipmentRevision = 2;

        var confirm = CreateHandler().ValidateAndApply(
            new EquipWeaponRequest { ClientEquipSeq = 1, WeaponInstanceId = 99, KnownEquipmentRevision = 2 }, player);

        Assert.False(confirm.Accepted);
        Assert.Equal(2u, confirm.EquipmentRevision);
        Assert.Equal(42L, confirm.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, confirm.TemplateId);
        Assert.Equal(1, confirm.ElementId);
    }

    [Fact]
    public async Task Real_Login_Grants_Starting_Weapons_And_Baseline()
    {
        using var server = new Server3C(new TestLogger());
        var sent = new List<(MessageId id, IMessage msg)>();
        server.OfflineSendSink = (_, id, msg) => sent.Add((id, msg));
        var session = new FakeSession(1);
        var handler = new LoginHandler(server, server.RoomManager, new TestLogger());

        await handler.HandleLoginAsync(session, new LoginRequest
        {
            Username = "P1",
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            ZoneId = "room-a",
            SupportedFeatures = ProtocolFeatures.ElementalSwordQiV1,
            ElementalCatalogFingerprint = ElementalCatalogHasher.Compute(ElementalCatalogDefaults.Create())
        });

        var response = Assert.Single(sent.Where(m => m.id == MessageId.LoginResponse)
            .Select(m => (LoginResponse)m.msg));
        Assert.True(response.Success);
        Assert.True((response.EnabledFeatures & ProtocolFeatures.ElementalSwordQiV1) != 0);
        Assert.Equal(ElementalCatalogDefaults.Create().StartingWeaponTemplateIds.Count,
            response.WeaponInventory!.Weapons.Count);
        Assert.Equal(ElementalCatalogDefaults.DefaultSwordTemplateId, response.WeaponInventory.TemplateId);

        var player = server.GetEntity(response.PlayerId);
        Assert.NotNull(player);
        Assert.Equal("room-a", player!.ZoneId);
        Assert.Equal(ElementalCatalogDefaults.Create().StartingWeaponTemplateIds.Count,
            player.WeaponInstanceTemplates.Count);
        Assert.Equal(ElementalCatalogDefaults.DefaultSwordTemplateId, player.EquippedWeaponTemplateId);
        Assert.Equal(0u, player.EquipmentRevision);
        Assert.Equal(1u, player.LifeId);
    }

    [Fact]
    public async Task Real_Login_Rejects_Fingerprint_Mismatch_When_Feature_Requested()
    {
        using var server = new Server3C(new TestLogger());
        var sent = new List<(MessageId id, IMessage msg)>();
        server.OfflineSendSink = (_, id, msg) => sent.Add((id, msg));
        var session = new FakeSession(1);
        var handler = new LoginHandler(server, server.RoomManager, new TestLogger());

        await handler.HandleLoginAsync(session, new LoginRequest
        {
            Username = "P1",
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            ZoneId = "room-a",
            SupportedFeatures = ProtocolFeatures.ElementalSwordQiV1,
            ElementalCatalogFingerprint = "wrong"
        });

        var response = Assert.Single(sent.Where(m => m.id == MessageId.LoginResponse)
            .Select(m => (LoginResponse)m.msg));
        Assert.False(response.Success);
        Assert.Contains("fingerprint", response.Message);
        Assert.Null(response.WeaponInventory);
    }

    [Fact]
    public async Task Real_Equip_Handler_Route_Sends_Confirm()
    {
        using var server = new Server3C(new TestLogger());
        var sent = new List<(MessageId id, IMessage msg)>();
        server.OfflineSendSink = (_, id, msg) => sent.Add((id, msg));
        var session = new FakeSession(1);
        var login = new LoginHandler(server, server.RoomManager, new TestLogger());
        await login.HandleLoginAsync(session, new LoginRequest
        {
            Username = "P1",
            ProtocolVersion = ProtocolConstants.ProtocolVersion,
            ZoneId = "room-a",
            SupportedFeatures = ProtocolFeatures.ElementalSwordQiV1,
            ElementalCatalogFingerprint = ElementalCatalogHasher.Compute(ElementalCatalogDefaults.Create())
        });
        var loginResponse = (LoginResponse)sent.Single(m => m.id == MessageId.LoginResponse).msg;
        var ice = loginResponse.WeaponInventory!.Weapons
            .Single(w => w.TemplateId == ElementalCatalogDefaults.IceSwordTemplateId);

        await server.EquipHandler.HandleAsync(server, session, new EquipWeaponRequest
        {
            ClientEquipSeq = 1,
            WeaponInstanceId = ice.InstanceId,
            KnownEquipmentRevision = 0
        });

        var confirm = (EquipWeaponConfirm)sent.Single(m => m.id == MessageId.EquipWeaponConfirm).msg;
        Assert.True(confirm.Accepted);
        Assert.Equal(1u, confirm.EquipmentRevision);
        Assert.Equal(ice.InstanceId, confirm.WeaponInstanceId);
        Assert.Equal(ElementalCatalogDefaults.IceSwordTemplateId, confirm.TemplateId);
        Assert.Equal(1, confirm.ElementId);
    }

    [Fact]
    public void Respawn_Increments_LifeId()
    {
        var player = new PlayerState(1) { MaxHp = 100f, Hp = 0f };
        player.MarkDead(1);
        Assert.True(player.TryRespawn(123, 66)); // (123 - 1) * 66ms = 8052ms >= 8s
        Assert.Equal(2u, player.LifeId);
    }

    private sealed class FakeSession : IServerSession
    {
        public FakeSession(long sessionId) => SessionId = sessionId;
        public long SessionId { get; }
        public bool IsConnected => true;
        public DateTime LastActivityTime => DateTime.UtcNow;
        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;
        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
        public void UpdateActivity() { }
        public bool IsTimedOut(TimeSpan timeout) => false;
        public Task CloseAsync(string reason = "Normal") => Task.CompletedTask;
        public void Dispose() { }
    }
}
