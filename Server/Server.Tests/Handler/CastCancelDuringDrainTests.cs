using System;
using System.Collections.Generic;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// C1 回归：状态附加 → <see cref="ServerStatusManager"/> 控制 sink → <c>CancelCaster</c>
/// 可能重入 <c>DrainResolved</c> 的单遍压缩。取消不得在迭代中 <c>RemoveAt</c>：
/// 修复前 B 的项被删后，写指针照写原下标 —— 两个待判定施法时越界抛异常，
/// 三个时静默覆盖并丢弃后一笔施法（C）。
/// </summary>
public class CastCancelDuringDrainTests
{
    /// <summary>10002 首段 = decide + ceil(0.35s × 15) = +6 tick。</summary>
    private const uint LightningFirstStage = 6;

    /// <summary>30001 首段 = decide + ceil(0.8s × 15) = +12 tick（首段未到前被取消）。</summary>
    private const uint LongCastFirstStage = 12;

    private static CastRequest Lightning() => new CastRequest
    {
        SkillId = 10002,
        Shape = new HitShape { ShapeType = 0, Range = 2.5f, Angle = 120f },
        ClientCastSeq = 0
    };

    private static CastRequest LongCast() => new CastRequest
    {
        SkillId = 30001,
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f },
        ImpactX = 0f, ImpactY = 0f, ImpactZ = 1f,
        ClientCastSeq = 0
    };

    private static CastRequest Spin() => new CastRequest
    {
        SkillId = 20002,
        Shape = new HitShape { ShapeType = 1, AreaRadius = 2.5f, Angle = 120f },
        ClientCastSeq = 0
    };

    private static PlayerState AddPlayer(EntityStore store, long id, float z)
    {
        var player = store.AddPlayer(id, "P" + id);
        player.UpdatePosition(new Vector3(0f, 0f, z), 0f, 0f, 1L, 0U);
        player.ApplyDamage(-10000f); // 抬血：避免首段暴击击杀导致状态不附加
        return player;
    }

    private sealed class World
    {
        public readonly EntityStore Store = new();
        public readonly ElementalCatalogDefinition Catalog = ElementalCatalogDefaults.Create();
        public readonly CombatResolver Resolver;
        public readonly PlayerState A;
        public readonly PlayerState B;
        public readonly PlayerState C;
        public readonly List<CastCancelBroadcast> Cancels = new();

        /// <summary>模拟 Server3C.CurrentTick：控制 sink 不接收 tick，取消用当前 drain 的 tick。</summary>
        public uint Tick;

        public World()
        {
            Resolver = new CombatResolver(Store, new Random(1), Catalog);
            A = AddPlayer(Store, 1, z: 0f);
            A.EquippedWeaponInstanceId = 1;
            A.EquippedWeaponTemplateId = ElementalCatalogDefaults.ThunderSwordTemplateId;
            B = AddPlayer(Store, 2, z: 1.5f);
            C = AddPlayer(Store, 3, z: -3f);

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

        public void Drain(uint tick)
        {
            Tick = tick;
            Resolver.DrainResolved(tick);
        }
    }

    [Fact]
    public void CancelCaster_During_Drain_Does_Not_Throw_And_Remaining_Stages_Resolve()
    {
        var world = new World();
        var bCast = world.Resolver.Decide(world.B.PlayerId, LongCast(), tick: 0);
        Assert.True(bCast.Accepted);
        var aCast = world.Resolver.Decide(world.A.PlayerId, Lightning(), tick: 1);
        Assert.True(aCast.Accepted);

        // 主循环每 tick 都 drain。B 的首段（tick 12）尚未到，A 的首段（tick 7）先到。
        for (uint t = 0; t < 1 + LightningFirstStage; t++)
            world.Drain(t);

        // tick 7：A 命中 B → 麻痹 → 控制 sink → CancelCaster(B)。
        // 修复前这里在 DrainResolved 的压缩写回处抛 ArgumentOutOfRangeException。
        world.Tick = 1 + LightningFirstStage;
        var resolved = world.Resolver.DrainResolved(world.Tick);

        var aFirst = Assert.Single(resolved);
        Assert.Equal(aCast.CastId, aFirst.CastId);
        Assert.NotEmpty(aFirst.Hits);
        Assert.NotNull(aFirst.Broadcast);

        // B 的 CastCancelBroadcast 仍返回（Server3C 随后 BroadcastCancel 发送）。
        var cancel = Assert.Single(world.Cancels);
        Assert.Equal(bCast.CastId, cancel.CastId);
        Assert.Equal(world.B.PlayerId, cancel.CasterPlayerId);
        Assert.Equal(NpcActionCancelReason.Hit, cancel.Reason);

        // A 的剩余段仍结算并携带广播（10002 共 3 段：7/11/14）。
        var later = new List<CombatResolver.CastResolution>();
        for (uint t = world.Tick + 1; t <= world.Tick + 20; t++)
            later.AddRange(world.Resolver.DrainResolved(t));

        var aLater = later.Where(r => r.CastId == aCast.CastId).ToList();
        Assert.Equal(2, aLater.Count);
        Assert.All(aLater, r => Assert.NotNull(r.Broadcast));

        // B 后续不再结算（首段在 tick 12 前已被取消）。
        Assert.DoesNotContain(later, r => r.CastId == bCast.CastId);
        Assert.Single(world.Cancels);
    }

    [Fact]
    public void CancelCaster_During_Drain_Does_Not_Silently_Drop_Third_Pending_Cast()
    {
        var world = new World();
        var bCast = world.Resolver.Decide(world.B.PlayerId, LongCast(), tick: 0);
        var aCast = world.Resolver.Decide(world.A.PlayerId, Lightning(), tick: 1);
        var cCast = world.Resolver.Decide(world.C.PlayerId, Spin(), tick: 1);
        Assert.True(bCast.Accepted && aCast.Accepted && cCast.Accepted);

        for (uint t = 0; t < 1 + LightningFirstStage; t++)
            world.Drain(t);

        // 修复前：CancelCaster(B) 删掉下标 0 后，压缩把 A 写回下标 1 —— 覆盖 C。
        world.Tick = 1 + LightningFirstStage;
        var resolved = world.Resolver.DrainResolved(world.Tick);

        Assert.Contains(resolved, r => r.CastId == aCast.CastId);
        Assert.Contains(resolved, r => r.CastId == cCast.CastId);
        Assert.All(resolved.Where(r => r.CastId != 0), r => Assert.NotNull(r.Broadcast));
        Assert.Single(world.Cancels);

        // 后续 tick C 仍按自己的时间轴推进（旋转多段），B 不再结算。
        var later = new List<CombatResolver.CastResolution>();
        for (uint t = world.Tick + 1; t <= world.Tick + LongCastFirstStage; t++)
            later.AddRange(world.Resolver.DrainResolved(t));

        Assert.DoesNotContain(later, r => r.CastId == bCast.CastId);
        Assert.Contains(later, r => r.CastId == cCast.CastId);
    }
}
