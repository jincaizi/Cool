using System;
using KcpServer.Movement;
using Xunit;

namespace KcpServer.Tests.Movement;

/// <summary>
/// ORCA 解算器验收测试。断言只用"素面"物理量（间距、速度模长、位移），不读实现中间量
/// —— 唯一例外是 solver.LastSolveUsedFallback（证实"已重叠"确实走了回退 LP）。
/// 模拟循环（解算 → 按 dt 积分）写在测试里：解算器本身是纯函数，不持有位置状态。
/// </summary>
public class OrcaSolverTests
{
    private const float Dt = 0.05f;      // 仿真步长 = 解算器的 timeStep（仅"已重叠"分支用）
    private const float Tau = 4f;        // 时间视界
    private const float Radius = 0.5f;   // 单个智能体半径（合并半径 = 1.0）
    private const float MaxSpeed = 1.5f; // 最大速度
    private const float CombinedRadius = 2f * Radius;
    private const float PenetrationTolerance = 0.02f; // 允许的穿入容差（离散化 + 浮点）

    // ---------- 测试 1：无邻居恒等（护栏，最重要）----------

    [Fact]
    public void No_Neighbors_Returns_Preferred_Velocity_Bit_Exact()
    {
        var solver = new OrcaSolver(Tau, Dt);
        var self = new OrcaAgent(
            positionX: 3f, positionZ: -7f,
            velocityX: 1.1f, velocityZ: -0.3f,
            radius: Radius, maxSpeed: MaxSpeed,
            preferredVelocityX: 1.2345678f, preferredVelocityZ: -0.7654321f);

        OrcaVelocity v = solver.Solve(self, Array.Empty<OrcaNeighbor>());

        // 逐位相等：无邻居时解算器必须原样返回 v_pref（接线后既有 e2e/布局断言不受影响）
        Assert.Equal(1.2345678f, v.X);
        Assert.Equal(-0.7654321f, v.Z);
        Assert.False(solver.LastSolveUsedFallback);
    }

    [Fact]
    public void No_Neighbors_Clamps_Overspeed_Preferred_Velocity_To_Max_Speed()
    {
        var solver = new OrcaSolver(Tau, Dt);
        var self = new OrcaAgent(
            positionX: 0f, positionZ: 0f,
            velocityX: 0f, velocityZ: 0f,
            radius: Radius, maxSpeed: 2.5f,
            preferredVelocityX: 3f, preferredVelocityZ: 4f); // |v_pref| = 5 > 2.5

        OrcaVelocity v = solver.Solve(self, Array.Empty<OrcaNeighbor>());

        // 被截断到 maxSpeed 圆上：方向不变、模长 = maxSpeed（(3,4) 缩一半 → (1.5,2)，可精确表示）
        Assert.Equal(1.5f, v.X);
        Assert.Equal(2f, v.Z);
    }

    [Fact]
    public void Non_Threatening_Neighbor_Leaves_Preferred_Velocity_Untouched()
    {
        var solver = new OrcaSolver(Tau, Dt);
        var self = new OrcaAgent(
            positionX: 0f, positionZ: 0f,
            velocityX: 1.2345678f, velocityZ: -0.7654321f,
            radius: Radius, maxSpeed: MaxSpeed,
            preferredVelocityX: 1.2345678f, preferredVelocityZ: -0.7654321f);
        // 邻居在 50m 外（远超 τ·|v| = 4×1.45m）→ 约束不该咬住期望速度
        var neighbors = new[]
        {
            new OrcaNeighbor(0f, 50f, 0f, 0f, Radius, OrcaSolver.ReciprocalResponsibility)
        };

        OrcaVelocity v = solver.Solve(self, neighbors);

        Assert.Equal(1.2345678f, v.X);
        Assert.Equal(-0.7654321f, v.Z);
    }

    // ---------- 测试 2：对撞 ----------

