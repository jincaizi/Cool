using System;
using System.IO;
using System.Linq;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Config;
using KcpServer.Monster;
using Xunit;
using Xunit.Abstractions;

namespace KcpServer.Tests;

/// <summary>
/// 「判定时刻 = 动画判定帧」（2026-10-16 改造）的**自动化验收**——把原先只能靠肉眼估算的
/// 角度/距离/时间变成断言里的数字。三条：
///   ① 前摇期致死 → 这一刀作废（判定帧复查施法者存活，CombatResolver 的 D-4）；
///   ② 丢目标 ≠ 打不中：怪物视野半角 45° 与判定半角 60° 之间那条带子里，TargetId 已被清空、
///      这一刀照样落地（**这是本轮的设计真相**，用两个静态角度钉死）；
///   ③ 贴脸绕圈空挥 / 拉开一点绕圈命中：判定帧上"玩家相对怪物转过的角度"与 60° 判定半角的分界
///      （头号功能的可验证形式）。
///
/// 装置与 MonsterWindupTests / MonsterCombatIntegrationTests 同款：这些测试不跑 PositionSync 校验，
/// **逐 tick 直接改玩家位置**就是"模拟一个绕圈的玩家"的手段。
/// 配置数字一律从构建输出目录的 monster_config.json 读、技能表值从 CombatRules 读（不抄）。
/// </summary>
public class MonsterAcceptanceTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>实测数字（夹角/距离/tick）走测试输出——断言消息只在失败时可见，验收报告要能读到"实测 vs 期望"。</summary>
    public MonsterAcceptanceTests(ITestOutputHelper output) => _output = output;

    // ---------- 装置（本仓库惯例：各测试类各自持有一份 private 装置） ----------

    private static (EntityStore Store, MonsterModule Module) Create()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        return (store, module);
    }

    private static PlayerState LoginPlayer(EntityStore store, long id, Vector3 position)
    {
        var p = store.AddPlayer(id, $"P{id}");
        p.IsLoggedIn = true;
        p.UpdatePosition(position, 0f, 0f, 1L, 0U);
        return p;
    }

    /// <summary>读构建输出目录里的模板配置（assertion 里的距离/角度门槛从这里来，不在测试里抄数字）</summary>
    private static MonsterData ConfigOf(int templateId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "monster_config.json");
        return ConfigLoader.LoadMonsterTemplates(path)[templateId];
    }

    /// <summary>
    /// 跑到首次决策：返回（决策 tick, 入队的请求）。前摇到期 = 决策 tick + CombatRules.HitDelayTicks(entry)。
    /// 用显式 hasValue 而非"tick == 0 当哨兵"——首次施法恰好发生在 tick 0 是常态（生成即贴脸）。
    /// </summary>
    private static (uint DecideTick, CastRequest Request, CombatResolver.CastResolution Decision)
        RunToFirstDecision(MonsterModule module, CombatResolver resolver, uint tickBudget = 30)
    {
        for (uint t = 0; t < tickBudget; t++)
        {
            module.Tick(t);
            foreach (var (npcId, request) in module.DrainPendingCasts())
            {
                var decision = resolver.Decide(npcId, request, t);
                Assert.True(decision.Accepted,
                    $"前置：这一刀必须真的成立（{decision.RejectReason}）——否则后续断言空转");
                return (t, request, decision);
            }
        }
        Assert.Fail($"前置：{tickBudget} tick 内怪物没有出手（感知 / 距离 / 冷却哪一环断了？）");
        return (0, null!, null!); // 不可达（Assert.Fail 抛）
    }

    /// <summary>命中判定半角：怪物挥击的判定锥恒为 ±Angle/2（MonsterBtActions 发 Angle=120）——
    /// 引用实际入队的请求，而不是在测试里写 60。</summary>
    private static float JudgmentConeHalfAngle(CastRequest request, SkillEntry entry)
    {
        Assert.True(entry.HitDelaySeconds > 0f, "前置：怪物技能应有判定延迟（无延迟就没有前摇可测）");
        return request.Shape!.Angle * 0.5f;
    }

    /// <summary>从 (from) 指向 (to) 的方位角（度；yaw 语义 = HitResolver/TargetDetector：0 → +Z）</summary>
    private static float YawBetween(Vector3 from, Vector3 to)
    {
        float dx = to.X - from.X, dz = to.Z - from.Z;
        return (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
    }

    /// <summary>绕 (center) 半径 radius、方位角 phi（度）的点</summary>
    private static Vector3 PointOnCircle(Vector3 center, float radius, float phiDegrees)
    {
        float rad = phiDegrees * (float)(Math.PI / 180.0);
        return new Vector3(
            center.X + (float)Math.Sin(rad) * radius,
            center.Y,
            center.Z + (float)Math.Cos(rad) * radius);
    }

    /// <summary>最短角差（度）——yaw 环绕安全（同 MonsterWindupTests 的助手）</summary>
    private static float YawDeltaDegrees(float a, float b)
    {
        float d = (a - b) % 360f;
        if (d > 180f) d -= 360f;
        if (d < -180f) d += 360f;
        return Math.Abs(d);
    }

    /// <summary>怪物朝向（服务端实体上 flush 过的 yaw）对目标的夹角（度）</summary>
    private static float AngleToTarget(PlayerState monster, PlayerState target)
        => YawDeltaDegrees(monster.Rotation, YawBetween(monster.Position, target.Position));

    // ---------- A1：前摇期致死 → 无伤害 ----------

    /// <summary>
    /// 决策 → 前摇期内击杀怪物 → 判定帧这一刀必须**没有伤害**：施法者死亡在判定时复查
    /// （CombatResolver.ResolveStage 的 `!caster.IsAlive` → 返回 null 丢弃），不是"回头补一刀"。
    /// Wolf skill 50002：范围 2.0 / Angle 120 / HitDelay 0.35s（=6 tick）。
    /// </summary>
    [Fact]
    public void Windup_Death_Cancels_The_Swing_And_No_Damage_Lands()
    {
        var (store, module) = Create();
        // Wolf 生成在原点前方 1.5m，朝向原点（SpawnMonster 恒面向原点）——玩家在原点：开局即感知 + 在攻击距离内
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        var player = LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));
        var resolver = new CombatResolver(store, new Random(42));

        var (decideTick, request, _) = RunToFirstDecision(module, resolver);
        Assert.True(CombatRules.TryGet(50002, out var entry));
        uint resolveTick = decideTick + CombatRules.HitDelayTicks(entry!);
        Assert.Equal(50002, request.SkillId);

        // 前摇期内（T+2，落在 T+1 .. ART-1 里）把怪打死——用玩家裁决口径的入口：
        // 怪物的 IsAlive 由 PlayerState.ApplyDamage 决定，而不是模块自己算的。
        // **时间轴必须连续**：T+1 .. ART-1 每个 tick 都跑（击杀只是在 killTick 那一格额外插一刀），
        // 漏 tick 在前摇无位移的当前几何下无害，但换成需要转向/位移的用例就会咬人。
        uint killTick = decideTick + 2;
        Assert.True(killTick < resolveTick, $"前置：击杀要发生在前摇内（kill={killTick} resolve={resolveTick}）");
        for (uint t = decideTick + 1; t < resolveTick; t++)
        {
            if (t == killTick) store.GetPlayer(wolfId)!.ApplyDamage(9999f);
            module.Tick(t);
        }

        var state = module.GetState(wolfId);
        Assert.NotNull(state);
        Assert.True(state!.DeathTick.HasValue, "前置：怪物已进入死亡窗口（死亡由 HP 变化在下一 tick 察觉）");
        Assert.Equal(100f, player.Hp); // 判定前玩家一滴血没掉

        // 判定帧这一 tick + 真实裁决链（与 Server3C 主循环同序：怪物 tick 之后 DrainResolved）
        module.Tick(resolveTick);
        var resolved = resolver.DrainResolved(resolveTick);

        Assert.Equal(100f, player.Hp);                                       // ① 玩家 HP 未下降
        Assert.True(state.DeathTick.HasValue);                               // ② 怪物在死亡窗口内
        Assert.DoesNotContain(resolved, r => r.Hits.Count > 0);              // ③ 该 caster 这一刀无命中

        _output.WriteLine($"A1 实测：决策 T={decideTick}，判定帧 ART={resolveTick}（HitDelayTicks="
            + $"{CombatRules.HitDelayTicks(entry!)}），击杀 tick={killTick}；判定后玩家 HP={player.Hp:F0}"
            + $"（期望 100），怪物 DeathTick={state.DeathTick}，判定帧命中数=0");
    }

    // ---------- A2：丢目标 ≠ 打不中（静态角度） ----------

    /// <summary>
    /// 两个静态角度钉死"视野锥 ≠ 判定锥"这条带子（Wolf：visionAngle **90** → 视野半角 45°；
    /// 挥击判定锥恒 120 → 判定半角 60°）：
    ///   · 偏轴 55°：出视野锥（目标丢失、TargetId 清空）但仍在判定锥内 ⇒ **这一刀照样落地**；
    ///   · 偏轴 65°：出判定锥 ⇒ 纯空挥。
    /// 玩家**只在决策之后搬一次**（决策必须发生在原点——怪要先拿到目标才会有前摇），此后不再动，
    /// 让前摇期"朝最后已知目标位置 90°/s 转向"成为唯一变量（本例中该转向恰好是零：最后已知位置
    /// 就在怪的正前方，故怪的朝向冻结在决策那一刻）。
    /// 距离恒 1.5m ≤ attackRange 2.0 ⇒ 差异只能来自锥角，不可能来自射程。
    /// </summary>
    [Theory]
    [InlineData(55f, true)]   // 视野半角 45 < 55 < 判定半角 60：目标丢失，但这一刀落地（设计真相）
    [InlineData(65f, false)]  // > 判定半角 60：空挥
    public void Lost_Target_Still_Hits_When_Inside_The_Judgment_Cone(float offAxisDegrees, bool expectHit)
    {
        var (store, module) = Create();
        long wolfId = module.SpawnMonster(2, new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.5f));
        var player = LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));
        var resolver = new CombatResolver(store, new Random(42));
        var config = ConfigOf(2);

        var (decideTick, request, _) = RunToFirstDecision(module, resolver);
        Assert.True(CombatRules.TryGet(50002, out var entry));
        uint resolveTick = decideTick + CombatRules.HitDelayTicks(entry!);
        float halfAngle = JudgmentConeHalfAngle(request, entry!);

        // 前置：决策发生在原点——正对着怪（视野锥内、攻击距离内）
        var wolfEntity = store.GetPlayer(wolfId)!;
        float spawnDistance = Vector3.Distance(wolfEntity.Position, player.Position);
        Assert.True(spawnDistance <= config.attackRange,
            $"前置：开局距离 {spawnDistance:F2}m 必须 ≤ attackRange {config.attackRange}m");
        Assert.True(AngleToTarget(wolfEntity, player) <= config.visionAngle * 0.5f,
            "前置：开局玩家必须在视野锥内（否则怪不会决策）");

        // 决策后搬一次：以怪物为原点、以怪物朝向为轴偏 offAxis、距离 1.5m（仍 ≤ attackRange）
        float wolfFacingYaw = wolfEntity.Rotation;
        float offAxisYaw = wolfFacingYaw + offAxisDegrees;
        const float placeDistance = 1.5f;
        player.UpdatePosition(PointOnCircle(wolfEntity.Position, placeDistance, offAxisYaw), 0f, 0f, 1L, 0U);

        for (uint t = decideTick + 1; t <= resolveTick; t++) module.Tick(t); // 玩家此后不再动
        var resolved = resolver.DrainResolved(resolveTick);

        float finalDistance = Vector3.Distance(wolfEntity.Position, player.Position);
        float finalAngle = AngleToTarget(wolfEntity, player);
        var state = module.GetState(wolfId)!;
        var hit = resolved.SelectMany(r => r.Hits).FirstOrDefault(h => h.TargetPlayerId == 1);

        string numbers = $"偏轴 {offAxisDegrees}°/判定半角 {halfAngle:F0}°："
            + $"实测夹角 {finalAngle:F2}°、距离 {finalDistance:F3}m（attackRange {config.attackRange}m，"
            + $"视野半角 {config.visionAngle * 0.5f:F0}°）、TargetId={(state.TargetId.HasValue ? $"有({state.TargetId})" : "空")}";
        Assert.True(expectHit == (hit != null),
            $"{numbers}——期望{(expectHit ? "命中" : "空挥")}，实得{(hit != null ? $"命中 {hit.Damage}" : "空挥")}");
        Assert.True(expectHit == (finalAngle <= halfAngle), $"{numbers}——夹角与判定半角的关系与结果不一致");
        // ② 与此同时目标已被清空：55° 那一格正是"怪丢了目标，这一刀照样落地"
        Assert.Null(state.TargetId);
        // 距离落在射程内 ⇒ 空挥只可能来自锥角（排除"跑出射程"这个错误原因）
        Assert.True(finalDistance <= config.attackRange + 0.01f,
            $"{numbers}——距离必须在 attackRange 内，否则空挥的成因是射程而不是锥角");
        // 伤害期望值从**表里读**（不抄常量）：怪物 crit 0 ⇒ 期望伤害恒 = BaseDamage
        Assert.True(CombatRules.TryGet(request.SkillId, out var skill), $"技能 {request.SkillId} 不在 CombatRules 表内");
        Assert.Equal(0f, skill!.CriticalRate);
        float expectedHp = expectHit ? 100f - skill!.BaseDamage : 100f;
        Assert.Equal(expectedHp, player.Hp, 4);
        _output.WriteLine($"A2 {numbers} → {(hit != null ? $"命中 {hit.Damage}" : "空挥")}（期望 {(expectHit ? "命中" : "空挥")}），"
            + $"玩家 HP={player.Hp:F0}（期望 {expectedHp:F0}）");
    }

    // ---------- A3：贴脸绕圈 → 空挥；拉开一点绕圈 → 命中 ----------

    /// <summary>
    /// 本轮头号功能的可验证形式：判定帧上"玩家相对怪物转过的角度"与 60° 判定半角的分界。
    /// DogKnight（template 3：attackRange 2.2 / visionAngle 120 / 50003）在 (0,0,2.0) 面向原点，
    /// 玩家从决策后每 tick 沿以怪物为圆心的圆周走：ω = 冲刺速度 ÷ 半径（8 m/s ÷ r）。
    ///   · r = 1.5m：玩家 ~305°/s，怪物前摇转向 90°/s ⇒ 6 tick 内相对转过 ~86° > 60° ⇒ **空挥**；
    ///   · r = 2.1m：ω ~218°/s ⇒ 相对转过 ~51° < 60° ⇒ **命中**。
    /// 两次的判定帧距离都 ≤ attackRange —— 差异**只能**来自锥角（本用例的关键：
    /// 排除"其实是跑出射程才空挥"这个错误原因；assertion 消息里打出实测夹角与相对转角）。
    /// </summary>
    [Theory]
    [InlineData(1.5f, false)]  // 贴脸绕圈：角速度 305°/s ≫ 怪物 90°/s → 空挥
    [InlineData(2.1f, true)]   // 拉开一点：相对转角 ~51° < 60° → 命中
    public void Circling_At_Attack_Range_Whiffs_Only_When_Relative_Turn_Exceeds_The_Cone(
        float radius, bool expectHit)
    {
        var (store, module) = Create();
        long dogId = module.SpawnMonster(3, new Vector3(0f, 0f, 2.0f), new Vector3(0f, 0f, 2.0f));
        var player = LoginPlayer(store, 1, new Vector3(0f, 0f, 0f));
        var resolver = new CombatResolver(store, new Random(42));
        var config = ConfigOf(3);

        var (decideTick, request, _) = RunToFirstDecision(module, resolver);
        Assert.True(CombatRules.TryGet(50003, out var entry));
        uint resolveTick = decideTick + CombatRules.HitDelayTicks(entry!);
        float halfAngle = JudgmentConeHalfAngle(request, entry!);

        var dog = store.GetPlayer(dogId)!;
        float wolfYawAtDecision = dog.Rotation;
        float startDistance = Vector3.Distance(dog.Position, player.Position);
        Assert.True(startDistance <= config.attackRange,
            $"前置：开局距离 {startDistance:F2}m 必须 ≤ attackRange {config.attackRange}m（否则不会决策）");

        // 玩家绕圈：ω = 冲刺速度 ÷ 半径；Δt 与 MonsterModule 同源（不硬编码 0.0667）
        const float sprintSpeed = 8f; // 客户端冲刺速度（Assets/.../Sys3C/Character/CharacterController.cs）
        float degreesPerTick = sprintSpeed / radius * MonsterModule.TickDeltaSeconds
                               * (float)(180.0 / Math.PI);
        float phi = YawBetween(dog.Position, player.Position); // 初始方位角（决策时玩家在原点 = 怪的正前方）
        float sweptDegrees = 0f;

        for (uint t = decideTick + 1; t <= resolveTick; t++)
        {
            phi += degreesPerTick;
            sweptDegrees += degreesPerTick;
            player.UpdatePosition(PointOnCircle(dog.Position, radius, phi), 0f, 0f, 1L, 0U);
            module.Tick(t);
        }
        var resolved = resolver.DrainResolved(resolveTick);

        float finalDistance = Vector3.Distance(dog.Position, player.Position);
        float finalAngle = AngleToTarget(dog, player);
        float monsterTurned = YawDeltaDegrees(dog.Rotation, wolfYawAtDecision);
        var hit = resolved.SelectMany(r => r.Hits).FirstOrDefault(h => h.TargetPlayerId == 1);

        float playerDegPerSecond = sprintSpeed / radius * (float)(180.0 / Math.PI);
        string numbers =
            $"半径 {radius}m：玩家 {playerDegPerSecond:F0}°/s（ω=冲刺÷半径）绕过 {sweptDegrees:F1}°，"
            + $"怪物前摇只转了 {monsterTurned:F1}°（90°/s），判定帧实测夹角 {finalAngle:F2}°（半角 {halfAngle:F0}°）、"
            + $"距离 {finalDistance:F3}m（attackRange {config.attackRange}m）";
        Assert.True(expectHit == (hit != null),
            $"{numbers}——期望{(expectHit ? "命中" : "空挥")}，实得{(hit != null ? $"命中 {hit.Damage}" : "空挥")}");
        Assert.True(expectHit == (finalAngle <= halfAngle), $"{numbers}——夹角与判定半角的关系与结果不一致");
        // 关键反证：两次的判定帧距离都在射程内——空挥只可能来自锥角
        Assert.True(finalDistance <= config.attackRange + 0.01f,
            $"{numbers}——距离必须在 attackRange 内，否则空挥的成因是射程而不是锥角");
        // 伤害期望值从**表里读**（不抄常量）：怪物 crit 0 ⇒ 期望伤害恒 = BaseDamage
        Assert.True(CombatRules.TryGet(request.SkillId, out var skill), $"技能 {request.SkillId} 不在 CombatRules 表内");
        Assert.Equal(0f, skill!.CriticalRate);
        float expectedHp = expectHit ? 100f - skill!.BaseDamage : 100f;
        Assert.Equal(expectedHp, player.Hp, 4);
        _output.WriteLine($"A3 {numbers} → {(hit != null ? $"命中 {hit.Damage}" : "空挥")}（期望 {(expectHit ? "命中" : "空挥")}），"
            + $"玩家 HP={player.Hp:F0}（期望 {expectedHp:F0}）");
    }
}
