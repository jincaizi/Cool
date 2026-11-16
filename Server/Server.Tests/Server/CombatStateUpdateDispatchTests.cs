using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// Plan 02 Part B 终审 I3：`Server3C.SendCombatStateUpdates` 的收件人/脏位契约。
/// 服务端源码直接编译进测试程序集，下行经 `OfflineSendSink` 旁路观察（不依赖网络）。
/// </summary>
public class CombatStateUpdateDispatchTests
{
    [Fact]
    public void Dirty_Entity_Update_Goes_To_Self_And_Same_Scene_Observers_Only()
    {
        using var server = new Server3C(new TestLogger());
        var sent = new List<(long SessionId, MessageId Id, IMessage Message)>();
        server.OfflineSendSink = (session, id, message) =>
            sent.Add((session.SessionId, id, message));

        // 施法者 + 同场景观察者
        var caster = server.RegisterEntity(1, "Caster");
        caster.ZoneId = "room-a";
        caster.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0L, 0U);
        var ally = server.RegisterEntity(2, "Ally");
        ally.ZoneId = "room-a";
        ally.UpdatePosition(new Vector3(10f, 0f, 0f), 0f, 0f, 0L, 0U);

        // 异场景观察者（AOI 距离内，但场景过滤必须挡掉）
        var other = server.RegisterEntity(3, "Other");
        other.ZoneId = "room-b";
        other.UpdatePosition(new Vector3(10f, 0f, 0f), 0f, 0f, 0L, 0U);

        // NPC 观察者：在视野内但没有 session，不得成为收件人（也不该抛）
        var npc = server.RegisterEntity(20_000_001, "Npc");
        npc.Kind = (byte)EntityKind.Npc;
        npc.TemplateId = 1;
        npc.ZoneId = "room-a";
        npc.UpdatePosition(new Vector3(5f, 0f, 0f), 0f, 0f, 0L, 0U);

        server.BindPlayerToSession(1, new FakeSession(101));
        server.BindPlayerToSession(2, new FakeSession(102));
        server.BindPlayerToSession(3, new FakeSession(103));

        caster.EquippedWeaponInstanceId = 42;
        caster.EquippedWeaponTemplateId = ElementalCatalogDefaults.IceSwordTemplateId;
        caster.BumpCombatVersion();
        uint version = caster.CombatVersion;
        Assert.True(caster.CombatDirty, "Arrange 前提：实体已脏");

        server.SendCombatStateUpdates();

        Assert.Equal(2, sent.Count);
        Assert.All(sent, m => Assert.Equal(MessageId.CombatStateUpdate, m.Id));
        Assert.Contains(sent, m => m.SessionId == 101L);   // 本人
        Assert.Contains(sent, m => m.SessionId == 102L);   // 同场景观察者
        Assert.DoesNotContain(sent, m => m.SessionId == 103L); // 异场景不收
        // NPC 没有 session，因此不会出现在 sent（FakeSession id 也不存在）
        Assert.All(sent, m => Assert.NotEqual(20_000_001L, m.SessionId));

        var updates = sent.Select(m => (CombatStateUpdate)m.Message).ToList();
        Assert.All(updates, u =>
        {
            Assert.Equal(1L, u.EntityId);
            Assert.Equal(version, u.Projection.CombatVersion);
            Assert.Equal(42L, u.Projection.Equipment!.WeaponInstanceId);
        });
        Assert.False(caster.CombatDirty, "发送后 dirty 必须清掉");

        sent.Clear();
        server.SendCombatStateUpdates();
        Assert.Empty(sent);
        Assert.Equal(version, caster.CombatVersion); // 发送只清 dirty，不动版本
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
