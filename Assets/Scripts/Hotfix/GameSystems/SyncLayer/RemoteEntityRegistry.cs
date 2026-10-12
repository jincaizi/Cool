using System.Collections.Generic;
using Cool.Protocol;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 远程实体注册表 —— 管理远程实体渲染对象（N5b 起按 `EntityKind` 分三支：
    /// 真人 = 角色预制体 + Animator，bot = 胶囊占位，怪物 = 球体占位；
    /// P2 快照插值渲染：RemoteInterpolator 缓冲 2-tick 延迟插值，不再直接定位）。生成/销毁由快照流驱动。
    /// </summary>
    public sealed class RemoteEntityRegistry
    {
        private const long ExpiryTicks = 12_000_000L; // 1.2s 无快照即视为出视野/离线（AOI 停流语义；≫ 静止保活 4 tick ≈ 267ms）

        private readonly Dictionary<long, RemoteEntityView> _views = new Dictionary<long, RemoteEntityView>();
        private readonly RemoteInterpolator _interpolator = new RemoteInterpolator();
        private readonly Dictionary<long, float> _lastKnownYaw = new Dictionary<long, float>();
        private readonly Dictionary<long, long> _lastSampleTicks = new Dictionary<long, long>();
        private readonly Transform _root;
        /// <summary>远程真人角色预制体（N5b）——null 时真人退化为胶囊占位，见 CreateView</summary>
        private readonly GameObject? _playerAvatarPrefab;
        /// <summary>怪物模型目录（templateId → prefab，宿主接线）——null 或未命中时怪物退化为球体占位，见 CreateView</summary>
        private readonly IReadOnlyDictionary<int, GameObject>? _npcPrefabs;
        private readonly NpcAttackSchedule _attacks = new NpcAttackSchedule();
        /// <summary>本帧到点的挥砍（Drain 的契约是**只追加**、不清空调用方列表，所以每帧复用它、先 Clear）</summary>
        private readonly List<NpcAttackSchedule.DueAttack> _dueAttacks = new List<NpcAttackSchedule.DueAttack>();
        private bool _warnedMissingAvatar;

        public int Count => _views.Count;

        /// <summary>当前视图实体 ID 集合（T7 AOI 验收统计用）</summary>
        public IEnumerable<long> PlayerIds => _views.Keys;

        /// <param name="playerAvatarPrefab">
        /// 远程真人角色的预制体（N5b）。省略 = 占位渲染（测试宿主与未接线的场景走这条），
        /// **不会** `Instantiate(null)`。
        /// </param>
        /// <param name="npcPrefabs">
        /// 怪物模型目录：templateId → 预制体（宿主从 `ClientNetworkHost._npcPrefabs` 建表传入）。
        /// 省略/未命中 = 球体占位（未接线的测试宿主与场景走这条）——与真人分支同款"缺配置即降级"。
        /// </param>
        public RemoteEntityRegistry(Transform root, GameObject? playerAvatarPrefab = null,
            IReadOnlyDictionary<int, GameObject>? npcPrefabs = null)
        {
            _root = root;
            _playerAvatarPrefab = playerAvatarPrefab;
            _npcPrefabs = npcPrefabs;
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

            // P5：Npc 动作状态（占位期仅存储供测试/上层观察——动画表现随真实模型 backlog）。
            // 无条件覆盖安全前提：服务端唯一快照生产者（AoiBroadcaster.BuildForObserver）恒发全掩码；
            // 若未来出现部分位快照，此处须改为按 FieldMask 位守卫（与 _lastKnownYaw 范式对齐）
            view.ActionState = snapshot.ActionState;

            // N5b：存活是**状态**不是边沿事件（D-N1/D-N2——Dead 位每次快照都置位）。
            // 已在视野内的观察者最多要等 4 tick（≈264ms）的静止保活窗口才看到它翻位，
            // 所以这里每次都推给动画驱动，由驱动自行幂等去重（"读状态"而不是"读变化"）。
            view.Dead = snapshot.Dead;
            view.Animator?.Apply(snapshot.ActionState, snapshot.Dead);

            if ((snapshot.FieldMask & (ushort)EntityFieldMask.Position) != 0)
            {
                // beacon 仅带 Position 位：Rotation 是 protobuf 默认 0 而非权威 yaw——
                // 按位掩码契约携带 yaw（脏快照带 Rotation 位时更新 last-known，beacon 沿用）
                if ((snapshot.FieldMask & (ushort)EntityFieldMask.Rotation) != 0)
                    _lastKnownYaw[snapshot.PlayerId] = snapshot.Rotation;

                float yaw = _lastKnownYaw.TryGetValue(snapshot.PlayerId, out var last) ? last : 0f;
                _interpolator.AddSample(snapshot.PlayerId, serverTicks,
                    new Cool.SyncCore.Vec3(snapshot.X, snapshot.Y, snapshot.Z), yaw,
                    snapshot.ActionState, snapshot.Dead);
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
                Remove(id); // 内部同步清 _views + 插值缓冲 + 攻击排程 + _lastSampleTicks
        }

        /// <summary>每帧渲染（nowServerTicks 换算后；renderDelayTicks = 2×ServerTickRateMs 换算）</summary>
        public void UpdateRender(long nowServerTicks, long renderDelayTicks)
        {
            long renderAt = nowServerTicks - renderDelayTicks;

            // 攻击排程**先于**状态写入：PlayAttack 的契约是先写 MonsterState=3 再打 Trigger，
            // 排程项到点必须在自己这一帧抢先落地；反过来的话同一帧的样本会排在挥砍之后写参数
            // （受击/死亡样本会被这一刀盖回去，locomotion 样本也会把刚起手的挥砍往回拉）。
            _dueAttacks.Clear();
            _attacks.Drain(renderAt, _dueAttacks);
            foreach (var due in _dueAttacks)
                if (_views.TryGetValue(due.EntityId, out var attacker))
                    attacker.NpcAnimator?.PlayAttack(due.Variant);

            foreach (var kv in _views)
            {
                // 位置/朝向插值；状态（actionState/dead）不在这里取——它由下面单独按
                // 阶梯规则采样后喂给怪物驱动（与插值共用同一个 renderAt）
                if (_interpolator.TryGetRenderState(kv.Key, nowServerTicks, renderDelayTicks,
                    out var pos, out var rot, out _, out _))
                {
                    kv.Value.Position = pos;
                    kv.Value.Rotation = rot;
                }

                if (kv.Value.NpcAnimator != null
                    && _interpolator.TryGetSampleState(kv.Key, renderAt, out var sampled, out var sampledDead))
                    kv.Value.NpcAnimator.ApplyState(sampled, sampledDead);
            }
        }

        /// <summary>
        /// 服务端裁定的一次挥砍（CastBroadcast）——排到该时间戳对应的渲染时刻再播
        /// （变体由时间戳推出，见 NpcAttackSchedule）。
        /// 只对**已存在且带驱动**的怪物排程：球体降级没有可驱动的东西，排了也只能空转；
        /// 而视图还不存在（广播先于快照到达）时同样丢弃——这里不建视图，视图的生命周期由快照流独占。
        /// </summary>
        public void ScheduleAttack(long casterId, long serverTimestamp)
        {
            if (!_views.TryGetValue(casterId, out var view) || view.Kind != (byte)EntityKind.Npc) return;
            if (view.NpcAnimator == null) return;   // 球体降级：没有可驱动的东西
            _attacks.Schedule(casterId, serverTimestamp);
        }

        /// <summary>采出某视图在 renderAt 处的状态（阶梯）。供 UpdateRender 与测试使用。</summary>
        public bool TrySampleNpcState(long entityId, long renderAt, out byte actionState, out bool dead)
        {
            actionState = 0;
            dead = false;
            return _interpolator.TryGetSampleState(entityId, renderAt, out actionState, out dead);
        }

        public bool Remove(long playerId)
        {
            if (_views.TryGetValue(playerId, out var view))
            {
                DestroyView(view.GameObject);
                _views.Remove(playerId);
                _interpolator.Remove(playerId); // 插值缓冲同步清理（防重连同 ID 残留旧样本）
                _attacks.Clear(playerId); // 待播挥砍同步清理（视图没了，没人可驱动）
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
            _attacks.ClearAll(); // 与视图同生共死：宿主销毁/退出世界后不得再播任何挥砍
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
            // 怪物：宿主接线的目录命中 → 真模型 + 动画驱动；未命中/未接线 → 球体降级
            // （与真人分支同款"缺配置即降级"，名字都携带实体身份与模板 id）。
            // 注：不访问 Renderer 组件着色——非 MonoBehaviour 类禁用 GetComponent（项目规则）。
            if (snapshot.Kind == (byte)EntityKind.Npc)
            {
                if (_npcPrefabs != null && _npcPrefabs.TryGetValue(snapshot.TemplateId, out var prefab) && prefab != null)
                {
                    var model = Object.Instantiate(prefab, _root, false);
                    // 名字**保留预制体名作前缀**再补实体身份：验收/诊断要靠前缀认"这条是真模型还是球体"
                    model.name = $"{prefab.name}_{playerId}_T{snapshot.TemplateId}";
                    // AddComponent 同步跑 Awake——与真人路径的 avatar.AddComponent<RemoteEntityAnimator>() 同惯例：
                    // 驱动在 Awake 里用 GetComponentInChildren 找到模型自带的 Animator。本类是普通类，
                    // 项目规则禁止在非 MonoBehaviour 上下文 GetComponent，所以驱动只能这样加（预制体本身保持纯美术）。
                    var driver = model.AddComponent<NpcEntityAnimator>();
                    return new RemoteEntityView(model, snapshot.Kind, snapshot.TemplateId, npcAnimator: driver);
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // 降级占位（目录未命中/未接线）
                go.name = $"Monster_{playerId}_T{snapshot.TemplateId}";
                go.transform.SetParent(_root, false);
                return new RemoteEntityView(go, snapshot.Kind, snapshot.TemplateId);
            }

            // 压测 bot：胶囊占位。**绝不能上角色模型**——`--bots 100` 是 P3（AOI）演示的前提，
            // 100 个蒙皮网格 + Animator 会在手机浏览器上直接压垮帧率（D-P5-7 / R2）
            if (snapshot.Kind == (byte)EntityKind.PlayerBot)
            {
                var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                capsule.name = $"Bot_{playerId}";
                capsule.transform.SetParent(_root, false);
                return new RemoteEntityView(capsule, snapshot.Kind);
            }

            // 真人玩家：角色预制体 + Animator（D-P5-7）——你和面试官互相看到的应该是真角色
            if (_playerAvatarPrefab == null)
            {
                // 未接线（测试宿主 / 没配场景的宿主）：退化为占位。**不 `Instantiate(null)`**
                // ——那会抛异常并把整条快照消费链带崩（远程玩家直接不渲染）。
                if (!_warnedMissingAvatar)
                {
                    _warnedMissingAvatar = true;
                    Debug.LogWarning("[Sync] remote player avatar prefab not wired; " +
                                     "real players fall back to capsule placeholders");
                }
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                fallback.name = $"RemotePlayer_{playerId}";
                fallback.transform.SetParent(_root, false);
                return new RemoteEntityView(fallback, snapshot.Kind);
            }

            var avatar = Object.Instantiate(_playerAvatarPrefab, _root, false);
            avatar.name = $"RemotePlayer_{playerId}";
            // AddComponent 同步跑 Awake：3C 装备在同一个 Start/Update 之前就被停掉（见该类注释）
            var animator = avatar.AddComponent<RemoteEntityAnimator>();
            return new RemoteEntityView(avatar, snapshot.Kind, animator: animator);
        }
    }

    /// <summary>远程实体渲染视图（P1 占位；P5 加 Npc 元数据；N5b 加真人角色与动画驱动）</summary>
    public sealed class RemoteEntityView
    {
        public GameObject GameObject { get; }
        /// <summary>实体种类（EntityKind；getter-only——Unity 无 IsExternalInit，禁用 init，P4 偏差同教训）</summary>
        public byte Kind { get; }
        /// <summary>Npc 模板 ID（Kind=Npc 时有效；玩家恒 0）</summary>
        public int TemplateId { get; }
        /// <summary>动作/战斗状态（Npc=NpcAnimationState；玩家恒 0）</summary>
        public byte ActionState { get; set; }
        /// <summary>服务端权威存活状态（D-N1：命名 Dead——protobuf 零默认让"活着"不占字节）</summary>
        public bool Dead { get; set; }
        /// <summary>远程动画驱动（只有真人角色有；占位为 null）</summary>
        public RemoteEntityAnimator? Animator { get; }
        /// <summary>怪物动画驱动（Kind=Npc 且预制体带组件时非空；球体占位为 null）</summary>
        public NpcEntityAnimator? NpcAnimator { get; }

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

        public RemoteEntityView(GameObject go, byte kind = 0, int templateId = 0,
            RemoteEntityAnimator? animator = null, NpcEntityAnimator? npcAnimator = null)
        {
            GameObject = go;
            Kind = kind;
            TemplateId = templateId;
            Animator = animator;
            NpcAnimator = npcAnimator;
        }
    }
}
