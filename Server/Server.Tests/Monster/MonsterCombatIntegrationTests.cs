using System;
using Cool.Protocol;
using KcpServer.Monster;
using Xunit;

namespace KcpServer.Tests;

/// <summary>P5 T3：怪物施法全链集成（MonsterModule tick + CombatResolver drain——冷却记录闭环，T2 审查补项）</summary>
public class MonsterCombatIntegrationTests
{
    [Fact]
    public void Npc_Casts_Are_Spaced_By_Cooldown_After_Resolver_Drain()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        module.SpawnDefaultLayout(4);
        var player = store.AddPlayer(1, "P");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);

        var resolver = new CombatResolver(store, new Random(42));
        uint lastCastTick = 0;
        int castCount = 0;

        for (uint t = 0; t < 120; t++)
        {
            module.Tick(t);
            foreach (var (npcId, request) in module.DrainPendingCasts())
            {
                var decision = resolver.Decide(npcId, request, t);
                if (!decision.Accepted) continue; // BT 冷却门先行——正常不应发生（防御跳过）
                castCount++;
                if (castCount > 1)
                    Assert.True(t - lastCastTick >= 15, $"casts spaced only {t - lastCastTick} ticks (cooldown not enforced)");
                lastCastTick = t;
            }
            resolver.DrainResolved(t);   // 推进队列（本用例不消费结果；判定延迟 >0 ⇒ 决策当 tick 取不到这一条）
        }

        // 120 tick：追击 ~45 tick 进入射程，此后每 15 tick 一次——期望 ≥3 次间隔施法
        Assert.True(castCount >= 3, $"expected multiple cooldown-spaced casts, got {castCount}");
    }
}
