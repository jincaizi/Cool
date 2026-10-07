using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    /// <summary>
    /// P6 ⑤：刷怪记账（纯逻辑，无 GameObject 依赖——EditMode 可测）。
    /// 职责：按组维护存活数，并记住每只怪的**初始生成点**供重生复位。
    /// 抽出的理由：本次 bug 正是记账错误（死亡按 MonsterId 记、到期重生整组），
    /// 必须有回归测试锁死；而 PlayMode 测 MonsterSpawner 需要真实 Prefab 资产，成本过高。
    /// </summary>
    public sealed class SpawnLedger
    {
        private struct Entry
        {
            public int GroupIndex;
            public Vector3 Origin;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly Dictionary<int, int> _aliveByGroup = new Dictionary<int, int>();

        /// <summary>该组当前存活数（未记录过的组返回 0）</summary>
        public int AliveCount(int groupIndex)
            => _aliveByGroup.TryGetValue(groupIndex, out int count) ? count : 0;

        /// <summary>记录一次生成：存活 +1，并记住该实体的归属组与初始生成点</summary>
        public void RecordSpawn(int groupIndex, int entityId, Vector3 origin)
        {
            _entries[entityId] = new Entry { GroupIndex = groupIndex, Origin = origin };
            _aliveByGroup.TryGetValue(groupIndex, out int count);
            _aliveByGroup[groupIndex] = count + 1;
        }

        /// <summary>
        /// 记录一次死亡：存活 -1，并回吐该实体的归属组与初始生成点。
        /// 未记录的实体返回 false 且不改变任何计数（防重复死亡事件污染计数）。
        /// </summary>
        public bool RecordDeath(int entityId, out int groupIndex, out Vector3 origin)
        {
            groupIndex = 0;
            origin = Vector3.zero;

            if (!_entries.TryGetValue(entityId, out var entry)) return false;

            _entries.Remove(entityId);
            groupIndex = entry.GroupIndex;
            origin = entry.Origin;

            _aliveByGroup.TryGetValue(groupIndex, out int count);
            _aliveByGroup[groupIndex] = Mathf.Max(0, count - 1);
            return true;
        }

        /// <summary>清空全部记账（DespawnAll 用）</summary>
        public void Clear()
        {
            _entries.Clear();
            _aliveByGroup.Clear();
        }
    }
}
