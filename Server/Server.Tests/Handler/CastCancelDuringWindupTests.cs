using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// Plan 06 任务 2：麻痹取消玩家前摇的确定性用例。A 的雷 10002 判定命中 B 时，
/// B 的可打断 10001 尚未判定；状态附加 → 控制 sink（与
/// <see cref="Server3C.ApplyPlayerControl"/> 玩家分支逐行同构）→ <c>CancelCaster(B)</c>。
///
/// 怪物分支（<c>MonsterModule.CancelAction</c> / <c>NpcActionPhase.Cancelled</c>，
/// 含霸体免打断与重生代次）已由 <c>MonsterStatusControlTests</c> 的
/// `Paralyze_Cancels_Unarmored_Windup_Through_Host_Sink`、
/// `Paralyze_Is_Ignored_During_Armored_Windup`、
/// `Respawned_Monster_Gets_Control_Sink_And_Paralyze_Cancels_Windup` 覆盖，此处不再重复。
/// </summary>
public class CastCancelDuringWindupTests
{
    /// <summary>10002 首段 = decide + ceil(0.35s × 15Hz) = +6 tick（攻速倍率 1）。</summary>
    private const uint LightningFirstStage = 6;

    private static CastRequest Lightning(long targetId) => new CastRequest
    {
        SkillId = 10002,
        TargetPlayerId = targetId,
        Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f },
        ClientCastSeq = 0
    };

    private static PlayerState AddPlayer(EntityStore store, long id, float z, float rotation)
    {
        var player = store.AddPlayer(id, "P" + id);
        player.UpdatePosition(new Vector3(0f, 0f, z), rotation, 0f, 1L, 0U);
        player.ApplyDamage(-10000f); // 抬血：避免 10002 挥击+剑气的两段命中击杀导致后续断言失真
        return player;
    }

    private sealed class World
    {
        public readonly EntityStore Store = new();
        public readonly ElementalCatalogDefinition Catalog = ElementalCatalogDefaults.Create();
        public readonly CombatResolver Resolver;
        public readonly PlayerState A;
        public readonly PlayerState B;
        public readonly List<CastCancelBroadcast> Cancels = new();

        /// <summary>模拟 Server3C.CurrentTick：控制 sink 不接收 tick，取消用当前 drain 的 tick。</summary>
        public uint Tick;

        public World()
        {
            Resolver = new CombatResolver(Store, new Random(1), Catalog);
            // A 面向 +Z（B 在前），B 面向 -Z（A 在前）——10002 的锥形/剑气流都能命中对方。
            A = AddPlayer(Store, 1, z: 0f, rotation: 0f);
            A.EquippedWeaponInstanceId = 1;
            A.EquippedWeaponTemplateId = ElementalCatalogDefaults.ThunderSwordTemplateId;
            B = AddPlayer(Store, 2, z: 1.5f, rotation: 180f);

            // 与 Server3C.ApplyPlayerControl 的玩家分支一致：写权威控制状态后，
            // 首次 ActionLock 取消该实体未提交的施法并广播。
            B.ConfigureStatuses(Catalog, (speed, locked) =>
            {
                bool wasLocked = B.ControlActionLock;
                B.ApplyControl(speed, locked);
                if (!wasLocked && locked)
                    Cancels.AddRange(Resolver.CancelCaster(B.PlayerId, Tick,
                        NpcActionCancelReason.Hit));
            });
        }

        public List<CombatResolver.CastResolution> Drain(uint tick)
        {
            Tick = tick;
            return Resolver.DrainResolved(tick);
        }

        public List<CombatResolver.CastResolution> DrainRange(uint from, uint to)
        {
            var all = new List<CombatResolver.CastResolution>();
            for (uint t = from; t <= to; t++) all.AddRange(Drain(t));
            return all;
        }
    }

    [Fact]
    public void Hit_During_Windup_Cancels_Pending_Cast_With_Authoritative_Tick()
    {
        var world = new World();
        // A 先决策（判定 = 0+6），B 后决策（首段 = 5+5）：A 的判定落在 B 的前摇里。
        var aCast = world.Resolver.Decide(world.A.PlayerId, Lightning(world.B.PlayerId), tick: 0);
        Assert.True(aCast.Accepted);
        var bCast = world.Resolver.Decide(world.B.PlayerId, new CastRequest
        {
            SkillId = 10001,
            TargetPlayerId = world.A.PlayerId,
            Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
        }, tick: 5);
        Assert.True(bCast.Accepted);

        var before = world.DrainRange(0, 5);
        Assert.Empty(before);                                     // 两个首段都未到
        Assert.Equal(bCast.CastId, world.B.ActiveCast?.CastId);   // B 处于 Windup 投影
        Assert.Equal(NpcActionPhase.Windup, world.B.ActiveCast!.Phase);
        Assert.False(world.B.ControlActionLock);

        var hitTick = LightningFirstStage; // 0 + 6
        var resolved = world.Drain(hitTick);

        // A 的雷首段：挥击 + 剑气两个独立伤害实例同时结算，B 吃到麻痹。
        var aHit = Assert.Single(resolved);
        Assert.Equal(aCast.CastId, aHit.CastId);
        Assert.Equal(2, aHit.Hits.Count);
        Assert.True(world.B.ControlActionLock);

        // 取消广播逐字段权威：CastId / 生命代次 / 生效 tick / 原因。
        var cancel = Assert.Single(world.Cancels);
        Assert.Equal(bCast.CastId, cancel.CastId);
        Assert.Equal(world.B.LifeId, cancel.CasterLifeId);
        Assert.Equal(world.B.PlayerId, cancel.CasterPlayerId);
        Assert.Equal(hitTick, cancel.EffectiveTick);
        Assert.Equal(NpcActionCancelReason.Hit, cancel.Reason);

        // 未提交段整组作废：活动施法投影清空，重复取消幂等。
        Assert.Null(world.B.ActiveCast);
        Assert.Empty(world.Resolver.CancelCaster(world.B.PlayerId, hitTick + 1,
            NpcActionCancelReason.Hit));

        // 后续 tick 不再产生 B 的判定（既无 DamageResult 素材，也无发射描述）。
        var later = world.DrainRange(hitTick + 1, hitTick + 20);
        Assert.DoesNotContain(later, r => r.CastId == bCast.CastId);
        Assert.DoesNotContain(later.SelectMany(r => r.Hits), h => h.CastId == bCast.CastId);
        Assert.DoesNotContain(later, r => r.Launch != null && r.Launch.CastId == bCast.CastId);
        Assert.Single(world.Cancels); // 状态每目标每施法只附加一次，不重复取消
    }

    [Fact]
    public void Armored_Heavy_Attack_Completes_While_Paralyzed()
    {
        var world = new World();
        Assert.True(world.Resolver.Decide(world.A.PlayerId, Lightning(world.B.PlayerId), tick: 0).Accepted);
        var bCast = world.Resolver.Decide(world.B.PlayerId, Lightning(world.A.PlayerId), tick: 5);
        Assert.True(bCast.Accepted);

        world.DrainRange(0, LightningFirstStage);
        Assert.True(world.B.ControlActionLock);
        var paralyze = Assert.Single(world.B.Statuses!.Runtime.Active);
        Assert.Equal(LightningFirstStage + 12, paralyze.EndTick);
        Assert.Empty(world.Cancels);

        var stages = world.DrainRange(LightningFirstStage + 1, paralyze.EndTick)
            .Where(r => r.CastId == bCast.CastId).ToList();
        Assert.Equal(3, stages.Count);
        Assert.NotNull(stages[0].Launch);
        Assert.NotEmpty(stages[0].Hits);
        Assert.All(stages, r => Assert.Null(r.Stop));
        Assert.Empty(world.Cancels);
    }

    [Fact]
    public void Lightning_Keeps_A_Released_Fireball_On_Its_Original_Impact()
    {
        var world = new World();
        var target = AddPlayer(world.Store, 3, z: 13.5f, rotation: 0f);
        var fireball = world.Resolver.Decide(world.B.PlayerId, new CastRequest
        {
            SkillId = 20003,
            ImpactZ = 13.5f
        }, tick: 1);
        Assert.True(fireball.Accepted);
        Assert.True(world.Resolver.Decide(world.A.PlayerId, Lightning(world.B.PlayerId), tick: 0).Accepted);

        world.DrainRange(0, LightningFirstStage);

        Assert.True(world.B.ControlActionLock);
        Assert.DoesNotContain(world.Cancels, cancel => cancel.CastId == fireball.CastId);
        world.B.UpdatePosition(new Vector3(0f, 0f, -10f), 0f, 0f, 2L, 0U);
        var explosion = Assert.Single(world.DrainRange(7, 20), r => r.CastId == fireball.CastId);
        Assert.Equal(target.PlayerId, Assert.Single(explosion.Hits).TargetPlayerId);
    }

    [Fact]
    public void Explicit_CastId_Cancel_Stops_Only_That_Instance_And_Other_Continues()
    {
        var world = new World();
        var first = world.Resolver.Decide(world.B.PlayerId, Lightning(world.A.PlayerId), tick: 0);
        Assert.True(first.Accepted);
        // 冷却 0.5s = 8 tick：第二个实例在 tick 8 可接受（首段 14，未发射）。
        var second = world.Resolver.Decide(world.B.PlayerId, Lightning(world.A.PlayerId), tick: 8);
        Assert.True(second.Accepted);

        Assert.Empty(world.DrainRange(0, 5));
        var launch = Assert.Single(world.Drain(LightningFirstStage));
        Assert.Equal(first.CastId, launch.CastId); // 第一个实例已发射

        var cancels = world.Resolver.CancelPending(world.B.PlayerId, 10002, second.CastId,
            tick: 9, NpcActionCancelReason.Forced);
        var cancel = Assert.Single(cancels);
        Assert.Equal(second.CastId, cancel.CastId);
        Assert.Equal(world.B.LifeId, cancel.CasterLifeId);
        Assert.Equal(9u, cancel.EffectiveTick);

        var later = world.DrainRange(9, 30);
        Assert.DoesNotContain(later, r => r.CastId == second.CastId);
        // 另一个实例继续：第 2、3 段（10 / 13）照常结算。
        var firstRemaining = later.Where(r => r.CastId == first.CastId).ToList();
        Assert.Equal(2, firstRemaining.Count);
        Assert.All(firstRemaining, r => Assert.NotNull(r.Broadcast));
    }
}
