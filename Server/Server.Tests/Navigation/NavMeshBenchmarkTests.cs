using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using KcpServer;
using KcpServer.Navigation;
using Xunit;
using Xunit.Abstractions;

namespace KcpServer.Tests.Navigation;

/// <summary>C1 验收：200 次查询的耗时分布 + 200 怪预算 soak。阈值刻意宽松（CI/调试构建下
/// 仍要稳定通过），具体数字以输出为准并记录在 Server/Navigation/README.md。</summary>
public class NavMeshBenchmarkTests
{
    private readonly ITestOutputHelper _out;
    public NavMeshBenchmarkTests(ITestOutputHelper output) => _out = output;

    private static string NavBinPath => Path.Combine(AppContext.BaseDirectory, "Config", "nav", "demoday.navbin");

    [Fact]
    public void Two_Hundred_Queries_And_Budget_Soak()
    {
        NavData data = NavDataFormat.Read(NavBinPath);
        var graph = NavMeshGraph.FromData(data);
        var finder = new NavMeshPathfinder(graph);
        var service = new NavigationService(graph, "demoday")
        {
            MaxQueriesPerTick = 16,
            MaxMillisecondsPerTick = 25
        };

        List<Vector3> points = SamplePoints(graph);
        Assert.True(points.Count >= 20, $"only {points.Count} on-mesh sample points");

        // 预热（JIT/缓存），不计入统计
        for (int i = 0; i < 20; i++)
            finder.FindPath(points[i % points.Count], points[(i * 7 + 3) % points.Count], 0.5f);

        var queries = BuildQueries(points, count: 200);
        var timings = new List<double>(queries.Count);
        int success = 0, blocked = 0;
        double maxMs = 0;
        foreach (var (start, goal, radius) in queries)
        {
            long begin = Stopwatch.GetTimestamp();
            NavPath path = finder.FindPath(start, goal, radius);
            double ms = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
            timings.Add(ms);
            if (ms > maxMs) maxMs = ms;
            if (path.Success) success++;
            else if (path.FailReason is "blocked" or "too-complex") blocked++;
        }

        double avg = Average(timings);
        _out.WriteLine($"QUERY n={timings.Count} success={success} blocked={blocked} avg={avg:F2}ms max={maxMs:F2}ms p95={Percentile(timings, 0.95):F2}ms");
        var byBucket = new Dictionary<int, List<double>>();
        for (int i = 0; i < timings.Count; i++)
        {
            int bucket = i % 4;
            if (!byBucket.TryGetValue(bucket, out var list)) byBucket[bucket] = list = new List<double>();
            list.Add(timings[i]);
        }
        foreach (var kv in byBucket)
        {
            float target = new[] { 5f, 10f, 20f, 35f }[kv.Key];
            _out.WriteLine($"  bucket {target}m: avg={Average(kv.Value):F2}ms max={Max(kv.Value):F2}ms");
        }
        int slowest = 0;
        for (int i = 1; i < timings.Count; i++) if (timings[i] > timings[slowest]) slowest = i;
        var sq = queries[slowest];
        _out.WriteLine($"  slowest #{slowest} r={sq.Radius} d={DistanceXZ(sq.Start, sq.Goal):F1}m t={timings[slowest]:F2}ms " +
            $"start=({sq.Start.X:F1},{sq.Start.Z:F1}) goal=({sq.Goal.X:F1},{sq.Goal.Z:F1})");
        Assert.True(avg < 50.0, $"average query {avg:F2}ms is unreasonably slow");

        // 最慢查询在预算内重试：deadline 中止后 portal 缓存保留，重试应能在有限次内完成
        // （否则预算会把这条路径永久饿死——必须验证不会 livelock）。
        var slowQuery = queries[slowest];
        NavPath slowPath = null!;
        int attempts = 0;
        for (int i = 0; i < 50; i++)
        {
            service.BeginTick(1000 + (uint)i);
            attempts++;
            if (service.TryFindPath(new NavQuery("demoday", slowQuery.Start, slowQuery.Goal, slowQuery.Radius), out slowPath))
                break;
            if (slowPath.FailReason != "budget") break;
        }
        _out.WriteLine($"SLOW-RETRY attempts={attempts} success={slowPath.Success} reason={slowPath.FailReason}");
        Assert.True(slowPath.Success || slowPath.FailReason != "budget",
            $"slow query still budget-blocked after {attempts} attempts (livelock)");

        // 200 怪预算 soak：每 tick 最多 16 次（spec §4.3），20 个 tick。
        // 断言：单 tick 实际耗时 ≤ 时间预算 + 一次查询的余量（预算在查询前检查，
        // 所以最坏情况是"最后一个放行的查询"跑完）。同时必须看到预算生效（有拒绝）。
        var tickTimes = new List<double>();
        int totalAttempts = 0, totalQueries = 0;
        for (uint tick = 0; tick < 20; tick++)
        {
            service.BeginTick(tick);
            long begin = Stopwatch.GetTimestamp();
            // 每 tick 32 次尝试 > MaxQueriesPerTick=16：后一半必须被预算拒绝（次数闸门），
            // 否则“预算生效”的断言会随查询分布时快时慢而抖动。
            for (int i = 0; i < 32; i++)
            {
                var (start, goal, radius) = queries[(int)((tick * 32 + (uint)i) % queries.Count)];
                service.TryFindPath(new NavQuery("demoday", start, goal, radius), out _);
                totalAttempts++;
            }
            double ms = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
            tickTimes.Add(ms);
        }
        totalQueries = (int)service.QueryCount;
        double tickAvg = Average(tickTimes);
        double tickMax = 0;
        foreach (double ms in tickTimes) if (ms > tickMax) tickMax = ms;
        _out.WriteLine($"BUDGET ticks=20 avgTick={tickAvg:F2}ms maxTick={tickMax:F2}ms queries={totalQueries}/{totalAttempts} " +
            $"rejections={service.BudgetRejections} lastTick={service.LastTickMilliseconds:F2}ms");

        // 单次查询内部也带同一预算（deadline 检查在 A* 每次扩展/每 16 个校验采样），
        // 所以 tick 超调只来自一次 portal clearance 级别的检查粒度，实测 < 5ms。
        Assert.True(tickMax <= 40.0, $"tick {tickMax:F2}ms exceeds 25ms budget + check granularity");
        Assert.True(service.BudgetRejections > 0, "expected at least one budget rejection over 320 attempts");
    }

