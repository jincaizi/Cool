using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>
    /// P4 服务端权威战斗裁决核心（spec §5-P4）——校验（已知技能/存活/冷却/形状上限）
    /// → HitResolver 几何命中 → D4 服务端掷骰 → 格挡减伤（防御中的受击者）→ 权威扣血 + 冷却记录。
    /// 纯核心不依赖 Server3C/session（单测直用）；主循环独占线程（R2），无锁。
    /// </summary>
    public sealed class CombatResolver
    {
        private readonly EntityStore _store;
        private readonly Random _rng;

        public CombatResolver(EntityStore store, Random? rng = null)
        {
            _store = store;
            _rng = rng ?? new Random();
        }

        /// <summary>施放裁决结果（输出载体）——决策阶段只填 Accepted/RejectReason/Broadcast/IsNpcCaster，
        /// 判定阶段（DrainResolved）才填 Hits</summary>
        public sealed class CastResolution
        {
            public bool Accepted;
            public string RejectReason = string.Empty;
            public CastBroadcast? Broadcast;
            public bool IsNpcCaster;
            public readonly List<HitEntry> Hits = new List<HitEntry>();
        }

        /// <summary>一次施法的判定组（同一施法可排多段——剑气/火球分段推进）。可变 class：
        /// <see cref="NextStage"/>/<see cref="Spent"/> 等在 DrainResolved 推进时原地更新。</summary>
        private sealed class PendingCast
        {
            public readonly long CasterId;
            public readonly CastRequest Request;
            public readonly CastBroadcast Broadcast;
            public readonly bool IsNpcCaster;
            public readonly ProjectileSpec? Projectile;
            public readonly float Charge;
            public readonly uint[] StageTicks;              // 升序；无 Projectile 时长度 1 = 首段 tick
            public int NextStage;                            // 下一段下标
            public readonly HashSet<long> GroupHitIds = new HashSet<long>();  // LineAll 组内去重
            public bool Spent;                               // 火球首中即停
            public bool OriginCaptured;
            public Vec3 Origin;                              // 首段捕获的施法者位置
            public float Yaw;

            public PendingCast(long casterId, CastRequest request, CastBroadcast broadcast,
                bool isNpcCaster, ProjectileSpec? projectile, float charge, uint firstTick)
            {
                CasterId = casterId; Request = request; Broadcast = broadcast;
                IsNpcCaster = isNpcCaster; Projectile = projectile; Charge = charge;
                StageTicks = BuildStageTicks(projectile, firstTick);
            }

            private static uint[] BuildStageTicks(ProjectileSpec? spec, uint firstTick)
            {
                if (spec == null || spec.Stages <= 1) return new[] { firstTick };
                var ticks = new uint[spec.Stages];
                for (int k = 0; k < spec.Stages; k++)
                    ticks[k] = firstTick + (uint)Math.Ceiling(
                        k * spec.TotalFlightSeconds / spec.Stages * CombatRules.TickRateHz);
                return ticks;
            }
        }

        private readonly List<PendingCast> _pending = new List<PendingCast>();

        /// <summary>决策：校验 + 冷却记录 + 生成广播 + 入队。判定（几何/掷骰/扣血）推迟到首段到期 tick
        /// （DrainResolved → ResolveStage）。</summary>
        public CastResolution Decide(long casterId, CastRequest request, uint tick)
        {
            var resolution = new CastResolution();
            var caster = _store.GetPlayer(casterId);
            if (caster == null) return Reject(resolution, "caster-not-found");
            if (!caster.IsAlive) return Reject(resolution, "caster-dead");
            if (!CombatRules.TryGet(request.SkillId, out var entry))
                return Reject(resolution, "unknown-skill");
            if (!CombatRules.ValidateShape(request.Shape, entry))
                return Reject(resolution, "shape-tampered");
            if (!caster.IsCastReady(request.SkillId, tick, CombatRules.CooldownTicks(entry)))
                return Reject(resolution, "cooldown");

            // 2026-10-18：投射物落点/蓄力校验（只有 CircleAtPoint 需要落点）
            var projectileError = CombatRules.ValidateProjectileRequest(request, entry,
                caster.Position.X, caster.Position.Z);
            if (projectileError != null) return Reject(resolution, projectileError);
            var charge = CombatRules.ClampChargeProgress(request.ChargeProgress);
            var firstTick = tick + CombatRules.HitDelayTicks(entry);

            // D-3：冷却记录在决策（空挥也冷却，D-P4-5）
            caster.RecordCast(request.SkillId, tick);

            resolution.Accepted = true;
            resolution.IsNpcCaster = caster.Kind == (byte)EntityKind.Npc;
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
                ServerTimestamp = ServerClock.NowTicks()
            };
            // D-1/D-2：判定帧延迟由服务端表自持——判定发生在 tick + HitDelayTicks
            _pending.Add(new PendingCast(casterId, request, resolution.Broadcast, resolution.IsNpcCaster,
                entry.Projectile, charge, firstTick));
            return resolution;
        }

        /// <summary>取出到期（含本 tick）的待判定施法并按段裁决。单遍压缩，出队顺序 = 入队顺序（确定性）。</summary>
        public List<CastResolution> DrainResolved(uint tick)
        {
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
                    var resolution = ResolveStage(pending);
                    // **每个判定都产出一条**（可能 Hits 为空）——沿用原合同（原实现对每次判定都入列，
                    // 空 Hits 由广播端 `BroadcastDamageAsync` 跳过）。按 Hits.Count 过滤会改变既有
                    // 单段技能的返回值，可能打破既有测试。
                    if (resolution != null)
                        (resolved ??= new List<CastResolution>()).Add(resolution);
                    pending.NextStage++;
                }
                if (!pending.Spent && pending.NextStage < pending.StageTicks.Length) keep = true;
                if (keep) _pending[write++] = pending;
            }
            if (write < _pending.Count) _pending.RemoveRange(write, _pending.Count - write);
            return resolved ?? new List<CastResolution>();
        }

        /// <summary>判定一段：复查施法者存活 → 几何（按投射物种类）→ 服务端掷骰 → 扣血。
        /// 返回 null 表示丢弃（施法者已死/已移除/表项消失）。</summary>
        private CastResolution? ResolveStage(PendingCast pending)
        {
            var caster = _store.GetPlayer(pending.CasterId);
            if (caster == null || !caster.IsAlive) return null;   // 施法者已死：整组丢弃
            if (!CombatRules.TryGet(pending.Request.SkillId, out var entry)) return null; // 决策已校验，防御分支

            var candidates = BuildCandidates(pending);            // 原 ResolveHits 里的候选构造，抽成方法
            var spec = pending.Projectile;
            List<long> hitIds;
            List<long>? swingHitIds = null;

            if (spec == null)
            {
                hitIds = ResolveSwing(caster, pending.Request, candidates);
            }
            else
            {
                if (!pending.OriginCaptured)
                {
                    // 首段捕获一次：剑气/火球沿施法者当时的朝向，不随后续移动改变
                    pending.Origin = new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z);
                    pending.Yaw = caster.Rotation;
                    pending.OriginCaptured = true;
                }
                // 10002：单机里 0.35s 挥击与剑气是两个独立伤害实例（spec §3.2），投射物分支
                // 不能把挥击吞掉。只在首段结算一次，用施法者当前位置/朝向（与 spec==null 同源）。
                if (spec.KeepsSwing && pending.NextStage == 0)
                    swingHitIds = ResolveSwing(caster, pending.Request, candidates);

                int stage = pending.NextStage;
                hitIds = spec.Kind switch
                {
                    ProjectileKind.LineAll => HitResolver.ResolveLineAll(
                        pending.Origin, pending.Yaw,
                        stage * spec.Length / spec.Stages, spec.Length / spec.Stages,
                        spec.HalfWidth, pending.GroupHitIds, candidates),
                    ProjectileKind.LineNearestSplash => ResolveNearestStage(pending, spec, stage, candidates),
                    ProjectileKind.CircleAtPoint => HitResolver.ResolveCircle(
                        new Vec3(pending.Request.ImpactX, pending.Request.ImpactY, pending.Request.ImpactZ),
                        ScaledRadius(spec, pending.Charge), candidates),
                    _ => new List<long>(),
                };
            }

            var resolution = new CastResolution
            {
                Accepted = true, IsNpcCaster = pending.IsNpcCaster, Broadcast = pending.Broadcast
            };
            // 挥击实例先行（单机判定帧顺序：挥击结算、同帧再生成剑气），且**不写入 GroupHitIds、
            // 不受其 banned 过滤**——同一目标在挥击与剑气两条线上各结算一次（不跨组去重）
            if (swingHitIds != null)
            {
                foreach (var targetId in swingHitIds)
                    ApplyHit(pending, caster, entry, spec, targetId, resolution);
            }
            foreach (var targetId in hitIds)
            {
                if (!pending.GroupHitIds.Add(targetId)) continue;   // 组内每目标一次
                ApplyHit(pending, caster, entry, spec, targetId, resolution);
            }
            if (spec != null && spec.Kind == ProjectileKind.LineNearestSplash && hitIds.Count > 0)
                pending.Spent = true;
            return resolution;
        }

        /// <summary>挥击实例几何（与 spec==null 同源）：施法者位置/朝向 + 请求形状 + 锁定目标。</summary>
        private static List<long> ResolveSwing(PlayerState caster, CastRequest request,
            List<CombatCandidate> candidates)
            => HitResolver.Resolve(
                new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z),
                caster.Rotation, request.TargetPlayerId, request.Shape, candidates);

        /// <summary>对一个命中目标结算：掷骰（+蓄力倍率）→ 格挡 → 扣血 → HitEntry。
        /// 调用方决定是否先经 GroupHitIds（挥击实例不经过去重，与剑气各算一次）。</summary>
        private void ApplyHit(PendingCast pending, PlayerState caster, SkillEntry entry,
            ProjectileSpec? spec, long targetId, CastResolution resolution)
        {
            var target = _store.GetPlayer(targetId);
            if (target == null) return;

            // 掷骰顺序不变：暴击照常掷，格挡只在最后乘倍率（确定性可复现——固定种子的
            // 期望值不因格挡分支错位；调换这里两句会让既有种子序列整体漂移）
            float damage = RollDamage(entry, out bool isCritical);
            if (spec != null && spec.ChargeScales)
                damage *= 1f + (spec.ChargeDamageMaxScale - 1f) * pending.Charge;

            // 格挡减伤（D-P4-8 服务端化）：防御中 + 攻击者落在受击者正面 BlockHalfAngleDeg 锥内
            // + 盾未耗尽 ⇒ 伤害 × BlockDamageScale、扣耐久、命中条目标记 IsBlocked。
            // **信任面（诚实登记）**：IsDefending 由客户端上报、yaw 来自客户端上报的位置同步——
            // 服务端只做几何与耐久校验，与既有"客户端权威位置 + 服务端部分校验"同一哲学，
            // 不是防作弊闸门（位置校验的边界就是本裁决的边界）。
            bool blocked = target.IsDefending
                && target.ShieldDurability > 0f
                && CombatRules.IsBlocked(target.Rotation,
                    caster.Position.X - target.Position.X,
                    caster.Position.Z - target.Position.Z);
            if (blocked)
            {
                damage *= CombatRules.BlockDamageScale;
                target.ConsumeShieldDurability(CombatRules.ShieldDurabilityPerBlock);
            }

            target.ApplyDamage(damage);
            resolution.Hits.Add(new HitEntry
            {
                TargetPlayerId = targetId,
                Damage = damage,
                IsCritical = isCritical,
                IsBlocked = blocked,
                TargetRemainingHp = target.Hp, // 扣后权威余血（T12 甲-2）——格挡分支同样成立
                HitX = target.Position.X,
                HitZ = target.Position.Z
            });
        }

        /// <summary>候选（P4：全部在线实体 − caster；P5 D-P5-3 扩展）：
        ///  玩家施法 → 玩家 + 怪物（PvE 挥击自动目标按锥内最近者自然命中怪物——
        ///    跨界误伤玩家 backlog 项落地；bot 压测实体同 P4 语义不排除）
        ///  怪物施法 → 仅已登录玩家（怪物互不打；bot 不可被怪物命中——**这条过滤不是死代码**）</summary>
        private List<CombatCandidate> BuildCandidates(PendingCast pending)
        {
            var candidates = new List<CombatCandidate>();
            foreach (var p in _store.All)
            {
                if (p.PlayerId == pending.CasterId) continue;
                if (pending.IsNpcCaster && (p.Kind != (byte)EntityKind.Player || !p.IsLoggedIn)) continue;
                candidates.Add(new CombatCandidate(p.PlayerId,
                    new Vec3(p.Position.X, p.Position.Y, p.Position.Z), p.IsAlive));
            }
            return candidates;
        }

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
        private float RollDamage(SkillEntry entry, out bool isCritical)
        {
            isCritical = entry.CriticalRate > 0f && _rng.NextDouble() < entry.CriticalRate;
            return entry.BaseDamage * (isCritical ? CombatRules.CritMultiplier : 1f);
        }

        private static CastResolution Reject(CastResolution resolution, string reason)
        {
            resolution.RejectReason = reason;
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

        /// <summary>拒绝计数（验收/统计口径，与 AoiBroadcaster.TotalEntitySends 同惯例）</summary>
        public long RejectedCasts { get; private set; }

        public CastHandler(Server3C server, ILogger logger, CombatResolver resolver)
        {
            _server = server;
            _logger = logger;
            _resolver = resolver;
        }

        /// <summary>裁决入口（怪物：tick 内决策 → 校验 + 冷却 + 入队）</summary>
        public CombatResolver.CastResolution Decide(long casterId, CastRequest request, uint tick)
            => _resolver.Decide(casterId, request, tick);

        /// <summary>到期取出（Server3C 每 tick 在 TickRespawns 之前调用）</summary>
        public List<CombatResolver.CastResolution> DrainResolved(uint tick)
            => _resolver.DrainResolved(tick);

        /// <summary>决策阶段广播"挥砍开始"（F7：时间戳 = 剪辑起始时刻）。拒绝分支内部处理（日志 + 计数）。
        /// casterId/request 只有拒绝日志用得到——拒绝时 <c>decision.Broadcast</c> 恒为 null，
        /// 不传进来日志就只剩空 id（审计口径须可证伪）。</summary>
        public async Task BroadcastSwingAsync(long casterId, CastRequest request, CombatResolver.CastResolution decision)
        {
            if (!decision.Accepted || decision.Broadcast == null)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: caster={casterId} skill={request.SkillId} reason={decision.RejectReason}");
                return;
            }

            var broadcast = decision.Broadcast;
            if (decision.IsNpcCaster)
            {
                foreach (var recipientId in _server.GetObserversOf(broadcast.CasterPlayerId))
                {
                    var s = _server.GetSession(recipientId);
                    if (s != null)
                        await _server.SendToSessionAsync(s, MessageId.CastBroadcast, broadcast);
                }
                return;
            }

            var castRecipients = new HashSet<long> { broadcast.CasterPlayerId };
            foreach (var p in _server.GetVisiblePlayers(broadcast.CasterPlayerId))
                castRecipients.Add(p.PlayerId);
            foreach (var recipientId in castRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.CastBroadcast, broadcast);
            }
        }

        /// <summary>判定阶段广播伤害（D-P4-6）。施法者收件人：仅玩家施法时包含（怪物无 session）。</summary>
        public async Task BroadcastDamageAsync(CombatResolver.CastResolution resolution)
        {
            if (resolution.Hits.Count == 0 || resolution.Broadcast == null) return;

            var result = new DamageResult
            {
                SourcePlayerId = resolution.Broadcast.CasterPlayerId,
                SkillId = resolution.Broadcast.SkillId,
                ServerTimestamp = ServerClock.NowTicks(),
                Hits = resolution.Hits
            };

            var damageRecipients = new HashSet<long>();
            if (!resolution.IsNpcCaster) damageRecipients.Add(resolution.Broadcast.CasterPlayerId);
            foreach (var hit in resolution.Hits)
                damageRecipients.Add(hit.TargetPlayerId);

            foreach (var recipientId in damageRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.DamageResult, result);
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

            // F17：包在 tick 之间处理，此刻 CurrentTick 仍是上一 tick 的值——
            // 本次决策归属"本轮即将运行的 tick"，+1 后与怪物路径（tick 内决策）口径一致。
            // 语义出处：Server3C.CurrentTick 的注释（"packet 处理时比 lambda 已推进值落后一个迭代——勿'修正'为实时值"）。
            // **本行是本分支最易被误删的一处**：删掉 `+ 1` 不会让任何测试变红（已知覆盖缺口，
            // 见 CastResolutionSchedulingTests.Judgment_Lands_Exactly_On_DecideTick_Plus_HitDelay_Ticks 的注释）。
            var decision = _resolver.Decide(playerId.Value, request, _server.CurrentTick + 1);
            await BroadcastSwingAsync(playerId.Value, request, decision);
        }
    }
}
