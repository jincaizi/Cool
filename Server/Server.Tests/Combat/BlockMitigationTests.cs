using System;
using Cool.Protocol;
using Cool.SyncCore;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// PvP 格挡减伤（D-P4-8 服务端化）：防御中 + 攻击者落在正面 BlockHalfAngleDeg 锥内
/// ⇒ 伤害 × BlockDamageScale；锥外 / 未防御 ⇒ 全额。盾耐久已删除（2026-10-21），
/// 盾寿命只由客户端精力控制。
/// CombatResolver 纯核心直测（装置照 CombatResolverNpcTests，固定种子）。
/// </summary>
public class BlockMitigationTests
{
    private const long Player = 1L;
    private const long Npc = 2_000_001L;

    /// <summary>
    /// 单玩家 + 单怪世界：玩家在原点、朝向 <paramref name="playerYaw"/>（0 ⇒ +Z）；
    /// 怪物在 +Z <paramref name="npcZ"/> 处、朝向玩家（yaw 180——怪物挥击自身的锥校验需要）。
    /// </summary>
    private static (EntityStore Store, CombatResolver Resolver) World(
        float npcZ, float playerYaw = 0f, bool defending = false)
    {
        var store = new EntityStore();
        store.AddPlayer(Player, "P");
        var player = store.GetPlayer(Player)!;
        player.IsLoggedIn = true;   // 怪物施法的候选集过滤要求玩家已登录（CombatResolver.ResolveStage）
        player.UpdatePosition(new Vector3(0f, 0f, 0f), playerYaw, 0f, 0L, 0U);
        player.IsDefending = defending;
        store.AddNpc(Npc, "Slime#1", 1, 100f, new Vector3(0f, 0f, npcZ));
        store.GetPlayer(Npc)!.Rotation = 180f;

        var resolver = new CombatResolver(store, new Random(42));
        return (store, resolver);
    }

    /// <summary>怪物这一口咬的请求（50001：dmg 10 crit 0——纯确定性，格挡断言可写精确值）。</summary>
    private static CastRequest MonsterBite() => new CastRequest
    {
        SkillId = 50001,
        TargetPlayerId = Player,
        Shape = new HitShape { ShapeType = 0, Range = 1.5f, Angle = 120f }
    };

    /// <summary>玩家挥砍（10001：dmg 50 crit 15%——固定种子下双世界逐击对拍用）。</summary>
    private static CastRequest PlayerSwing(long target) => new CastRequest
    {
        SkillId = 10001,
        TargetPlayerId = target,
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
    };

    /// <summary>决策 → 推进到判定帧（decideTick + 本请求的 HitDelayTicks）→ 取回该次施法的结果。</summary>
    private static CombatResolver.CastResolution Resolve(CombatResolver resolver, long casterId,
        CastRequest request, uint decideTick)
    {
        var decision = resolver.Decide(casterId, request, decideTick);
        Assert.True(decision.Accepted, "cast rejected: " + decision.RejectReason);
        if (!decision.Accepted) return decision;
        CombatRules.TryGet(request.SkillId, out var entry);
        foreach (var r in resolver.DrainResolved(decideTick + CombatRules.HitDelayTicks(entry!)))
            if (r.Broadcast!.CasterPlayerId == casterId && r.Broadcast.SkillId == request.SkillId)
                return r;
        return decision;
    }

    /// <summary>① 防御中 + 锥内 ⇒ 伤害 = BaseDamage × BlockDamageScale（精确值 + 余血口径）。</summary>
    [Fact]
    public void Defending_In_Cone_Takes_BlockDamageScale_Damage()
    {
        var (store, resolver) = World(npcZ: 1.2f, defending: true);
        var player = store.GetPlayer(Player)!;

        var hit = Assert.Single(Resolve(resolver, Npc, MonsterBite(), 10).Hits);

        Assert.True(hit.IsBlocked);
        Assert.Equal(10f * CombatRules.BlockDamageScale, hit.Damage, 4);   // 3（50001 无暴击域）
        Assert.Equal(100f - 3f, player.Hp, 4);
        Assert.Equal(player.Hp, hit.TargetRemainingHp, 4);                 // 扣后权威余血（T12 甲-2）
    }

