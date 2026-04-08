using System.Collections.Generic;
using System.Linq;

namespace KcpServer.AI.Combat
{
    public class AggroTable
    {
        private readonly Dictionary<long, float> _entries = new();
        private const float DecayRateInCombat = 0.05f;    // 5% per second
        private const float DecayRateOutOfRange = 0.20f;   // 20% per second

        public void AddAggro(long targetId, float amount)
        {
            if (_entries.TryGetValue(targetId, out var existing))
                _entries[targetId] = existing + amount;
            else
                _entries[targetId] = amount;
        }

        public void RemoveAggro(long targetId)
        {
            _entries.Remove(targetId);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public void DecayAll(float deltaTime, bool targetInRange)
        {
            float decayRate = targetInRange ? DecayRateInCombat : DecayRateOutOfRange;
            float decayFactor = 1f - (decayRate * deltaTime);

            var keys = _entries.Keys.ToList();
            foreach (var key in keys)
            {
                float newValue = _entries[key] * decayFactor;
                if (newValue <= 0)
                    _entries.Remove(key);
                else
                    _entries[key] = newValue;
            }
        }

        public long? GetHighestAggroTarget()
        {
            if (_entries.Count == 0) return null;
            return _entries.OrderByDescending(kv => kv.Value).First().Key;
        }
    }
}