    [Fact]
    public void Head_On_Agents_Separate_And_Both_Keep_Moving_Forward()
    {
        var solver = new OrcaSolver(Tau, Dt);
        var a = new Body(x: -5f, z: 0f, vx: MaxSpeed, vz: 0f, prefVx: MaxSpeed, prefVz: 0f);
        var b = new Body(x: 5f, z: 0f, vx: -MaxSpeed, vz: 0f, prefVx: -MaxSpeed, prefVz: 0f);
        var bodies = new[] { a, b };

        SimResult sim = Simulate(bodies, solver, ticks: 240); // 12s，足够走完 10m 的相向行程

        // 全程不得互相穿入（MinGap = 间距 − 合并半径，负值即穿入）
        Assert.True(sim.MinGap >= -PenetrationTolerance,
            "对撞最小间距 " + sim.MinGap + "（< -" + PenetrationTolerance + " 即穿入）");
        // 互相错开：双方都越过了对方的起始位置（不是双双停在原地）
        Assert.True(bodies[0].X > 0f, "A 未越过原点（x=" + bodies[0].X + "）——被顶住了？");
        Assert.True(bodies[1].X < 0f, "B 未越过原点（x=" + bodies[1].X + "）——被顶住了？");
    }

    // ---------- 测试 2b：中间的人被两侧冲锋者挤住（约束集不可行 → linearProgram3 回退）----------

    [Fact]
    public void Squeezed_Agent_Uses_Fallback_And_Is_Not_Run_Over()
    {
        var solver = new OrcaSolver(Tau, Dt);
        // 站着不动的中间人（想停住）+ 左右两个不同速度的冲锋者：两侧的锥互相对顶，
        // 谁都满足不了全部约束 → "已经被挤在中间"，必须走最小化最大违反量的回退分支
        var middle = new Body(x: 0f, z: 0f, vx: 0f, vz: 0f, prefVx: 0f, prefVz: 0f);
        var left = new Body(x: -1.2f, z: 0f, vx: MaxSpeed, vz: 0f, prefVx: MaxSpeed, prefVz: 0f);
        var right = new Body(x: 1.2f, z: 0f, vx: -0.6f, vz: 0f, prefVx: -0.6f, prefVz: 0f);
        var bodies = new[] { middle, left, right };

        SimResult sim = Simulate(bodies, solver, ticks: 240); // 12s

        Assert.True(sim.FallbackEverUsed, "两侧对顶应出现不可行约束（走回退）");
        Assert.True(sim.MinGap >= -PenetrationTolerance, "挤压最小间距 " + sim.MinGap);
        Assert.True(!sim.RePenetratedAfterSeparation, "分开后又撞进去了（抖动）");
        // 回退求解后三人仍是分立的（没有被压成一坨）
        for (int i = 0; i < bodies.Length; i++)
        {
            for (int j = i + 1; j < bodies.Length; j++)
            {
                Assert.True(Distance(bodies[i], bodies[j]) >= CombinedRadius - PenetrationTolerance,
                    "第 " + i + " 与第 " + j + " 个仍叠在一起：距离 " + Distance(bodies[i], bodies[j]));
            }
        }
    }

    // ---------- 测试 3：交叉 ----------

    [Fact]
    public void Crossing_Paths_Do_Not_Overlap_And_Resume_Preferred_Course()
    {
        var solver = new OrcaSolver(Tau, Dt);
        var a = new Body(x: -5f, z: 0f, vx: MaxSpeed, vz: 0f, prefVx: MaxSpeed, prefVz: 0f);   // 沿 +X
        var b = new Body(x: 0f, z: -5f, vx: 0f, vz: MaxSpeed, prefVx: 0f, prefVz: MaxSpeed);   // 沿 +Z
        var bodies = new[] { a, b }; // 两条路径在原点相交，且到达时间相同

        SimResult sim = Simulate(bodies, solver, ticks: 240); // 12s

        Assert.True(sim.MinGap >= -PenetrationTolerance, "交叉最小间距 " + sim.MinGap);
        // 两者都穿过了交叉点（没有卡住/死锁）
        Assert.True(bodies[0].X > 1f && bodies[1].Z > 1f,
            "未通过交叉点：A=(" + bodies[0].X + "," + bodies[0].Z + ") B=(" + bodies[1].X + "," + bodies[1].Z + ")");
        // 远离后约束不再咬住 → 速度回到期望方向附近
        Assert.True(Math.Abs(bodies[0].VX - MaxSpeed) < 0.05f && Math.Abs(bodies[0].VZ) < 0.05f,
            "A 未回到期望方向：v=(" + bodies[0].VX + "," + bodies[0].VZ + ")");
        Assert.True(Math.Abs(bodies[1].VZ - MaxSpeed) < 0.05f && Math.Abs(bodies[1].VX) < 0.05f,
            "B 未回到期望方向：v=(" + bodies[1].VX + "," + bodies[1].VZ + ")");
    }

