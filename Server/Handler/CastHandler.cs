using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Navigation;

namespace KcpServer
{
    /// <summary>
    /// P4 服务端权威战斗裁决核心（spec §5-P4）——校验（已知技能/存活/冷却/形状上限）
    /// → HitResolver 几何命中 → D4 服务端掷骰 → 格挡减伤（防御中的受击者）→ 权威扣血 + 冷却记录。
    /// 纯核心不依赖 Server3C/session（单测直用）；主循环独占线程（R2），无锁。
    /// </summary>
    public sealed class CombatResolver
    {
        public Func<long, KcpServer.Monster.MonsterActionRunner?>? NpcActionRunner { get; set; }
        private readonly EntityStore _store;
        private readonly Random _rng;
        private readonly ElementalCatalogDefinition _elementalCatalog;
        private readonly float _defaultWeaponBaseAttack;
        private ulong _nextCastId;
        private ulong _nextEventId;

        public CombatResolver(EntityStore store, Random? rng = null,
            ElementalCatalogDefinition? elementalCatalog = null)
        {
            _store = store;
            _rng = rng ?? new Random();
            _elementalCatalog = elementalCatalog ?? ElementalCatalogDefaults.Create();
            var defaultWeapon = _elementalCatalog.Weapons.Find(
                w => w.TemplateId == _elementalCatalog.DefaultWeaponTemplateId);
            _defaultWeaponBaseAttack = defaultWeapon?.BaseAttack ?? 1f;
        }

        /// <summary>全局事件 ID（跨重试去重；Plan 04 填入 <see cref="HitEntry.EventId"/>）。</summary>
        public ulong AllocateEventId() => ++_nextEventId;

        /// <summary>NPC 施法的判定帧 LOS 回调（参数：casterId, from, to）。由 Server3C 在
        /// EnableMonsters 后接线到 AttackLineOfSight.HasLineOfSight（0.25m 探针）；null = 不做 LOS（无导航宿主）。
        /// **只对 NPC caster 生效**——玩家施法的静态遮挡不在本期（spec §4.5/§9.1-8）。</summary>
        public Func<long, Vector3, Vector3, bool>? NpcLineOfSight { get; set; }

        /// <summary>NPC 受玩家伤害回调（参数：npcId, attackerId, 实际伤害）。
        /// 在 ApplyHit 扣血后调用；格挡分支传减伤后的伤害。null = 不归属（测试/无怪物宿主）。</summary>
        public Action<long, long, float>? NpcDamaged { get; set; }
        public Action<long, Vector3>? NpcPushed { get; set; }
        public NavigationService? ChargePushNavigation { get; set; }

        /// <summary>服务端认可的地面高度（B1 高度限幅）：参数 = 客户端上报位置，返回 navmesh 地面 Y。
        /// null = 无导航宿主，高度直接用客户端自报值（不额外钳制）。只作用于非 NPC 实体。</summary>
        public Func<Vec3, float?>? GroundHeight { get; set; }

        /// <summary>施放裁决结果（输出载体）——决策阶段只填 Accepted/RejectReason/Broadcast/IsNpcCaster，
        /// 判定阶段（DrainResolved）才填 Hits</summary>
        public sealed class CastResolution
        {
            public bool Accepted;
            public string RejectReason = string.Empty;
            public CastBroadcast? Broadcast;
            public bool IsNpcCaster;

            /// <summary>本人确认（接受/拒绝都带；重复序号重发原结论）。</summary>
            public CastConfirm? Confirm;

            /// <summary>重复的 ClientCastSeq：重发原确认，不重播观察者广播、不安排新判定。</summary>
            public bool Replay;

            public ulong CastId;
            public ProjectileLaunchBroadcast? Launch;
            public ProjectileStopBroadcast? Stop;
            public readonly List<HitEntry> Hits = new List<HitEntry>();

            /// <summary>LineAll 线形命中被格挡吸收时的停点（2026-10-21）</summary>
            public bool ProjectileStopped;
            public Vec3 StopPosition;

            /// <summary>剑气回荡决策结果（Accepted 时非 null；接触判定时只带 CastId）。</summary>
            public SwordQiEchoCast? Echo;

            /// <summary>下行 DamageResult 的剑气回荡实例 ID（非本技能为 0）。</summary>
            public ulong EchoCastId;

            /// <summary>下行 DamageResult 的来源种类（值同 EntityKind）</summary>
            public int SourceKind => IsNpcCaster ? (int)EntityKind.Npc : (int)EntityKind.Player;

            /// <summary>下行 DamageResult 组装（字段搬运，不改判定结果）。</summary>
            public DamageResult ToDamageResult(long serverTimestamp) => new DamageResult
            {
                SourcePlayerId = Broadcast!.CasterPlayerId,
                SkillId = Broadcast.SkillId,
                SourceKind = SourceKind,
                ServerTimestamp = serverTimestamp,
                Hits = Hits,
                ProjectileStopped = ProjectileStopped,
                StopX = StopPosition.X,
                StopZ = StopPosition.Z,
                EchoCastId = EchoCastId
            };
        }

        /// <summary>一次施法的判定组（同一施法可排多段——剑气/火球分段推进）。可变 class：
        /// <see cref="NextStage"/>/<see cref="Spent"/> 等在 DrainResolved 推进时原地更新。</summary>
        private sealed class PendingCast
        {
            public KcpServer.Monster.MonsterActionRunner? Runner;
            public ulong InstanceId;
            public readonly long CasterId;
            public readonly CastRequest Request;
            public readonly CastBroadcast Broadcast;
            public readonly bool IsNpcCaster;
            public readonly ProjectileSpec? Projectile;
            public readonly RepeatSpec? Repeat;               // 旋转多段；null = 单段
            public readonly float Charge;
            public uint[] StageTicks;              // 升序；无 Projectile/Repeat 时长度 1 = 首段 tick
            public int NextStage;                            // 下一段下标
            public readonly HashSet<long> GroupHitIds = new HashSet<long>();  // LineAll 组内去重
            public readonly Dictionary<long, int> RepeatHitCounts = new Dictionary<long, int>(); // 旋转每目标计数
            public bool Spent;                               // 火球首中即停
            public bool OriginCaptured;
            public Vec3 Origin;                              // 首段捕获的施法者位置
            public float Yaw;

            // 属性剑气 v1：施法时冻结的武器快照与全局 CastId
            public ulong CastId;
            public WeaponCastSnapshot Weapon;
            public bool Launched;
            public bool ReleasedOnRequest;
            public bool CanBeInterruptedByDamage;
            public bool CancelBroadcast;
            public string SceneId = "";

            // Plan 04：元素状态每目标每次施法只附加一次（挥砍 + 剑气两个伤害实例同组）
            public readonly HashSet<(long TargetId, uint LifeId)> ElementAppliedTargets = new();

            public PendingCast(long casterId, CastRequest request, CastBroadcast broadcast,
                bool isNpcCaster, ProjectileSpec? projectile, RepeatSpec? repeat, float charge, uint firstTick)
            {
                CasterId = casterId; Request = request; Broadcast = broadcast;
                IsNpcCaster = isNpcCaster; Projectile = projectile; Repeat = repeat; Charge = charge;
                StageTicks = BuildStageTicks(projectile, repeat, firstTick);
            }

            private static uint[] BuildStageTicks(ProjectileSpec? spec, RepeatSpec? repeat, uint firstTick)
            {
                if (repeat.HasValue) return repeat.Value.BuildStageTicks(firstTick);
                if (spec == null || spec.Stages <= 1) return new[] { firstTick };
                var ticks = new uint[spec.Stages];
                for (int k = 0; k < spec.Stages; k++)
                    ticks[k] = firstTick + (uint)Math.Ceiling(
                        k * spec.TotalFlightSeconds / spec.Stages * CombatRules.TickRateHz);
                return ticks;
            }
        }

        private readonly List<PendingCast> _pending = new List<PendingCast>();

        /// <summary>在途发射描述（按 CastId）——晚进观察者基线补发用。Stop 时移除；
        /// 正常飞满射程不产生 Stop，由 <see cref="PruneExpiredLaunches"/> 按飞行时长清理，
        /// 并由 <see cref="PruneActiveLaunches"/> 兜底 128 条上界。</summary>
        private readonly Dictionary<ulong, ProjectileLaunchBroadcast> _activeLaunches =
            new Dictionary<ulong, ProjectileLaunchBroadcast>();

        /// <summary>指定施法者的在途投射物（发送**原始** launch，含 ServerTimestamp，
        /// 客户端据此补进度）。</summary>
        public IEnumerable<ProjectileLaunchBroadcast> ActiveLaunchesFor(long casterId)
            => _activeLaunches.Values.Where(l => l.CasterPlayerId == casterId);

        /// <summary>测试入口：同一查询的具体化列表。</summary>
        internal List<ProjectileLaunchBroadcast> ActiveLaunchesForTest(long casterId)
            => ActiveLaunchesFor(casterId).ToList();

        /// <summary>在途发射表有界清理：按 LaunchTick 淘汰最旧，最多 128 条
        /// （与 CastHandler 的 _projectileRecipients 窗口同口径）。</summary>
        private void PruneActiveLaunches()
        {
            const int maxEntries = 128;
            if (_activeLaunches.Count <= maxEntries) return;
            var oldest = _activeLaunches.Values.OrderBy(l => l.LaunchTick).ToList();
            int remove = _activeLaunches.Count - maxEntries;
            for (int i = 0; i < remove; i++)
                _activeLaunches.Remove(oldest[i].CastId);
        }

        /// <summary>正常飞满射程的发射持续清理：tick 已超过飞行 tick 数 + 2 tick 裕量即移除。
        /// 生产 tick 单调递增，uint 相减 wrap-safe（15Hz 下 ~9.1 年才 wrap）。</summary>
        private void PruneExpiredLaunches(uint tick)
        {
            if (_activeLaunches.Count == 0) return;
            var expired = new List<ulong>();
            foreach (var pair in _activeLaunches)
            {
                var launch = pair.Value;
                if (launch.Speed <= 0f || launch.Range <= 0f) continue;
                uint travelTicks = (uint)Math.Ceiling(
                    launch.Range / launch.Speed * CombatRules.TickRateHz);
                if (tick - launch.LaunchTick > travelTicks + 2)
                    expired.Add(pair.Key);
            }
            foreach (var castId in expired) _activeLaunches.Remove(castId);
        }

        private const int SwordQiEchoSkillId = 20006;
        private readonly List<SwordQiEchoCast> _echoCasts = new List<SwordQiEchoCast>();
        private readonly List<SwordQiEchoCast> _endedEchoCasts = new List<SwordQiEchoCast>();
        private ulong _nextEchoCastId;