    /// <summary>200 条查询：5/10/20/35m 四档距离 × 50 条，半径按模板循环
    /// （Wolf 0.5 / DogKnight 0.88 / SpiderBoss 1.74）。</summary>
    private static List<(Vector3 Start, Vector3 Goal, float Radius)> BuildQueries(List<Vector3> points, int count)
    {
        float[] radii = { 0.5f, 0.5f, 0.88f, 1.74f };
        float[] targets = { 5f, 10f, 20f, 35f };
        var queries = new List<(Vector3, Vector3, float)>(count);
        int seed = 12345;
        for (int i = 0; i < count; i++)
        {
            float target = targets[i % targets.Length];
            Vector3 start = points[i % points.Count];
            Vector3 goal = start;
            for (int attempt = 0; attempt < points.Count; attempt++)
            {
                seed = unchecked(seed * 1103515245 + 12345);
                Vector3 candidate = points[Math.Abs(seed) % points.Count];
                float d = DistanceXZ(start, candidate);
                if (MathF.Abs(d - target) <= target * 0.25f + 2f) { goal = candidate; break; }
            }
            queries.Add((start, goal, radii[i % radii.Length]));
        }
        return queries;
    }

    private static List<Vector3> SamplePoints(NavMeshGraph graph)
    {
        var points = new List<Vector3>();
        for (float x = -48f; x <= 72f; x += 6f)
        for (float z = -60f; z <= 50f; z += 6f)
        {
            var candidate = new Vector3(x, 0f, z);
            if (graph.FindNearestPoint(candidate, 2f, out Vector3 projected, out _, out _))
                points.Add(projected);
        }
        return points;
    }

    private static double Average(List<double> values)
    {
        double sum = 0;
        foreach (double v in values) sum += v;
        return values.Count == 0 ? 0 : sum / values.Count;
    }

    private static double Max(List<double> values)
    {
        double max = 0;
        foreach (double v in values) if (v > max) max = v;
        return max;
    }

    private static double Percentile(List<double> values, double p)
    {
        var sorted = new List<double>(values);
        sorted.Sort();
        return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];
    }

    private static float DistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