    /// <summary>② 防御中但锥外（背对攻击者）⇒ 全额，且不格挡。</summary>
    [Fact]
    public void Defending_But_Facing_Away_Takes_Full_Damage()
    {
        var (store, resolver) = World(npcZ: 1.2f, playerYaw: 180f, defending: true);
        var player = store.GetPlayer(Player)!;

        var hit = Assert.Single(Resolve(resolver, Npc, MonsterBite(), 10).Hits);

        Assert.False(hit.IsBlocked);
        Assert.Equal(10f, hit.Damage, 4);
        Assert.Equal(90f, player.Hp, 4);
    }

    /// <summary>③ 未防御 ⇒ 全额（回归：格挡接入不改变既有裁决）。</summary>
    [Fact]
    public void Not_Defending_Takes_Full_Damage()
    {
        var (store, resolver) = World(npcZ: 1.2f, defending: false);
        var player = store.GetPlayer(Player)!;

        var hit = Assert.Single(Resolve(resolver, Npc, MonsterBite(), 10).Hits);

        Assert.False(hit.IsBlocked);
        Assert.Equal(10f, hit.Damage, 4);
        Assert.Equal(90f, player.Hp, 4);
    }

    /// <summary>④ 耐久删除后：连续格挡不再有上限（旧的“第 5 击不再减伤”已不适用）。</summary>
    [Fact]
    public void Defending_Blocks_Repeatedly_Without_Durability_Limit()
    {
        var (store, resolver) = World(npcZ: 1.2f, defending: true);
        var player = store.GetPlayer(Player)!;

        for (int i = 0; i < 8; i++)
        {
            var blocked = Assert.Single(Resolve(resolver, Npc, MonsterBite(), (uint)(10 + i * 30)).Hits);
            Assert.True(blocked.IsBlocked, $"第 {i + 1} 次格挡应命中");
            Assert.Equal(3f, blocked.Damage, 4);
        }
        Assert.Equal(100f - 8 * 3f, player.Hp, 4);   // 8 × 3 = 24
    }

    /// <summary>
    /// ⑤ 掷骰顺序不变：固定种子双世界（唯一差异 = 受击者是否防御）⇒ 逐击暴击结果相同、
    /// 格挡伤害 = 非格挡伤害 × BlockDamageScale。D4「服务器掷骰确定性可复现」的机器化防线。
    /// </summary>
    [Fact]
    public void Block_Multiplies_After_The_Crit_Roll_Without_Shifting_It()
    {
        var (_, plainResolver) = Duel(defending: false);
        var (_, blockResolver) = Duel(defending: true);
        const long Defender = 3L;

        for (int i = 0; i < 3; i++)
        {
            uint decideTick = (uint)(10 + i * 20); // 10001 冷却是 0 ⇒ tick 只需递增
            var plain = Assert.Single(Resolve(plainResolver, Player, PlayerSwing(Defender), decideTick).Hits);
            var scaled = Assert.Single(Resolve(blockResolver, Player, PlayerSwing(Defender), decideTick).Hits);

            Assert.Equal(plain.IsCritical, scaled.IsCritical);
            Assert.Equal(plain.Damage * CombatRules.BlockDamageScale, scaled.Damage, 4);
            Assert.True(scaled.IsBlocked);
        }
    }

    /// <summary>双人决斗世界：攻击者在原点朝 +Z，受击者在 +Z 1.2m 处、**面朝攻击者**（yaw 180）。
    /// 受击者 Hp 拔高到 1000：3 刀 {50..75} 会打死 100 血的目标 ⇒ 非格挡世界第三击会因
    /// "死者不可被命中"（HitResolver 的 Alive 过滤）静默落空，对拍失真。</summary>
    private static (EntityStore Store, CombatResolver Resolver) Duel(bool defending)
    {
        var store = new EntityStore();
        store.AddPlayer(Player, "A");
        store.GetPlayer(Player)!.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 0L, 0U);
        store.AddPlayer(3L, "B");
        var defender = store.GetPlayer(3L)!;
        defender.MaxHp = 1000f;
        defender.Hp = 1000f;
        defender.UpdatePosition(new Vector3(0f, 0f, 1.2f), 180f, 0f, 0L, 0U);
        defender.IsDefending = defending;

        return (store, new CombatResolver(store, new Random(42)));
    }
}
