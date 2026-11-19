using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using KcpServer;
using Xunit;

namespace KcpServer.Tests;

/// <summary>
/// Plan 05 任务 1：AOI 观察者反向索引（entityId → observers）。
/// 索引由 AoiGrid.GetVisible 在 visibleNow 翻转时维护；结果必须与旧 Server3C
/// 暴力扫描（逐 observer 调 GetVisible 查成员）在相同 tick/迟滞状态下逐项相等。
/// </summary>
public class AoiReverseIndexTests
{
    private static PlayerState P(long id, float x, float z)
    {
        var p = new PlayerState(id);
        p.UpdatePosition(new Vector3(x, 0, z), 0f, 0f, 1L, 0U);
        return p;
    }

    private static List<long> Sorted(IEnumerable<long> ids)
    {
        var list = new List<long>(ids);
        list.Sort();
        return list;
    }

    [Fact]
    public void GetObserversOf_Matches_Brute_Force_Across_Random_Ticks()
    {
        // 50 实体、30 tick 随机游走。10 个观察者固定在 10m 格点上，walker 每轴每 tick
        // ≤5m（5m 格点）——观察者的候选格覆盖在 ±90m 外至少再延伸 10m，因此 walker 从
        // 可见跳到 >90m 时一定落在候选格内被评估（翻转必被索引记录）。这是旧暴力逻辑
        // 与索引可比的"同一 tick/迟滞状态"前提。
        const int observerCount = 10;
        const int entityCount = 50;
        var rng = new Random(20261120);
        var players = new List<PlayerState>();
        for (long id = 1; id <= observerCount; id++)
        {
            float x = MathF.Round((float)(rng.NextDouble() * 300 - 150) / 10f) * 10f;
            float z = MathF.Round((float)(rng.NextDouble() * 300 - 150) / 10f) * 10f;
            players.Add(P(id, x, z));
        }
        for (long id = observerCount + 1; id <= entityCount; id++)
        {
            float x = MathF.Round((float)(rng.NextDouble() * 300 - 150) / 5f) * 5f;
            float z = MathF.Round((float)(rng.NextDouble() * 300 - 150) / 5f) * 5f;
            players.Add(P(id, x, z));
        }

        var grid = new AoiGrid();
        int observedPairs = 0;

        for (uint tick = 1; tick <= 30; tick++)
        {
            // 观察者不动；其余实体 5m 格点随机游走
            for (int i = observerCount; i < players.Count; i++)
            {
                var p = players[i];
                float x = Math.Clamp(p.Position.X + (rng.Next(3) - 1) * 5f, -150f, 150f);
                float z = Math.Clamp(p.Position.Z + (rng.Next(3) - 1) * 5f, -150f, 150f);
                p.UpdatePosition(new Vector3(x, 0, z), 0f, 0f, tick, tick);
            }

            // 一次完整观察者扫描（生产 tick 的 GetVisible 同款），记录每个 observer 的可见集
            var visibleByObserver = new Dictionary<long, HashSet<long>>();
            for (int i = 0; i < observerCount; i++)
            {
                var observer = players[i];
                var visible = grid.GetVisible(observer.PlayerId, players);
                visibleByObserver[observer.PlayerId] = new HashSet<long>(visible.Select(e => e.PlayerId));
            }

            // 旧 Server3C 暴力逻辑：逐 observer 查可见集成员
            foreach (var entity in players)
            {
                var expected = players.Take(observerCount)
                    .Where(o => visibleByObserver[o.PlayerId].Contains(entity.PlayerId))
                    .Select(o => o.PlayerId)
                    .OrderBy(id => id)
                    .ToList();
                var actual = grid.GetObserversOf(entity.PlayerId);
                actual.Sort();
                Assert.Equal(expected, actual);
                Assert.Equal(actual.Count, actual.Distinct().Count());
                observedPairs += actual.Count;
            }
        }

        Assert.True(observedPairs > 0, "随机场景必须产生非空观察关系（否则等价断言空转）");
        Assert.Equal(observerCount, grid.TrackedObservers);
    }