    // ---------- 测试 4：不参与的静止障碍（责任 1.0）----------

    [Fact]
    public void Stationary_Non_Participating_Obstacle_Is_Never_Entered()
    {
        var solver = new OrcaSolver(Tau, Dt);
        var mover = new Body(x: -5f, z: 0f, vx: MaxSpeed, vz: 0f, prefVx: MaxSpeed, prefVz: 0f);
        var obstacle = new Body(x: 0f, z: 0f, vx: 0f, vz: 0f, prefVx: 0f, prefVz: 0f);
        obstacle.Frozen = true;                                        // 不参与：不会移动
        obstacle.Responsibility = OrcaSolver.FullResponsibility;       // 我们全责（α = 1）
        var bodies = new[] { mover, obstacle };

        SimResult sim = Simulate(bodies, solver, ticks: 200); // 10s

        Assert.True(sim.MinGap >= -PenetrationTolerance, "绕障最小间距 " + sim.MinGap);
        // 每个解出的速度都必须整段 τ 避开障碍（α = 1 = 我们全责：半平面与速度障碍相切）
        Assert.True(sim.WorstFullResponsibilityHorizonGap >= -0.01f,
            "有解出的速度会在 τ 内撞上不参与的障碍：最小视界间距 " + sim.WorstFullResponsibilityHorizonGap);
        // 绕开了（继续前进，而不是顶住或被弹开）
        Assert.True(mover.X > 2f, "没绕过去：(" + mover.X + "," + mover.Z + ")");
    }

    // ---------- 测试 5：已重叠（linearProgram3 回退）----------

    [Fact]
    public void Overlapping_Agents_Separate_Without_Jitter()
    {
        var solver = new OrcaSolver(Tau, Dt);
        // 初始就互相在体内：间距 0.2 < 合并半径 1.0，且都想往反方向走
        var a = new Body(x: 0f, z: 0f, vx: 0f, vz: 0f, prefVx: MaxSpeed, prefVz: 0f);
        var b = new Body(x: 0.2f, z: 0f, vx: 0f, vz: 0f, prefVx: -MaxSpeed, prefVz: 0f);
        var bodies = new[] { a, b };

        // 首帧的约束集不可行（分离所需的相对速度远超 maxSpeed）→ 必须走回退
        var probe = new OrcaAgent(a.X, a.Z, a.VX, a.VZ, Radius, MaxSpeed, a.PrefVX, a.PrefVZ);
        var probeNeighbor = new[]
        {
            new OrcaNeighbor(b.X, b.Z, b.VX, b.VZ, Radius, OrcaSolver.ReciprocalResponsibility)
        };
        solver.Solve(probe, probeNeighbor);
        Assert.True(solver.LastSolveUsedFallback, "已重叠时判定的解应不可行（走 LinearProgram3）");

        SimResult sim = Simulate(bodies, solver, ticks: 120); // 6s

        // 初始重叠是构造出来的（间距 0.2，合并半径 1.0 → 间距 = −0.8）：不得再被加深
        float initialGap = 0.2f - CombinedRadius;
        Assert.True(sim.MinGap >= initialGap - PenetrationTolerance,
            "分离过程中穿入被加深：MinGap " + sim.MinGap + " < 初始 " + initialGap);
        // 分开了（间距回到 ≥ 0 的某一刻）
        Assert.True(sim.MaxGap >= -PenetrationTolerance,
            "始终没分开：全程最大间距 " + sim.MaxGap);
        // 分开之后不得再次穿入（抖动/发散判据）
        Assert.True(!sim.RePenetratedAfterSeparation, "分开后又撞进去了（抖动）");
        // 注：两侧完全共线是最退化的构型（det(p, w) = 0），ORCA 会收敛到"贴着站住"而不是绕过去
        //     （互相贴住、速度趋 0，见下一条测试：偏一点就能正常绕过）。这里只要求真的分开。
    }

