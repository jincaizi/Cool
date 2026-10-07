using System;
using System.Collections.Generic;
using UnityEngine;
using Hotfix.GameSystems.Skills;

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

        private Transform _container;
        private readonly List<MonsterEntity> _aliveMonsters = new();
        private readonly SpawnLedger _ledger = new();
        // 计时器改按实体 id 记（原按 MonsterId 记 → 到期重生整组，是爆炸 bug 的根源）
        private readonly Dictionary<int, float> _respawnTimers = new();
        private readonly Dictionary<int, int> _respawnGroupOf = new();
        private readonly Dictionary<int, Vector3> _respawnOrigins = new();
        private readonly Queue<MonsterEntity> _pool = new();

        private void Awake()
        {
            // Create a dedicated container node for organization in the hierarchy
            var containerGo = new GameObject("Monsters");
            containerGo.transform.SetParent(transform);
            _container = containerGo.transform;
        }

        private void Start()
        {
            foreach (var group in Groups)
                SpawnGroupInternal(group);
        }

        private void Update()
        {
            if (_respawnTimers.Count == 0) return;

            var toRespawn = new List<int>();
            foreach (var kvp in _respawnTimers)
            {
                if (Time.time >= kvp.Value) toRespawn.Add(kvp.Key);
            }

            foreach (var entityId in toRespawn)
            {
                _respawnTimers.Remove(entityId);
                if (!_respawnGroupOf.TryGetValue(entityId, out int groupIndex)) continue;
                _respawnGroupOf.Remove(entityId);
                if (!_respawnOrigins.TryGetValue(entityId, out Vector3 origin)) continue;
                _respawnOrigins.Remove(entityId);

                if (groupIndex < 0 || groupIndex >= Groups.Length) continue;

                // 只重生这一只，位置 = 它自己的初始生成点（不是整组、不是死亡位置）
                // 组索引必须回传：否则重生出来的怪记账落在 -1 组，下次死亡后不再重生
                Spawn(Groups[groupIndex].Config, origin, groupIndex);
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
                Spawn(config, pos, groupIndex: Array.IndexOf(Groups, group));
            }
        }

        public MonsterEntity Spawn(MonsterConfig config, Vector3 position)
            => Spawn(config, position, groupIndex: -1);

        public MonsterEntity Spawn(MonsterConfig config, Vector3 position, int groupIndex)
        {
            if (config.Prefab == null)
            {
                Debug.LogError($"[MonsterSpawner] Prefab is null for {config.MonsterId}");
                return null;
            }

            // Try pool first
            MonsterEntity entity = null;
            while (_pool.Count > 0)
            {
                var pooled = _pool.Dequeue();
                if (pooled != null && pooled.gameObject != null)
                {
                    entity = pooled;
                    break;
                }
            }

            if (entity != null)
            {
                // Reuse pooled monster
                entity.gameObject.SetActive(true);
                entity.transform.SetPositionAndRotation(position, Quaternion.identity);
                entity.transform.SetParent(_container);
                entity.Init(config, position);
            }
            else
            {
                // Fresh instantiation
                var go = Instantiate(config.Prefab, position, Quaternion.identity, _container);
                entity = go.GetComponent<MonsterEntity>();
                if (entity == null)
                {
                    Debug.LogError($"[MonsterSpawner] MonsterEntity component not found on prefab {config.MonsterId}");
                    Destroy(go);
                    return null;
                }
                entity.Init(config, position);
            }

            // 订阅不会累积：MonsterEntity.Init 会先清空自身事件（见 (e)），
            // 每次生成恰好挂一个处理器。原实现每次生成都 += 且从不清，
            // 池化复用后同一只怪死亡会触发多个处理器，放大爆炸 bug。
            entity.OnDeathComplete += () => HandleMonsterDeath(entity.GetInstanceID());

            _aliveMonsters.Add(entity);
            _ledger.RecordSpawn(groupIndex, entity.GetInstanceID(), position);

            EventBus.Emit(new MonsterSpawnEvent(config.MonsterId, position));
            return entity;
        }

        /// <summary>
        /// P6 ⑤：一次死亡只登记一只待重生（原实现按 MonsterId 记、到期重生整组）。
        /// 重生点 = 该实体的初始生成点（ledger 保存），不是死亡位置（D-P6-13）。
        /// </summary>
        private void HandleMonsterDeath(int entityId)
        {
            if (!_ledger.RecordDeath(entityId, out int groupIndex, out Vector3 origin)) return;

            _aliveMonsters.RemoveAll(m => m == null || m.GetInstanceID() == entityId);

            _respawnGroupOf[entityId] = groupIndex;
            _respawnOrigins[entityId] = origin;
            _respawnTimers[entityId] = Time.time + RespawnDelay;
        }

        /// <summary>
        /// P6 ⑥：彻底销毁本 spawner 的全部怪物（联网模式切走时用）。
        /// 只做 Destroy + 清集合——PhysicsRegistry / EntityDisplayManager 的注销由
        /// MonsterEntity.OnDestroy 负责，在此重复注销有双注销风险（D-P6-14）。
        /// 注意：销毁后本 spawner 不再刷怪；模式在启动时确定、运行期不切换，可接受。
        /// </summary>
        public void DespawnAll()
        {
            foreach (var entity in _aliveMonsters)
            {
                if (entity != null) Destroy(entity.gameObject);
            }

            _aliveMonsters.Clear();
            _pool.Clear();
            _respawnTimers.Clear();
            _respawnGroupOf.Clear();
            _respawnOrigins.Clear();
            _ledger.Clear();
        }
    }
}