    [Fact]
    public void Index_Follows_Hysteresis_Enter_Hold_Leave()
    {
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var mover = P(2, 50, 0);
        var all = new[] { observer, mover };

        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, all)); // 50m 进入
        Assert.Equal(new[] { 1L }, grid.GetObserversOf(2));

        mover.UpdatePosition(new Vector3(85, 0, 0), 0f, 0f, 2L, 1U);   // 迟滞带 80~90m
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, all));
        Assert.Equal(new[] { 1L }, grid.GetObserversOf(2));

        mover.UpdatePosition(new Vector3(95, 0, 0), 0f, 0f, 3L, 2U);   // >90m 离开
        Assert.DoesNotContain(mover, grid.GetVisible(observer.PlayerId, all));
        Assert.Empty(grid.GetObserversOf(2));

        mover.UpdatePosition(new Vector3(50, 0, 0), 0f, 0f, 4L, 3U);   // 重新进入
        Assert.Contains(mover, grid.GetVisible(observer.PlayerId, all));
        Assert.Equal(new[] { 1L }, grid.GetObserversOf(2));
    }

    [Fact]
    public void RemoveEntity_Clears_Index_And_Rejoin_Same_Id_Does_Not_Resurrect()
    {
        var grid = new AoiGrid();
        var observer = P(1, 0, 0);
        var entity = P(2, 50, 0);
        Assert.Contains(entity, grid.GetVisible(observer.PlayerId, new[] { observer, entity }));
        Assert.Equal(new[] { 1L }, grid.GetObserversOf(2));

        grid.RemoveEntity(2);
        Assert.Empty(grid.GetObserversOf(2));

        // 同 id 重登于迟滞带（85m）：首现不可见，索引也不得凭旧状态残留
        var reborn = P(2, 85, 0);
        Assert.DoesNotContain(reborn, grid.GetVisible(observer.PlayerId, new[] { observer, reborn }));
        Assert.Empty(grid.GetObserversOf(2));
    }

    [Fact]
    public void RemoveObserver_Removes_It_From_Every_Entity_Set()
    {
        var grid = new AoiGrid();
        var a = P(1, 0, 0);
        var b = P(2, 10, 0);
        var entity = P(3, 50, 0);
        var all = new[] { a, b, entity };

        grid.GetVisible(a.PlayerId, all);
        grid.GetVisible(b.PlayerId, all);
        Assert.Equal(new[] { 1L, 2L }, Sorted(grid.GetObserversOf(3)));
        Assert.Equal(2, grid.TrackedObservers);

        grid.RemoveObserver(1);
        Assert.Equal(new[] { 2L }, grid.GetObserversOf(3));
        Assert.Equal(1, grid.TrackedObservers);

        grid.RemoveObserver(2);
        Assert.Empty(grid.GetObserversOf(3));
        Assert.Equal(0, grid.TrackedObservers);

        // 断开后重扫：索引重新建立，不残留已移除观察者
        grid.GetVisible(b.PlayerId, all);
        Assert.Equal(new[] { 2L }, grid.GetObserversOf(3));
    }

    [Fact]
    public void Broadcaster_Exposes_Index_And_Cleans_On_Remove()
    {
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        var entity = store.AddPlayer(2, "E");
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        entity.UpdatePosition(new Vector3(50, 0, 0), 0f, 0f, 1L, 0U);

        var broadcaster = new AoiBroadcaster();
        Assert.False(broadcaster.HasSightState);

        broadcaster.BuildForObserver(observer, 1, store);
        Assert.True(broadcaster.HasSightState);
        Assert.Equal(new[] { 1L }, broadcaster.GetObserversOf(2));

        broadcaster.RemoveEntity(2);
        Assert.Empty(broadcaster.GetObserversOf(2));

        // 实体仍在 store 中：下个 tick 重新建立索引
        broadcaster.BuildForObserver(observer, 2, store);
        Assert.Equal(new[] { 1L }, broadcaster.GetObserversOf(2));

        broadcaster.RemoveObserver(1);
        Assert.Empty(broadcaster.GetObserversOf(2));
        Assert.False(broadcaster.HasSightState);
    }

    [Fact]
    public void Server3C_Falls_Back_On_First_Call_Then_Uses_Index_And_Filters_Sessionless()
    {
        using var server = new Server3C(new TestLogger());
        var observer = server.RegisterEntity(1, "Obs");
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        var observer2 = server.RegisterEntity(4, "Obs2");
        observer2.UpdatePosition(new Vector3(10, 0, 0), 0f, 0f, 1L, 0U);
        var target = server.RegisterEntity(2, "Target");
        target.UpdatePosition(new Vector3(50, 0, 0), 0f, 0f, 1L, 0U);
        var noSession = server.RegisterEntity(3, "NoSession");
        noSession.UpdatePosition(new Vector3(40, 0, 0), 0f, 0f, 1L, 0U);

        server.BindPlayerToSession(1, new FakeSession(11));
        server.BindPlayerToSession(4, new FakeSession(14));
        server.BindPlayerToSession(2, new FakeSession(12));
        // 实体 3 在目标视野内但没有 session——两条路径都必须过滤

        // 首 tick：SendCombatStateUpdates/SendNpcActionEvents 早于 BroadcastWorldSnapshot，
        // 尚无观察者快照——旧暴力路径兜底，否则第一条 311 漏发。
        Assert.Equal(new[] { 1L, 4L }, Sorted(server.GetObserversOf(2)));
        // 第二次：索引已由首轮 GetVisible 建好，结果不变
        Assert.Equal(new[] { 1L, 4L }, Sorted(server.GetObserversOf(2)));

        // 目标移出视野（对两观察者均 >90m 且仍在候选格内被评估）→ 刷新观察者视野后索引清空
        target.UpdatePosition(new Vector3(110, 0, 0), 0f, 0f, 2L, 1U);
        server.GetVisiblePlayers(1);
        server.GetVisiblePlayers(4);
        Assert.Empty(server.GetObserversOf(2));
    }

    [Fact]
    public void GetObserversOf_Median_Under_One_Millisecond_For_1000_Entities_100_Observers()
    {
        const int observerCount = 100;
        const int entityCount = 1000;
        var rng = new Random(2026);

        var store = new EntityStore();
        var observers = new List<PlayerState>(observerCount);
        for (long id = 1; id <= observerCount; id++)
        {
            // 100 个观察者聚在 45×45m 方阵；目标实体 101 在方阵中央，对全部观察者可见
            var observer = store.AddPlayer(id, "O" + id);
            observer.UpdatePosition(new Vector3((id - 1) % 10 * 5f, 0, (id - 1) / 10 * 5f), 0f, 0f, 1L, 0U);
            observers.Add(observer);
        }
        var target = store.AddPlayer(101, "Target");
        target.UpdatePosition(new Vector3(25, 0, 25), 0f, 0f, 1L, 0U);
        for (long id = 102; id <= entityCount; id++)
        {
            var e = store.AddPlayer(id, "E" + id);
            e.UpdatePosition(new Vector3((float)(rng.NextDouble() * 4000 - 2000), 0,
                (float)(rng.NextDouble() * 4000 - 2000)), 0f, 0f, 1L, 0U);
        }

        var broadcaster = new AoiBroadcaster();
        foreach (var observer in observers)
            broadcaster.BuildForObserver(observer, 1, store);

        Assert.True(broadcaster.HasSightState);
        Assert.Equal(observerCount, broadcaster.GetObserversOf(101).Count); // 非空最坏路径：复制 100 个 id

        for (int i = 0; i < 10; i++) broadcaster.GetObserversOf(101); // 预热（JIT/首次分配）

        var samples = new List<double>(100);
        for (int i = 0; i < 100; i++)
        {
            // 每 10 次混入一次非空目标（100 个观察者），其余为空集合快路径——
            // 不许改成只测非空：两种查询都必须是亚毫秒
            long id = i % 10 == 0 ? 101 : 500 + i % 400;
            long start = Stopwatch.GetTimestamp();
            var ids = broadcaster.GetObserversOf(id);
            double ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            samples.Add(ms);
            if (id == 101) Assert.NotEmpty(ids);
        }
        samples.Sort();
        double median = samples[samples.Count / 2];
        Assert.True(median <= 1.0,
            "GetObserversOf 中位耗时 " + median.ToString("F4") + "ms 超过 1ms（Debug 回归护栏）");
    }

    private sealed class FakeSession : IServerSession
    {
        public FakeSession(long sessionId) => SessionId = sessionId;
        public long SessionId { get; }
        public bool IsConnected => true;
        public DateTime LastActivityTime => DateTime.UtcNow;
        public event EventHandler<byte[]>? DataReceived;
        public event EventHandler? Disconnected;
        public event EventHandler<Exception>? Error;
        public void Send(MessageId messageId, IMessage message, MessageFlags flags = MessageFlags.Reliable) { }
        public void UpdateActivity() { }
        public bool IsTimedOut(TimeSpan timeout) => false;
        public Task CloseAsync(string reason = "Normal") => Task.CompletedTask;
        public void Dispose() { }
    }
}
