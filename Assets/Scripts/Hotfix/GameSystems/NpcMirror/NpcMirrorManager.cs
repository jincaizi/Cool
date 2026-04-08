using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.NpcMirror
{
    public class NpcMirrorManager
    {
        private readonly Dictionary<long, NpcMirrorComponent> _mirrors = new();
        private GameObject _npcPrefab;

        public void SetPrefab(GameObject prefab)
        {
            _npcPrefab = prefab;
        }

        public NpcMirrorComponent CreateNpc(long npcId, int templateId, Vector3 position, Quaternion rotation)
        {
            if (_mirrors.TryGetValue(npcId, out var existing))
                return existing;

            var mirror = new NpcMirrorComponent(npcId, position, rotation);

            if (_npcPrefab != null)
            {
                var go = Object.Instantiate(_npcPrefab, position, rotation);
                mirror.SetGameObject(go);
            }

            _mirrors[npcId] = mirror;
            return mirror;
        }

        public void RemoveNpc(long npcId)
        {
            if (_mirrors.TryGetValue(npcId, out var mirror))
            {
                if (mirror.GameObject != null)
                {
                    Object.Destroy(mirror.GameObject);
                }
                _mirrors.Remove(npcId);
            }
        }

        public void OnNpcSpawn(long npcId, int templateId, Vector3 position, Quaternion rotation)
        {
            CreateNpc(npcId, templateId, position, rotation);
        }

        public void OnNpcDespawn(long npcId)
        {
            RemoveNpc(npcId);
        }

        public void OnNpcPosSync(long npcId, Vector3 position, Quaternion rotation)
        {
            if (_mirrors.TryGetValue(npcId, out var mirror))
            {
                mirror.SetPosition(position);
                mirror.SetRotation(rotation);
            }
        }

        public void OnNpcAnimSync(long npcId, NpcAnimationState state)
        {
            if (_mirrors.TryGetValue(npcId, out var mirror))
            {
                mirror.SetAnimationState(state);
            }
        }

        public void Update(float deltaTime)
        {
            foreach (var mirror in _mirrors.Values)
            {
                mirror.Update(deltaTime);
            }
        }
    }
}
