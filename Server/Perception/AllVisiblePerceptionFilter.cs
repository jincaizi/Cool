using System.Collections.Generic;
using System.Linq;

namespace KcpServer
{
    /// <summary>全量可见占位实现（P1 阶段；P3 替换为 AoiGrid）</summary>
    public sealed class AllVisiblePerceptionFilter : IPerceptionFilter
    {
        public long[] GetVisiblePlayerIds(long observerPlayerId, IReadOnlyCollection<PlayerState> allPlayers)
        {
            return allPlayers.Where(p => p.PlayerId != observerPlayerId)
                             .Select(p => p.PlayerId)
                             .ToArray();
        }
    }
}
