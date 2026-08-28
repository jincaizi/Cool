using System.Collections.Generic;
using Cool.Protocol;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 远程实体注册表 —— 管理远程玩家渲染对象（P1：胶囊体占位 + 快照直接定位；
    /// 插值渲染是 P2 范围）。生成/销毁由快照流驱动。
    /// </summary>
    public sealed class RemoteEntityRegistry
    {
        private readonly Dictionary<long, RemoteEntityView> _views = new Dictionary<long, RemoteEntityView>();
        private readonly Transform _root;

        public int Count => _views.Count;

        public RemoteEntityRegistry(Transform root)
        {
            _root = root;
        }

        /// <summary>应用一条快照：未知实体 → 生成；已知实体 → 定位（按位掩码）</summary>
        public void ApplySnapshot(EntitySnapshot snapshot)
        {
            if (!_views.TryGetValue(snapshot.PlayerId, out var view))
            {
                view = CreateView(snapshot.PlayerId);
                _views[snapshot.PlayerId] = view;
            }

            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Position) != 0)
                view.Position = new Vector3(snapshot.X, snapshot.Y, snapshot.Z);
            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Rotation) != 0)
                view.Rotation = Quaternion.Euler(0f, snapshot.Rotation, 0f);
        }

        public bool Remove(long playerId)
        {
            if (_views.TryGetValue(playerId, out var view))
            {
                Object.Destroy(view.GameObject);
                _views.Remove(playerId);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            foreach (var view in _views.Values)
                Object.Destroy(view.GameObject);
            _views.Clear();
        }

        private RemoteEntityView CreateView(long playerId)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"RemotePlayer_{playerId}";
            go.transform.SetParent(_root, false);
            return new RemoteEntityView(go);
        }
    }

    /// <summary>远程实体渲染视图（P1 占位）</summary>
    public sealed class RemoteEntityView
    {
        public GameObject GameObject { get; }
        public Vector3 Position
        {
            get => GameObject.transform.position;
            set => GameObject.transform.position = value;
        }
        public Quaternion Rotation
        {
            get => GameObject.transform.rotation;
            set => GameObject.transform.rotation = value;
        }

        public RemoteEntityView(GameObject go)
        {
            GameObject = go;
        }
    }
}
