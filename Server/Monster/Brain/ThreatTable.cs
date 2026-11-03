using System;
using System.Collections.Generic;

namespace KcpServer.Monster
{
    /// <summary>
    /// 怪物仇恨表（P2）。主循环线程独占，无锁。
    /// 威胁来源：直接伤害（damage × ThreatPerDamage + ThreatFlat）、首次感知（ThreatInitial）、嘲讽。
    /// v1 不衰减；脱战 Clear()。同分按 playerId 升序，保证跨进程确定性。
    /// </summary>
    public sealed class ThreatTable
    {
        public const float ThreatPerDamage = 1.0f;
        public const float ThreatFlat = 10f;
        public const float ThreatInitial = 10f;
        public const float TargetSwitchThreshold = 0.2f;

        private readonly Dictionary<long, float> _threat = new Dictionary<long, float>();

        public int Count => _threat.Count;
        public IReadOnlyCollection<long> PlayerIds => _threat.Keys;

        public void Add(long playerId, float amount)
        {
            if (amount < 0f) return;
            _threat.TryGetValue(playerId, out float current);
            _threat[playerId] = current + amount;
        }

        /// <summary>直接伤害的威胁：伤害本身 + 固定值（格挡后按实际伤害调用）。</summary>
        public void AddDamage(long playerId, float damage)
            => Add(playerId, damage * ThreatPerDamage + ThreatFlat);

        public void AddInitial(long playerId) => Add(playerId, ThreatInitial);

        /// <summary>嘲讽目标可能尚未产生伤害：先建 0 威胁条目，再由选择器临时置顶。</summary>
        public void Ensure(long playerId)
        {
            if (!_threat.ContainsKey(playerId)) _threat[playerId] = 0f;
        }

        public bool Remove(long playerId) => _threat.Remove(playerId);
        public void Clear() => _threat.Clear();
        public bool Contains(long playerId) => _threat.ContainsKey(playerId);
        public float ThreatOf(long playerId)
            => _threat.TryGetValue(playerId, out float value) ? value : 0f;

        /// <summary>最高威胁的有效目标；同分取较小 playerId（与字典遍历顺序无关）。</summary>
        public long? Highest(Func<long, bool> isValid)
        {
            long? best = null;
            float bestThreat = float.MinValue;
            foreach (var pair in _threat)
            {
                if (!isValid(pair.Key)) continue;
                if (best == null
                    || pair.Value > bestThreat
                    || (pair.Value == bestThreat && pair.Key < best.Value))
                {
                    best = pair.Key;
                    bestThreat = pair.Value;
                }
            }
            return best;
        }
    }

    /// <summary>目标选择：最高威胁 + 当前目标 20% 滞后 + 嘲讽覆盖。</summary>
    public static class TargetSelector
    {
        public static long? Select(ThreatTable threats, long? currentTarget, Func<long, bool> isValid,
            long? tauntTarget = null, float switchThreshold = ThreatTable.TargetSwitchThreshold)
        {
            if (tauntTarget.HasValue && isValid(tauntTarget.Value))
                return tauntTarget;

            long? highest = threats.Highest(isValid);
            if (!highest.HasValue) return null;
            if (!currentTarget.HasValue || currentTarget.Value == highest.Value) return highest;
            if (!isValid(currentTarget.Value)) return highest;

            // 新目标必须比当前目标高 20% 以上才换；否则保留当前，避免两人抢仇恨来回抖。
            float currentThreat = threats.ThreatOf(currentTarget.Value);
            float highestThreat = threats.ThreatOf(highest.Value);
            return highestThreat > currentThreat * (1f + switchThreshold) ? highest : currentTarget;
        }
    }
}
