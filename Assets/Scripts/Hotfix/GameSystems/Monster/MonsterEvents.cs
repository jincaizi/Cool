using Hotfix.GameSystems.Skills.Events;
using UnityEngine;

namespace Hotfix.GameSystems.Monster
{
    public struct MonsterDeathEvent : IEvent
    {
        public string MonsterId;
        public Vector3 Position;
        public LootResult[] Loot;

        public MonsterDeathEvent(string monsterId, Vector3 position, LootResult[] loot)
        {
            MonsterId = monsterId;
            Position = position;
            Loot = loot;
        }
    }

    public struct MonsterSpawnEvent : IEvent
    {
        public string MonsterId;
        public Vector3 Position;

        public MonsterSpawnEvent(string monsterId, Vector3 position)
        {
            MonsterId = monsterId;
            Position = position;
        }
    }
}
