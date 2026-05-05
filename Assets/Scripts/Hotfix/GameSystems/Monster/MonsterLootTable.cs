using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    [Serializable]
    public struct LootEntry
    {
        public string ItemId;
        public int MinCount;
        public int MaxCount;
        [Range(0f, 1f)]
        public float DropChance;
    }

    public struct LootResult
    {
        public string ItemId;
        public int Count;
    }

    [CreateAssetMenu(fileName = "LootTable", menuName = "Game/Monster/LootTable")]
    public class MonsterLootTable : ScriptableObject
    {
        public LootEntry[] Entries;
        public int GoldMin = 5;
        public int GoldMax = 20;

        public List<LootResult> Roll()
        {
            var results = new List<LootResult>();
            int gold = UnityEngine.Random.Range(GoldMin, GoldMax + 1);
            if (gold > 0) results.Add(new LootResult { ItemId = "Gold", Count = gold });

            if (Entries != null)
            {
                foreach (var entry in Entries)
                {
                    if (UnityEngine.Random.value < entry.DropChance)
                    {
                        int count = UnityEngine.Random.Range(entry.MinCount, entry.MaxCount + 1);
                        results.Add(new LootResult { ItemId = entry.ItemId, Count = count });
                    }
                }
            }
            return results;
        }
    }
}
