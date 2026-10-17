using System.Collections.Generic;
using Cool.Protocol;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 远程实体注册表 —— 管理远程实体渲染对象（N5b 起按 `EntityKind` 分两支：
    /// 玩家类（真人 + bot）= 外观目录 → 角色预制体 + Animator，怪物 = 模型目录 / 球体占位；
    /// P2 快照插值渲染：RemoteInterpolator 缓冲 2-tick 延迟插值，不再直接定位）。生成/销毁由快照流驱动。
    /// </summary>
    public sealed class RemoteEntityRegistry
    {
        private const long ExpiryTicks = 12_000_000L; // 1.2s 无快照即视为出视野/离线（AOI 停流语义；≫ 静止保活 4 tick ≈ 267ms）

        /// <summary>地形对象名（DemoDay 的地形 MeshCollider 所在对象）——落地采样的解析锚点</summary>
        internal const string TerrainObjectName = "LowpolyTerrain";

        /// <summary>
        /// 落地采样复用半径（米）。**是量出来的，不是拍的**：按"网格 + 每格采一次"的做法，
        /// 0.5m 网格在本场景斜坡上实测偏差到 0.0159m、0.1m 网格到 0.0311m（湖东岸堤坡实测坡度 ~49%
        /// ——10cm 就落差 4.9cm），都超过 1cm 的验收线 ⇒ 改成**按实体游标**：挪动超过本半径就重采。
        /// 残差 ≤ 坡度 × 半径×√2，49% 坡 ⇒ ≤ 7mm ✓；静止的怪（含位置抖动 < 1cm）零射线，
        /// 成本 ∝ **走过的距离**（与帧率无关），不是每帧每实体一次。
        /// </summary>
        private const float GroundSampleRadius = 0.01f;

        /// <summary>落地射线：高空向下、400m 长（Map 落差仅 ~0.7m，留足余量覆盖树/山）</summary>
        private const float GroundRayStartY = 200f;
        private const float GroundRayLength = 400f;

        /// <summary>
        /// 速度反推的钳位上限（= 步频上限 1.5 × 标称冲刺速度 8.0 —— design §4 速度反推）。
        /// **由常量推导，别写死 12f**：写死会在标称跑速调整时静默漂移——守护用例只钉
        /// `Nominal*` 与本地 `CharacterController` 的一致性，钉不到这个派生值。
        /// </summary>
        private const float SpeedClamp = RemoteEntityAnimator.MaxLocomotionSpeed * RemoteEntityAnimator.NominalSprintSpeed;

        private readonly Dictionary<long, RemoteEntityView> _views = new Dictionary<long, RemoteEntityView>();
        private readonly RemoteInterpolator _interpolator = new RemoteInterpolator();
        private readonly Dictionary<long, float> _lastKnownYaw = new Dictionary<long, float>();
        private readonly Dictionary<long, long> _lastSampleTicks = new Dictionary<long, long>();
        private readonly Transform _root;
        /// <summary>玩家类实体（真人 + bot）的外观目录——null 时退化为胶囊占位，见 CreateView</summary>
        private readonly IReadOnlyList<AppearanceEntry>? _appearances;
        /// <summary>怪物模型目录（templateId → prefab，宿主接线）——null 或未命中时怪物退化为球体占位，见 CreateView</summary>
        private readonly IReadOnlyDictionary<int, GameObject>? _npcPrefabs;
        private readonly NpcAttackSchedule _attacks = new NpcAttackSchedule();
        /// <summary>本帧到点的挥砍（Drain 的契约是**只追加**、不清空调用方列表，所以每帧复用它、先 Clear）</summary>
        private readonly List<NpcAttackSchedule.DueAttack> _dueAttacks = new List<NpcAttackSchedule.DueAttack>();
        private bool _warnedMissingAvatar;

        /// <summary>落地采样游标（每实体一份）：上次采样的 XZ + 结果，见 <see cref="GroundY"/></summary>
        private readonly Dictionary<long, GroundSample> _groundSamples = new Dictionary<long, GroundSample>();
        /// <summary>采过的那具地形碰撞体（来自物理查询本身——本类是普通类，不许 GetComponent）</summary>
        private Collider? _terrainCollider;
        /// <summary>解析只做一次（场景里没有地形就一直是 null，不再重试）</summary>
        private bool _terrainResolveAttempted;
        private bool _warnedNoGround;

        /// <summary>分档策略（宿主可覆盖预算/阈值；URL 旋钮见 TierQuery）</summary>
        public RemoteTierPolicy TierPolicy { get; } = new RemoteTierPolicy();

        /// <summary>
        /// Npc 落地采样的层掩码（宿主可覆盖，默认 = Default 层——本项目的地形层：DemoDay 的
        /// `LowpolyTerrain` 在 layer 0，与玩家 prefab 的 `GroundLayer`（m_Bits 1）同惯例）。
        /// **掩码本身挡不住树/石头**：它们同在 Default（探针实测 Tree1/Tree2 带 CapsuleCollider、
        /// 离地 3.1m / 6.0m）⇒ 采样还要校验命中体是地形（见 <see cref="TrySampleTerrain"/>）。
        /// </summary>
        public LayerMask GroundMask { get; set; } = 1;

        /// <summary>
        /// 地形根（落地采样锚点）。默认懒解析：按 <see cref="TerrainObjectName"/> 在场景里找；
        /// 测试注入桩用（**不依赖当前打开的场景里有没有真地形**——EditMode 测试跑在打开的场景里）。
        /// </summary>
        internal Transform? TerrainRoot { get; set; }

        /// <summary>
        /// 观察者位置提供者（本地玩家位置，与 AOI 同心）。**未注入、或本帧返回 null = 不分档
        /// （保持全 Full）**——测试宿主与既有行为不受影响（延续"缺配置即降级"的惯例）。
        /// 返回 null 的场景：本地角色还没装配（`_localEntry` 为空）——此时拿不到有意义的距离。
        /// </summary>
        public System.Func<Vector3?>? ObserverPositionProvider { get; set; }

        private readonly List<TierCandidate> _tierCandidates = new List<TierCandidate>();
        private readonly List<RemoteTier> _tierResults = new List<RemoteTier>();

        public int Count => _views.Count;

        /// <summary>当前 Full 档视图数（诊断读数用）</summary>
        public int FullCount { get; private set; }
        /// <summary>当前 Proxy 档视图数（诊断读数用）</summary>
        public int ProxyCount { get; private set; }

        /// <summary>当前视图实体 ID 集合（T7 AOI 验收统计用）</summary>
        public IEnumerable<long> PlayerIds => _views.Keys;

        /// <param name="appearances">
        /// 玩家类实体（真人与 bot）的外观目录。**省略 = 全部退化为胶囊占位**
        /// （未接线的测试宿主与场景走这条，与怪物分支同款"缺配置即降级"）。
        /// </param>
        /// <param name="npcPrefabs">
        /// 怪物模型目录：templateId → 预制体（宿主从 `ClientNetworkHost._npcPrefabs` 建表传入）。
        /// 省略/未命中 = 球体占位（未接线的测试宿主与场景走这条）——与玩家类分支同款"缺配置即降级"。
        /// </param>
        public RemoteEntityRegistry(Transform root,
            IReadOnlyList<AppearanceEntry>? appearances = null,
            IReadOnlyDictionary<int, GameObject>? npcPrefabs = null)
        {
            _root = root;
            _appearances = appearances;
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
            // 只在 Full 档驱动：降档后写参数是白费（且 Animator 已随 FullRoot 禁用）
            if (view.Tier == RemoteTier.Full)
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

            // 攻击排程**先于**状态写入。两种顺序的同帧**终值**其实一样（PlayAttack 当场置忙位并写
            // MonsterState=3，反过来的话状态样本先写、随后仍被 PlayAttack 的写入收尾）——真正分叉的
            // 只有**同一帧又采到 Hit/Death** 这一种：排程在前时那条样本排在后、当场把挥砍的 3 盖回去
            // （并释放忙位），受击/死亡当帧可见；反过来的话这一帧会被挥砍盖住，受击表现晚一帧。
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
                    // D17：速度由**渲染位移**反推（动画要匹配屏幕上的移动，且免动协议/插值器）。
                    // 钳位上限 = 1.5 × 冲刺速度：重生/服务器校正的瞬移不该让 Blend 跳到跑档。
                    kv.Value.TrackSpeed(pos, renderAt, SpeedClamp);

                    // Npc 落地吸附：服务端布局是 2D 的（XZ 才有效，y 恒 0），而 DemoDay 的地面在
                    // 这些点位是 -0.113 ~ +0.624m ⇒ 不吸附模型会埋进地里。只换 y，且写在 TrackSpeed
                    // **之后**：速度反推照旧吃插值器的原始位置（插值/速度/分档逻辑一行不动）。
                    // 真人/bot 视图不碰（客户端权威，y 由本地物理决定）。
                    if (kv.Value.Kind == (byte)EntityKind.Npc)
                    {
                        var snapped = kv.Value.Position;
                        snapped.y = GroundY(kv.Key, pos.x, pos.z, pos.y);
                        kv.Value.Position = snapped;
                    }
                }

                if (kv.Value.NpcAnimator != null
                    && TrySampleNpcState(kv.Key, renderAt, out var sampled, out var sampledDead))
                    kv.Value.NpcAnimator.ApplyState(sampled, sampledDead);
            }

            // 分档（design §4）：插值写完位置之后、驱动写入之前——本帧的档位就是这个位置算出来的。
            // 未注入观察者（测试宿主）= 建即 Full，什么都不用做；已注入但本帧拿不到位置
            //（本地角色还没装配 / 观察者模式）= 不分档，**把玩家类视图显式保持/恢复为 Full**——
            // "保持全 Full"说的是视图状态，不只是读数（注入态下 CreateView 先建的是 Proxy）。
            if (ObserverPositionProvider == null)
            {
                RecountTiers();
            }
            else if (ObserverPositionProvider.Invoke() is Vector3 observer)
            {
                ApplyTiers(observer);
            }
            else
            {
                foreach (var kv in _views)
                    if (kv.Value.Kind != (byte)EntityKind.Npc)
                        SetTier(kv.Value, RemoteTier.Full);   // 已 Full 的走早退
                RecountTiers();
            }

            // 行走驱动写入（此时位置与速度都已就位）。同样只在 Full 档写：降档后是白费
            //（Animator 已随 FullRoot 禁用）。
            foreach (var kv in _views)
                if (kv.Value.Tier == RemoteTier.Full)
                    kv.Value.Animator?.ApplyLocomotion(kv.Value.Speed);
        }

        /// <summary>每帧分档：距离（迟滞）+ 预算（最近优先）→ 目标档 → 换档。</summary>
        private void ApplyTiers(Vector3 observer)
        {
            _tierCandidates.Clear();
            foreach (var kv in _views)
            {
                // 怪物不参与分档（design D15）：它们是玩法实体（碰撞体/受击/攻击排程耦合），
                // 且决策点共用——将来要扩，加一个字段即可。
                if (kv.Value.Kind == (byte)EntityKind.Npc) continue;
                if (kv.Value.FullRoot == null && kv.Value.ProxyRoot == null) continue;  // 纯降级占位视图

                float dx = kv.Value.Position.x - observer.x;
                float dz = kv.Value.Position.z - observer.z;
                _tierCandidates.Add(new TierCandidate
                {
                    PlayerId = kv.Key,
                    DistanceSqr = dx * dx + dz * dz,
                    CurrentlyFull = kv.Value.Tier == RemoteTier.Full
                });
            }

            TierPolicy.Decide(_tierCandidates, _tierResults);

            for (int i = 0; i < _tierCandidates.Count; i++)
                if (_views.TryGetValue(_tierCandidates[i].PlayerId, out var view))
                    SetTier(view, _tierResults[i]);

            RecountTiers();
        }

        /// <summary>
        /// 诊断读数（Full/Proxy 计数）。Npc 跳过（与分档同样的排除口径）；`Tier != Full`
        /// （含 Tier 为 null 的纯降级占位视图）都记代理——读数与所见一致。
        /// 单一实现：分档路径与未分档路径都调它（两处各写一份口径会悄悄分叉）。
        /// </summary>
        private void RecountTiers()
        {
            FullCount = 0;
            ProxyCount = 0;
            foreach (var kv in _views)
            {
                if (kv.Value.Kind == (byte)EntityKind.Npc) continue;
                if (kv.Value.Tier == RemoteTier.Full) FullCount++;
                else ProxyCount++;
            }
        }

        /// <summary>
        /// 换档（惰性建表现）。降档 = `FullRoot.SetActive(false)`——**整棵禁用**：Animator、
        /// 蒙皮、渲染器全停，这正是表示分级相对于网格 LOD 的价值（LOD 省不到这几项）。
        /// **不销毁**（会再升回来）；升级时强制重写一次驱动参数（Animator 禁用即重置状态），
        /// 并重新归零 Attack/Hit/Defend 层权重（禁用重置含层权重——见 ResetLayerWeights）。
        /// </summary>
        private void SetTier(RemoteEntityView view, RemoteTier tier)
        {
            if (view.Tier == tier && (tier == RemoteTier.Full ? view.FullRoot != null : view.ProxyRoot != null))
                return;

            view.Tier = tier;

            if (tier == RemoteTier.Full)
            {
                if (view.FullRoot == null && view.FullPrefab != null)
                {
                    var avatar = Object.Instantiate(view.FullPrefab, view.GameObject.transform, false);
                    avatar.name = "Full";
                    view.FullRoot = avatar;
                    // AddComponent 同步跑 Awake：归零层权重、停掉 3C 装备，赶在同一个 Start/Update 之前
                    view.Animator = avatar.AddComponent<RemoteEntityAnimator>();
                }
                if (view.FullRoot == null)
                {
                    // 没有 Full 资产（该条目只配了代理）：档位如实落回 Proxy，别让 Tier 与眼前所见脱节
                    view.Tier = RemoteTier.Proxy;
                    return;
                }
                view.FullRoot.SetActive(true);
                view.ProxyRoot?.SetActive(false);
                view.Animator!.InvalidateDedup();
                view.Animator!.ResetLayerWeights();
                // 升档当场**回放锁存的状态**（规格 §5："升档：失效 _lastKey 并强制重写一次"）：
                // 只"失效"的话重写要等下一条快照——静止实体保活 4 tick（≈264ms），而
                // `m_KeepAnimatorStateOnDisable=0` 会把 Animator 重置 ⇒ 死亡/受击中的实体
                // 从 Proxy 升 Full 时会先站直最多 264ms。游标刚被 InvalidateDedup 置 -1，必定写下去。
                view.Animator.Apply(view.ActionState, view.Dead);
            }
            else
            {
                if (view.ProxyRoot == null)
                {
                    // 代理：目录条目配了 `Proxy` 预制体就用它（美术产出 billboard/impostor 的可插拔
                    // 接缝——换档机制一行不改），留空 = 运行时胶囊
                    var proxy = view.ProxyPrefab != null
                        ? Object.Instantiate(view.ProxyPrefab)
                        : GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    StripCollider(proxy);   // D19：整棵代理层级都不许有启用的 Collider
                    proxy.name = "Proxy";
                    proxy.transform.SetParent(view.GameObject.transform, false);
                    view.ProxyRoot = proxy;
                }
                view.ProxyRoot.SetActive(true);
                view.FullRoot?.SetActive(false);
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
                _groundSamples.Remove(playerId); // 落地采样游标同生共死（防重连同 ID 残留旧采样）
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
            _groundSamples.Clear(); // 与视图同生共死（换世界后地形可能不同）
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

        /// <summary>
        /// Npc 视图的落地高度：对地形向下射线采样一次。**未命中 = 原样返回 fallbackY（服务端 y），
        /// 不回退到 0**——没有地形的区域（测试宿主、地形范围外）回退 0 会把怪压到地下。
        ///
        /// 三条约束（都是实测得来的）：
        ///  1. **必须只认地形**：裸射线会先打到树/石头（探针实测 (-25,-15) 命中 Tree2 离地 6m、
        ///     (20,-20) 命中 Tree1 离地 3.1m）⇒ 命中体不是地形就会把怪吸到树冠上；
        ///  2. **掩码分不开地形与树**：DemoDay 里地形与树/石头同在 Default 层，所以掩码只作粗筛，
        ///     身份校验（<see cref="IsTerrainHit"/>）才是精筛；
        ///  3. **复用按实体游标，不按粗网格**：地形静态 ⇒ 挪窝不超过 <see cref="GroundSampleRadius"/>
        ///     就复用上次采样（静止的怪零射线、成本 ∝ 走过的距离）。粗网格（0.5m / 0.1m）曾被
        ///     实测否掉：本场景堤坡 ~49%，格内漂移能到 1.6cm / 3.1cm，越过 1cm 验收线。
        /// </summary>
        /// <param name="entityId">采样游标的归属者（每实体一份）。</param>
        internal float GroundY(long entityId, float x, float z, float fallbackY)
        {
            if (_groundSamples.TryGetValue(entityId, out var last)
                && Mathf.Abs(x - last.X) <= GroundSampleRadius
                && Mathf.Abs(z - last.Z) <= GroundSampleRadius)
                return last.HasGround ? last.Y : fallbackY;

            bool hit = TrySampleTerrain(x, z, out float groundY);
            _groundSamples[entityId] = new GroundSample { X = x, Z = z, Y = groundY, HasGround = hit };
            if (hit) return groundY;

            if (!_warnedNoGround)
            {
                _warnedNoGround = true;   // 只打一次：没地面是环境属性（测试宿主/出图），不是每帧事件
                Debug.LogWarning("[Sync] no ground under npc sample at (" + x + ", " + z +
                                 "); npc views keep their server y");
            }
            return fallbackY;
        }

        /// <summary>采样游标：上次采样的位置与结果（<see cref="HasGround"/>=false 时 <see cref="Y"/> 无意义）</summary>
        private struct GroundSample
        {
            public float X;
            public float Z;
            public float Y;
            public bool HasGround;
        }

        /// <summary>一次地形采样。false = 该点没有地面（未解析出地形 / 射线没打中地形）。</summary>
        private bool TrySampleTerrain(float x, float z, out float groundY)
        {
            groundY = 0f;
            if (TerrainRoot == null && !_terrainResolveAttempted)
            {
                _terrainResolveAttempted = true;
                var found = GameObject.Find(TerrainObjectName);
                TerrainRoot = found != null ? found.transform : null;   // null = 场景里没有地形，不再重试
            }
            if (TerrainRoot == null) return false;

            var ray = new Ray(new Vector3(x, GroundRayStartY, z), Vector3.down);

            // 快路径：掩码命中且命中体就是地形（无遮挡时一条射线就够）
            if (Physics.Raycast(ray, out var hit, GroundRayLength, GroundMask) && IsTerrainHit(hit.collider))
            {
                _terrainCollider = hit.collider;
                groundY = hit.point.y;
                return true;
            }

            // 兜底：被树冠/石头截胡（掩码分不开：它们与地形同在 Default）⇒ 在全部命中里按身份挑地形，
            // 取最上面那层（地形网面可能被射线穿过一次以上，取最近的一条才是脚下这面）
            var hits = Physics.RaycastAll(ray, GroundRayLength, GroundMask);
            float best = float.MaxValue;
            bool found2 = false;
            for (int i = 0; i < hits.Length; i++)
            {
                if (!IsTerrainHit(hits[i].collider) || hits[i].distance >= best) continue;
                best = hits[i].distance;
                groundY = hits[i].point.y;
                _terrainCollider = hits[i].collider;
                found2 = true;
            }
            return found2;
        }

        /// <summary>
        /// 命中体是不是地形：按解析出的地形根做**身份**判断（不是按名字，也不是按碰撞体类型——
        /// 石头/树桩/大蘑菇同样是 MeshCollider）。碰撞体引用来自物理查询本身：本类是普通类，
        /// 项目规则禁止在这里 GetComponent。
        /// </summary>
        private bool IsTerrainHit(Collider? collider)
        {
            if (collider == null) return false;
            if (_terrainCollider != null && collider == _terrainCollider) return true;   // 认过的地形，直接命中
            if (TerrainRoot == null) return false;
            var t = collider.transform;
            return t == TerrainRoot || t.IsChildOf(TerrainRoot);
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
                    // 视图是**表现**，不得参与伤害链：摘掉预制体自带的玩法组件（见 StripGameplay）。
                    StripGameplay(model);
                    // AddComponent 同步跑 Awake——与真人路径的 avatar.AddComponent<RemoteEntityAnimator>() 同惯例：
                    // 驱动在 Awake 里用 GetComponentInChildren 找到模型自带的 Animator。本类是普通类，
                    // 项目规则禁止在非 MonoBehaviour 上下文 GetComponent，所以驱动只能这样加（预制体本身保持纯美术）。
                    var driver = model.AddComponent<NpcEntityAnimator>();
                    // 纯客户端身体碰撞体（2026-10-13）：让本地玩家撞不过去（Boss 不再能穿过）。
                    // 只挡本地玩家——层矩阵/查询排查见 NpcBodyCollider 的类注释与验收报告。
                    model.AddComponent<NpcBodyCollider>();
                    return new RemoteEntityView(model, snapshot.Kind, snapshot.TemplateId, npcAnimator: driver);
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);   // 降级占位（目录未命中/未接线）
                go.name = $"Monster_{playerId}_T{snapshot.TemplateId}";
                go.transform.SetParent(_root, false);
                return new RemoteEntityView(go, snapshot.Kind, snapshot.TemplateId);
            }

            // 玩家类（真人 + bot）：**同一条路径**（design D1/D6）——外观目录查表 → 包装根 +
            // 惰性建表现（Full 档才实例化/挂驱动，见 SetTier）。
            // 旧的两支（真人 = 预制体 / bot = 胶囊）已合并：AOI 之后同屏实体数由局部密度决定，
            // 实测 `--bots 100` 的同屏峰值只有 5~6 个，'100 个蒙皮网格' 从来没发生过。
            var entry = snapshot.Kind == (byte)EntityKind.PlayerBot
                ? AppearanceResolver.EntryAt(_appearances, AppearanceResolver.BotIndex(playerId, _appearances?.Count ?? 0))
                : AppearanceResolver.FindId(_appearances, AppearanceResolver.DefaultId);
            var fullPrefab = entry?.Full;
            if (fullPrefab == null)
            {
                // 未接线 / 未命中：占位降级（**不 `Instantiate(null)`**——那会抛异常并把整条
                // 快照消费链带崩，远程玩家直接不渲染）
                if (!_warnedMissingAvatar)
                {
                    _warnedMissingAvatar = true;
                    Debug.LogWarning("[Sync] no appearance configured; remote players/bots " +
                                     "fall back to capsule placeholders");
                }
                var fallback = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                StripCollider(fallback);   // D19：CreatePrimitive 自带 Collider，远端实体不该挡人
                fallback.name = KindName(playerId, snapshot.Kind);
                fallback.transform.SetParent(_root, false);
                return new RemoteEntityView(fallback, snapshot.Kind);
            }

            // 包装根：位置/朝向写它，两个表现子物体挂它下面（分档换表现——见 SetTier）
            var viewRoot = new GameObject(KindName(playerId, snapshot.Kind));
            viewRoot.transform.SetParent(_root, false);
            var view = new RemoteEntityView(viewRoot, snapshot.Kind);
            view.FullPrefab = fullPrefab;
            view.ProxyPrefab = entry?.Proxy;   // 留空 ⇒ Proxy 档运行时生成胶囊（见 SetTier）
            // 无观察者（测试宿主/未接线）= 不分档，直接建 Full——既有测试在 ApplySnapshot 之后
            // 立刻断言 Animator，不能等 UpdateRender。
            SetTier(view, ObserverPositionProvider == null ? RemoteTier.Full : RemoteTier.Proxy);
            return view;
        }

        /// <summary>视图名：验收/诊断靠它认实体身份（保留既有命名）</summary>
        private static string KindName(long playerId, byte kind)
            => kind == (byte)EntityKind.PlayerBot ? $"Bot_{playerId}" : $"RemotePlayer_{playerId}";

        /// <summary>
        /// 去掉**整棵层级**上的碰撞体（D19：远端实体是视图不是物理体）。代理可能来自美术预制体
        /// ——碰撞体可能挂在根上，也可能在子物体上，都要处理。**先禁用再销毁**：运行时 `Destroy`
        /// 帧末才生效，只销毁的话"创建帧内"仍有一个启用碰撞体（层矩阵挡不住 OverlapSphere 这类查询）。
        /// </summary>
        private static void StripCollider(GameObject go)
        {
            foreach (var collider in go.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                if (Application.isPlaying) Object.Destroy(collider);
                else Object.DestroyImmediate(collider);
            }
        }

        /// <summary>
        /// 摘掉 npc 视图上预制体自带的**玩法组件** —— 视图只负责表现，本地伤害链不得够得着它。
        ///
        /// 原缺陷（2026-10-17，联网模式下打怪必抛）：`SlimePBR` / `TurtleShellPBR` 这两个预制体
        /// 带着完整的 `MonsterEntity`（`IDamageable` + `IEffectTarget`）。视图路径从不调 `Init`，
        /// 于是视图上那个实体的 `_stats` 是 null；而技能判定走
        /// `Physics.OverlapSphereNonAlloc(shape.TargetMask)` + `TryGetComponent&lt;IEffectTarget&gt;()`
        /// **不是注册表**，因此预制体那个 enabled 的 CapsuleCollider 一路把未初始化的实体送进了
        /// `TakeDamage`（`MonsterEntity.cs:174` 第一句 NRE）。DogKnight / Spider 预制体不带玩法组件，
        /// 这正是该缺陷「常出现但不是必现」的原因。
        ///
        /// 每个组件都「先禁再销毁」，理由与 StripCollider 逐字相同：`Destroy` 帧末才生效，
        /// 创建帧内它仍在；而 `enabled = false` **挡不住接口调用**，所以那一帧由
        /// `MonsterEntity.TakeDamage` / `IsAlive` 的 `_stats` 判空兜住（两处配合才闭环）。
        ///
        /// **只摘玩法组件**：模型、Animator、渲染与 NpcEntityAnimator 驱动全部保留。
        /// 其它组件（HitZone / AttackHitbox / NavMeshAgent）在视图上的行为未调查、也未被该缺陷
        /// 触及，故不动 —— 顺手改会改变既有表现。
        /// </summary>
        private static void StripGameplay(GameObject model)
        {
            foreach (var entity in model.GetComponentsInChildren<Hotfix.GameSystems.Monster.MonsterEntity>(true))
            {
                entity.enabled = false;
                if (Application.isPlaying) Object.Destroy(entity);
                else Object.DestroyImmediate(entity);
            }
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
        /// <summary>当前档位（null = 表现尚未建立：无 Full 资产的纯降级占位视图）。
        /// **注意**：这种视图被 SetTier 碰过之后会落到 `Proxy`（没有 Full 资产时升档分支
        /// 如实退回 Proxy，见 SetTier）——所以 `Tier == null` 只说明"从未参与过换档"，
        /// 不代表读数口径上被排除（`RecountTiers` 把 `Tier != Full` 全记代理）。</summary>
        public RemoteTier? Tier { get; internal set; }
        /// <summary>近档预制体（分档升档时惰性实例化用；null = 无 Full 资产）</summary>
        public GameObject? FullPrefab { get; internal set; }
        /// <summary>远档代理预制体（**可插拔接缝**：美术产出 billboard/impostor 时填进
        /// `AppearanceEntry.Proxy` 即可换掉运行时胶囊；null = 运行时生成胶囊）</summary>
        public GameObject? ProxyPrefab { get; internal set; }
        /// <summary>近档的动画驱动（玩家类视图专用；升档时才存在，纯降级占位为 null。
        /// **bot 在接线态同样有驱动**——不是只有真人有）</summary>
        public RemoteEntityAnimator? Animator { get; internal set; }
        /// <summary>近档表现（模型实例）。null = 尚未建/无 Full 资产</summary>
        public GameObject? FullRoot { get; internal set; }
        /// <summary>远档表现（代理）。null = 尚未建</summary>
        public GameObject? ProxyRoot { get; internal set; }
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

        /// <summary>平滑后的渲染速度（m/s）——由相邻两次插值位置反推（design D17）</summary>
        public float Speed { get; private set; }

        /// <summary>上一次渲染写下的位置（速度反推用）</summary>
        private Vector3 _lastRenderPosition;

        /// <summary>上一次渲染的服务器时刻（&lt; 0 = 尚无，首帧保持上一速度）</summary>
        private long _lastRenderTicks = -1L;

        /// <summary>速度反推 + 记账（registry 的渲染循环每帧调用一次）</summary>
        internal void TrackSpeed(Vector3 position, long renderAt, float maxSpeed)
        {
            float dt = _lastRenderTicks < 0
                ? 0f
                : (renderAt - _lastRenderTicks) / RenderSpeedEstimator.TicksPerSecond;
            Speed = RenderSpeedEstimator.Step(Speed, _lastRenderPosition, position, dt, maxSpeed);
            _lastRenderPosition = position;
            _lastRenderTicks = renderAt;
        }

        public RemoteEntityView(GameObject go, byte kind = 0, int templateId = 0,
            NpcEntityAnimator? npcAnimator = null)
        {
            GameObject = go;
            Kind = kind;
            TemplateId = templateId;
            NpcAnimator = npcAnimator;
        }
    }
}