    [Fact]
    public void Overlapping_Agents_With_Lateral_Offset_Pass_Each_Other()
    {
        var solver = new OrcaSolver(Tau, Dt);
        // 同样"生在对方体内"，但带一点横向错位（真实刷怪/传送会出现的几何）
        var a = new Body(x: 0f, z: 0f, vx: 0f, vz: 0f, prefVx: MaxSpeed, prefVz: 0f);
        var b = new Body(x: 0.15f, z: 0.12f, vx: 0f, vz: 0f, prefVx: -MaxSpeed, prefVz: 0f);
        var bodies = new[] { a, b };

        SimResult sim = Simulate(bodies, solver, ticks: 240); // 12s

        Assert.True(sim.MinGap >= -PenetrationTolerance - 0.8f, "分离过程中穿入被加深：MinGap " + sim.MinGap);
        Assert.True(sim.MaxGap >= -PenetrationTolerance, "始终没分开：全程最大间距 " + sim.MaxGap);
        Assert.True(!sim.RePenetratedAfterSeparation, "分开后又撞进去了（抖动）");
        // 各自按期望方向通过了对方（不是僵在原地）
        Assert.True(bodies[0].X > bodies[1].X, "A 没越过 B：A.x=" + bodies[0].X + " B.x=" + bodies[1].X);
    }

    // ---------- 测试 6：期望速度为零（互惠让路）----------

    [Fact]
    public void Stationary_Agent_Yields_Something_When_Another_Walks_Into_It()
    {
        var solver = new OrcaSolver(Tau, Dt);
        var stander = new Body(x: 0f, z: 0f, vx: 0f, vz: 0f, prefVx: 0f, prefVz: 0f);            // 想停住
        var walker = new Body(x: -5f, z: 0f, vx: MaxSpeed, vz: 0f, prefVx: MaxSpeed, prefVz: 0f); // 朝它走来
        var bodies = new[] { stander, walker };

        SimResult sim = Simulate(bodies, solver, ticks: 160); // 8s

        Assert.True(sim.MinGap >= -PenetrationTolerance, "最小间距 " + sim.MinGap);
        // 至少一方让开：停着的不可能被白撞（要么被推开、要么来者侧向绕开）
        bool standerMoved = Math.Abs(stander.X) > 0.05f || Math.Abs(stander.Z) > 0.05f;
        bool walkerSwerved = Math.Abs(walker.Z) > 0.05f;
        Assert.True(standerMoved || walkerSwerved,
            "两人都没让（stander=(" + stander.X + "," + stander.Z + "), walker z=" + walker.Z + "）");
    }

    // ---------- 测试 7：速度上限 ----------

    [Fact]
    public void Solved_Speed_Never_Exceeds_Max_Speed()
    {
        var solver = new OrcaSolver(Tau, Dt);
        // 混合场景：对撞 + 交叉 + 重叠 + 静止障碍挤在一起
        var a = new Body(x: -3f, z: 0f, vx: MaxSpeed, vz: 0f, prefVx: MaxSpeed, prefVz: 0f);
        var b = new Body(x: 3f, z: 0f, vx: -MaxSpeed, vz: 0f, prefVx: -MaxSpeed, prefVz: 0f);
        var c = new Body(x: 0f, z: -3f, vx: 0f, vz: MaxSpeed, prefVx: 0f, prefVz: MaxSpeed);
        var d = new Body(x: 0.1f, z: 0.1f, vx: 0f, vz: 0f, prefVx: 0f, prefVz: -MaxSpeed); // 与 a 重叠
        var obstacle = new Body(x: 0f, z: 3f, vx: 0f, vz: 0f, prefVx: 0f, prefVz: 0f);
        obstacle.Frozen = true;
        obstacle.Responsibility = OrcaSolver.FullResponsibility;
        var bodies = new[] { a, b, c, d, obstacle };

        SimResult sim = Simulate(bodies, solver, ticks: 300);

        Assert.True(sim.MaxSpeedSeen <= MaxSpeed + 1e-4f,
            "解出的速度超过 maxSpeed：" + sim.MaxSpeedSeen);
        foreach (var body in bodies)
            Assert.True(!float.IsNaN(body.X) && !float.IsNaN(body.Z) && !float.IsNaN(body.VX) && !float.IsNaN(body.VZ),
                "出现 NaN（速度或位置）");
    }

    // ---------- 退化：位置完全重合且相对速度为零 ----------

