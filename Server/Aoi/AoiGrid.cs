using System.Collections.Generic;

namespace KcpServer
{
    /// <summary>
    /// 网格 AOI（spec §5-P3）——按观察者坐标 ±LeaveRadius 推导候选格范围（粗筛），
    /// 精确半径过滤 + 迟滞边界。**网格是过滤器而非索引**：每 observer 每 tick 仍遍历
    /// allPlayers 一次（O(N)，数百实体毫秒级——性能目标为"不随世界面积增长"，由粗筛保证）。
    /// 迟滞状态按 observer 维度存 _sightState；实体注销须经 RemoveEntity 清除——playerId
    /// 单调递增不重用，但登录-注销-再登录同 id 若无清除会残留 seen=true，凭旧状态在迟滞带
    /// 内错误可见。
    /// GetVisible 在 visibleNow 翻转时同步维护反向索引 _observersByEntity，供
    /// GetObserversOf O(1) 反查（取代逐 observer 重扫）；可见性/迟滞结果不受影响。
    /// </summary>
    public sealed class AoiGrid
    {
        public const float CellSize = 40f;
        public const float SightRadius = 80f;
        public const float LeaveRadius = 90f; // 迟滞：进 ≤80m，出须 >90m

        private readonly Dictionary<long, Dictionary<long, bool>> _sightState =
            new Dictionary<long, Dictionary<long, bool>>();

        // 反向索引 entityId → 能看到它的 observer 集合。只在 GetVisible 的 visibleNow 翻转时
        // 维护（进入加入、离开移除）；实体侧清理走 RemoveEntity，观察者侧走 RemoveObserver。
        private readonly Dictionary<long, HashSet<long>> _observersByEntity =
            new Dictionary<long, HashSet<long>>();

        /// <summary>已建立视野状态的观察者数（&gt;0 表示反向索引已可用）。</summary>
        public int TrackedObservers => _sightState.Count;

        /// <summary>能看到指定实体的观察者（索引快照；顺序不保证，调用方不依赖）</summary>
        public List<long> GetObserversOf(long entityId)
            => _observersByEntity.TryGetValue(entityId, out var set)
                ? new List<long>(set) : new List<long>();

        /// <summary>观察者可见实体列表（实体自身；顺序 = allPlayers 遍历序，调用方不依赖）</summary>
        public List<PlayerState> GetVisible(long observerId, IReadOnlyCollection<PlayerState> allPlayers)
        {
            var result = new List<PlayerState>();
            if (!_sightState.TryGetValue(observerId, out var seen))
            {
                seen = new Dictionary<long, bool>();
                _sightState[observerId] = seen;
            }

            // 观察者当前位置（找不到观察者 → 空视野）
            PlayerState? observer = null;
            foreach (var p in allPlayers)
                if (p.PlayerId == observerId) { observer = p; break; }
            if (observer == null) return result;

            float ox = observer.Position.X, oz = observer.Position.Z;
            // 候选格范围由半径从坐标推导。范围必须 ≥ LeaveRadius(90) 而非 SightRadius(80)：
            // ① 迟滞环（80~90m）实体须持续被评估（坐标 mod 40 ∈ (30,40) 时 ±80 只覆盖
            //    到 80~120−u < 90——出界判定漏评估 → 停流早于 90m + seen 残留 ghost）；
            // ② 若实体在候选范围外离开（>90m）seen 不更新——回落到 80~90m 时凭 stale-true
            //    错误地"仍可见"（重进须 ≤80）。
            // 也不能取"所在格 ±1"：观察者贴格边时短边覆盖不足整半径。±90 覆盖 ≤5×5=25 格候选。
            int colMin = CellOf(ox - LeaveRadius), colMax = CellOf(ox + LeaveRadius);
            int rowMin = CellOf(oz - LeaveRadius), rowMax = CellOf(oz + LeaveRadius);

            foreach (var p in allPlayers)
            {
                if (p.PlayerId == observerId) continue;
                int col = CellOf(p.Position.X), row = CellOf(p.Position.Z);
                if (col < colMin || col > colMax || row < rowMin || row > rowMax) continue;

                float dx = p.Position.X - ox, dz = p.Position.Z - oz;
                float distSqr = dx * dx + dz * dz;
                bool wasVisible = seen.TryGetValue(p.PlayerId, out var v) && v;
                bool visibleNow = distSqr <= SightRadius * SightRadius
                    || (wasVisible && distSqr <= LeaveRadius * LeaveRadius);
                seen[p.PlayerId] = visibleNow;

                // 只在翻转时维护反向索引——逐 tick 的可见性判定保持不变。
                if (wasVisible != visibleNow)
                {
                    if (visibleNow)
                    {
                        if (!_observersByEntity.TryGetValue(p.PlayerId, out var observers))
                        {
                            observers = new HashSet<long>();
                            _observersByEntity[p.PlayerId] = observers;
                        }
                        observers.Add(observerId);
                    }
                    else if (_observersByEntity.TryGetValue(p.PlayerId, out var existingObservers))
                    {
                        existingObservers.Remove(observerId);
                        if (existingObservers.Count == 0) _observersByEntity.Remove(p.PlayerId);
                    }
                }

                if (visibleNow) result.Add(p);
            }

            return result;
        }

        /// <summary>实体注销——从所有观察者视野状态与反向索引清除（防同 id 重登残留迟滞豁免）</summary>
        public void RemoveEntity(long entityId)
        {
            foreach (var seen in _sightState.Values)
                seen.Remove(entityId);
            _observersByEntity.Remove(entityId);
        }

        /// <summary>移除观察者全部视野状态与反向索引条目（会话断开时由 Server3C 调用）</summary>
        public void RemoveObserver(long observerId)
        {
            _sightState.Remove(observerId);
            var emptied = new List<long>();
            foreach (var pair in _observersByEntity)
            {
                pair.Value.Remove(observerId);
                if (pair.Value.Count == 0) emptied.Add(pair.Key);
            }
            foreach (var entityId in emptied)
                _observersByEntity.Remove(entityId);
        }

        private static int CellOf(float coord)
        {
            // 向下取整（负坐标必须 floor：-45 → cell -2；MathF.Floor 处理精确负整数倍）
            return (int)System.MathF.Floor(coord / CellSize);
        }
    }
}
