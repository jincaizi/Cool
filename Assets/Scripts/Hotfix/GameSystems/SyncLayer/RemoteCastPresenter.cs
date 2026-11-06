using System;
using System.Collections.Generic;
using Cool.Protocol;
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

        private readonly RemoteEntityRegistry? _registry;
        private readonly long _localPlayerId;
        private readonly SkillPresentationCatalog? _catalog;
        private readonly Func<SkillData, Vector3, Vector3, IRemoteProjectileVisual?>? _projectileFactory;
        private readonly Action<GameObject, Vector3, Quaternion>? _castVfxSpawner;
        private readonly Action<GameObject, Vector3, Quaternion>? _releaseVfxSpawner;
        private readonly Action<MeteorConfig, DamageBlock, Vector3>? _meteorSpawner;
        private readonly List<Tracked> _tracked = new List<Tracked>();

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
            Action<MeteorConfig, DamageBlock, Vector3>? meteorSpawner = null)
        {
            _registry = registry;
            _localPlayerId = localPlayerId;
            _catalog = catalog;
            _projectileFactory = projectileFactory;
            _castVfxSpawner = castVfxSpawner;
            _releaseVfxSpawner = releaseVfxSpawner;
            _meteorSpawner = meteorSpawner;
        }

        /// <summary>收到远程玩家的施法广播（CombatNetworkBridge.Update 主线程调用）。</summary>
        public void OnCastBroadcast(CastBroadcast broadcast)
        {
            if (_registry == null || _catalog == null) return;
            if (broadcast.CasterPlayerId == _localPlayerId) return;

            var view = _registry.GetView(broadcast.CasterPlayerId);
            if (view == null || view.Kind != (byte)EntityKind.Player) return;
            if (!_catalog.TryGet(broadcast.SkillId, out var data)) return;

            // 立即播放：伤害结果可能先于下一帧到达，等它再播会让挥砍落后于受击
            view.Animator?.PlayAttack(data.AnimatorTrigger,
                (data is ChargedSkillData ? data.ReleaseClip : data.CastClip)?.length ?? DefaultAttackDuration);

            var presentation = GetPresentation(data);
            if (presentation?.CastVFX != null)
                (_castVfxSpawner ?? DefaultVfxSpawner)(presentation.CastVFX, view.Position, view.Rotation);

            // 起点与本地 SkillExecutor 同源：脚底 + 按朝向旋转的资产偏移
            // （火球缺了它会把地面算低 1.2m，剑气会整段在脚踝高度）。
            Vector3 forward = view.Rotation * Vector3.forward;
            Vector3 origin = ComputeProjectileOrigin(data, view.Position, forward);
            Vector3 impact = new Vector3(broadcast.ImpactX, broadcast.ImpactY, broadcast.ImpactZ);
            IRemoteProjectileVisual? visual;
            bool hasImpact = data is ProjectileSkillData projectileData && projectileData.HoldToAim;
            if (_projectileFactory != null)
                visual = _projectileFactory(data, origin, forward);
            else
                visual = DefaultFactory(data, origin, forward, hasImpact ? impact : (Vector3?)null);
            if (visual != null)
                _tracked.Add(new Tracked(broadcast.CasterPlayerId, broadcast.SkillId, visual,
                    hasImpact, impact));

            // 落点类技能没有飞行体：在服务端给定的落点直接生成表现
            if (data is ChargedSkillData && presentation?.ReleaseVFX != null)
                (_releaseVfxSpawner ?? DefaultVfxSpawner)(
                    presentation.ReleaseVFX, impact, Quaternion.identity);
            if (data.Meteor != null)
                (_meteorSpawner ?? MeteorSpawner.Spawn)(
                    data.Meteor, data.Damage?.Clone(), impact);
        }

        /// <summary>远程伤害结果：对齐投射物停点/爆炸（本地施法者由本地执行器负责）。</summary>
        public void OnDamageResult(DamageResult result)
        {
            if (result == null) return;
            if (result.SourcePlayerId == _localPlayerId) return;

            var tracked = FindEarliestActive(result.SourcePlayerId, result.SkillId);
            if (tracked == null) return;

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
            else if (result.SkillId == (int)SkillID.HeavyAttack && result.ProjectileStopped)
            {
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
        }

        /// <summary>实体视图被删除（离开 AOI/断线/重建）：该施法者的跟踪项全部作废。</summary>
        public void ClearForEntity(long entityId)
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
                if (_tracked[i].CasterId == entityId)
                    _tracked.RemoveAt(i);
        }

        /// <summary>断开/重建：清空全部跟踪项。</summary>
        public void Clear() => _tracked.Clear();

        internal void TrackForTest(long casterId, int skillId, IRemoteProjectileVisual visual,
            Vector3? impact = null)
            => _tracked.Add(new Tracked(casterId, skillId, visual, impact.HasValue, impact ?? default));

        internal void ApplyDamageForTest(DamageResult result) => OnDamageResult(result);

        /// <summary>跟踪表条目数（测试用于验证 Update 的清理与"普通命中不消费"）。</summary>
        internal int TrackedCountForTest => _tracked.Count;

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

        /// <summary>SkillData 基类没有 Presentation 属性（只在具体子类上）；按现有类型取。</summary>
        private static PresentationBlock? GetPresentation(SkillData data)
            => (data as ChargedSkillData)?.Presentation
               ?? (data as ProjectileSkillData)?.Presentation
               ?? (data as InstantSkillData)?.Presentation
               ?? (data as ComboSkillData)?.Presentation
               ?? (data as ChanneledSkillData)?.Presentation
               ?? (data as SpinSkillData)?.Presentation;

        private static IRemoteProjectileVisual? DefaultFactory(SkillData data, Vector3 origin,
            Vector3 forward, Vector3? targetPoint = null)
        {
            if (data.SwordQi != null)
            {
                var qi = SwordQiSpawner.Spawn(data.SwordQi, data.Damage?.Clone(), origin, forward);
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
