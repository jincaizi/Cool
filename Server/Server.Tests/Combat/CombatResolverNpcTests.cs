using System;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>P5 T3：怪物参与的战斗裁决（CombatResolver 纯核心直测，无 Server3C/session 依赖）</summary>
public class CombatResolverNpcTests
{
    private static readonly Random SeededRng = new Random(42);

    private static (EntityStore Store, CombatResolver Resolver, long Player, long Npc) World(
        float npcZ, int templateId = 1)
    {
        var store = new EntityStore();
        long player = 1;
        long npc = MonsterModule.NpcIdSeed + 1;
        store.AddPlayer(player, "P");
        store.GetPlayer(player)!.IsLoggedIn = true;
        // 怪物在玩家正前方 +Z（yaw=0 锥内——自动目标用例依赖方向语义；显式目标用例方向无关）
        store.AddNpc(npc, $"Slime#{npc}", templateId, 100f, new Vector3(0f, 0f, npcZ));
        // AddNpc 朝向硬编码 0（+Z）——怪物自动目标需面向原点玩家（yaw=180 锥内；显式目标用例方向无关）
        store.GetPlayer(npc)!.Rotation = 180f;
        var resolver = new CombatResolver(store, SeededRng);
        return (store, resolver, player, npc);
    }

    private static CastRequest PlayerSwing(long target) => new CastRequest
    {
        SkillId = 10001, // BA_Combo1：Single range 2，dmg 50 crit 15%（种子固定 → 掷骰确定性）
        TargetPlayerId = target,
        Shape = new HitShape { ShapeType = 0, Range = 2f, Angle = 120f }
    };

    private static CastRequest MonsterBite(long target, float range) => new CastRequest
    {
        SkillId = 50001, // Slime：dmg 10 crit 0——伤害恒 10（纯确定性）
        TargetPlayerId = target,
        Shape = new HitShape { ShapeType = 0, Range = range, Angle = 120f }
    };

    [Fact]
    public void Player_Cast_Explicit_Npc_Target_Damages_Npc_Authoritatively()
    {
        var (store, resolver, player, npc) = World(1.5f); // 玩家前方 1.5m（10001 射程 2 内）
        var npcState = store.GetPlayer(npc)!;

        var result = resolver.ResolveCast(player, PlayerSwing(npc), 10);

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(npc, hit.TargetPlayerId);
        Assert.Equal(100f - hit.Damage, npcState.Hp);      // 服务器权威扣血
        Assert.True(hit.Damage is 50f or 75f);             // 种子 42 下暴击域 {50, 75}
        Assert.Equal(npcState.Hp, hit.TargetRemainingHp);  // 余血 = 扣后 HP（双端一致载体）
    }

    [Fact]
    public void Player_Cast_Auto_Target_Hits_Closer_Npc_Not_Farther_Player()
    {
        var (store, resolver, player, npc) = World(1.5f);
        var other = store.AddPlayer(2, "B");
        other.IsLoggedIn = true;
        other.UpdatePosition(new Vector3(0f, 0f, 3f), 0f, 0f, 1L, 0U); // 前方 3m——锥内但射程 2 外

        var result = resolver.ResolveCast(player, PlayerSwing(0), 10); // 自动目标（P4 backlog 跨界判别落地）

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(npc, hit.TargetPlayerId); // 锥内射程内最近者 = 怪物（挥击打怪不再误伤附近玩家）
        Assert.Equal(100f, store.GetPlayer(2)!.Hp); // 射程外玩家不误伤
    }

    [Fact]
    public void Npc_Cast_Hits_Player_With_Exact_Damage_And_Records_Cooldown()
    {
        var (store, resolver, player, npc) = World(1.5f);

        var result = resolver.ResolveCast(npc, MonsterBite(player, 1.5f), 10);

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(player, hit.TargetPlayerId);
        Assert.Equal(10f, hit.Damage);                 // crit 0 → 掷骰恒 10
        Assert.Equal(90f, store.GetPlayer(player)!.Hp);
        Assert.Equal(npc, result.Broadcast!.CasterPlayerId); // 客户端表现/浮字源 id

        // 冷却：同 tick 重施被拒（RecordCast 即时生效）
        var second = resolver.ResolveCast(npc, MonsterBite(player, 1.5f), 10);
        Assert.False(second.Accepted);
        Assert.Equal("cooldown", second.RejectReason);
    }

    [Fact]
    public void Npc_Cast_Ignores_Other_Npcs()
    {
        var (store, resolver, player, npc) = World(1.5f);
        store.AddNpc(npc + 1, "Wolf#2", 2, 150f, new Vector3(1.6f, 0f, 0f)); // 贴邻怪物

        var result = resolver.ResolveCast(npc, MonsterBite(player, 1.5f), 10);

        var hit = Assert.Single(result.Hits); // 邻怪不被命中（D-P5-3 怪物互不打——回归锁定）
        Assert.Equal(player, hit.TargetPlayerId);
    }

    [Fact]
    public void Npc_Cast_Auto_Target_Skips_Non_Logged_In_Entities()
    {
        var (store, resolver, player, npc) = World(1.5f);
        var bot = store.AddPlayer(99, "Bot");
        bot.UpdatePosition(new Vector3(0f, 0f, 0.8f), 0f, 0f, 1L, 0U); // 未登录 bot 比玩家更近（锥内）

        var result = resolver.ResolveCast(npc, MonsterBite(0, 1.5f), 10); // 自动目标

        Assert.True(result.Accepted);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(player, hit.TargetPlayerId); // bot 不可被怪物命中（候选集过滤——本用例是策略变更的失败驱动）
        Assert.Equal(100f, bot.Hp);
    }

    [Fact]
    public void Dead_Npc_Cannot_Cast_And_Cannot_Be_Hit()
    {
        var (store, resolver, player, npc) = World(1.5f);
        var npcState = store.GetPlayer(npc)!;
        npcState.ApplyDamage(100f); // 死亡（死亡展示窗口内仍在 store）

        Assert.False(npcState.IsAlive);

        var cast = resolver.ResolveCast(npc, MonsterBite(player, 1.5f), 10);
        Assert.False(cast.Accepted);
        Assert.Equal("caster-dead", cast.RejectReason);

        var swing = resolver.ResolveCast(player, PlayerSwing(npc), 10);
        Assert.True(swing.Accepted);   // 施法合法（空挥冷却语义）
        Assert.Empty(swing.Hits);      // 死亡怪物不可被命中（Alive 过滤）
    }
}
