using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

public class SwordQiEchoCombatTests
{
    private static PlayerState Player(EntityStore store, long id, float x, float z,
        string zone = "Global", uint features = ProtocolFeatures.SwordQiEcho,
        byte kind = (byte)EntityKind.Player, bool loggedIn = true)
    {
        var player = store.AddPlayer(id, "P" + id);
        player.Kind = kind;
        player.IsLoggedIn = loggedIn;
        player.ZoneId = zone;
        player.Features = features;
        player.UpdatePosition(new Vector3(x, 0f, z), 0f, 0f, 1L, 0U);
        return player;
    }

    private static PlayerState Npc(EntityStore store, long id, float x, float z, float hp = 1000f)
    {
        var npc = store.AddNpc(id, "N" + id, 1, hp, new Vector3(x, 0f, z), 0.5f);
        npc.ZoneId = "Global";
        return npc;
    }

    private static CastRequest EchoRequest(ulong requestId = 1, ulong bindingId = 1)
        => new CastRequest
        {
            SkillId = 20006,
            EchoRequestId = requestId,
            EchoBindingId = bindingId,
            Shape = new HitShape(),
        };

    private static CastRequest SwingRequest(long targetId, int skillId = 10001)
        => new CastRequest
        {
            SkillId = skillId,
            TargetPlayerId = targetId,
            Shape = new HitShape
            {
                ShapeType = 0,
                Range = skillId == 50001 ? 1.5f : 2f,
                Angle = 120f,
            },
        };

    private static List<HitEntry> Hits(IEnumerable<CombatResolver.CastResolution> resolutions)
        => resolutions.SelectMany(r => r.Hits).ToList();