        /// <summary>决策：校验 + 冷却记录 + 生成广播 + 入队。判定（几何/掷骰/扣血）推迟到首段到期 tick
        /// （DrainResolved → ResolveStage）。</summary>
        public CastResolution Decide(long casterId, CastRequest request, uint tick)
        {
            var resolution = new CastResolution();
            var caster = _store.GetPlayer(casterId);
            if (caster == null) return Reject(resolution, "caster-not-found");

            // 同序号重试：重发原确认（含拒绝），不重播观察者广播、不安排新判定。
            if (request.ClientCastSeq != 0
                && caster.RecentCastConfirms.TryGetValue(request.ClientCastSeq, out var cached))
            {
                resolution.Accepted = cached.Accepted;
                resolution.RejectReason = cached.RejectReason;
                resolution.Confirm = cached;
                resolution.CastId = cached.CastId;
                resolution.Replay = true;
                return resolution;
            }
            if (request.ClientCastSeq != 0 && request.ClientCastSeq <= caster.LastCastSeq)
                return Reject(resolution, caster, request, "stale-seq");

            if (!caster.IsAlive) return Reject(resolution, caster, request, "caster-dead");
            if (caster.ControlActionLock) return Reject(resolution, caster, request, "control-lock");
            if (!CombatRules.TryGet(request.SkillId, out var entry))
                return Reject(resolution, caster, request, "unknown-skill");
            if (request.SkillId == SwordQiEchoSkillId)
                return DecideEcho(caster, request, tick, entry);
            if (!CombatRules.ValidateShape(request.Shape, entry))
                return Reject(resolution, caster, request, "shape-tampered");

            // 新客户端带装备版本与生命代次；0 = 旧客户端/旧测试，跳过对应校验。
            if (request.EquipmentRevision != caster.EquipmentRevision)
                return Reject(resolution, caster, request, "stale-equipment");
            if (request.CasterLifeId != 0 && request.CasterLifeId != caster.LifeId)
                return Reject(resolution, caster, request, "stale-life");

            bool isBasic = request.SkillId == 10001 || request.SkillId == 10002;
            if (isBasic)
            {
                // 10001 零基础冷却仍受接续窗约束；10002 受冷却约束。两者都在施法时冻结。
                if (NpcTickMath.IsBefore(tick, caster.NextAllowedAttackTick))
                    return Reject(resolution, caster, request, "cooldown");
            }
            else if (!caster.IsCastReady(request.SkillId, tick, CombatRules.CooldownTicks(entry)))
            {
                return Reject(resolution, caster, request, "cooldown");
            }

            // 2026-10-18：投射物落点/蓄力校验（只有 CircleAtPoint 需要落点）
            var projectileError = CombatRules.ValidateProjectileRequest(request, entry,
                caster.Position.X, caster.Position.Z);
            if (projectileError != null) return Reject(resolution, caster, request, projectileError);
            var charge = CombatRules.ClampChargeProgress(request.ChargeProgress);

            var weapon = ResolveWeapon(caster);
            float attackSpeed = isBasic ? weapon.AttackSpeedMultiplier : 1f;
            var timing = FindTiming(request.SkillId);
            uint releaseDelay = isBasic && timing != null
                ? SecondsToTicks(timing.WindupSeconds / attackSpeed)
                : CombatRules.HitDelayTicks(entry);
            uint firstTick = tick + releaseDelay + CombatRules.ProjectileTravelTicks(entry.Projectile,
                caster.Position.X, caster.Position.Z, request.ImpactX, request.ImpactZ);
            bool releasedOnRequest = releaseDelay == 0u
                && entry.Projectile is { Kind: ProjectileKind.CircleAtPoint, Speed: > 0f };

            var runner = caster.Kind == (byte)EntityKind.Npc ? NpcActionRunner?.Invoke(casterId) : null;
            if (runner != null) {
                var error = runner.Validate(request.SkillId, request.TargetPlayerId, tick);
                if (error != null) return Reject(resolution, caster, request, error);
                if (!runner.TryBeginSkill(request.SkillId, request.TargetPlayerId, tick)) return Reject(resolution, caster, request, "npc-action-busy");
            }
            // D-3：冷却记录在决策（空挥也冷却，D-P4-5）。普攻用攻速缩放后的最早出手 tick。
            if (isBasic)
                caster.NextAllowedAttackTick = tick + NextAllowedTicks(timing, entry, attackSpeed);
            else
                caster.RecordCast(request.SkillId, tick);

            ulong castId = ++_nextCastId;
            var casterState = _store.GetPlayer(casterId);
            var displacement = _elementalCatalog.Displacements.Find(d => d.SkillId == request.SkillId);
            if (displacement != null && casterState != null)
            {
                float activeSeconds = displacement.DashDuration
                    - displacement.StartupSeconds - displacement.RecoverySeconds;
                casterState.Displacement = new DisplacementWindow
                {
                    CastId = castId,
                    SkillId = request.SkillId,
                    StartTick = tick,
                    // 状态减速会把同一段距离拉长；4× 窗口覆盖 SlowRatio ≤ 0.75，总距离仍按
                    // DashDistance + 1 帧封顶——窗口只防旧 CastId 在很久以后被复用。
                    EndTick = tick + SecondsToTicks(displacement.DashDuration * 4f),
                    TotalDistance = displacement.DashDistance,
                    Speed = activeSeconds > 0f ? displacement.DashDistance / activeSeconds : 0f
                };
            }

            bool chargeBlast = request.SkillId == CombatRules.ChargeBlastSkillId;
            resolution.Accepted = true;
            resolution.IsNpcCaster = caster.Kind == (byte)EntityKind.Npc;
            resolution.CastId = castId;
            resolution.Broadcast = new CastBroadcast
            {
                SkillId = request.SkillId,
                CasterPlayerId = casterId,
                TargetPlayerId = request.TargetPlayerId,
                CasterX = caster.Position.X,
                CasterY = caster.Position.Y,
                CasterZ = caster.Position.Z,
                CasterRotation = caster.Rotation,
                Shape = request.Shape,
                ServerTimestamp = ServerClock.NowTicks(),
                ImpactX = chargeBlast ? caster.Position.X : request.ImpactX,
                ImpactY = chargeBlast ? caster.Position.Y : request.ImpactY,
                ImpactZ = chargeBlast ? caster.Position.Z : request.ImpactZ,
                ChargeProgress = chargeBlast ? charge : request.ChargeProgress,
                CastId = castId,
                CasterLifeId = caster.LifeId,
                AttackSpeedMultiplier = attackSpeed,
                ElementId = weapon.ElementId,
                EquipmentRevision = weapon.EquipmentRevision
            };
            resolution.Confirm = BuildConfirm(caster, request, weapon, true, "", castId, tick, attackSpeed);
            CacheConfirm(caster, request, resolution.Confirm);

            // D-1/D-2：判定帧延迟由服务端表自持——判定发生在
            // tick + HitDelayTicks + ProjectileTravelTicks（CircleAtPoint 飞行延迟，见上方 firstTick）
            var pending = new PendingCast(casterId, request, resolution.Broadcast, resolution.IsNpcCaster,
                entry.Projectile, entry.Repeat, charge, firstTick)
            {
                Runner = runner,
                InstanceId = runner?.Current.InstanceId ?? 0,
                CastId = castId,
                Weapon = weapon,
                ReleasedOnRequest = releasedOnRequest,
                CanBeInterruptedByDamage = entry.CanBeInterruptedByDamage,
                SceneId = caster.ZoneId
            };
            _pending.Add(pending);
            if (runner != null) { pending.StageTicks = runner.StageTicks(); resolution.Broadcast.NpcAction = runner.Current; }
            if (!releasedOnRequest)
            {
                uint actionTicks = releaseDelay + 1;
                if (isBasic && timing != null)
                    actionTicks = Math.Max(actionTicks, SecondsToTicks(
                        (timing.WindupSeconds + timing.ActiveSeconds + timing.RecoverySeconds) / attackSpeed));
                else if (entry.Repeat.HasValue)
                    actionTicks = pending.StageTicks[^1] - tick + 1;
                else if (displacement != null)
                    actionTicks = Math.Max(actionTicks, SecondsToTicks(displacement.DashDuration));
                caster.RecordCharacterAction(castId, tick, tick + actionTicks);
            }

            // 属性剑气 v1：活动施法投影从 Windup 开始（EndTick = 最后一段判定 tick）。
            if (casterState != null)
                SetActiveCast(casterState, pending, NpcActionPhase.Windup);
            return resolution;
        }

        private WeaponCastSnapshot ResolveWeapon(PlayerState caster)
        {
            var definition = _elementalCatalog.Weapons.Find(w => w.TemplateId == caster.EquippedWeaponTemplateId)
                ?? _elementalCatalog.Weapons.Find(w => w.TemplateId == _elementalCatalog.DefaultWeaponTemplateId);
            if (definition == null)
                definition = new WeaponDefinition
                {
                    TemplateId = 0,
                    BaseAttack = 1f,
                    AttackSpeedMultiplier = 1f,
                    SwordQiDamageMultiplier = 1f
                };

            return new WeaponCastSnapshot(caster.EquippedWeaponInstanceId, definition.TemplateId,
                definition.ElementId, caster.EquipmentRevision, definition.BaseAttack,
                definition.AttackSpeedMultiplier, definition.SwordQiDamageMultiplier);
        }

        private AttackTimingDefinition? FindTiming(int skillId)
        {
            foreach (var timing in _elementalCatalog.AttackTimings)
                if (timing.SkillId == skillId) return timing;
            return null;
        }

        private static uint SecondsToTicks(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds <= 0f) return 0u;
            double ticks = Math.Ceiling(seconds * CombatRules.TickRateHz);
            return ticks >= uint.MaxValue ? uint.MaxValue : (uint)ticks;
        }

        private static uint NextAllowedTicks(AttackTimingDefinition? timing, SkillEntry entry, float speed)
        {
            if (timing == null)
                return Math.Max(1u, CombatRules.CooldownTicks(entry));
            float seconds = Math.Max(timing.CooldownSeconds, timing.ChainWindowSeconds);
            return Math.Max(1u, SecondsToTicks(seconds / speed));
        }

        /// <summary>活动施法投影：Decide 成功 → Windup；判定期推进 → Active/Recovery；段结束/取消/死亡 → 清空。</summary>
        private static void SetActiveCast(PlayerState caster, PendingCast pending, NpcActionPhase phase)
        {
            caster.ActiveCast = new ActiveCastProjection
            {
                CastId = pending.CastId,
                SkillId = pending.Request.SkillId,
                Phase = phase,
                EndTick = pending.StageTicks.Length > 0 ? pending.StageTicks[^1] : 0u
            };
            caster.BumpCombatVersion();
        }