    [Fact]
    public void Fully_Coincident_Agents_Do_Not_Produce_NaN()
    {
        var solver = new OrcaSolver(Tau, Dt);
        // 完全重合 + 相对速度为零：几何上不存在"该往哪边让"的答案。要求不编造方向、也不产生 NaN
        var self = new OrcaAgent(1f, 2f, 0.3f, 0.4f, Radius, MaxSpeed, 0.5f, 0f);
        var neighbors = new[]
        {
            new OrcaNeighbor(1f, 2f, 0.3f, 0.4f, Radius, OrcaSolver.ReciprocalResponsibility)
        };

        OrcaVelocity v = solver.Solve(self, neighbors);

        Assert.False(float.IsNaN(v.X) || float.IsNaN(v.Z), "退化输入产生了 NaN");
        float speed = (float)Math.Sqrt(v.X * v.X + v.Z * v.Z);
        Assert.True(speed <= MaxSpeed + 1e-4f, "退化输入速度越界：" + speed);
        // 该邻居不产生约束 → 与无邻居同等：逐位返回 v_pref
        Assert.Equal(self.PreferredVelocityX, v.X);
        Assert.Equal(self.PreferredVelocityZ, v.Z);
    }

    // ---------- 确定性 ----------

    [Fact]
    public void Solve_Is_Deterministic_For_Identical_Input()
    {
        var self = new OrcaAgent(0f, 0f, 1f, 0.5f, Radius, MaxSpeed, 1.2f, 0.3f);
        var neighbors = new[]
        {
            new OrcaNeighbor(-1f, 1f, 0.5f, -0.2f, Radius, OrcaSolver.ReciprocalResponsibility),
            new OrcaNeighbor(1f, 1f, -0.5f, -0.3f, Radius, OrcaSolver.ReciprocalResponsibility),
            new OrcaNeighbor(0f, -1f, 0f, 0.6f, Radius, OrcaSolver.FullResponsibility)
        };

        OrcaVelocity first = new OrcaSolver(Tau, Dt).Solve(self, neighbors);
        OrcaVelocity second = new OrcaSolver(Tau, Dt).Solve(self, neighbors);

        Assert.Equal(first.X, second.X);
        Assert.Equal(first.Z, second.Z);
    }

    // ---------- 仿真脚手架 ----------

    /// <summary>仿真体：位置/速度/期望速度 + 半径/MaxSpeed + 对他人承担的责任 α</summary>
    private sealed class Body
    {
        public float X;
        public float Z;
        public float VX;
        public float VZ;
        public readonly float PrefVX;
        public readonly float PrefVZ;
        public readonly float Radius;
        public readonly float MaxSpeed;
        /// <summary>自身对这个邻居承担的责任 α（解算时写进 OrcaNeighbor.Responsibility）</summary>
        public float Responsibility = OrcaSolver.ReciprocalResponsibility;
        /// <summary>冻结体（不参与解算、不移动）——用于"不参与的静止障碍"</summary>
        public bool Frozen;

        public Body(float x, float z, float vx, float vz, float prefVx, float prefVz,
            float radius = OrcaSolverTests.Radius, float maxSpeed = OrcaSolverTests.MaxSpeed)
        {
            X = x; Z = z; VX = vx; VZ = vz;
            PrefVX = prefVx; PrefVZ = prefVz;
            Radius = radius; MaxSpeed = maxSpeed;
        }
    }

    private struct SimResult
    {
        public float MinGap;                 // 全程最小间距（= 距离 − 半径和，负值 = 穿入）
        public float MaxGap;                 // 全程最大间距
        public bool RePenetratedAfterSeparation; // 一旦分开过，之后是否又穿入（抖动/发散判据）
        public float MaxSpeedSeen;           // 全程解出的最大速度模长
        public bool FallbackEverUsed;        // 全程是否出现过"约束集不可行"的回退求解
        /// <summary>解出速度对"不参与邻居（α=1）"的 τ 视界最小间距：α=1 时 ORCA 保证 ≥ 0</summary>
        public float WorstFullResponsibilityHorizonGap;
    }

    /// <summary>
    /// 推进 ticks 个 tick：每 tick 内所有非冻结体按**当前**状态异步解算，再按 dt 积分
    /// （拆两个半步以采样 tick 中点的间距）。
    /// </summary>
    private static SimResult Simulate(Body[] bodies, OrcaSolver solver, int ticks)
    {
        var result = new SimResult
        {
            MinGap = float.PositiveInfinity,
            MaxGap = float.NegativeInfinity,
            MaxSpeedSeen = 0f,
            WorstFullResponsibilityHorizonGap = float.PositiveInfinity
        };
        bool separated = false;

        var newVX = new float[bodies.Length];
        var newVZ = new float[bodies.Length];
        var neighbors = new OrcaNeighbor[bodies.Length];

        for (int t = 0; t < ticks; t++)
        {
            Record(ref result, ref separated, MinGap(bodies));

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].Frozen) { newVX[i] = 0f; newVZ[i] = 0f; continue; }

