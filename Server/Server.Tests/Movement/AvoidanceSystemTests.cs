using System;
using Cool.Protocol;
using KcpServer.Monster;
using KcpServer.Movement;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// 2026-10-13：ORCA 避让的**接线**验收（解算器本身在 OrcaSolverTests 里，本文件只测接进来之后的行为）。
///
/// 五条护栏（对应接线任务 §2 的硬要求）：
///   1. 无邻居 ⇒ 与接线前**逐位相同**（怪物 / 机器人两条路径各一条用例）；
///   2. 收敛不重叠（多体奔向同一点：既不穿模，也不卡死）；
///   3. 玩家不被服务端推动、也不会被智能体穿过去（α = 1 全责）；
///   4. 攻击目标要排除（追击的怪得进得了 attackRange，但不能撞开挡在中间的路人）；
///   5. --no-avoid 等价于接线前（负对照真的改变了行为，活体对照才有意义）。
/// </summary>
public class AvoidanceSystemTests
{
    private const float Dt = 0.066f;              // 主循环 tick（MonsterModule.TickDeltaSeconds）
    private const float BotRadius = BotRunner.BotRadius;
    private const float BotSpeed = BotRunner.BotSpeedPerTick;

    private static (EntityStore Store, MonsterModule Module) CreateModule()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        return (store, module);
    }

    /// <summary>建一个只看得见"传入 store"的避让系统（半径表按模板 id 查，模板表在模块里）</summary>
    private static AvoidanceSystem CreateAvoidance(EntityStore store, MonsterModule? module = null)
        => new AvoidanceSystem(store,
            module != null ? module.RadiusOfTemplate : (Func<int, float>)(_ => AvoidanceSystem.DefaultMonsterRadius));

    // ------------------------------------------------------------------
    // 1. 无邻居 ⇒ 逐位不变
    // ------------------------------------------------------------------

    [Fact]
    public void No_Neighbors_Monster_Movement_Is_Bit_For_Bit_The_Unwired_Formula()
    {
        var (store, module) = CreateModule();
        long id = module.SpawnMonster(3, new Vector3(-20f, 0f, 20f), new Vector3(-20f, 0f, 20f));
        var state = module.GetState(id)!;
        var mover = new SimpleMoveSystem(CreateAvoidance(store, module));

        var target = new Vector3(120f, 0f, -60f);   // 直线走，全程离任何别人都很远（世界里也没有别人）
        var reference = state.Position;             // 接线前的积分（逐字抄旧 SimpleMoveSystem.MoveTo 的算式）

        for (int tick = 0; tick < 300; tick++)
        {
            Vector3 dir = target - reference;
            dir.Y = 0;
            dir = dir.Normalize();
            reference = reference + dir * state.Config.moveSpeed * Dt;

            mover.MoveTo(state, target, Dt);

            Assert.Equal(reference.X, state.Position.X);   // float 精确相等（不是容差比较）
            Assert.Equal(reference.Z, state.Position.Z);
        }
    }

    [Fact]
    public void No_Neighbors_Bot_Movement_Is_Bit_For_Bit_The_Unwired_Runner()
    {
        // 同一颗随机种子跑两遍：A 组不接避让（= 接线前那条路径），B 组接了但世界里只有自己 ⇒ 必须逐位相同
        long nextA = 1, nextB = 1;
        var storeA = new EntityStore();
        var runnerA = new BotRunner(storeA, () => nextA++, seed: 4242);
        var storeB = new EntityStore();
        var avoidance = CreateAvoidance(storeB);
        var runnerB = new BotRunner(storeB, () => nextB++, seed: 4242, avoidance: avoidance);

        runnerA.Spawn(5);
        runnerB.Spawn(5);

        for (uint tick = 0; tick < 200; tick++)
        {
            avoidance.EnsureSnapshot(tick);       // Server3C 主循环 tick 起始处的同款调用顺序
            if (tick == 0) Assert.Equal(5, avoidance.AgentCount);   // 接线是活的（否则本用例会"空转"假绿）
            runnerA.Tick(tick);
            runnerB.Tick(tick);
        }

        foreach (var a in storeA.All)
        {
            var b = storeB.GetPlayer(a.PlayerId)!;
            Assert.Equal(a.Position.X, b.Position.X);
            Assert.Equal(a.Position.Z, b.Position.Z);
        }
    }

    // ------------------------------------------------------------------
    // 2. 收敛不重叠
    // ------------------------------------------------------------------

    [Fact]
    public void Converging_Agents_Do_Not_Overlap_And_Still_Get_To_The_Point()
    {
        var store = new EntityStore();
        var avoidance = CreateAvoidance(store);
        const float radius = 0.5f;
        const float step = 0.132f;                  // 2 m/s（怪物量级）
        var spots = new[] { (10f, 0f), (-10f, 0f), (0f, 10f), (0f, -10f) };   // 四方向奔向同一点
        var ids = new long[spots.Length];
        var positions = new Vector3[spots.Length];
        var velocities = new Vector3[spots.Length];

        for (int i = 0; i < spots.Length; i++)
        {
            ids[i] = 100 + i;
            positions[i] = new Vector3(spots[i].Item1, 0f, spots[i].Item2);
            var e = store.AddPlayer(ids[i], "Agent" + i);
            e.Kind = (byte)EntityKind.PlayerBot;
            e.UpdatePosition(positions[i], 0f, step / Dt, 1L, 0U);
        }

        float minDistance = float.MaxValue;
        for (uint tick = 0; tick < 400; tick++)
        {
            // 每 tick 与生产同序：先拍快照 → 逐个解算 → 就地应用 → flush 回 store（下一 tick 的快照用）
            avoidance.EnsureSnapshot(tick);
            for (int i = 0; i < ids.Length; i++)
            {
                Vector3 toTarget = new Vector3(-positions[i].X, 0f, -positions[i].Z).Normalize();
                Vector3 preferred = new Vector3(toTarget.X * step, 0f, toTarget.Z * step);
                Vector3 solved = avoidance.Solve(ids[i], null, positions[i], velocities[i], preferred, radius, step);
                velocities[i] = solved;
                positions[i] = positions[i] + solved;
                store.GetPlayer(ids[i])!.UpdatePosition(positions[i], YawOf(solved), step / Dt, tick + 2, 0U);
            }

            for (int i = 0; i < positions.Length; i++)
                for (int j = i + 1; j < positions.Length; j++)
                    minDistance = Math.Min(minDistance, Distance(positions[i], positions[j]));
        }

        // 不重叠：最小间距 ≥ 半径和 − 容差。
        // 容差取 0.1（半径和的 10%），比算法单测里的 0.02 松——这是**四体对称同点**这种最难的构型：
        // 四个人恰好对称地挤到一起时会绕成一个漩涡（实测 400 tick 都在动、没有僵住），
        // 全程最小间距实测 0.9497（漩涡后期，tick 210），即最多 5% 的视觉体交叠；
        // 收敛/挤压那一段（前 120 tick）一直 ≥ 0.99。判别力仍然充足：关掉避让是**对穿**（实测 0.34），
        // 真正卡死/叠住会掉到 0 附近（OrcaSolverTests 的变异实验量级 −0.5 ~ −1.0）。
        Assert.True(minDistance >= radius * 2f - 0.1f,
            $"四体奔向同一点时出现了穿模：最小间距 {minDistance:F4} < {radius * 2f - 0.1f:F4}");

        // 没卡死：都到点了（环绕/让开之后仍收敛在原点附近），且都还在动
        foreach (var p in positions)
        {
            float d = Distance(p, new Vector3(0f, 0f, 0f));
            Assert.True(d <= 3f, $"智能体被卡住了：末位置距目标点 {d:F2}m（应回到 3m 内）");
        }
        foreach (var v in velocities)
            Assert.True(v.X * v.X + v.Z * v.Z > 0.01f * 0.01f, "末 tick 速度为零——被挤停了");
    }

    // ------------------------------------------------------------------
    // 3. 玩家：不被推动，也不被穿过去
    // ------------------------------------------------------------------

    [Fact]
    public void Player_Is_Never_Moved_Server_Side_And_Never_Penetrated()
    {
        var store = new EntityStore();
        var avoidance = CreateAvoidance(store);
        const float step = BotSpeed;               // 机器人量级（8m/s）——最难的迎面场景

        var player = store.AddPlayer(1, "P1");
        player.IsLoggedIn = true;
        player.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);
        var playerStart = player.Position;

        // 机器人从 -Z 侧正面冲向 +Z 侧，路径穿过玩家
        var bot = store.AddPlayer(2, "Bot");
        bot.Kind = (byte)EntityKind.PlayerBot;
        var botPos = new Vector3(0f, 0f, -8f);
        bot.UpdatePosition(botPos, 0f, step / Dt, 1L, 0U);
        var botVelocity = new Vector3(0f, 0f, 0f);

        float minDistance = float.MaxValue;
        float worstHorizon = float.MaxValue;

        for (uint tick = 0; tick < 200; tick++)
        {
            avoidance.EnsureSnapshot(tick);

            Vector3 preferred = new Vector3(0f, 0f, step);   // 一直朝 +Z
            Vector3 solveFrom = botPos;                      // 解算时（移动前）的位置——见下面的视界注释
            Vector3 solved = avoidance.Solve(2, null, botPos, botVelocity, preferred, BotRadius, step);
            botVelocity = solved;
            botPos = botPos + solved;
            bot.UpdatePosition(botPos, YawOf(solved), step / Dt, tick + 2, 0U);

            minDistance = Math.Min(minDistance, Distance(botPos, player.Position));

            // 「α = 1 全责」的可证伪断言：**本 tick 解出的速度**在 τ 内不会撞上这个不动的邻居。
            // 只看轨迹会被下一 tick 的解算纠回来（上一轮审查的实测教训），所以必须逐 tick 看速度。
            // 视界从**解算时的位置**起算（不是移动后的位置）：ORCA 的保证是"以这个速度、从这个位置
            // 走 τ，不会撞"，下一 tick 会重新解算并重新建立不变式。用移动后的位置去量等于把保证
            // 推迟了一格——实测那样量会看到 0.512 的"假违反"（解算器其实恰好相切）。
            worstHorizon = Math.Min(worstHorizon,
                RayDiscMinDistance(solveFrom, solved, player.Position, avoidance.TimeHorizonTicks));
        }

        // 玩家位置逐位不变：服务端只接受上报位置（D-N5），避让**不许**顺手推玩家
        Assert.Equal(playerStart.X, player.Position.X);
        Assert.Equal(playerStart.Z, player.Position.Z);

        // 轨迹不穿模（半径和 0.5 + 0.5）
        Assert.True(minDistance >= 1f - 0.02f,
            $"机器人从玩家身上穿过去了：最小间距 {minDistance:F4} < 0.98");

        // 且每个 tick 解出的速度在 τ 内都留出了半径和（α=1 的语义；责任权重写成 0.5 这条就红）
        Assert.True(worstHorizon >= 1f - 0.01f,
            $"解出的速度会在 τ 内撞上不参与的玩家：最小视界间距 {worstHorizon:F4}");
    }

    // ------------------------------------------------------------------
    // 4. 攻击目标排除
    // ------------------------------------------------------------------

    [Fact]
    public void Chasing_Monster_Reaches_Its_Target_But_Goes_Around_A_Bystander()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long wolfId = module.SpawnMonster(2, new Vector3(12f, 0f, 0f), new Vector3(12f, 0f, 0f)); // Wolf：检测 15m
        var wolf = module.GetState(wolfId)!;

        var target = store.AddPlayer(1, "Target");      // 原点——Wolf 开局即感知（15 ≥ 12）
        target.IsLoggedIn = true;
        target.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);

        var bystander = store.AddPlayer(2, "Bystander"); // 直线上挡着一个机器人（不参与避让的活体障碍）
        bystander.Kind = (byte)EntityKind.PlayerBot;
        bystander.UpdatePosition(new Vector3(6f, 0f, 0f), 0f, 0f, 1L, 0U);

        float worstGap = float.MaxValue;
        for (uint tick = 0; tick < 400; tick++)
        {
            module.Tick(tick);
            worstGap = Math.Min(worstGap, Distance(wolf.Position, bystander.Position));
        }

        // 目标排除：追得到（不排除的话怪会绕着目标转，永远进不了 attackRange）——
        // 这条同时被既有 MonsterModuleTests.Detection_Aggro_And_Attack_Queue_Targets_Closest_Player 钉着
        float distToTarget = Distance(wolf.Position, target.Position);
        Assert.True(distToTarget <= wolf.Config.attackRange + 0.2f,
            $"追击目标没进攻击距离：{distToTarget:F3}m > {wolf.Config.attackRange}");

        // 路人照绕：半径和 0.5 + 0.5 = 1.0（这条证明"排除"只针对当前目标，不是把所有人都当空气）
        Assert.True(worstGap >= 1f - 0.02f, $"怪从路人身上穿过去了：最小间距 {worstGap:F4}");
    }

    [Fact]
    public void Chase_Ignores_Its_Own_Target_And_Therefore_Never_Brakes()
    {
        var store = new EntityStore();
        long seed = MonsterModule.NpcIdSeed;
        var module = new MonsterModule(store, () => ++seed);
        long wolfId = module.SpawnMonster(2, new Vector3(12f, 0f, 0f), new Vector3(12f, 0f, 0f)); // 3.5m/s
        var wolf = module.GetState(wolfId)!;

        var target = store.AddPlayer(1, "Target");
        target.IsLoggedIn = true;
        target.UpdatePosition(new Vector3(0f, 0f, 0f), 0f, 0f, 1L, 0U);

        float fullStep = wolf.Config.moveSpeed * Dt;
        float slowestChaseStep = float.MaxValue;

        for (uint tick = 0; tick < 120; tick++)
        {
            module.Tick(tick);
            if (Distance(wolf.Position, target.Position) <= wolf.Config.attackRange) break;   // 进攻击距离就不再走
            float step = (float)Math.Sqrt(wolf.CurrentVelocity.X * wolf.CurrentVelocity.X
                                          + wolf.CurrentVelocity.Z * wolf.CurrentVelocity.Z);
            slowestChaseStep = Math.Min(slowestChaseStep, step);
        }

        // 目标排除的可证伪断言：追**自己的目标**时速度全程 = 配置速度。
        // 不排除的话怪会半责地"让开"自己的目标（α=0.5）：实测在 ~4.7m 处开始刹车
        // （每 tick 位移从 0.231 掉到 0.12），到达攻击距离从 43 tick 拖到 49 tick。
        // 注意**不能**只断言"进得了 attackRange"——所有模板的攻击距离（1.5~2.5）都大于半径和
        // （1.0~2.16），不排除也进得去；退化只体现在"追击途中减速/绕行"上。
        Assert.True(slowestChaseStep >= fullStep * 0.99f,
            $"追击被自己的目标拖慢了：最慢一步 {slowestChaseStep:F4} < 满速 {fullStep:F4}（目标没被排除）");
    }

    // ------------------------------------------------------------------
    // 5. --no-avoid 负对照
    // ------------------------------------------------------------------

    [Fact]
    public void Disabled_Avoidance_Lets_Agents_Pass_Through_Each_Other()
    {
        var store = new EntityStore();
        var avoidance = CreateAvoidance(store);
        avoidance.Enabled = false;                  // 等价于 CLI --no-avoid

        float minDistance = RunHeadOnPair(store, avoidance, out _);

        // 负对照必须**真的**不一样：关了避让就该穿模（否则活体对照的 A/B 两组数字没有区分力）。
        // 阈值 0.5（半径和 1.0 的一半）：两人沿同一条直线相向而行，tick 采样只在积分点上取，
        // 交叉那一刻落在两个 tick 之间——实测最近采样点是 0.34（接近 0 但不会正好是 0），
        // 而开了避让是 ≥ 1.0，两者差得很开。
        Assert.True(minDistance < 0.5f,
            $"关掉避让后两个智能体本该径直对穿，实测最小间距 {minDistance:F4}——说明开关没接上");
    }

    [Fact]
    public void Enabled_Avoidance_Keeps_The_Same_Head_On_Pair_Apart()
    {
        var store = new EntityStore();
        var avoidance = CreateAvoidance(store);
        float minDistance = RunHeadOnPair(store, avoidance, out _);

        Assert.True(minDistance >= 1f - 0.02f, $"迎面二人组出现了穿模：最小间距 {minDistance:F4} < 0.98");
    }

    /// <summary>两个机器人从两侧对穿（半径和 1.0），返回全程最小间距</summary>
    private static float RunHeadOnPair(EntityStore store, AvoidanceSystem avoidance, out float endGap)
    {
        var a = store.AddPlayer(11, "A");
        var b = store.AddPlayer(12, "B");
        a.Kind = (byte)EntityKind.PlayerBot;
        b.Kind = (byte)EntityKind.PlayerBot;
        var pa = new Vector3(-6f, 0f, 0f);
        var pb = new Vector3(6f, 0f, 0f);
        a.UpdatePosition(pa, YawOf(new Vector3(BotSpeed, 0f, 0f)), BotSpeed / Dt, 1L, 0U);
        b.UpdatePosition(pb, YawOf(new Vector3(-BotSpeed, 0f, 0f)), BotSpeed / Dt, 1L, 0U);
        var va = new Vector3(0f, 0f, 0f);
        var vb = new Vector3(0f, 0f, 0f);

        float min = float.MaxValue;
        for (uint tick = 0; tick < 200; tick++)
        {
            avoidance.EnsureSnapshot(tick);
            va = avoidance.Solve(11, null, pa, va, new Vector3(BotSpeed, 0f, 0f), BotRadius, BotSpeed);
            vb = avoidance.Solve(12, null, pb, vb, new Vector3(-BotSpeed, 0f, 0f), BotRadius, BotSpeed);
            pa = pa + va;
            pb = pb + vb;
            a.UpdatePosition(pa, YawOf(va), BotSpeed / Dt, tick + 2, 0U);
            b.UpdatePosition(pb, YawOf(vb), BotSpeed / Dt, tick + 2, 0U);
            min = Math.Min(min, Distance(pa, pb));
        }
        endGap = Distance(pa, pb);
        return min;
    }

    // ------------------------------------------------------------------

    private static float Distance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>朝向（度）——与生产侧 flush 同口径（yaw=0 → +Z；BotRunner.YawOf / MonsterModule.YawOf）。
    /// **测试自己驱动智能体时必须按真实朝向 flush**：快照的速度是 <c>Speed × (sin yaw, cos yaw)</c> 还原的，
    /// 塞 yaw=0 会把一个朝 +X 走的人报成"朝 +Z 走"（避让就按错的方向让——调试时踩过）。</summary>
    private static float YawOf(Vector3 velocity)
        => (float)(Math.Atan2(velocity.X, velocity.Z) * 180.0 / Math.PI);

    /// <summary>
    /// 从 <paramref name="from"/> 沿 <paramref name="velocity"/>（米/tick）走 τ tick，
    /// 与不动点 <paramref name="disc"/> 的**最小**距离（负 = 穿进了点里）。
    /// 就是"解出的速度在时间视界内会不会撞上不动的邻居"的可证伪度量。
    /// </summary>
    private static float RayDiscMinDistance(Vector3 from, Vector3 velocity, Vector3 disc, float horizonTicks)
    {
        float px = disc.X - from.X, pz = disc.Z - from.Z;
        float vx = velocity.X, vz = velocity.Z;
        float vv = vx * vx + vz * vz;
        float t = vv > 1e-12f ? (px * vx + pz * vz) / vv : 0f;   // 最近点参数
        if (t < 0f) t = 0f;
        else if (t > horizonTicks) t = horizonTicks;
        float dx = px - vx * t, dz = pz - vz * t;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }
}