        private static void ClearActiveCast(PlayerState? caster, ulong castId, bool clearDisplacement = true)
        {
            if (caster == null) return;
            bool changed = false;
            if (caster.ActiveCast != null && caster.ActiveCast.CastId == castId)
            {
                caster.ActiveCast = null;
                changed = true;
            }
            // 正常完成最后一段时保留位移窗口：窗口寿命由 EndTick（DashDuration×4）限界，
            // 而不是施法判定结束（20001 判定 ~5 tick，窗口 18 tick）。
            // 取消/死亡/复活/新位移施法仍然清窗口。
            if (clearDisplacement && caster.Displacement != null && caster.Displacement.CastId == castId)
            {
                caster.Displacement = null;
                changed = true;
            }
            if (changed) caster.BumpCombatVersion();
        }

        private static CastConfirm BuildConfirm(PlayerState caster, CastRequest request,
            WeaponCastSnapshot weapon, bool accepted, string reason, ulong castId, uint tick,
            float attackSpeed)
            => new CastConfirm
            {
                ClientCastSeq = request.ClientCastSeq,
                Accepted = accepted,
                RejectReason = reason,
                CastId = castId,
                SkillId = request.SkillId,
                CasterLifeId = caster.LifeId,
                EquipmentRevision = caster.EquipmentRevision,
                AttackSpeedMultiplier = attackSpeed,
                AcceptedTick = accepted ? tick : 0u,
                WeaponInstanceId = caster.EquippedWeaponInstanceId,
                TemplateId = caster.EquippedWeaponTemplateId,
                ElementId = weapon.ElementId
            };

        private static void CacheConfirm(PlayerState caster, CastRequest request, CastConfirm confirm)
        {
            if (request.ClientCastSeq == 0 || request.ClientCastSeq <= caster.LastCastSeq) return;
            caster.LastCastSeq = request.ClientCastSeq;
            caster.RecentCastConfirms[request.ClientCastSeq] = confirm;
            if (caster.RecentCastConfirms.Count <= 16) return;

            var keys = new List<uint>(caster.RecentCastConfirms.Keys);
            keys.Sort();
            int remove = caster.RecentCastConfirms.Count - 16;
            for (int i = 0; i < remove; i++)
                caster.RecentCastConfirms.Remove(keys[i]);
        }

        /// <summary>20006 决策：选敌、锁定生命、记录冷却、提交无敌窗口、生成首次接触时间表。</summary>
        private CastResolution DecideEcho(PlayerState caster, CastRequest request, uint tick, SkillEntry entry)
        {
            var resolution = new CastResolution();
            if (caster.Kind != (byte)EntityKind.Player)
                return Reject(resolution, "not-player");
            bool recast = (caster.Features & ProtocolFeatures.SwordQiEchoRecast) != 0;
            bool twoStageRequest = request.EchoAction != SwordQiEchoAction.Legacy;
            if (recast != twoStageRequest)
                return Reject(resolution, "echo-version");
            if (!caster.IsCastReady(request.SkillId, tick, CombatRules.CooldownTicks(entry)))
                return Reject(resolution, "cooldown");

            var origin = new Vec3(caster.Position.X, ResolveCombatHeight(caster), caster.Position.Z);
            var prepared = recast
                ? _echoCasts.Find(cast => cast.CasterId == caster.PlayerId && cast.Preparing
                    && cast.CasterLifeId == caster.LifeId)
                : null;
            if (request.EchoAction == SwordQiEchoAction.Prepare)
            {
                if (prepared != null)
                {
                    if (prepared.BindingId != request.EchoBindingId
                        || unchecked((int)(tick - prepared.EndTick)) >= 0)
                        return Reject(resolution, "already-prepared");
                    prepared.RequestId = request.EchoRequestId;
                    return AcceptEcho(resolution, prepared);
                }
                var cast = CreateEchoCast(caster, request, entry);
                cast.Preparing = true;
                SetEchoTimeline(cast, request, tick, origin,
                    (uint)Math.Ceiling(SwordQiEchoRules.PrepareSeconds * CombatRules.TickRateHz));
                _echoCasts.Add(cast);
                return AcceptEcho(resolution, cast);
            }
            if (recast && (request.EchoAction != SwordQiEchoAction.Release || prepared == null
                || prepared.CastId != request.EchoPreparedCastId || prepared.BindingId != request.EchoBindingId
                || unchecked((int)(tick - prepared.EndTick)) >= 0))
                return Reject(resolution, "not-prepared");

            var targets = SwordQiEchoRules.SelectTargets(origin, SwordQiEchoRules.Spec.Radius,
                SwordQiEchoRules.Spec.MaxTargets, BuildEchoCandidates(caster));
            if (targets.Count == 0)
                return Reject(resolution, "no-target");

            uint durationTicks = (uint)Math.Ceiling(
                SwordQiEchoRules.Spec.DurationSeconds * CombatRules.TickRateHz);
            if (durationTicks < 2) return Reject(resolution, "bad-duration");
            var launch = prepared ?? CreateEchoCast(caster, request, entry);
            ulong seed = recast ? unchecked(launch.CastId * 0x9e3779b97f4a7c15UL) : 0;
            var contacts = SwordQiEchoRules.BuildContacts(origin, targets,
                SwordQiEchoRules.Spec.SwordCount, durationTicks, seed);

            foreach (var contact in contacts)
            {
                var target = _store.GetPlayer(contact.TargetId);
                if (target == null) continue;
                launch.Contacts.Add(new SwordQiEchoCast.Contact
                {
                    TargetId = target.PlayerId,
                    Target = target,
                    TargetLifeId = target.LifeId,
                    SwordIndex = contact.SwordIndex,
                    OffsetTicks = contact.OffsetTicks,
                    Position = contact.Position
                });
            }

            if (launch.Contacts.Count == 0)
                return Reject(resolution, "no-target");

            launch.Preparing = false;
            SetEchoTimeline(launch, request, tick, origin, durationTicks);
            caster.RecordCast(request.SkillId, tick);
            caster.SetInvulnerable(tick, launch.EndTick);
            if (prepared == null) _echoCasts.Add(launch);
            return AcceptEcho(resolution, launch);
        }

        private SwordQiEchoCast CreateEchoCast(PlayerState caster, CastRequest request, SkillEntry entry)
            => new SwordQiEchoCast
            {
                CasterId = caster.PlayerId,
                Caster = caster,
                CasterLifeId = caster.LifeId,
                ZoneId = caster.ZoneId,
                CastId = ++_nextEchoCastId,
                BindingId = request.EchoBindingId,
                TwoStage = request.EchoAction != SwordQiEchoAction.Legacy,
                CooldownTicks = CombatRules.CooldownTicks(entry),
                BaseDamage = SwordQiEchoRules.DefaultDamage,
                CriticalRate = entry.CriticalRate
            };

        private static void SetEchoTimeline(SwordQiEchoCast cast, CastRequest request, uint tick,
            Vec3 origin, uint duration)
        {
            cast.RequestId = request.EchoRequestId;
            cast.StartTick = tick;
            cast.EndTick = tick + duration;
            cast.Origin = origin;
            cast.Rotation = cast.Caster.Rotation;
            cast.Broadcast = new CastBroadcast
            {
                SkillId = request.SkillId,
                CasterPlayerId = cast.CasterId,
                CasterX = origin.X,
                CasterY = origin.Y,
                CasterZ = origin.Z,
                CasterRotation = cast.Rotation,
                Shape = request.Shape,
                ServerTimestamp = ServerClock.NowTicks()
            };
            if (cast.OrbitStartedTimestamp == 0)
                cast.OrbitStartedTimestamp = cast.Broadcast.ServerTimestamp;
        }

        private static CastResolution AcceptEcho(CastResolution resolution, SwordQiEchoCast cast)
        {
            resolution.Accepted = true;
            resolution.Broadcast = cast.Broadcast;
            resolution.Echo = cast;
            return resolution;
        }

        private List<CombatCandidate> BuildEchoCandidates(PlayerState caster)
        {
            var candidates = new List<CombatCandidate>();
            foreach (var entity in _store.All)
            {
                if (entity.PlayerId == caster.PlayerId) continue;
                bool formalPlayer = entity.Kind == (byte)EntityKind.Player && entity.IsLoggedIn;
                bool npc = entity.Kind == (byte)EntityKind.Npc;
                if (!formalPlayer && !npc) continue;
                if (!string.Equals(entity.ZoneId, caster.ZoneId, StringComparison.Ordinal)) continue;
                candidates.Add(new CombatCandidate(entity.PlayerId,
                    new Vec3(entity.Position.X, ResolveCombatHeight(entity), entity.Position.Z),
                    entity.IsAlive, entity.BodyRadius, BodyHeightOf(entity)));
            }
            return candidates;
        }

        /// <summary>取出到期（含本 tick）的待判定施法并按段裁决。单遍压缩，出队顺序 = 入队顺序（确定性）。</summary>
        public List<CastResolution> DrainResolved(uint tick)
        {
            PruneExpiredLaunches(tick);
            List<CastResolution>? resolved = null;
            int write = 0;
            for (int read = 0; read < _pending.Count; read++)
            {
                var pending = _pending[read];
                bool keep = false;
                while (!pending.Spent
                       && pending.NextStage < pending.StageTicks.Length
                       && pending.StageTicks[pending.NextStage] <= tick)
                {
                    if (pending.Runner != null && !pending.Runner.TryCommitStage(pending.InstanceId, tick))
                    {
                        ClearActiveCast(_store.GetPlayer(pending.CasterId), pending.CastId);
                        pending.Spent = true;
                        break;
                    }
                    var resolution = ResolveStage(pending, tick);
                    // 施法者死亡/移除：整组丢弃未到期段。只逐段返回 null 会让复活后的
                    // 后续段继续结算（B3：旋转死亡/换命后不得残留伤害实例）。
                    if (resolution == null)
                    {
                        var caster = _store.GetPlayer(pending.CasterId);
                        caster?.CancelCharacterAction(pending.CastId);
                        ClearActiveCast(caster, pending.CastId);
                        // 已发射的剑气：向已收到发射描述的会话补一条权威终止。
                        if (pending.Launched)
                        {
                            (resolved ??= new List<CastResolution>()).Add(new CastResolution
                            {
                                Accepted = true,
                                IsNpcCaster = pending.IsNpcCaster,
                                Broadcast = pending.Broadcast,
                                CastId = pending.CastId,
                                Stop = new ProjectileStopBroadcast
                                {
                                    CastId = pending.CastId,
                                    CasterPlayerId = pending.CasterId,
                                    CasterLifeId = pending.Broadcast.CasterLifeId,
                                    CombatSceneId = pending.SceneId,
                                    StopTick = tick,
                                    Reason = ProjectileStopReason.Death,
                                    StopX = pending.Origin.X,
                                    StopZ = pending.Origin.Z
                                }
                            });
                            _activeLaunches.Remove(pending.CastId);
                            pending.Launched = false;
                        }
                        pending.Spent = true;
                        break;
                    }
                    // **每个判定都产出一条**（可能 Hits 为空）——沿用原合同（原实现对每次判定都入列，
                    // 空 Hits 由广播端 `BroadcastDamageAsync` 跳过）。按 Hits.Count 过滤会改变既有
                    // 单段技能的返回值，可能打破既有测试。
                    (resolved ??= new List<CastResolution>()).Add(resolution);
                    pending.NextStage++;

                    // 判定期推进活动施法阶段；最后一段结算或本段提前终止（格挡/溅射命中）后清空，
                    // 否则 Spent pending 被压缩丢弃后 ActiveCast 再无清理路径。
                    // 最后一段是正常完成：保留位移窗口（按 EndTick 限界）；提前终止按取消处理清窗口。
                    var casterState = _store.GetPlayer(pending.CasterId);
                    if (pending.NextStage >= pending.StageTicks.Length)
                        ClearActiveCast(casterState, pending.CastId, clearDisplacement: false);
                    else if (pending.Spent)
                        ClearActiveCast(casterState, pending.CastId);
                    else if (casterState != null)
                        SetActiveCast(casterState, pending,
                            pending.NextStage == 1 ? NpcActionPhase.Active : NpcActionPhase.Recovery);
                }
                if (!pending.Spent && pending.NextStage < pending.StageTicks.Length) keep = true;
                if (keep) _pending[write++] = pending;
            }
            if (write < _pending.Count) _pending.RemoveRange(write, _pending.Count - write);
            ResolveEchoCasts(tick, ref resolved);
            return resolved ?? new List<CastResolution>();
        }

