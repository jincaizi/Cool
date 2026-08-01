using System.Collections.Generic;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 旋转技能单目标命中计数器 - 每个目标独立计数，达到上限后拒绝再次命中。
    /// 计数贯穿整个施放过程（目标离开再回来继续累计）。
    /// </summary>
    public class SpinHitTracker
    {
        private readonly int _maxHitsPerTarget;
        private readonly Dictionary<int, int> _hitCounts = new Dictionary<int, int>();

        public SpinHitTracker(int maxHitsPerTarget)
        {
            _maxHitsPerTarget = maxHitsPerTarget;
        }

        /// <summary>
        /// 尝试记录一次命中。上限 &lt;= 0 表示不设上限。
        /// </summary>
        public bool TryRecordHit(int instanceId)
        {
            _hitCounts.TryGetValue(instanceId, out int count);
            if (_maxHitsPerTarget > 0 && count >= _maxHitsPerTarget)
                return false;
            _hitCounts[instanceId] = count + 1;
            return true;
        }

        public int GetHitCount(int instanceId)
        {
            return _hitCounts.TryGetValue(instanceId, out int count) ? count : 0;
        }

        public void Clear()
        {
            _hitCounts.Clear();
        }
    }
}