    [Fact]
    public void Recast_Selects_At_Release_And_Keeps_The_Original_Orbit()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(1));
        var caster = Player(store, 1, 0, 0,
            features: ProtocolFeatures.SwordQiEcho | ProtocolFeatures.SwordQiEchoRecast);
        var request = EchoRequest();
        request.EchoAction = SwordQiEchoAction.Prepare;
        var waiting = resolver.Decide(1, request, 100).Echo!;
        Assert.True(waiting.Preparing);
        Assert.Empty(waiting.Contacts);
        Assert.Equal(75u, waiting.EndTick - waiting.StartTick);
        Assert.False(caster.IsInvulnerableAt(110));
        Assert.Empty(Hits(resolver.DrainResolved(110)));
        long orbitStart = waiting.OrbitStartedTimestamp;

        request = EchoRequest(2);
        request.EchoAction = SwordQiEchoAction.Release;
        request.EchoPreparedCastId = waiting.CastId;
        Assert.Equal("no-target", resolver.Decide(1, request, 115).RejectReason);
        Assert.True(waiting.Preparing);

        caster.UpdatePosition(new Vector3(30, 0, 0), 0, 0, 20L, 2u);
        for (int index = 0; index < 10; index++) Npc(store, 10 + index, 30f, 1f + index);
        request.EchoRequestId = 3;
        var flight = resolver.Decide(1, request, 120).Echo!;
        Assert.Same(waiting, flight);
        Assert.False(flight.Preparing);
        Assert.Equal(orbitStart, flight.OrbitStartedTimestamp);
        Assert.Equal(30f, flight.Origin.X);
        Assert.Equal(Enumerable.Range(10, 8).Select(id => (long)id),
            flight.Contacts.Select(contact => contact.TargetId).OrderBy(id => id));
        Assert.Equal(4, flight.Contacts.Select(contact => contact.SwordIndex).Distinct().Count());
        Assert.All(flight.Contacts.GroupBy(contact => contact.SwordIndex), route => Assert.Equal(2, route.Count()));
        Assert.True(caster.IsInvulnerableAt(120));
        Assert.Equal(4, Hits(resolver.DrainResolved(126)).Count);
        Assert.Equal(4, Hits(resolver.DrainResolved(144)).Count);
        resolver.DrainResolved(150);
        Assert.Equal(SwordQiEchoPhase.Completed, Assert.Single(resolver.DrainEndedEchoCasts()).EndPhase);
    }

    [Fact]
    public void Expired_Preparation_Starts_Cooldown_Without_Attacking()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(2));
        var caster = Player(store, 1, 0, 0,
            features: ProtocolFeatures.SwordQiEcho | ProtocolFeatures.SwordQiEchoRecast);
        var target = Npc(store, 2, 1, 0);
        var request = EchoRequest();
        request.EchoAction = SwordQiEchoAction.Prepare;
        var prepared = resolver.Decide(1, request, 100).Echo!;
        Assert.Empty(resolver.DrainResolved(prepared.EndTick - 1));
        Assert.False(resolver.HasEndedEchoCasts);
        Assert.Empty(resolver.DrainResolved(prepared.EndTick));
        Assert.Equal(1000f, target.Hp);
        Assert.False(caster.IsInvulnerableAt(prepared.EndTick));
        Assert.Equal(SwordQiEchoPhase.Expired, Assert.Single(resolver.DrainEndedEchoCasts()).EndPhase);
        Assert.Equal("cooldown", resolver.Decide(1, request, prepared.EndTick + prepared.CooldownTicks - 1).RejectReason);
        Assert.True(resolver.Decide(1, request, prepared.EndTick + prepared.CooldownTicks).Accepted);
    }

    [Fact]
    public void EmptyCast_DoesNotRecordCooldownOrInvulnerability()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(1));
        var caster = Player(store, 1, 0f, 0f);

        var rejected = resolver.Decide(1, EchoRequest(1), 100);

        Assert.False(rejected.Accepted);
        Assert.Equal("no-target", rejected.RejectReason);
        Assert.False(caster.IsInvulnerableAt(100));

        Player(store, 2, 0f, 5f);
        var accepted = resolver.Decide(1, EchoRequest(2), 100);

        Assert.True(accepted.Accepted);
        Assert.NotNull(accepted.Echo);
        Assert.True(caster.IsInvulnerableAt(100));
        Assert.True(caster.IsInvulnerableAt(129));
        Assert.False(caster.IsInvulnerableAt(130));
        Assert.Equal(1, accepted.Echo!.Contacts.Count);
        Assert.Equal(6u, accepted.Echo.Contacts[0].OffsetTicks);
    }

    [Fact]
    public void MixedTargets_ResolveOnceAndDoNotReplaceInvalidTargets()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(2));
        Player(store, 1, 0f, 0f);
        Npc(store, 2, 0f, 2f);
        Player(store, 3, 3f, 0f);
        Player(store, 4, -3f, 0f, zone: "Other");
        Player(store, 5, 0f, -3f, kind: (byte)EntityKind.PlayerBot);

        var accepted = resolver.Decide(1, EchoRequest(), 100);

        Assert.True(accepted.Accepted);
        var cast = accepted.Echo!;
        Assert.Equal(2, cast.Contacts.Count);
        Assert.Contains(cast.Contacts, c => c.TargetId == 2);
        Assert.Contains(cast.Contacts, c => c.TargetId == 3);
        Assert.DoesNotContain(cast.Contacts, c => c.TargetId == 4 || c.TargetId == 5);

        // 同 ID 重新入场：新 PlayerState 对象不承接旧接触。
        store.RemovePlayer(2);
        var replacement = Npc(store, 2, 0f, 2f);
        Assert.NotSame(cast.Contacts.Single(c => c.TargetId == 2).Target, replacement);

        var atContact = Hits(resolver.DrainResolved(106));
        Assert.DoesNotContain(atContact, h => h.TargetPlayerId == 2);
        Assert.Contains(atContact, h => h.TargetPlayerId == 3);

        var later = Hits(resolver.DrainResolved(120));
        Assert.DoesNotContain(later, h => h.TargetPlayerId == 2);
    }

    [Fact]
    public void InvulnerableAtStartAndLastTick_ExpiresAtEnd()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(3));
        var caster = Player(store, 1, 0f, 0f);
        caster.MaxHp = 1000f;
        caster.Hp = 1000f;
        var echoTarget = Player(store, 2, 0f, 5f);
        echoTarget.MaxHp = 1000f;
        echoTarget.Hp = 1000f;
        var pvpAttacker = Player(store, 3, 0f, -1f);
        pvpAttacker.MaxHp = 1000f;
        pvpAttacker.Hp = 1000f;
        var npcAttacker = Npc(store, 4, 0f, -1f);
        npcAttacker.MaxHp = 1000f;
        npcAttacker.Hp = 1000f;
        var npcAttackerB = Npc(store, 5, 0f, -1f);
        npcAttackerB.MaxHp = 1000f;
        npcAttackerB.Hp = 1000f;
        // 属性剑气的 10001 接续窗（0.25s）会拒绝同一施法者 125 的连续决策；
        // 用第二名攻击者把"结束 tick 恢复受击"的边界与接续窗解耦。
        var pvpAttackerB = Player(store, 6, 0f, -1f);
        pvpAttackerB.MaxHp = 1000f;
        pvpAttackerB.Hp = 1000f;

        Assert.True(resolver.Decide(1, EchoRequest(), 100).Accepted);
        Assert.True(caster.IsInvulnerableAt(100));

        Assert.True(resolver.Decide(3, SwingRequest(1), 100).Accepted);          // 判定 tick 105
        Assert.True(resolver.Decide(4, SwingRequest(1, 50001), 100).Accepted);   // 判定 tick 106

        Assert.DoesNotContain(Hits(resolver.DrainResolved(105)), h => h.TargetPlayerId == 1);
        Assert.DoesNotContain(Hits(resolver.DrainResolved(106)), h => h.TargetPlayerId == 1);

        Assert.True(resolver.Decide(3, SwingRequest(1), 124).Accepted);          // 判定 tick 129
        Assert.True(resolver.Decide(5, SwingRequest(1, 50001), 123).Accepted);   // 判定 tick 129
        Assert.DoesNotContain(Hits(resolver.DrainResolved(129)), h => h.TargetPlayerId == 1);

        Assert.True(resolver.Decide(6, SwingRequest(1), 125).Accepted);          // 判定 tick 130
        Assert.True(resolver.Decide(4, SwingRequest(1, 50001), 124).Accepted);   // 判定 tick 130
        var atEnd = Hits(resolver.DrainResolved(130));
        Assert.Contains(atEnd, h => h.TargetPlayerId == 1);
        Assert.True(caster.Hp < 1000f);
    }

    [Fact]
    public void DuplicateRequest_CommitsOnce_AndDisconnectEndsCast()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(4));
        var caster = Player(store, 1, 0f, 0f);
        Player(store, 2, 0f, 5f);

        Assert.True(resolver.Decide(1, EchoRequest(1), 100).Accepted);
        var duplicate = resolver.Decide(1, EchoRequest(1), 100);
        Assert.False(duplicate.Accepted);
        Assert.Equal("cooldown", duplicate.RejectReason);

        resolver.RemoveEchoCastsFor(1);
        Assert.False(caster.IsInvulnerableAt(100));
        Assert.Empty(resolver.DrainResolved(200));
        Assert.Single(resolver.DrainEndedEchoCasts());
    }

    [Fact]
    public void TargetInvulnerableOrBlocked_SkipsContactAndDoesNotReschedule()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(5));
        Player(store, 1, 0f, 0f);
        var target = Player(store, 2, 0f, 2f);
        target.MaxHp = 1000f;
        target.Hp = 1000f;

        Assert.True(resolver.Decide(1, EchoRequest(), 100).Accepted);
        target.SetInvulnerable(106, 107);

        Assert.DoesNotContain(Hits(resolver.DrainResolved(106)), h => h.TargetPlayerId == 2);
        Assert.DoesNotContain(Hits(resolver.DrainResolved(120)), h => h.TargetPlayerId == 2);
    }

    [Fact]
    public void TargetBlockedAtContact_UsesBlockDamageOnce()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(6));
        Player(store, 1, 0f, 0f);
        var target = Player(store, 2, 0f, 2f);
        target.MaxHp = 1000f;
        target.Hp = 1000f;
        target.UpdatePosition(new Vector3(0f, 0f, 2f), 180f, 0f, 1L, 0U);
        target.IsDefending = true;

        Assert.True(resolver.Decide(1, EchoRequest(), 100).Accepted);
        var hit = Assert.Single(Hits(resolver.DrainResolved(106))
            .Where(h => h.TargetPlayerId == 2));

        Assert.True(hit.IsBlocked);
        float baseDamage = hit.IsCritical ? 150f : 100f;
        Assert.Equal(baseDamage * CombatRules.BlockDamageScale, hit.Damage, 4);
        Assert.DoesNotContain(Hits(resolver.DrainResolved(120)), h => h.TargetPlayerId == 2);
    }

    [Fact]
    public void BlockDirection_UsesCastOrigin_WhenCasterMoves()
    {
        var store = new EntityStore();
        var resolver = new CombatResolver(store, new Random(7));
        var caster = Player(store, 1, 0f, 0f);
        var target = Player(store, 2, 0f, 2f);
        target.MaxHp = 1000f;
        target.Hp = 1000f;
        target.UpdatePosition(new Vector3(0f, 0f, 2f), 180f, 0f, 1L, 0U);
        target.IsDefending = true;

        Assert.True(resolver.Decide(1, EchoRequest(), 100).Accepted);
        caster.UpdatePosition(new Vector3(5f, 0f, 2f), 0f, 0f, 2L, 1U);

        var hit = Assert.Single(Hits(resolver.DrainResolved(106))
            .Where(h => h.TargetPlayerId == 2));
        Assert.True(hit.IsBlocked,
            "格挡方向必须按接受施法时的圆心计算，不能改用施法者当前移动后的位置");
    }

    [Fact]
    public void ZoneAndFeature_FilterCandidatesAndRecipients()
    {
        var sameZone = new PlayerState(1) { ZoneId = "Global", Features = ProtocolFeatures.SwordQiEcho };
        var noFeature = new PlayerState(2) { ZoneId = "Global", Features = 0 };
        var otherZone = new PlayerState(3) { ZoneId = "Other", Features = ProtocolFeatures.SwordQiEcho };
        var map = new Dictionary<long, PlayerState>
        {
            [1] = sameZone,
            [2] = noFeature,
            [3] = otherZone,
            [4] = new PlayerState(4) { ZoneId = "Global", Features = ProtocolFeatures.SwordQiEcho },
        };

        var recipients = CastHandler.FilterEchoRecipients(
            new long[] { 1, 2, 3, 4 }, id => map.TryGetValue(id, out var p) ? p : null, "Global");

        Assert.Equal(new long[] { 1, 4 }, recipients.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Respawn_IncrementsCombatLifeAndClearsInvulnerability()
    {
        var player = new PlayerState(1);
        Assert.Equal(1u, player.LifeId);
        player.SetInvulnerable(10, 20);
        Assert.True(player.IsInvulnerableAt(10));

        player.ApplyDamage(150f);
        player.MarkDead(100);
        Assert.True(player.TryRespawn(222, 66)); // 122 × 66ms > 8s

        Assert.Equal(2u, player.LifeId);
        Assert.False(player.IsInvulnerableAt(10));
        Assert.Equal(0u, player.InvulnerableStartTick);
        Assert.Equal(0u, player.InvulnerableEndTick);
    }
}