                int count = 0;
                for (int j = 0; j < bodies.Length; j++)
                {
                    if (j == i) continue;
                    neighbors[count++] = new OrcaNeighbor(
                        bodies[j].X, bodies[j].Z, bodies[j].VX, bodies[j].VZ,
                        bodies[j].Radius, bodies[j].Responsibility);
                }

                var self = new OrcaAgent(
                    bodies[i].X, bodies[i].Z, bodies[i].VX, bodies[i].VZ,
                    bodies[i].Radius, bodies[i].MaxSpeed, bodies[i].PrefVX, bodies[i].PrefVZ);

                OrcaVelocity v = solver.Solve(self, new ReadOnlySpan<OrcaNeighbor>(neighbors, 0, count));
                newVX[i] = v.X;
                newVZ[i] = v.Z;
                if (solver.LastSolveUsedFallback) result.FallbackEverUsed = true;

                float speed = (float)Math.Sqrt(v.X * v.X + v.Z * v.Z);
                if (speed > result.MaxSpeedSeen) result.MaxSpeedSeen = speed;
            }

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].Frozen) continue;
                Advance(bodies[i], newVX[i], newVZ[i], 0.5f * Dt);
            }
            Record(ref result, ref separated, MinGap(bodies));

            // 行为断言：α = 1（对方不参与）时，每个解出的速度都要在 τ 视界内避开冻结障碍，
            // 与实现无关（直接把速度代入相对轨迹采样）
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].Frozen) continue;
                for (int j = 0; j < bodies.Length; j++)
                {
                    if (!bodies[j].Frozen) continue;
                    if (bodies[j].Responsibility < OrcaSolver.FullResponsibility) continue;
                    float horizonGap = HorizonGap(bodies[i], newVX[i], newVZ[i], bodies[j], solver.TimeHorizon);
                    if (horizonGap < result.WorstFullResponsibilityHorizonGap)
                        result.WorstFullResponsibilityHorizonGap = horizonGap;
                }
            }

            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].Frozen) continue;
                Advance(bodies[i], newVX[i], newVZ[i], 0.5f * Dt);
            }
        }

        Record(ref result, ref separated, MinGap(bodies));
        return result;
    }

    private static void Record(ref SimResult result, ref bool separated, float gap)
    {
        if (gap < result.MinGap) result.MinGap = gap;
        if (gap > result.MaxGap) result.MaxGap = gap;
        if (gap >= -PenetrationTolerance) separated = true;
        else if (separated) result.RePenetratedAfterSeparation = true;
    }

    /// <summary>
    /// 假定解出的速度在 τ 内不变，采样"移动体 vs 冻结障碍"的相对轨迹，返回最小间距。
    /// α = 1 时 ORCA 的半平面与速度障碍相切，故这个值必须 ≥ 0（与实现无关的行为断言）。
    /// </summary>
    private static float HorizonGap(Body mover, float vx, float vz, Body obstacle, float timeHorizon)
    {
        const int samples = 200;
        float worst = float.PositiveInfinity;
        for (int s = 0; s <= samples; s++)
        {
            float t = timeHorizon * s / samples;
            // 相对位置 p(t) = p_obstacle − p_mover − v_rel·t（v_rel = 移动体速度，障碍冻结）
            float dx = obstacle.X - mover.X - vx * t;
            float dz = obstacle.Z - mover.Z - vz * t;
            float gap = (float)Math.Sqrt(dx * dx + dz * dz) - mover.Radius - obstacle.Radius;
            if (gap < worst) worst = gap;
        }
        return worst;
    }

    private static void Advance(Body body, float vx, float vz, float dt)
    {
        body.X += vx * dt;
        body.Z += vz * dt;
        body.VX = vx;
        body.VZ = vz;
    }

    private static float Distance(Body a, Body b)
    {
        float dx = b.X - a.X;
        float dz = b.Z - a.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static float MinGap(Body[] bodies)
    {
        float minGap = float.PositiveInfinity;
        for (int i = 0; i < bodies.Length; i++)
        {
            for (int j = i + 1; j < bodies.Length; j++)
            {
                float gap = Distance(bodies[i], bodies[j]) - bodies[i].Radius - bodies[j].Radius;
                if (gap < minGap) minGap = gap;
            }
        }
        return minGap;
    }
}
