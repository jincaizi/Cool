using System.Collections.Generic;
using Cool.Protocol;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 远程实体注册表 —— 管理远程玩家渲染对象（胶囊体占位 + P2 快照插值渲染：
    /// RemoteInterpolator 缓冲 2-tick 延迟插值，不再直接定位）。生成/销毁由快照流驱动。
    /// </summary>
    public sealed class RemoteEntityRegistry
    {
        private readonly Dictionary<long, RemoteEntityView> _views = new Dictionary<long, RemoteEntityView>();
        private readonly RemoteInterpolator _interpolator = new RemoteInterpolator();
        private readonly Dictionary<long, float> _lastKnownYaw = new Dictionary<long, float>();
        private readonly Transform _root;

        public int Count => _views.Count;

        public RemoteEntityRegistry(Transform root)
        {
            _root = root;
        }

        /// <summary>
        /// 快照入插值缓冲（不再直接定位）。serverTicks = 所属 WorldSnapshot.ServerTimestamp
        /// （绝对服务器时钟——同批实体同值，插值时间基准统一）
        /// </summary>
        public void ApplySnapshot(EntitySnapshot snapshot, long serverTicks)
        {
            if (!_views.TryGetValue(snapshot.PlayerId, out var view))
            {
                view = CreateView(snapshot.PlayerId);
                _views[snapshot.PlayerId] = view;
            }

            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Position) != 0)
            {
                // beacon 仅带 Position 位：Rotation 是 protobuf 默认 0 而非权威 yaw——
                // 按位掩码契约携带 yaw（脏快照带 Rotation 位时更新 last-known，beacon 沿用）
                if ((snapshot.FieldMask & (ushort)EntityFieldMask.Rotation) != 0)
                    _lastKnownYaw[snapshot.PlayerId] = snapshot.Rotation;

                float yaw = _lastKnownYaw.TryGetValue(snapshot.PlayerId, out var last) ? last : 0f;
                _interpolator.AddSample(snapshot.PlayerId, serverTicks,
                    new Cool.SyncCore.Vec3(snapshot.X, snapshot.Y, snapshot.Z), yaw);
            }
        }

        /// <summary>每帧渲染（nowServerTicks 换算后；renderDelayTicks = 2×ServerTickRateMs 换算）</summary>
        public void UpdateRender(long nowServerTicks, long renderDelayTicks)
        {
            foreach (var kv in _views)
            {
                if (_interpolator.TryGetRenderState(kv.Key, nowServerTicks, renderDelayTicks,
                    out var pos, out var rot))
                {
                    kv.Value.Position = pos;
                    kv.Value.Rotation = rot;
                }
            }
        }

        public bool Remove(long playerId)
        {
            if (_views.TryGetValue(playerId, out var view))
            {
                Object.Destroy(view.GameObject);
                _views.Remove(playerId);
                _interpolator.Remove(playerId); // 插值缓冲同步清理（防重连同 ID 残留旧样本）
                _lastKnownYaw.Remove(playerId);
                return true;
            }
            return false;
        }

        public void Clear()
        {
            foreach (var view in _views.Values)
                Object.Destroy(view.GameObject);
            _views.Clear();
            _interpolator.Clear();
            _lastKnownYaw.Clear();
        }

        /// <summary>读远程实体视图（T6 验收断言用；无视图返回 null）</summary>
        public RemoteEntityView? GetView(long playerId)
            => _views.TryGetValue(playerId, out var view) ? view : null;

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
