using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Sys3C.Core;

namespace Hotfix.GameSystems.Monster
{
    public class MonsterSpawner : MonoBehaviour
    {
        [Serializable]
        public class SpawnGroup
        {
            public MonsterConfig Config;
            public int Count;
            public float SpawnRadius = 3f;
        }

        public SpawnGroup[] Groups;
        public float RespawnDelay = 30f;

        private readonly List<MonsterEntity> _aliveMonsters = new();
        private readonly Dictionary<string, float> _respawnTimers = new();

        private void Start()
        {
            foreach (var group in Groups)
                SpawnGroupInternal(group);
        }

        private void Update()
        {
            var toRespawn = new List<string>();
            foreach (var kvp in _respawnTimers)
            {
                if (Time.time >= kvp.Value)
                    toRespawn.Add(kvp.Key);
            }

            foreach (var key in toRespawn)
            {
                _respawnTimers.Remove(key);
                foreach (var group in Groups)
                {
                    if (group.Config.MonsterId == key)
                    {
                        SpawnGroupInternal(group);
                        break;
                    }
                }
            }
        }

        private void SpawnGroupInternal(SpawnGroup group)
        {
            var config = group.Config;
            for (int i = 0; i < group.Count; i++)
            {
                Vector3 pos;
                if (config != null && config.SpawnMode == SpawnMode.FixedPoints
                    && config.FixedSpawnPositions != null && config.FixedSpawnPositions.Length > 0)
                {
                    int idx = i % config.FixedSpawnPositions.Length;
                    pos = transform.position + config.FixedSpawnPositions[idx];
                }
                else
                {
                    pos = transform.position
                        + UnityEngine.Random.insideUnitSphere * group.SpawnRadius;
                    pos.y = transform.position.y;
                }
                Spawn(config, pos);
            }
        }

        public MonsterEntity Spawn(MonsterConfig config, Vector3 position)
        {
            if (config.Prefab == null)
            {
                Debug.LogError($"[MonsterSpawner] Prefab is null for {config.MonsterId}");
                return null;
            }

            var go = Instantiate(config.Prefab, position, Quaternion.identity);
            var entity = go.GetComponent<MonsterEntity>();
            if (entity == null)
            {
                Debug.LogError($"[MonsterSpawner] MonsterEntity component not found on prefab {config.MonsterId}");
                Destroy(go);
                return null;
            }

            entity.Init(config, position);
            entity.OnDeathComplete += () => HandleMonsterDeath(config, entity);
            _aliveMonsters.Add(entity);

            EventBus.Emit(new MonsterSpawnEvent(config.MonsterId, position));
            return entity;
        }

        private void HandleMonsterDeath(MonsterConfig config, MonsterEntity entity)
        {
            _aliveMonsters.Remove(entity);
            _respawnTimers[config.MonsterId] = Time.time + RespawnDelay;
        }
    }
}
