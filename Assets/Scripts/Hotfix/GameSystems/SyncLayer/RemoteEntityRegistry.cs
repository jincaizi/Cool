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
        private const long ExpiryTicks = 12_000_000L; // 1.2s 无快照即视为出视野/离线（AOI 停流语义；≫ 静止保活 4 tick ≈ 267ms）

        private readonly Dictionary<long, RemoteEntityView> _views = new Dictionary<long, RemoteEntityView>();
        private readonly RemoteInterpolator _interpolator = new RemoteInterpolator();
        private readonly Dictionary<long, float> _lastKnownYaw = new Dictionary<long, float>();
        private readonly Dictionary<long, long> _lastSampleTicks = new Dictionary<long, long>();
        private readonly Transform _root;

        public int Count => _views.Count;

        /// <summary>当前视图实体 ID 集合（T7 AOI 验收统计用）</summary>
        public IEnumerable<long> PlayerIds => _views.Keys;

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
                view = CreateView(snapshot.PlayerId, snapshot);
                _views[snapshot.PlayerId] = view;
            }

            // P5：Npc 动作状态（占位期仅存储供测试/上层观察——动画表现随真实模型 backlog）
            view.ActionState = snapshot.ActionState;

            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Position) != 0)
            {
                // beacon 仅带 Position 位：Rotation 是 protobuf 默认 0 而非权威 yaw——
                // 按位掩码契约携带 yaw（脏快照带 Rotation 位时更新 last-known，beacon 沿用）
                if ((snapshot.FieldMask & (ushort)EntityFieldMask.Rotation) != 0)
                    _lastKnownYaw[snapshot.PlayerId] = snapshot.Rotation;

                float yaw = _lastKnownYaw.TryGetValue(snapshot.PlayerId, out var last) ? last : 0f;
                _interpolator.AddSample(snapshot.PlayerId, serverTicks,
                    new Cool.SyncCore.Vec3(snapshot.X, snapshot.Y, snapshot.Z), yaw);
                _lastSampleTicks[snapshot.PlayerId] = serverTicks; // 只在真收到样本时刷新
            }
        }

        /// <summary>
        /// 缺席超时清理（P3 AOI：视野外/离线实体自然停流——1.2s 无快照即销毁）。
        /// nowServerTicks 与快照时间同基准（WorldSnapshot.ServerTimestamp 绝对服务器时钟）。
        /// PlayerLeaveRoom 路径仍即时删（断线显式通知）。时钟收敛前由上层跳过调用。
        /// </summary>
        public void UpdateExpiry(long nowServerTicks)
        {
            if (_lastSampleTicks.Count == 0) return;
            var expired = new List<long>();
            foreach (var kv in _lastSampleTicks)
                if (nowServerTicks - kv.Value > ExpiryTicks)
                    expired.Add(kv.Key);
            foreach (var id in expired)
                Remove(id); // 内部同步清 _views + 插值缓冲 + _lastSampleTicks
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
                DestroyView(view.GameObject);
                _views.Remove(playerId);
                _interpolator.Remove(playerId); // 插值缓冲同步清理（防重连同 ID 残留旧样本）
                _lastKnownYaw.Remove(playerId);
                _lastSampleTicks.Remove(playerId); // 缺席追踪同步清理（UpdateExpiry 重扫不残留）
                return true;
            }
            return false;
        }

        public void Clear()
        {
            foreach (var view in _views.Values)
                DestroyView(view.GameObject);
            _views.Clear();
            _interpolator.Clear();
            _lastKnownYaw.Clear();
            _lastSampleTicks.Clear();
        }

        /// <summary>读远程实体视图（T6 验收断言用；无视图返回 null）</summary>
        public RemoteEntityView? GetView(long playerId)
            => _views.TryGetValue(playerId, out var view) ? view : null;

        /// <summary>
        /// 视图销毁（双态：运行时延迟 Destroy；EditMode 测试无播放上下文——
        /// Destroy 会报"may not be called from edit mode"错误日志导致 NUnit 失败）
        /// </summary>
        private static void DestroyView(GameObject go)
        {
            if (Application.isPlaying)
                Object.Destroy(go);
            else
                Object.DestroyImmediate(go);
        }

        private RemoteEntityView CreateView(long playerId, EntitySnapshot snapshot)
        {
            // P5：Npc 视图 = 球体占位（玩家胶囊区分）；名称携带模板 id。
            // 真实怪物模型/动画表现随 P3 backlog"真实角色模型渲染"落地（D-P5-7）。
            // 注：不访问 Renderer 组件着色——非 MonoBehaviour 类禁用 GetComponent（项目规则）。
            if (snapshot.Kind == (byte)EntityKind.Npc)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Monster_{playerId}_T{snapshot.TemplateId}";
                go.transform.SetParent(_root, false);
                return new RemoteEntityView(go, snapshot.Kind, snapshot.TemplateId);
            }

            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = $"RemotePlayer_{playerId}";
            capsule.transform.SetParent(_root, false);
            return new RemoteEntityView(capsule);
        }
    }

    /// <summary>远程实体渲染视图（P1 占位；P5 加 Npc 元数据）</summary>
    public sealed class RemoteEntityView
    {
        public GameObject GameObject { get; }
        /// <summary>实体种类（EntityKind；getter-only——Unity 无 IsExternalInit，禁用 init，P4 偏差同教训）</summary>
        public byte Kind { get; }
        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）</summary>
        public int TemplateId { get; }
        /// <summary>动作/战斗状态（Npc=NpcAnimationState；玩家恒 0）</summary>
        public byte ActionState { get; set; }

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

        public RemoteEntityView(GameObject go, byte kind = 0, int templateId = 0)
        {
            GameObject = go;
            Kind = kind;
            TemplateId = templateId;
        }
    }
}
