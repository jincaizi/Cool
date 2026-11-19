using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Runtime.Meteor;
using Hotfix.GameSystems.Skills.Runtime.Projectile;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>远程投射物的视觉接缝（EditMode 用假实现断言停点/爆炸）。</summary>
    public interface IRemoteProjectileVisual
    {
        bool IsActive { get; }
        void ExplodeAt(Vector3 point);
        void StopAtPoint(Vector3 point);
    }

    /// <summary>火球的远程视觉：爆炸 = 服务端命中点；被"停住"也只可能是爆炸。</summary>
    internal sealed class FireballVisual : IRemoteProjectileVisual
    {
        private readonly FireballProjectile _projectile;

        public FireballVisual(FireballProjectile projectile) => _projectile = projectile;

        public bool IsActive => _projectile != null && _projectile.IsActive;
        public void ExplodeAt(Vector3 point) { if (_projectile != null) _projectile.ExplodeAt(point); }
        public void StopAtPoint(Vector3 point) { if (_projectile != null) _projectile.ExplodeAt(point); }
    }

    /// <summary>剑气的远程视觉：服务端停点 = 沿程停住；没有爆炸形态。</summary>
    internal sealed class SwordQiVisual : IRemoteProjectileVisual
    {
        private readonly SwordQiProjectile _projectile;

        public SwordQiVisual(SwordQiProjectile projectile) => _projectile = projectile;

        public bool IsActive => _projectile != null && _projectile.IsActive;
        public void ExplodeAt(Vector3 point) { if (_projectile != null) _projectile.StopAtPoint(point); }
        public void StopAtPoint(Vector3 point) { if (_projectile != null) _projectile.StopAtPoint(point); }
    }

    /// <summary>
    /// 远程玩家的施法表现（spec §5-P4 客户端侧）：
    ///  - CastBroadcast 到达即播攻击动画（不能等 DamageResult：零命中延迟技能会先看到受击再看到挥砍），
    ///    在渲染位姿生成 CastVFX，并按 (casterId, skillId) 跟踪 10002 剑气 / 20003 火球视觉；
    ///  - 远程 DamageResult 触发停点：HoldToAim 火球在 CastBroadcast 上报的落点爆炸（其余火球用
    ///    HitX/HitZ），被格挡的剑气在 StopX/StopZ 停住；
    ///  - 20004（蓄力爆发）与 30001（陨石）没有飞行体，直接在服务端落点生成释放/陨石表现。
    /// 本地施法者（由本地 SkillExecutor 负责）与 NPC（由 ScheduleAttack 负责）一律跳过。
    /// 线程：全部经 CombatNetworkBridge.Update 在主线程调用。
    /// </summary>
    public sealed class RemoteCastPresenter
    {
        private const float DefaultAttackDuration = 0.8f;
        private const float VfxLifetimeSeconds = 1.5f;

        private sealed class Tracked
        {
            public readonly long CasterId;
            public readonly int SkillId;
            public readonly IRemoteProjectileVisual Visual;
            public readonly bool HasImpact;
            public readonly Vector3 Impact;

            public Tracked(long casterId, int skillId, IRemoteProjectileVisual visual,
                bool hasImpact = false, Vector3 impact = default)
            {
                CasterId = casterId;
                SkillId = skillId;
                Visual = visual;
                HasImpact = hasImpact;
                Impact = impact;
            }
        }

        private sealed class TrackedCast
        {
            public ulong CastId;
            public long CasterId;
            public int SkillId;
            public int ElementId;
            public IRemoteProjectileVisual Visual = null!;
        }

        private readonly RemoteEntityRegistry? _registry;
        private readonly long _localPlayerId;
        private readonly SkillPresentationCatalog? _catalog;
        private readonly Func<SkillData, Vector3, Vector3, IRemoteProjectileVisual?>? _projectileFactory;
        private readonly Action<GameObject, Vector3, Quaternion>? _castVfxSpawner;
        private readonly Action<GameObject, Vector3, Quaternion>? _releaseVfxSpawner;
        private readonly Action<MeteorConfig, DamageBlock, Vector3>? _meteorSpawner;
        private readonly Func<SkillData, Vector3, Vector3, float, IRemoteProjectileVisual?>? _launchProjectileFactory;
        private readonly Func<string>? _sceneIdProvider;
        private readonly Func<long>? _serverNowTicksProvider;
        /// <summary>最近世界快照的服务器 tick（活动施法剩余时长按 EndTick − 它换算）</summary>
        private readonly Func<uint>? _serverTickProvider;
        private readonly List<Tracked> _tracked = new List<Tracked>();
        private readonly Dictionary<ulong, TrackedCast> _castVisuals = new Dictionary<ulong, TrackedCast>();
        private readonly Dictionary<ulong, int> _castSkillIds = new Dictionary<ulong, int>();
        /// <summary>CastId → 施法者实体 id（取消时找到攻击表现所属的玩家视图）</summary>
        private readonly Dictionary<ulong, long> _castCasters = new Dictionary<ulong, long>();
        /// <summary>已取消的 CastId（有界）——晚到的发射/挥砍表现据此跳过；已发射的视觉继续飞行</summary>
        private readonly HashSet<ulong> _cancelledCasts = new HashSet<ulong>();
        /// <summary>已按 ActiveCast/CastBroadcast（两条路径共用）播放过攻击的 CastId（有界）——
        /// 服务端每段相位推进都会 bump 投影，同一 CastId 只播一次，后续 Active/Recovery 更新跳过</summary>
        private readonly HashSet<ulong> _activeCastPlayed = new HashSet<ulong>();
        private readonly Dictionary<ulong, ProjectileStopBroadcast> _pendingStops =
            new Dictionary<ulong, ProjectileStopBroadcast>();
        private readonly HashSet<ulong> _seenLaunches = new HashSet<ulong>();
        private readonly HashSet<ulong> _seenStops = new HashSet<ulong>();

        /// <param name="registry">远程视图来源；为 null 时只走测试接缝（EditMode 假视觉）</param>
        /// <param name="catalog">技能表现目录；为 null 时 OnCastBroadcast 直接返回</param>
        /// <param name="projectileFactory">测试注入；留空走 FireballSpawner/SwordQiSpawner</param>
        /// <param name="castVfxSpawner">测试注入；留空走一次性实例化</param>
        /// <param name="releaseVfxSpawner">测试注入；留空走一次性实例化（20004 落点释放特效）</param>
        /// <param name="meteorSpawner">测试注入；留空走 MeteorSpawner（30001 落点陨石）</param>
        public RemoteCastPresenter(
            RemoteEntityRegistry? registry,
            long localPlayerId,
            SkillPresentationCatalog? catalog,
            Func<SkillData, Vector3, Vector3, IRemoteProjectileVisual?>? projectileFactory = null,
            Action<GameObject, Vector3, Quaternion>? castVfxSpawner = null,
            Action<GameObject, Vector3, Quaternion>? releaseVfxSpawner = null,
            Action<MeteorConfig, DamageBlock, Vector3>? meteorSpawner = null,
            Func<SkillData, Vector3, Vector3, float, IRemoteProjectileVisual?>? launchProjectileFactory = null,
            Func<string>? sceneIdProvider = null,
            Func<long>? serverNowTicksProvider = null,
            Func<uint>? serverTickProvider = null)
        {
            _registry = registry;
            _localPlayerId = localPlayerId;
            _catalog = catalog;
            _projectileFactory = projectileFactory;
            _castVfxSpawner = castVfxSpawner;
            _releaseVfxSpawner = releaseVfxSpawner;
            _meteorSpawner = meteorSpawner;
            _launchProjectileFactory = launchProjectileFactory;
            _sceneIdProvider = sceneIdProvider;
            _serverNowTicksProvider = serverNowTicksProvider;
            _serverTickProvider = serverTickProvider;
        }

        /// <summary>收到远程玩家的施法广播（CombatNetworkBridge.Update 主线程调用）。</summary>
        public void OnCastBroadcast(CastBroadcast broadcast)
        {
            // 记录 CastId → SkillId/施法者，供晚到/独立的 ProjectileLaunchBroadcast 查表现，
            // 以及 CastCancelBroadcast 找到对应的攻击表现。
            if (broadcast.CastId != 0)
                RememberCastSkill(broadcast.CastId, broadcast.SkillId, broadcast.CasterPlayerId);

            // 取消先到：整条施法表现都不补（CastVFX / 非权威剑气 / ReleaseVFX / Meteor）。
            if (broadcast.CastId != 0 && _cancelledCasts.Contains(broadcast.CastId))
                return;

            if (_registry == null || _catalog == null) return;
            if (broadcast.CasterPlayerId == _localPlayerId) return;

            var view = _registry.GetView(broadcast.CasterPlayerId);
            if (view == null || view.Kind != (byte)EntityKind.Player) return;
            if (!_catalog.TryGet(broadcast.SkillId, out var data)) return;

            // 立即播放：伤害结果可能先于下一帧到达，等它再播会让挥砍落后于受击。
            // 攻击起手与 OnActiveCast 共用 _activeCastPlayed：同一 CastId 先到者起手，另一条
            // 只负责 VFX/投射物（下面的 CastVFX/ReleaseVFX/Meteor 分支不受此门影响）。
            // 没有 Animator（Proxy 档）时不起手也不占槽——升 Full 后由基线 ActiveCast 补播。
            var animator = view.Animator;
            bool attackAlreadyPresented = broadcast.CastId != 0
                && _activeCastPlayed.Contains(broadcast.CastId);
            if (!attackAlreadyPresented && animator != null)
            {
                if (broadcast.CastId != 0)
                {
                    if (_activeCastPlayed.Count >= 256) _activeCastPlayed.Clear();
                    _activeCastPlayed.Add(broadcast.CastId);
                }
                animator.PlayAttack(data.AnimatorTrigger, AttackClipDuration(data),
                    broadcast.AttackSpeedMultiplier, broadcast.CastId);
            }

            var presentation = GetPresentation(data);
            if (presentation?.CastVFX != null)
                (_castVfxSpawner ?? DefaultVfxSpawner)(presentation.CastVFX, view.Position, view.Rotation);

            // 起点与本地 SkillExecutor 同源：脚底 + 按朝向旋转的资产偏移
            // （火球缺了它会把地面算低 1.2m，剑气会整段在脚踝高度）。
            Vector3 forward = view.Rotation * Vector3.forward;
            Vector3 origin = ComputeProjectileOrigin(data, view.Position, forward);
            Vector3 impact = new Vector3(broadcast.ImpactX, broadcast.ImpactY, broadcast.ImpactZ);

            // 属性剑气 v1：剑气视觉只由服务端发射描述创建（同一 CastId 只建一次）。
            // 火球/旧服务端（CastId=0）保持原路径。
            bool authoritativeSwordQi = broadcast.CastId != 0 && data.SwordQi != null;
            if (!authoritativeSwordQi)
            {
                bool hasImpact = data is ProjectileSkillData projectileData && projectileData.HoldToAim;
                IRemoteProjectileVisual? visual = _projectileFactory != null
                    ? _projectileFactory(data, origin, forward)
                    : DefaultFactory(data, origin, forward, hasImpact ? impact : (Vector3?)null);
                if (visual != null)
                    _tracked.Add(new Tracked(broadcast.CasterPlayerId, broadcast.SkillId, visual,
                        hasImpact, impact));
            }

            // 落点类技能没有飞行体：在服务端给定的落点直接生成表现
            if (data is ChargedSkillData && presentation?.ReleaseVFX != null)
                (_releaseVfxSpawner ?? DefaultVfxSpawner)(
                    presentation.ReleaseVFX, impact, Quaternion.identity);
            if (data.Meteor != null)
                (_meteorSpawner ?? MeteorSpawner.Spawn)(
                    data.Meteor, data.Damage?.Clone(), impact);
        }

        /// <summary>
        /// 权威剑气发射：按 CastId 唯一创建。晚到发射按服务器时钟补飞行进度；
        /// 终止已先到时不再生成视觉。本人与旁观者走同一路径。
        /// </summary>
        public void OnProjectileLaunch(ProjectileLaunchBroadcast launch)
        {
            if (launch == null) return;
            if (_cancelledCasts.Contains(launch.CastId)) return;
            if (_sceneIdProvider != null && _sceneIdProvider() != launch.CombatSceneId) return;
            if (_castVisuals.ContainsKey(launch.CastId) || !_seenLaunches.Add(launch.CastId)) return;
            PruneSeenLaunches();

            // 终止先到：该发剑气已经结束，不再生成。
            if (_pendingStops.Remove(launch.CastId)) return;

            if (_catalog == null || !_catalog.TryGet(launch.SkillId, out var data) || data.SwordQi == null)
                return;

            Vector3 origin = new Vector3(launch.OriginX, launch.OriginY, launch.OriginZ);
            float radians = launch.Yaw * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            float startDistance = ComputeStartDistance(launch);
            if (startDistance >= launch.Range) return;

            IRemoteProjectileVisual? visual = _launchProjectileFactory != null
                ? _launchProjectileFactory(data, origin, direction, startDistance)
                : DefaultFactory(data, origin, direction, null, startDistance, launch.ElementId);
            if (visual == null) return;

            _castVisuals[launch.CastId] = new TrackedCast
            {
                CastId = launch.CastId,
                CasterId = launch.CasterPlayerId,
                SkillId = launch.SkillId,
                ElementId = launch.ElementId,
                Visual = visual
            };
        }

        /// <summary>权威剑气终止：只停指定 CastId；先到时缓存，后续发射直接跳过。</summary>
        public void OnProjectileStop(ProjectileStopBroadcast stop)
        {
            if (stop == null) return;
            if (_sceneIdProvider != null && _sceneIdProvider() != stop.CombatSceneId) return;
            if (!_seenStops.Add(stop.CastId)) return;
            PruneSeenStops();

            if (_castVisuals.TryGetValue(stop.CastId, out var tracked))
            {
                tracked.Visual.StopAtPoint(new Vector3(stop.StopX, 0f, stop.StopZ));
                _castVisuals.Remove(stop.CastId);
                _castSkillIds.Remove(stop.CastId);
                return;
            }

            _pendingStops[stop.CastId] = stop;
            if (_pendingStops.Count > 64)
                _pendingStops.Clear();
        }

        /// <summary>
        /// 权威取消（前摇/持续施法被打断）：只停止施法者的攻击表现，已发射的剑气视觉
        /// (<see cref="_castVisuals"/>) 继续飞行。CastId 记入有界取消集，供晚到的
        /// CastBroadcast/ProjectileLaunchBroadcast 跳过补播。
        /// </summary>
        public void OnCastCancel(CastCancelBroadcast cancel)
        {
            if (cancel == null || cancel.CastId == 0) return;
            if (_cancelledCasts.Count >= 256) _cancelledCasts.Clear();
            _cancelledCasts.Add(cancel.CastId);
            if (_castCasters.TryGetValue(cancel.CastId, out var casterId)
                && _registry?.GetView(casterId) is { } view)
                view.Animator?.CancelAttack(cancel.CastId);
            // 已发射的 _castVisuals 保留飞行；取消只停止攻击表现。
        }

        /// <summary>
        /// 活动施法投影（CombatStateUpdate）：投影只是权威"正在施法"的快照，
        /// 用 EndTick − 当前服务器 tick 计算剩余时长，播放攻击动画（与 CastBroadcast 共用
        /// CastId 取消通道）。只处理远程玩家（本地玩家由 SkillExecutor 负责，NPC 走排程）。
        /// </summary>
        public void OnActiveCast(long entityId, ActiveCastProjection activeCast)
        {
            if (activeCast == null || activeCast.CastId == 0) return;
            if (_cancelledCasts.Contains(activeCast.CastId)) return;
            if (_registry == null || _catalog == null) return;
            if (entityId == _localPlayerId) return;
            var view = _registry.GetView(entityId);
            if (view == null || view.Kind != (byte)EntityKind.Player) return;
            if (!_catalog.TryGet(activeCast.SkillId, out var data)) return;
            // Proxy 档没有 Animator：本次播不了就不占去重槽，升 Full 时由 SetTier 重放。
            if (view.Animator == null) return;

            // 过期施法不补播：基线或投影可能晚于 EndTick 到达（2 tick 插值延迟 + 网络抖动），
            // 重放早已结束的攻击只会让旁观端看到无来由的挥砍。
            if (_serverTickProvider != null && activeCast.EndTick <= _serverTickProvider()) return;

            // 去重只在**确实可播**之后登记：视图尚未建立（首条更新早于基线快照）时
            // 不能把 CastId 记为已播，否则后续相位更新会被永久跳过。
            if (_activeCastPlayed.Count >= 256) _activeCastPlayed.Clear();
            if (!_activeCastPlayed.Add(activeCast.CastId)) return;

            // 记录 CastId → 施法者：服务端取消（CastCancelBroadcast）据此定位攻击表现。
            RememberCastSkill(activeCast.CastId, activeCast.SkillId, entityId);

            float remaining = DefaultAttackDuration;
            if (_serverTickProvider != null && activeCast.EndTick > _serverTickProvider())
                remaining = (activeCast.EndTick - _serverTickProvider())
                            / (float)CombatRules.TickRateHz;

            view.Animator.PlayAttack(data.AnimatorTrigger,
                Mathf.Min(AttackClipDuration(data), Mathf.Max(0.1f, remaining)), 1f, activeCast.CastId);
        }

        private float ComputeStartDistance(ProjectileLaunchBroadcast launch)
        {
            if (_serverNowTicksProvider == null || launch.ServerTimestamp == 0 || launch.Speed <= 0f)
                return 0f;

            long now = _serverNowTicksProvider();
            if (now <= launch.ServerTimestamp) return 0f;
            float elapsed = (now - launch.ServerTimestamp) / (float)System.TimeSpan.TicksPerSecond;
            return Mathf.Clamp(elapsed * launch.Speed, 0f, launch.Range);
        }

        private void RememberCastSkill(ulong castId, int skillId, long casterId)
        {
            if (_castSkillIds.Count >= 256)
            {
                _castSkillIds.Clear();
                _castCasters.Clear();
            }
            _castSkillIds[castId] = skillId;
            _castCasters[castId] = casterId;
        }

        private void PruneSeenLaunches()
        {
            if (_seenLaunches.Count <= 256) return;
            _seenLaunches.Clear();
        }

        private void PruneSeenStops()
        {
            if (_seenStops.Count <= 256) return;
            _seenStops.Clear();
        }

        /// <summary>远程伤害结果：对齐投射物停点/爆炸（本地施法者由本地执行器负责）。</summary>
        public void OnDamageResult(DamageResult result)
        {
            if (result == null) return;
            if (result.SourcePlayerId == _localPlayerId) return;

            var tracked = FindEarliestActive(result.SourcePlayerId, result.SkillId);
            if (tracked == null) return;

            bool authoritativeCast = result.Hits != null && result.Hits.Count > 0
                                     && result.Hits[0].CastId != 0;

            if (result.SkillId == (int)SkillID.Fireball
                && result.Hits != null && result.Hits.Count > 0)
            {
                var hit = result.Hits[0];
                Vector3 point = tracked.HasImpact
                    ? tracked.Impact
                    : new Vector3(hit.HitX, 0f, hit.HitZ);
                tracked.Visual.ExplodeAt(point);
                _tracked.Remove(tracked);
            }
            else if (result.SkillId == (int)SkillID.HeavyAttack && result.ProjectileStopped
                     && !authoritativeCast)
            {
                // 旧服务端兼容：没有 CastId 时仍按 (caster, skill) 停点。
                tracked.Visual.StopAtPoint(new Vector3(result.StopX, 0f, result.StopZ));
                _tracked.Remove(tracked);
            }
        }

        /// <summary>每帧清理已回收/已失效的视觉（飞满射程的剑气、自然落地的火球）。</summary>
        public void Update(float deltaTime)
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                var visual = _tracked[i].Visual;
                if (visual == null || !visual.IsActive)
                    _tracked.RemoveAt(i);
            }

            var finished = new List<ulong>();
            foreach (var pair in _castVisuals)
                if (pair.Value.Visual == null || !pair.Value.Visual.IsActive)
                    finished.Add(pair.Key);
            foreach (ulong castId in finished)
            {
                _castVisuals.Remove(castId);
                _castSkillIds.Remove(castId);
            }
        }

        /// <summary>实体视图被删除（离开 AOI/断线/重建）：该施法者的跟踪项全部作废。</summary>
        public void ClearForEntity(long entityId)
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
                if (_tracked[i].CasterId == entityId)
                    _tracked.RemoveAt(i);

            var casts = new List<ulong>();
            foreach (var pair in _castVisuals)
                if (pair.Value.CasterId == entityId)
                    casts.Add(pair.Key);
            foreach (ulong castId in casts)
            {
                _castVisuals.Remove(castId);
                _castSkillIds.Remove(castId);
            }
        }

        /// <summary>断开/重建：清空全部跟踪项。</summary>
        public void Clear()
        {
            _tracked.Clear();
            _castVisuals.Clear();
            _castSkillIds.Clear();
            _castCasters.Clear();
            _cancelledCasts.Clear();
            _activeCastPlayed.Clear();
            _pendingStops.Clear();
            _seenLaunches.Clear();
            _seenStops.Clear();
        }

        internal void TrackForTest(long casterId, int skillId, IRemoteProjectileVisual visual,
            Vector3? impact = null)
            => _tracked.Add(new Tracked(casterId, skillId, visual, impact.HasValue, impact ?? default));

        internal void ApplyDamageForTest(DamageResult result) => OnDamageResult(result);

        /// <summary>跟踪表条目数（测试用于验证 Update 的清理与"普通命中不消费"）。</summary>
        internal int TrackedCountForTest => _tracked.Count;

        /// <summary>按 CastId 跟踪的剑气数（属性剑气测试）。</summary>
        internal int CastVisualCountForTest => _castVisuals.Count;

        /// <summary>已按 ActiveCast 播放过的 CastId 数（去重回归测试）。</summary>
        internal int ActiveCastPlayedCountForTest => _activeCastPlayed.Count;

        /// <summary>
        /// 投射物起点：与本地 `SkillExecutor` 同源（`owner.position + TransformDirection(SpawnOffset)`）。
        /// 只看平面朝向——远程视图的 yaw 只绕 Y，压平后仍等价；传零向量时退化为不旋转。
        /// </summary>
        internal static Vector3 ComputeProjectileOrigin(SkillData data, Vector3 position, Vector3 forward)
        {
            Vector3 offset = Vector3.zero;
            if (data.SwordQi != null)
                offset = data.SwordQi.SpawnOffset;
            else if (data is ProjectileSkillData projectile)
                offset = projectile.SpawnOffset;

            if (offset == Vector3.zero) return position;

            var flat = new Vector3(forward.x, 0f, forward.z);
            Quaternion rotation = flat.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : Quaternion.identity;
            return position + rotation * offset;
        }

        private Tracked? FindEarliestActive(long casterId, int skillId)
        {
            foreach (var tracked in _tracked)
            {
                if (tracked.CasterId != casterId || tracked.SkillId != skillId) continue;
                if (tracked.Visual == null || !tracked.Visual.IsActive) continue;
                return tracked;
            }
            return null;
        }

        /// <summary>
        /// 攻击动画时长：蓄力技能用释放剪辑，其余用施法剪辑；未赋值时退回默认时长。
        /// **必须用 Unity 的 `!= null` 重载**：未赋值的 AnimationClip 是 fake-null
        /// （如 10002/20001 的 `_castClip: {fileID: 0}`），`?.length` 会绕过重载并抛
        /// UnassignedReferenceException。OnCastBroadcast 与 OnActiveCast 共用本方法。
        /// </summary>
        private static float AttackClipDuration(SkillData data)
        {
            var clip = data is ChargedSkillData ? data.ReleaseClip : data.CastClip;
            return clip != null ? clip.length : DefaultAttackDuration;
        }

        /// <summary>SkillData 基类没有 Presentation 属性（只在具体子类上）；按现有类型取。</summary>
        private static PresentationBlock? GetPresentation(SkillData data)
            => (data as ChargedSkillData)?.Presentation
               ?? (data as ProjectileSkillData)?.Presentation
               ?? (data as InstantSkillData)?.Presentation
               ?? (data as ComboSkillData)?.Presentation
               ?? (data as ChanneledSkillData)?.Presentation
               ?? (data as SpinSkillData)?.Presentation;

        private static IRemoteProjectileVisual? DefaultFactory(SkillData data, Vector3 origin,
            Vector3 forward, Vector3? targetPoint = null, float startDistance = 0f, int elementId = 0)
        {
            if (data.SwordQi != null)
            {
                var qi = SwordQiSpawner.Spawn(data.SwordQi, data.Damage?.Clone(), origin, forward,
                    startDistance, elementId);
                return qi != null ? new SwordQiVisual(qi) : null;
            }
            if (data is ProjectileSkillData projectile)
            {
                var fireball = FireballSpawner.Spawn(projectile, data.Damage?.Clone(), origin, forward,
                    targetPoint);
                return fireball != null ? new FireballVisual(fireball) : null;
            }
            return null;
        }

        /// <summary>一次性表现：实例化 + 自动销毁（与本地 SkillExecutor 的 ReleaseVFX 同寿命）。</summary>
        private static void DefaultVfxSpawner(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;
            var vfx = UnityEngine.Object.Instantiate(prefab, position, rotation);
            UnityEngine.Object.Destroy(vfx, VfxLifetimeSeconds);
        }
    }
}
