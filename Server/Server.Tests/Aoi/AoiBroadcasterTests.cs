using System.Collections.Generic;
using Cool.Protocol;
using Xunit;

namespace KcpServer.Tests;

public class AoiBroadcasterTests
{
    [Fact]
    public void Moving_Near_Every_Tick_Static_Far_Every_Fourth()
    {
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        var near = store.AddPlayer(2, "Near");
        var far = store.AddPlayer(3, "Far");
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        near.UpdatePosition(new Vector3(10, 0, 0), 0f, 0f, 1L, 0U); // 近带 10m
        far.UpdatePosition(new Vector3(70, 0, 0), 0f, 0f, 1L, 0U);  // 远带 70m

        var broadcaster = new AoiBroadcaster();

        // tick 1：both 首帧
        Assert.Equal(new[] { 2L, 3L }, Ids(broadcaster.BuildForObserver(observer, tick: 1, store)));
        // tick 2-4：near 持续移动 → 每 tick；far 静止 → 4 tick 保活窗口内不发
        for (uint t = 2; t <= 4; t++)
        {
            near.UpdatePosition(new Vector3(10f + t, 0, 0), 0f, 0f, t, 0U);
            Assert.Equal(new[] { 2L }, Ids(broadcaster.BuildForObserver(observer, tick: t, store)));
        }
        // tick 5：near 移动 + far 保活到期
        near.UpdatePosition(new Vector3(15f, 0, 0), 0f, 0f, 5L, 0U);
        Assert.Equal(new[] { 2L, 3L }, Ids(broadcaster.BuildForObserver(observer, tick: 5, store)));
    }

    [Fact]
    public void Static_Near_Entity_Keepalive_Every_Four_Ticks()
    {
        // 静止近带实体不得占每 tick 带宽（终审 I5 语义）：首帧后每 4 tick 一帧
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        var idle = store.AddPlayer(2, "Idle");
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        idle.UpdatePosition(new Vector3(10, 0, 0), 0f, 0f, 1L, 0U);

        var broadcaster = new AoiBroadcaster();
        Assert.Equal(new[] { 2L }, Ids(broadcaster.BuildForObserver(observer, 1, store)));
        Assert.Empty(broadcaster.BuildForObserver(observer, 2, store));
        Assert.Empty(broadcaster.BuildForObserver(observer, 3, store));
        Assert.Empty(broadcaster.BuildForObserver(observer, 4, store));
        Assert.Equal(new[] { 2L }, Ids(broadcaster.BuildForObserver(observer, 5, store))); // 4 tick 保活
    }

    [Fact]
    public void Out_Of_Sight_Entity_Stops_Broadcast_And_Reentry_Resets()
    {
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        var mover = store.AddPlayer(2, "M");
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        mover.UpdatePosition(new Vector3(50, 0, 0), 0f, 0f, 1L, 0U);

        var broadcaster = new AoiBroadcaster();
        Assert.Contains(2L, Ids(broadcaster.BuildForObserver(observer, tick: 1, store)));

        // 移出视野（100m > 90m 迟滞出界）
        mover.UpdatePosition(new Vector3(100, 0, 0), 0f, 0f, 2L, 1U);
        Assert.DoesNotContain(2L, Ids(broadcaster.BuildForObserver(observer, tick: 2, store)));

        // 回到 50m——重见节拍重置，立即出现
        mover.UpdatePosition(new Vector3(50, 0, 0), 0f, 0f, 3L, 2U);
        Assert.Contains(2L, Ids(broadcaster.BuildForObserver(observer, tick: 3, store)));
    }

    [Fact]
    public void Snapshot_Carries_Current_Full_Fields()
    {
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        var mover = store.AddPlayer(2, "M");
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        mover.UpdatePosition(new Vector3(10, 0, 0), 90f, 4f, 1L, 1U);

        var broadcaster = new AoiBroadcaster();
        EntitySnapshot? captured = null;
        broadcaster.OnEntityBroadcast = (entityId, snap) => { if (entityId == 2) captured = snap; };
        broadcaster.BuildForObserver(observer, tick: 1, store);

        Assert.NotNull(captured);
        Assert.Equal(10f, captured!.X, 4);
        Assert.Equal(90f, captured.Rotation, 4);
        Assert.Equal(4f, captured.Speed, 4);
        Assert.Equal(1U, captured.Tick);
        Assert.Equal((ushort)(EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Speed), captured.FieldMask);
    }

    [Fact]
    public void RemoveEntity_Stops_All_Observer_Broadcasts_Of_That_Entity()
    {
        // 实体注销（断开）后不得再广播——同 id 重登前无残留节拍/视野状态
        var store = new EntityStore();
        var observer = store.AddPlayer(1, "Obs");
        var mover = store.AddPlayer(2, "M");
        observer.UpdatePosition(new Vector3(0, 0, 0), 0f, 0f, 1L, 0U);
        mover.UpdatePosition(new Vector3(50, 0, 0), 0f, 0f, 1L, 0U);

        var broadcaster = new AoiBroadcaster();
        Assert.Contains(2L, Ids(broadcaster.BuildForObserver(observer, 1, store)));

        store.RemovePlayer(2);          // 注销（登录断开路径）
        broadcaster.RemoveEntity(2);    // AOI 状态清理（Server3C.UnregisterEntity 接线）
        Assert.Empty(broadcaster.BuildForObserver(observer, 2, store));

        // 重登同 id 到 95m（首现 >80m 不可见——无迟滞豁免残留）
        var reborn = store.AddPlayer(2, "M2");
        reborn.UpdatePosition(new Vector3(95, 0, 0), 0f, 0f, 3L, 0U);
        Assert.DoesNotContain(2L, Ids(broadcaster.BuildForObserver(observer, 3, store)));
    }

    private static long[] Ids(List<EntitySnapshot> snaps)
    {
        var ids = new long[snaps.Count];
        for (int i = 0; i < snaps.Count; i++) ids[i] = snaps[i].PlayerId;
        return ids;
    }
}