        /// <summary>按固定顺序处理剑气回荡：普通 pending 先、Echo 后（同 tick 可复现）。</summary>
        private void ResolveEchoCasts(uint tick, ref List<CastResolution>? resolved)
        {
            int write = 0;
            for (int read = 0; read < _echoCasts.Count; read++)
            {
                var cast = _echoCasts[read];
                var caster = _store.GetPlayer(cast.CasterId);
                bool casterValid = caster != null
                    && ReferenceEquals(caster, cast.Caster)
                    && caster.LifeId == cast.CasterLifeId
                    && caster.IsAlive;
                if (!casterValid)
                {
                    EndEchoCast(cast);
                    continue;
                }

                if (cast.Preparing)
                {
                    if (unchecked((int)(tick - cast.EndTick)) >= 0)
                    {
                        cast.Caster.RecordCast(SwordQiEchoSkillId, cast.EndTick);
                        cast.EndPhase = SwordQiEchoPhase.Expired;
                        EndEchoCast(cast);
                    }
                    else _echoCasts[write++] = cast;
                    continue;
                }

                foreach (var contact in cast.Contacts)
                {
                    if (contact.Resolved) continue;
                    uint dueTick = cast.StartTick + contact.OffsetTicks;
                    if (dueTick > tick) continue;

                    contact.Resolved = true;
                    var target = _store.GetPlayer(contact.TargetId);
                    bool targetValid = target != null
                        && ReferenceEquals(target, contact.Target)
                        && target.LifeId == contact.TargetLifeId
                        && target.IsAlive
                        && string.Equals(target.ZoneId, cast.ZoneId, StringComparison.Ordinal)
                        && HitResolver.SurfaceDistance(cast.Origin,
                            new Vec3(target.Position.X, target.Position.Y, target.Position.Z),
                            target.BodyRadius) <= SwordQiEchoRules.Spec.Radius;
                    if (!targetValid) continue;

                    var contactResolution = new CastResolution
                    {
                        Accepted = true,
                        IsNpcCaster = false,
                        Broadcast = cast.Broadcast,
                        EchoCastId = cast.CastId
                    };
                    ApplyHit(cast.CasterId, cast.BaseDamage, cast.CriticalRate,
                        null, 0f, target.PlayerId, cast.Origin, tick, contactResolution);
                    if (contactResolution.Hits.Count > 0)
                        (resolved ??= new List<CastResolution>()).Add(contactResolution);
                }

                // 即使全部目标都已失效/结算，施法与无敌仍持续到原定 EndTick（规格 §无敌与联机时序）。
                if (tick >= cast.EndTick)
                {
                    if (cast.TwoStage) cast.EndPhase = SwordQiEchoPhase.Completed;
                    EndEchoCast(cast);
                    continue;
                }

                _echoCasts[write++] = cast;
            }

            if (write < _echoCasts.Count) _echoCasts.RemoveRange(write, _echoCasts.Count - write);
        }

        private void EndEchoCast(SwordQiEchoCast cast)
        {
            var caster = _store.GetPlayer(cast.CasterId);
            if (!cast.Preparing && caster != null && ReferenceEquals(caster, cast.Caster)
                && caster.LifeId == cast.CasterLifeId)
                caster.ClearInvulnerability();
            _endedEchoCasts.Add(cast);
        }

        /// <summary>玩家断开/移除时清掉其剑气回荡实例；已结束实例交给宿主发 Ended。</summary>
        public void RemoveEchoCastsFor(long playerId)
        {
            for (int i = _echoCasts.Count - 1; i >= 0; i--)
            {
                var cast = _echoCasts[i];
                if (cast.CasterId != playerId) continue;
                EndEchoCast(cast);
                _echoCasts.RemoveAt(i);
            }
        }

        public bool HasEndedEchoCasts => _endedEchoCasts.Count > 0;

        /// <summary>取出已结束的剑气回荡实例（宿主负责广播 Ended 并更新请求缓存）。</summary>
        public List<SwordQiEchoCast> DrainEndedEchoCasts()
        {
            var ended = new List<SwordQiEchoCast>(_endedEchoCasts);
            _endedEchoCasts.Clear();
            return ended;
        }

        public List<CastCancelBroadcast> CancelPending(long casterId, int skillId, ulong castId = 0,
            uint tick = 0, NpcActionCancelReason reason = NpcActionCancelReason.Forced)
        {
            var cancels = new List<CastCancelBroadcast>();
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pending = _pending[i];
                if (pending.Spent) continue; // 已取消/已结算，等 DrainResolved 压缩
                if (pending.CasterId != casterId || pending.Request.SkillId != skillId) continue;
                if (castId != 0 && pending.CastId != castId) continue;
                if (pending.ReleasedOnRequest) continue;

                var caster = _store.GetPlayer(casterId);
                caster?.CancelCharacterAction(pending.CastId);
                if (pending.Launched)
                {
                    if (pending.CancelBroadcast) break; // 最新实例已取消，重复取消幂等
                    pending.CancelBroadcast = true;
                }
                else
                {
                    // 不能在迭代中 RemoveAt：CancelCaster 可能由状态附加 → 控制 sink 在
                    // DrainResolved 单遍压缩途中重入（C1）。标 Spent 由压缩在本次/下一 tick 丢弃；
                    // 已写入压缩目标的 Spent 项多留一 tick 无害。
                    pending.Spent = true;
                    ClearActiveCast(caster, pending.CastId);
                }

                cancels.Add(new CastCancelBroadcast
                {
                    CastId = pending.CastId,
                    CasterLifeId = pending.Broadcast.CasterLifeId,
                    CasterPlayerId = casterId,
                    EffectiveTick = tick,
                    Reason = reason
                });
                break; // 只处理最新/指定这一条
            }
            return cancels;
        }

        public List<CastCancelBroadcast> CancelCaster(long casterId, uint tick, NpcActionCancelReason reason)
        {
            var cancels = new List<CastCancelBroadcast>();
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var pending = _pending[i];
                if (pending.Spent) continue; // 已取消/已结算，等 DrainResolved 压缩
                if (pending.CasterId != casterId) continue;
                if (reason == NpcActionCancelReason.Hit
                    && (pending.ReleasedOnRequest || !pending.CanBeInterruptedByDamage))
                    continue;

                var caster = _store.GetPlayer(casterId);
                caster?.CancelCharacterAction(pending.CastId);
                if (pending.Launched)
                {
                    if (pending.CancelBroadcast) continue;
                    pending.CancelBroadcast = true;
                }
                else
                {
                    // 同 CancelPending：重入安全——不在 DrainResolved 迭代中改 _pending。
                    pending.Spent = true;
                    ClearActiveCast(caster, pending.CastId);
                }
                cancels.Add(new CastCancelBroadcast
                {
                    CastId = pending.CastId,
                    CasterLifeId = pending.Broadcast.CasterLifeId,
                    CasterPlayerId = casterId,
                    EffectiveTick = tick,
                    Reason = reason
                });
            }
            return cancels;
        }

        /// <summary>判定一段：复查施法者存活 → 几何（按投射物种类）→ 服务端掷骰 → 扣血。
        /// 返回 null 表示丢弃（施法者已死/已移除/表项消失）。</summary>
        private CastResolution? ResolveStage(PendingCast pending, uint tick)
        {
            var caster = _store.GetPlayer(pending.CasterId);
            if (caster == null || !caster.IsAlive) return null;   // 施法者已死：整组丢弃
            if (!CombatRules.TryGet(pending.Request.SkillId, out var entry)) return null; // 决策已校验，防御分支

            bool chargeBlast = pending.Request.SkillId == CombatRules.ChargeBlastSkillId;
            var candidates = BuildCandidates(pending, chargeBlast ? CombatZone(caster) : null);
            var casterPos = new Vec3(caster.Position.X, ResolveCombatHeight(caster), caster.Position.Z);
            var impact = new Vec3(pending.Broadcast.ImpactX, pending.Broadcast.ImpactY, pending.Broadcast.ImpactZ);
            var spec = pending.Projectile;
            List<long> hitIds;
            List<long>? swingHitIds = null;

            if (spec == null)
            {
                hitIds = ResolveSwing(caster, casterPos, pending.Request, entry, candidates);
            }
            else
            {
                if (!pending.OriginCaptured)
                {
                    // 首段捕获一次：剑气/火球沿施法者当时的朝向，不随后续移动改变
                    pending.Origin = casterPos;
                    pending.Yaw = caster.Rotation;
                    pending.OriginCaptured = true;
                }
                // 10002：单机里 0.35s 挥击与剑气是两个独立伤害实例（spec §3.2），投射物分支
                // 不能把挥击吞掉。只在首段结算一次，用施法者当前位置/朝向（与 spec==null 同源）。
                if (spec.KeepsSwing && pending.NextStage == 0)
                    swingHitIds = ResolveSwing(caster, casterPos, pending.Request, entry, candidates);

                int stage = pending.NextStage;
                hitIds = spec.Kind switch
                {
                    ProjectileKind.LineAll => HitResolver.ResolveLineAll(
                        pending.Origin, pending.Yaw,
                        stage * spec.Length / spec.Stages, spec.Length / spec.Stages,
                        spec.HalfWidth, pending.GroupHitIds, candidates),
                    ProjectileKind.LineNearestSplash => ResolveNearestStage(pending, spec, stage, candidates),
                    ProjectileKind.CircleAtPoint => HitResolver.ResolveCircle(
                        impact, ScaledRadius(spec, pending.Charge), candidates),
                    _ => new List<long>(),
                };
            }

            // 判定帧 LOS（spec §4.5）：怪物起手后玩家躲到墙后时这一刀不能命中。
            // 只过滤 NPC caster；玩家施法保持原语义。逐目标过滤（AoE 被墙隔开的部分不结算）。
            if (pending.IsNpcCaster && NpcLineOfSight != null && hitIds.Count > 0)
            {
                hitIds = FilterVisibleTargets(pending.CasterId, caster.Position, hitIds);
                if (swingHitIds != null)
                    swingHitIds = FilterVisibleTargets(pending.CasterId, caster.Position, swingHitIds);
            }

            var resolution = new CastResolution
            {
                Accepted = true, IsNpcCaster = pending.IsNpcCaster, Broadcast = pending.Broadcast,
                CastId = pending.CastId
            };

            // 剑气首段：发布权威发射描述（与判定同一原点/朝向；之后各段不重复）。
            if (spec != null && spec.Kind == ProjectileKind.LineAll && pending.NextStage == 0)
            {
                pending.Launched = true;
                resolution.Launch = new ProjectileLaunchBroadcast
                {
                    CastId = pending.CastId,
                    CasterPlayerId = pending.CasterId,
                    CasterLifeId = pending.Broadcast.CasterLifeId,
                    CombatSceneId = pending.SceneId,
                    LaunchTick = pending.StageTicks[pending.NextStage],
                    OriginX = pending.Origin.X,
                    OriginY = pending.Origin.Y,
                    OriginZ = pending.Origin.Z,
                    Yaw = pending.Yaw,
                    Speed = spec.Speed > 0f ? spec.Speed : 18f,
                    Range = spec.Length,
                    ElementId = pending.Weapon.ElementId,
                    SkillId = pending.Request.SkillId,
                    ServerTimestamp = ServerClock.NowTicks()
                };
                _activeLaunches[resolution.Launch.CastId] = resolution.Launch;
                PruneActiveLaunches();
            }

            // 挥击实例先行（单机判定帧顺序：挥击结算、同帧再生成剑气），且**不写入 GroupHitIds、
            // 不受其 banned 过滤**——同一目标在挥击与剑气两条线上各结算一次（不跨组去重）
            if (swingHitIds != null)
            {
                foreach (var targetId in swingHitIds)
                    ApplyHit(pending.CasterId, entry.BaseDamage, entry.CriticalRate,
                        spec, pending.Charge, targetId, casterPos, tick, resolution,
                        pending, isProjectile: false);
            }

            // 剑气沿施法方向推进：按沿线距离排序，格挡者之前的先结算、之后的被吸收跳过
            if (spec != null && spec.Kind == ProjectileKind.LineAll)
                hitIds.Sort((a, b) => CompareAlong(pending, a, b));

            var lineAttackerPos = spec != null && spec.Kind == ProjectileKind.LineAll
                ? pending.Origin
                : chargeBlast ? impact : casterPos;

            foreach (var targetId in hitIds)
            {
                if (pending.Repeat is { } repeat)
                {
                    // 旋转：每目标独立计数（≤5），不能走 GroupHitIds 的“每施法一次”去重，
                    // 否则只加 StageTicks 会静默丢掉第 2–5 段。
                    pending.RepeatHitCounts.TryGetValue(targetId, out int repeatHits);
                    if (repeat.MaxHitsPerTarget > 0 && repeatHits >= repeat.MaxHitsPerTarget) continue;
                }
                else if (pending.Runner == null && !pending.GroupHitIds.Add(targetId)) continue;   // 组内每目标一次

                bool blocked = ApplyHit(pending.CasterId, entry.BaseDamage, entry.CriticalRate,
                    spec, pending.Charge, targetId, lineAttackerPos, tick, resolution,
                    pending, isProjectile: spec != null);
                if (pending.Repeat.HasValue)
                {
                    pending.RepeatHitCounts.TryGetValue(targetId, out int repeatHits);
                    pending.RepeatHitCounts[targetId] = repeatHits + 1;
                }
                if (blocked && spec != null && spec.Kind == ProjectileKind.LineAll)
                {
                    var blocker = _store.GetPlayer(targetId);
                    if (blocker != null)
                        resolution.StopPosition = new Vec3(
                            blocker.Position.X, blocker.Position.Y, blocker.Position.Z);
                    resolution.ProjectileStopped = true;
                    resolution.Stop = new ProjectileStopBroadcast
                    {
                        CastId = pending.CastId,
                        CasterPlayerId = pending.CasterId,
                        CasterLifeId = pending.Broadcast.CasterLifeId,
                        CombatSceneId = pending.SceneId,
                        StopTick = tick,
                        Reason = ProjectileStopReason.Blocked,
                        StopX = resolution.StopPosition.X,
                        StopZ = resolution.StopPosition.Z
                    };
                    _activeLaunches.Remove(resolution.Stop.CastId);
                    pending.Spent = true;
                    break;
                }
            }

            if (spec != null && spec.Kind == ProjectileKind.LineNearestSplash && hitIds.Count > 0)
                pending.Spent = true;
            return resolution;
        }

        /// <summary>按判定帧位置过滤 LOS 被墙挡住的命中目标（NPC 施法专用）。目标已移除时保留，
        /// 由 ApplyHit 的 null 守卫丢弃——不要在这里改变既有拒绝语义。</summary>
        private List<long> FilterVisibleTargets(long casterId, Vector3 from, List<long> targets)
        {
            var visible = new List<long>(targets.Count);
            foreach (long targetId in targets)
            {
                var target = _store.GetPlayer(targetId);
                if (target != null && !NpcLineOfSight!(casterId, from, target.Position)) continue;
                visible.Add(targetId);
            }
            return visible;
        }

        private int CompareAlong(PendingCast pending, long a, long b)
        {
            float da = AlongDistance(pending, a);
            float db = AlongDistance(pending, b);
            int byDistance = da.CompareTo(db);
            return byDistance != 0 ? byDistance : a.CompareTo(b);
        }

        private float AlongDistance(PendingCast pending, long targetId)
        {
            var target = _store.GetPlayer(targetId);
            if (target == null) return float.MaxValue;
            float fx = (float)Math.Sin(pending.Yaw * Math.PI / 180.0);
            float fz = (float)Math.Cos(pending.Yaw * Math.PI / 180.0);
            return (target.Position.X - pending.Origin.X) * fx
                 + (target.Position.Z - pending.Origin.Z) * fz;
        }

        /// <summary>挥击实例几何（与 spec==null 同源）：施法者位置/朝向 + 请求形状 + 锁定目标。
        /// B1：高度带只作用于挥击；投射物分支（ResolveLineAll/ResolveCircle）保持纯 XZ。</summary>
        private static List<long> ResolveSwing(PlayerState caster, Vec3 casterPos, CastRequest request,
            SkillEntry entry, List<CombatCandidate> candidates)
            => HitResolver.Resolve(casterPos, caster.Rotation, request.TargetPlayerId,
                request.Shape, candidates, entry.AttackHeight);

        /// <summary>对一个命中目标结算：无敌检查 → 掷骰（+蓄力倍率）→ 格挡 → 扣血 → HitEntry；返回是否格挡。
        /// 普通 PvE/PvP、属性剑气与剑气回荡共用；pending 非空时附带武器倍率/元素附加。
        /// 调用方决定是否先经 GroupHitIds（挥击实例不经过去重，与剑气各算一次）。</summary>
        private bool ApplyHit(long casterId, float baseDamage, float criticalRate,
            ProjectileSpec? spec, float charge, long targetId, Vec3 attackerPos, uint tick,
            CastResolution resolution, PendingCast? pending = null, bool isProjectile = false)
        {
            var target = _store.GetPlayer(targetId);
            if (target == null || !target.IsAlive) return false;
            if (target.IsInvulnerableAt(tick)) return false;

            // 掷骰顺序不变：暴击照常掷，格挡只在最后乘倍率（确定性可复现——固定种子的
            // 期望值不因格挡分支错位；调换这里两句会让既有种子序列整体漂移）
            float damage = RollDamage(baseDamage, criticalRate, out bool isCritical);
            if (pending != null && !pending.IsNpcCaster && IsBasicAttack(pending.Request.SkillId))
            {
                float weaponRatio = _defaultWeaponBaseAttack > 0f
                    ? pending.Weapon.BaseAttack / _defaultWeaponBaseAttack
                    : 1f;
                damage *= weaponRatio;
                // 剑气分支另乘共享剑气系数与武器剑气倍率；挥击分支不乘（KeepsSwing 的挥砍保持原值）。
                if (isProjectile && spec != null && spec.Kind == ProjectileKind.LineAll)
                    damage *= _elementalCatalog.SwordQi.SwordQiDamageCoefficient
                              * pending.Weapon.SwordQiDamageMultiplier;
            }
            if (spec != null && spec.ChargeScales)
                damage *= 1f + (spec.ChargeDamageMaxScale - 1f) * charge;

            // 格挡减伤（D-P4-8 服务端化）：防御中 + 攻击者落在受击者正面 BlockHalfAngleDeg 锥内
            // ⇒ 伤害 × BlockDamageScale、命中条目标记 IsBlocked。盾资源只由客户端精力控制
            //（2026-10-21 删除服务端耐久）。
            // **信任面（诚实登记）**：IsDefending 由客户端上报、yaw 来自客户端上报的位置同步——
            // 服务端只做姿态 + 几何判断，与既有"客户端权威位置 + 服务端部分校验"同一哲学，
            // 不是防作弊闸门（位置校验的边界就是本裁决的边界）。
            bool blocked = target.IsDefending
                && CombatRules.IsBlocked(target.Rotation,
                    attackerPos.X - target.Position.X,
                    attackerPos.Z - target.Position.Z);
            if (blocked)
                damage *= CombatRules.BlockDamageScale;

            // Plan 03：元素会话玩家 720 HP；NPC 直接伤害按同一基准缩放，保持原 PvE TTK。
            // 玩家对玩家/怪物、bot（MaxHp=100）不缩放。
            if (pending?.IsNpcCaster == true && target.Kind == (byte)EntityKind.Player && target.IsLoggedIn)
                damage = CombatRules.ScaleNpcDamageToPlayer(damage, target.MaxHp);

            // Plan 04：风易伤在扣血前读取——本次命中自身不吃刚附加的倍率，
            // 同次施法的后续命中（挥砍之后的剑气等）才读取新倍率。
            bool pvp = target.Kind == (byte)EntityKind.Player && pending?.IsNpcCaster != true;
            if (target.Statuses != null)
                damage *= target.Statuses.Runtime.IncomingDamageMultiplier(pvp,
                    _elementalCatalog.Pvp.WindVulnerabilityMultiplier);

            target.ApplyDamage(damage);
            if (target.Kind == (byte)EntityKind.Npc)
                NpcDamaged?.Invoke(target.PlayerId, casterId, damage);

            // Plan 04：10002 首个有效命中附加武器元素状态；每目标每次施法只附加一次
            // （挥砍先结算 ⇒ 同组剑气不再附加；剑气先命中则由剑气附加）。
            if (pending != null && !pending.IsNpcCaster && pending.Request.SkillId == 10002
                && pending.Weapon.ElementId > 0 && damage > 0f && !blocked && target.IsAlive)
            {
                var key = (targetId, target.LifeId);
                if (pending.ElementAppliedTargets.Add(key))
                {
                    target.ConfigureStatuses(_elementalCatalog);
                    var element = _elementalCatalog.Elements.Find(e => e.ElementId == pending.Weapon.ElementId);
                    if (element != null)
                        foreach (int effectId in element.StatusEffectIds)
                        {
                            var def = _elementalCatalog.Statuses.Find(s => s.EffectId == effectId);
                            if (def != null)
                                target.Statuses!.Apply(def, pending.CasterId, pending.Request.SkillId,
                                    pending.CastId, tick);
                        }
                }
            }

            uint seq = target.AllocateEventSeq();
            resolution.Hits.Add(new HitEntry
            {
                TargetPlayerId = targetId,
                Damage = damage,
                IsCritical = isCritical,
                IsBlocked = blocked,
                TargetRemainingHp = target.Hp, // 扣后权威余血（T12 甲-2）——格挡分支同样成立
                HitX = target.Position.X,
                HitZ = target.Position.Z,
                CastId = pending?.CastId ?? 0,
                DamageKind = DamageKind.Direct,
                TargetLifeId = target.LifeId,
                EventSeq = seq,
                EventId = AllocateEventId(),
                CombatVersion = target.CombatVersion // 状态/伤害 bump 之后的目标当前版本
            });
            if (!blocked && target.IsAlive && resolution.Broadcast?.SkillId == CombatRules.ChargeBlastSkillId)
                PushFromChargeBlast(target, attackerPos, resolution.Broadcast.CasterRotation, charge);
            return blocked;
        }

        private static bool IsBasicAttack(int skillId) => skillId == 10001 || skillId == 10002;

        private void PushFromChargeBlast(PlayerState target, Vec3 center, float yaw, float charge)
        {
            bool player = target.Kind == (byte)EntityKind.Player;
            if (player && (target.Features & ProtocolFeatures.AuthoritativeKnockback) == 0) return;

            Vector3 start = target.Position;
            float dx = start.X - center.X;
            float dz = start.Z - center.Z;
            float length = MathF.Sqrt(dx * dx + dz * dz);
            if (length < 0.001f)
            {
                float radians = yaw * MathF.PI / 180f;
                dx = MathF.Sin(radians);
                dz = MathF.Cos(radians);
                length = 1f;
            }

            float distance = CombatRules.ChargeBlastBasePushSpeed * CombatRules.ChargeBlastPushTime
                * CombatRules.ChargeBlastPushScale(charge);
            var displacement = new Vector3(dx * distance / length, 0f, dz * distance / length);
            Vector3 end = ChargePushNavigation == null
                ? start + displacement
                : ChargePushNavigation.ClampDisplacement(start, displacement,
                    Math.Clamp(target.BodyRadius * 0.4f, 0.1f, 0.65f));
            if (MathF.Abs(end.X - start.X) + MathF.Abs(end.Z - start.Z) < 0.05f) return;

            target.UpdatePosition(end, target.Rotation, 0f, target.LastUpdateTimestamp, target.LastReceivedSequence);
            if (player)
                target.MovementRevision++;
            else if (target.Kind == (byte)EntityKind.Npc)
                NpcPushed?.Invoke(target.PlayerId, end);
        }

        /// <summary>候选（P4：全部在线实体 − caster；P5 D-P5-3 扩展）：
        ///  玩家施法 → 玩家 + 怪物（PvE 挥击自动目标按锥内最近者自然命中怪物——
        ///    跨界误伤玩家 backlog 项落地；bot 压测实体同 P4 语义不排除）
        ///  怪物施法 → 仅已登录玩家（怪物互不打；bot 不可被怪物命中——**这条过滤不是死代码**）</summary>
        private List<CombatCandidate> BuildCandidates(PendingCast pending, string? zoneId)
        {
            var candidates = new List<CombatCandidate>();
            var caster = _store.GetPlayer(pending.CasterId);
            string zone = caster?.ZoneId ?? "";
            foreach (var p in _store.All)
            {
                if (p.PlayerId == pending.CasterId) continue;
                // 场景隔离：不同战斗场景的实体既不成为候选，也不接收广播。
                // 显式 zoneId（蓄力炎爆使用 CombatZone 归一化）优先；否则保持既有 ZoneId 原始语义。
                if (zoneId != null)
                {
                    if (CombatZone(p) != zoneId) continue;
                }
                else if (zone.Length != 0 && p.ZoneId != zone) continue;
                if (pending.IsNpcCaster && (p.Kind != (byte)EntityKind.Player || !p.IsLoggedIn)) continue;
                candidates.Add(new CombatCandidate(p.PlayerId,
                    new Vec3(p.Position.X, ResolveCombatHeight(p), p.Position.Z), p.IsAlive,
                    p.BodyRadius, BodyHeightOf(p)));
            }
            return candidates;
        }

        private static string CombatZone(PlayerState player)
            => string.IsNullOrEmpty(player.ZoneId) ? "Global" : player.ZoneId;

        /// <summary>B1：非 NPC 实体的判定高度——以客户端自报 Y 为候选，有 navmesh 地面时
        /// 钳到 [地面−1m, 地面+MaxJumpHeight]；NPC 位置是服务端权威，不钳制。</summary>
        internal float ResolveCombatHeight(PlayerState p)
        {
            if (GroundHeight == null || p.Kind == (byte)EntityKind.Npc) return p.Position.Y;
            float? ground = GroundHeight(new Vec3(p.Position.X, p.Position.Y, p.Position.Z));
            if (!ground.HasValue) return p.Position.Y;
            return Math.Clamp(p.Position.Y, ground.Value - 1f,
                ground.Value + MovementValidator.MaxJumpHeight);
        }

        /// <summary>受击体积高度：玩家 1.8m；NPC 用 bodyRadius 近似（最小 1m）。B1 高度重叠判定用。</summary>
        internal static float BodyHeightOf(PlayerState p)
            => p.Kind == (byte)EntityKind.Npc
                ? Math.Max(1f, p.BodyRadius * 2f)
                : CombatRules.PlayerBodyHeight;

        private static float ScaledRadius(ProjectileSpec spec, float charge)
            => spec.ChargeScales
                ? spec.Radius * (1f + (spec.ChargeAreaMaxScale - 1f) * charge)
                : spec.Radius;

        private List<long> ResolveNearestStage(PendingCast pending, ProjectileSpec spec, int stage,
            List<CombatCandidate> candidates)
        {
            long nearest = HitResolver.ResolveLineNearest(
                pending.Origin, pending.Yaw,
                stage * spec.Length / spec.Stages, spec.Length / spec.Stages,
                spec.HalfWidth, pending.GroupHitIds, candidates, out var hitPos);
            if (nearest == 0) return new List<long>();
            // 爆炸：以命中者位置为中心做半径 Round；组长已在 GroupHitIds 里，跳过重复
            var splash = HitResolver.ResolveCircle(hitPos, spec.Radius, candidates);
            splash.Remove(nearest);
            splash.Insert(0, nearest);
            return splash;
        }

        /// <summary>D4：战斗骰只有服务器掷——暴击 ×1.5；波动 0 与客户端 GameSettings.DamageFluctuation 默认对齐（D-P4-4）</summary>
        private float RollDamage(float baseDamage, float criticalRate, out bool isCritical)
        {
            isCritical = criticalRate > 0f && _rng.NextDouble() < criticalRate;
            return baseDamage * (isCritical ? CombatRules.CritMultiplier : 1f);
        }

        private static CastResolution Reject(CastResolution resolution, string reason)
        {
            resolution.RejectReason = reason;
            return resolution;
        }

        /// <summary>拒绝同时补发当前装备摘要（供客户端恢复），并缓存请求序号的幂等结论。</summary>
        private CastResolution Reject(CastResolution resolution, PlayerState caster,
            CastRequest request, string reason)
        {
            resolution.RejectReason = reason;
            var weapon = ResolveWeapon(caster);
            resolution.Confirm = BuildConfirm(caster, request, weapon, false, reason, 0, 0,
                weapon.AttackSpeedMultiplier);
            CacheConfirm(caster, request, resolution.Confirm);
            return resolution;
        }
    }

    /// <summary>
    /// P4 施放处理器（spec §5-P4）——CombatResolver 薄壳：session→playerId 映射 +
    /// 广播发送（D-P4-6：CastBroadcast → caster + 视野观察者；DamageResult → caster + 受击者）。
    /// 拒绝静默 + 审计日志 + 计数（验收口径）。主循环线程执行。
    /// </summary>
    public sealed class CastHandler
    {
        private readonly Server3C _server;
        private readonly ILogger _logger;
        private readonly CombatResolver _resolver;

        // 已收到发射描述的会话（终止时即使已移出视野也要送达）。有界清理见 BroadcastProjectileAsync。
        private readonly Dictionary<ulong, (HashSet<long> recipients, uint launchTick)> _projectileRecipients =
            new Dictionary<ulong, (HashSet<long>, uint)>();

        private sealed class EchoRequestRecord
        {
            public ulong LastRequestId;
            public SwordQiEchoState? LastResponse;
            public SwordQiEchoState? VisualState;
            public HashSet<long>? VisualRecipients;
            public ulong LastCastId;
        }

        private readonly Dictionary<long, EchoRequestRecord> _echoRequests =
            new Dictionary<long, EchoRequestRecord>();

        /// <summary>拒绝计数（验收/统计口径，与 AoiBroadcaster.TotalEntitySends 同惯例）</summary>
        public long RejectedCasts { get; private set; }

        public CastHandler(Server3C server, ILogger logger, CombatResolver resolver)
        {
            _server = server;
            _logger = logger;
            _resolver = resolver;
        }

        /// <summary>接线 NPC 判定帧 LOS（Server3C 在 EnableMonsters 后调用；null = 关闭检查）。</summary>
        public void SetNpcLineOfSight(Func<long, Vector3, Vector3, bool>? lineOfSight)
            => _resolver.NpcLineOfSight = lineOfSight;

        /// <summary>接线 NPC 受击归属（Server3C 在 EnableMonsters 后调用；null = 关闭）。</summary>
        public void SetNpcDamageObserver(Action<long, long, float>? observer)
            => _resolver.NpcDamaged = observer;

        public void SetNpcActionRunner(Func<long, KcpServer.Monster.MonsterActionRunner?>? runner)
            => _resolver.NpcActionRunner = runner;

        public void SetChargePush(Action<long, Vector3> npcPushed, NavigationService? navigation)
        {
            _resolver.NpcPushed = npcPushed;
            _resolver.ChargePushNavigation = navigation;
        }

        /// <summary>接线 B1 地面高度（Server3C 在 EnableMonsters 后调用；null = 不钳制客户端 Y）。</summary>
        public void SetGroundHeight(Func<Vec3, float?>? groundHeight)
            => _resolver.GroundHeight = groundHeight;

        /// <summary>裁决入口（怪物：tick 内决策 → 校验 + 冷却 + 入队）</summary>
        public CombatResolver.CastResolution Decide(long casterId, CastRequest request, uint tick)
            => _resolver.Decide(casterId, request, tick);

        /// <summary>到期取出（Server3C 每 tick 在 TickRespawns 之前调用）</summary>
        public List<CombatResolver.CastResolution> DrainResolved(uint tick)
        {
            var resolved = _resolver.DrainResolved(tick);
            if (_resolver.HasEndedEchoCasts)
            {
                foreach (var cast in _resolver.DrainEndedEchoCasts())
                    OnEchoCastEnded(cast);
            }
            return resolved;
        }

        /// <summary>决策阶段广播"挥砍开始"（F7：时间戳 = 剪辑起始时刻）。拒绝分支内部处理（日志 + 计数）。
        /// casterId/request 只有拒绝日志用得到——拒绝时 <c>decision.Broadcast</c> 恒为 null，
        /// 不传进来日志就只剩空 id（审计口径须可证伪）。</summary>
        public async Task BroadcastSwingAsync(long casterId, CastRequest request, CombatResolver.CastResolution decision)
        {
            // 本人始终收到 CastConfirm（含拒绝与幂等重发）；NPC 无 session，跳过。
            if (!decision.IsNpcCaster && decision.Confirm != null)
            {
                var casterSession = _server.GetSession(casterId);
                if (casterSession != null)
                    await _server.SendToSessionAsync(casterSession, MessageId.CastConfirm, decision.Confirm);
            }

            if (!decision.Accepted || decision.Broadcast == null)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: caster={casterId} skill={request.SkillId} reason={decision.RejectReason}");
                return;
            }

            // 重复序号只回原确认，不再向观察者重播挥砍广播。
            if (decision.Replay) return;
            // 剑气回荡走 SwordQiEchoState，不发普通 CastBroadcast（老客户端不认识 20006）。
            if (decision.Echo != null) return;

            var broadcast = decision.Broadcast;
            var caster = _server.GetEntity(broadcast.CasterPlayerId);
            string zone = caster?.ZoneId ?? "";
            if (decision.IsNpcCaster)
            {
                foreach (var recipientId in _server.GetObserversOf(broadcast.CasterPlayerId))
                {
                    if (zone.Length != 0 && _server.GetEntity(recipientId)?.ZoneId != zone) continue;
                    var s = _server.GetSession(recipientId);
                    if (s != null)
                        await _server.SendToSessionAsync(s, MessageId.CastBroadcast, broadcast);
                }
                return;
            }

            var castRecipients = new HashSet<long> { broadcast.CasterPlayerId };
            foreach (var p in _server.GetVisiblePlayers(broadcast.CasterPlayerId))
                if (zone.Length == 0 || p.ZoneId == zone)
                    castRecipients.Add(p.PlayerId);
            foreach (var recipientId in castRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.CastBroadcast, broadcast);
            }
        }

        /// <summary>伤害广播收件人（纯函数，便于单测）：施法者（怪物施法无 session，不收）+
        /// 每个受击者 + 施法者一侧的 AOI 观察者 + 每个受击者的 AOI 观察者，HashSet 去重。
        /// 玩家施法者的观察者取其对侧可见玩家（GetVisiblePlayers），怪物施法者取 GetObserversOf。</summary>
        public static HashSet<long> CollectDamageRecipients(
            IReadOnlyList<long> hitTargets, long casterId, bool npcCaster,
            Func<long, IEnumerable<long>> observersOf,
            Func<long, IEnumerable<PlayerState>> visiblePlayersOf)
        {
            var recipients = new HashSet<long>();
            if (!npcCaster) recipients.Add(casterId);
            foreach (var id in hitTargets) recipients.Add(id);

            IEnumerable<long> casterObservers = npcCaster
                ? observersOf(casterId)
                : visiblePlayersOf(casterId).Select(p => p.PlayerId);
            foreach (var id in casterObservers) recipients.Add(id);

            foreach (var target in hitTargets)
                foreach (var id in observersOf(target)) recipients.Add(id);

            return recipients;
        }

        /// <summary>全局事件 ID（Plan 04 灼烧跳数填 <see cref="HitEntry.EventId"/>）——复用判定器的分配器。</summary>
        public ulong AllocateEventId() => _resolver.AllocateEventId();

        /// <summary>Plan 04：灼烧跳数的伤害广播。收件人与直接伤害同规则
        /// （来源 + 目标 + 双方观察者）；来源恒为玩家施法者（怪物不施加元素状态）。
        /// 场景过滤与 <see cref="BroadcastDamageAsync"/> 一致（<see cref="Server3C.IsSameCombatScene"/>；
        /// 目标实体缺失时保持旧行为不过滤）。</summary>
        public Task BroadcastDotDamageAsync(long sourceId, int skillId, HitEntry hit)
        {
            var result = new DamageResult
            {
                SourcePlayerId = sourceId,
                SkillId = skillId,
                ServerTimestamp = ServerClock.NowTicks(),
                Hits = new List<HitEntry> { hit },
                SourceKind = (int)EntityKind.Player
            };
            var recipients = CollectDamageRecipients(
                new[] { hit.TargetPlayerId }, sourceId, npcCaster: false,
                id => _server.GetObserversOf(id), id => _server.GetVisiblePlayers(id));
            var target = _server.GetEntity(hit.TargetPlayerId);
            foreach (var id in recipients)
            {
                if (target != null && !Server3C.IsSameCombatScene(target, _server.GetEntity(id))) continue;
                var s = _server.GetSession(id);
                if (s != null)
                    _ = _server.SendToSessionAsync(s, MessageId.DamageResult, result);
            }
            return Task.CompletedTask;
        }

        /// <summary>判定阶段广播伤害（D-P4-6）。收件人含施法者、受击者与双方观察者（去重）；
        /// 怪物施法者无 session，由 GetSession 自然跳过。</summary>
        public async Task BroadcastDamageAsync(CombatResolver.CastResolution resolution)
        {
            if (resolution.Hits.Count == 0 || resolution.Broadcast == null) return;

            var result = resolution.ToDamageResult(ServerClock.NowTicks());

            var hitTargets = new List<long>();
            foreach (var hit in resolution.Hits) hitTargets.Add(hit.TargetPlayerId);

            var recipients = CollectDamageRecipients(
                hitTargets,
                resolution.Broadcast.CasterPlayerId,
                resolution.IsNpcCaster,
                id => _server.GetObserversOf(id),
                id => _server.GetVisiblePlayers(id));

            var casterEntity = _server.GetEntity(resolution.Broadcast.CasterPlayerId);
            if (casterEntity != null)
                recipients.RemoveWhere(id =>
                    _server.GetEntity(id) is PlayerState p && p.ZoneId != casterEntity.ZoneId);

            foreach (var recipientId in recipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.DamageResult, result);
            }
        }

        /// <summary>剑气权威发射/终止广播。收件人 = 施法者 + 同场景可见观察者；
        /// 终止时并上已收到发射描述的会话（移出视野后仍能收到）。</summary>
        public async Task BroadcastProjectileAsync(CombatResolver.CastResolution resolution)
        {
            if (resolution.Launch != null)
            {
                var recipients = CollectCastRecipients(
                    resolution.Launch.CasterPlayerId, resolution.IsNpcCaster);
                _projectileRecipients[resolution.Launch.CastId] =
                    (recipients, resolution.Launch.LaunchTick);
                PruneProjectileRecipients();

                foreach (var recipientId in recipients)
                {
                    var s = _server.GetSession(recipientId);
                    if (s != null)
                        await _server.SendToSessionAsync(
                            s, MessageId.ProjectileLaunchBroadcast, resolution.Launch);
                }
            }

            if (resolution.Stop != null)
            {
                var recipients = new HashSet<long>();
                if (_projectileRecipients.TryGetValue(resolution.Stop.CastId, out var known))
                {
                    recipients.UnionWith(known.recipients);
                    _projectileRecipients.Remove(resolution.Stop.CastId);
                }
                recipients.UnionWith(CollectCastRecipients(
                    resolution.Stop.CasterPlayerId, resolution.IsNpcCaster));

                foreach (var recipientId in recipients)
                {
                    var s = _server.GetSession(recipientId);
                    if (s != null)
                        await _server.SendToSessionAsync(
                            s, MessageId.ProjectileStopBroadcast, resolution.Stop);
                }
            }
        }

        private HashSet<long> CollectCastRecipients(long casterId, bool isNpc)
        {
            var recipients = new HashSet<long>();
            var caster = _server.GetEntity(casterId);
            string zone = caster?.ZoneId ?? "";
            recipients.Add(casterId);

            if (isNpc)
            {
                foreach (var id in _server.GetObserversOf(casterId))
                    if (zone.Length == 0 || _server.GetEntity(id)?.ZoneId == zone)
                        recipients.Add(id);
            }
            else
            {
                foreach (var p in _server.GetVisiblePlayers(casterId))
                    if (zone.Length == 0 || p.ZoneId == zone)
                        recipients.Add(p.PlayerId);
            }
            return recipients;
        }

        private void PruneProjectileRecipients()
        {
            const int maxEntries = 128;
            if (_projectileRecipients.Count <= maxEntries) return;

            var ordered = new List<KeyValuePair<ulong, (HashSet<long> recipients, uint launchTick)>>(
                _projectileRecipients);
            ordered.Sort((a, b) => a.Value.launchTick.CompareTo(b.Value.launchTick));
            int remove = _projectileRecipients.Count - maxEntries;
            for (int i = 0; i < remove; i++)
                _projectileRecipients.Remove(ordered[i].Key);
        }

        /// <summary>晚进观察者基线补发：把指定施法者的在途投射物原样重发一份
        /// （含原始 ServerTimestamp，客户端据此推导飞行进度）。只补发同 CombatSceneId 的观察者。</summary>
        public void ResendActiveProjectiles(long observerId, long casterId)
        {
            var observer = _server.GetEntity(observerId);
            foreach (var launch in _resolver.ActiveLaunchesFor(casterId))
            {
                if (observer == null || observer.ZoneId != launch.CombatSceneId) continue;
                var session = _server.GetSession(observerId);
                if (session != null)
                    _ = _server.SendToSessionAsync(session,
                        MessageId.ProjectileLaunchBroadcast, launch);
            }
        }

        public async Task HandleCastAsync(IServerSession session, CastRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: session={session.SessionId} skill={request.SkillId} reason=not-logged-in");
                return;
            }

            if (request.SkillId == 20006)
            {
                await HandleEchoCastAsync(session, playerId.Value, request);
                return;
            }

            // preTick 已在包处理之前把 CurrentTick 写成当前 tick（KCP 包与 WS after-pre-tick
            // 动作同基准）——决策直接取 CurrentTick，与怪物路径（tick 内决策）口径一致。
            // 守护：CastResolutionSchedulingTests.Packet_Path_Uses_CurrentTick_As_DecideTick。
            var decision = _resolver.Decide(playerId.Value, request, _server.CurrentTick);
            await BroadcastSwingAsync(playerId.Value, request, decision);
        }

        /// <summary>剑气回荡：请求缓存/重放 → 决策 → Started 广播。旧序号拒绝且不改变状态。</summary>
        private async Task HandleEchoCastAsync(IServerSession session, long playerId, CastRequest request)
        {
            var record = GetEchoRecord(playerId);

            if (request.EchoRequestId != 0 && request.EchoRequestId < record.LastRequestId)
            {
                await SendEchoStateAsync(session, BuildRejected(playerId, request, "stale-request"));
                return;
            }

            if (request.EchoRequestId != 0 && request.EchoRequestId == record.LastRequestId
                && record.LastResponse != null)
            {
                await SendEchoStateAsync(session, record.LastResponse);
                return;
            }

            // preTick 后的包路径与普通施法同基准：决策直接取 CurrentTick，不再 +1。
            var decision = _resolver.Decide(playerId, request, _server.CurrentTick);
            if (!decision.Accepted)
            {
                var rejected = BuildRejected(playerId, request, decision.RejectReason);
                record.LastRequestId = request.EchoRequestId;
                record.LastResponse = rejected;
                await SendEchoStateAsync(session, rejected);
                return;
            }

            var cast = decision.Echo!;
            var started = BuildStarted(cast);
            record.LastRequestId = request.EchoRequestId;
            record.LastResponse = started;
            record.VisualState = cast.TwoStage ? started : null;
            record.VisualRecipients = cast.TwoStage ? cast.Recipients : null;
            record.LastCastId = cast.CastId;
            cast.Recipients.UnionWith(CollectEchoRecipients(cast));
            await BroadcastEchoStateAsync(cast.Recipients, started);
        }

        private EchoRequestRecord GetEchoRecord(long playerId)
        {
            if (!_echoRequests.TryGetValue(playerId, out var record))
            {
                record = new EchoRequestRecord();
                _echoRequests[playerId] = record;
            }
            return record;
        }

        /// <summary>玩家断开/移除：清请求缓存与未结算实例；已结束实例由下一 tick 发 Ended。</summary>
        public void OnPlayerRemoved(long playerId)
        {
            _resolver.RemoveEchoCastsFor(playerId);
            _echoRequests.Remove(playerId);
        }

        private void OnEchoCastEnded(SwordQiEchoCast cast)
        {
            var ended = BuildEnded(cast);
            if (_echoRequests.TryGetValue(cast.CasterId, out var record)
                && record.LastCastId == cast.CastId)
            {
                ended.RequestId = record.LastRequestId;
                record.LastResponse = ended;
                if (cast.EndPhase == SwordQiEchoPhase.Ended) record.VisualState = null;
            }
            _ = BroadcastEchoStateAsync(cast.Recipients, ended);
        }

        private static SwordQiEchoState BuildStarted(SwordQiEchoCast cast)
        {
            var state = BuildBase(cast, cast.Preparing ? SwordQiEchoPhase.Prepared : SwordQiEchoPhase.Started);
            state.ServerTimestamp = cast.Broadcast.ServerTimestamp;
            foreach (var contact in cast.Contacts)
            {
                state.Contacts.Add(new SwordQiEchoContactMessage
                {
                    TargetId = contact.TargetId,
                    SwordIndex = contact.SwordIndex,
                    OffsetTicks = contact.OffsetTicks,
                    X = contact.Position.X,
                    Y = contact.Position.Y,
                    Z = contact.Position.Z
                });
            }
            return state;
        }

        private static SwordQiEchoState BuildEnded(SwordQiEchoCast cast)
        {
            var state = BuildBase(cast, cast.EndPhase);
            state.ServerTimestamp = ServerClock.NowTicks();
            return state;
        }

        private static SwordQiEchoState BuildRejected(long casterId, CastRequest request, string reason)
            => new SwordQiEchoState
            {
                Phase = SwordQiEchoPhase.Rejected,
                CasterId = casterId,
                RequestId = request.EchoRequestId,
                EchoBindingId = request.EchoBindingId,
                RejectReason = reason,
                ServerTimestamp = ServerClock.NowTicks()
            };

        private static SwordQiEchoState BuildBase(SwordQiEchoCast cast, SwordQiEchoPhase phase)
            => new SwordQiEchoState
            {
                Phase = phase,
                CasterId = cast.CasterId,
                CastId = cast.CastId,
                RequestId = cast.RequestId,
                EchoBindingId = cast.BindingId,
                StartTick = cast.StartTick,
                EndTick = cast.EndTick,
                CooldownTicks = cast.CooldownTicks,
                OriginX = cast.Origin.X,
                OriginY = cast.Origin.Y,
                OriginZ = cast.Origin.Z,
                Rotation = cast.Rotation,
                Radius = SwordQiEchoRules.Spec.Radius,
                SwordCount = SwordQiEchoRules.Spec.SwordCount,
                OvershootMeters = SwordQiEchoRules.Spec.OvershootMeters,
                OrbitStartedTimestamp = cast.OrbitStartedTimestamp
            };

        internal SwordQiEchoState? GetEchoVisualReplay(PlayerState observer, long casterId)
        {
            if ((observer.Features & ProtocolFeatures.SwordQiEchoRecast) == 0) return null;
            if (!_echoRequests.TryGetValue(casterId, out var record) || record.VisualState is not { } state)
                return null;
            var caster = _server.GetEntity(casterId);
            if (caster == null || !caster.IsAlive || caster.ZoneId != observer.ZoneId) return null;

            double duration = unchecked(state.EndTick - state.StartTick) / (double)CombatRules.TickRateHz;
            double elapsed = (ServerClock.NowTicks() - state.ServerTimestamp) / (double)TimeSpan.TicksPerSecond;
            double tail = SwordQiEchoRules.DissolveSeconds
                + (state.Phase == SwordQiEchoPhase.Started ? SwordQiEchoRules.FinishOrbitDelaySeconds : 0d);
            if (elapsed < duration + tail)
            {
                record.VisualRecipients?.Add(observer.PlayerId);
                return state;
            }
            record.VisualState = null;
            record.VisualRecipients = null;
            return null;
        }

        private HashSet<long> CollectEchoRecipients(SwordQiEchoCast cast)
        {
            var targetIds = new List<long>(cast.Contacts.Count);
            foreach (var contact in cast.Contacts) targetIds.Add(contact.TargetId);

            var recipients = CollectDamageRecipients(targetIds, cast.CasterId, npcCaster: false,
                id => _server.GetObserversOf(id),
                id => _server.GetVisiblePlayers(id));
            var matching = FilterEchoRecipients(recipients, id => _server.GetEntity(id), cast.ZoneId);
            if (cast.TwoStage)
                matching.RemoveWhere(id => ((_server.GetEntity(id)?.Features ?? 0u)
                    & ProtocolFeatures.SwordQiEchoRecast) == 0);
            return matching;
        }

        /// <summary>同区 + 已协商 SwordQiEcho 的收件人过滤（纯函数，便于单测）。</summary>
        internal static HashSet<long> FilterEchoRecipients(IEnumerable<long> candidateIds,
            Func<long, PlayerState?> lookup, string zoneId)
        {
            var result = new HashSet<long>();
            foreach (var id in candidateIds)
            {
                var entity = lookup(id);
                if (entity == null) continue;
                if (!string.Equals(entity.ZoneId, zoneId, StringComparison.Ordinal)) continue;
                if ((entity.Features & ProtocolFeatures.SwordQiEcho) == 0) continue;
                result.Add(id);
            }
            return result;
        }

        private async Task BroadcastEchoStateAsync(IEnumerable<long> recipients, SwordQiEchoState state)
        {
            foreach (var recipientId in recipients)
            {
                var session = _server.GetSession(recipientId);
                if (session != null)
                    await _server.SendToSessionAsync(session, MessageId.SwordQiEchoState, state);
            }
        }

        private Task SendEchoStateAsync(IServerSession session, SwordQiEchoState state)
            => _server.SendToSessionAsync(session, MessageId.SwordQiEchoState, state);

        /// <summary>取消施法（B3：旋转取消后丢弃未到期判定段；CastId=0 只取最新实例）。
        /// 已发射投射物继续飞行，CastCancelBroadcast 只停止攻击表现；未登录/无匹配实例幂等忽略。</summary>
        public void HandleCancel(IServerSession session, CancelCastRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue) return;
            var cancels = _resolver.CancelPending(playerId.Value, request.SkillId,
                request.CastId, _server.CurrentTick, NpcActionCancelReason.Forced);
            BroadcastCancel(cancels);
        }

        public async Task BroadcastCancelAsync(IReadOnlyList<CastCancelBroadcast> cancels)
        {
            // 先把全部收件人取好：UnregisterEntity 在 BroadcastCancel 返回后会立刻移除实体，
            // 首个 await 之后的 GetEntity 会返回 null，后续取消会静默丢失。
            var sends = new List<(IServerSession Session, CastCancelBroadcast Cancel)>();
            foreach (var cancel in cancels)
            {
                var caster = _server.GetEntity(cancel.CasterPlayerId);
                if (caster == null) continue;
                var recipients = CollectCastRecipients(cancel.CasterPlayerId,
                    caster.Kind == (byte)EntityKind.Npc);
                foreach (var recipientId in recipients)
                {
                    var session = _server.GetSession(recipientId);
                    if (session != null) sends.Add((session, cancel));
                }
            }

            foreach (var (session, cancel) in sends)
                await _server.SendToSessionAsync(session,
                    MessageId.CastCancelBroadcast, cancel);
        }

        public void BroadcastCancel(IReadOnlyList<CastCancelBroadcast> cancels)
            => _ = BroadcastCancelAsync(cancels);

        public List<CastCancelBroadcast> CancelCaster(long casterId, uint tick, NpcActionCancelReason reason)
            => _resolver.CancelCaster(casterId, tick, reason);
    }
}
