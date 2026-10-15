using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using Cool.SyncCore;

namespace KcpServer
{
    /// <summary>
    /// P4 服务端权威战斗裁决核心（spec §5-P4）——校验（已知技能/存活/冷却/形状上限）
    /// → HitResolver 几何命中 → D4 服务端掷骰 → 权威扣血 + 冷却记录。
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

        private readonly struct PendingCast
        {
            public readonly long CasterId;
            public readonly CastRequest Request;
            public readonly uint ResolveTick;
            public readonly CastBroadcast Broadcast;
            public readonly bool IsNpcCaster;

            public PendingCast(long casterId, CastRequest request, uint resolveTick,
                CastBroadcast broadcast, bool isNpcCaster)
            {
                CasterId = casterId; Request = request; ResolveTick = resolveTick;
                Broadcast = broadcast; IsNpcCaster = isNpcCaster;
            }
        }

        private readonly List<PendingCast> _pending = new List<PendingCast>();

        /// <summary>决策：校验 + 冷却记录 + 生成广播 + 入队。判定（几何/掷骰/扣血）推迟到 ResolveTick。</summary>
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
            _pending.Add(new PendingCast(casterId, request, tick + CombatRules.HitDelayTicks(entry),
                resolution.Broadcast, resolution.IsNpcCaster));
            return resolution;
        }

        /// <summary>取出到期（含本 tick）的待判定施法并裁决。单遍压缩，出队顺序 = 入队顺序（确定性）。</summary>
        public List<CastResolution> DrainResolved(uint tick)
        {
            List<CastResolution>? resolved = null;
            int write = 0;
            for (int read = 0; read < _pending.Count; read++)
            {
                var pending = _pending[read];
                if (pending.ResolveTick > tick) { _pending[write++] = pending; continue; }
                var resolution = ResolveHits(pending);
                if (resolution == null) continue;          // 施法者已死/已移除：丢弃这一刀（D-4）
                (resolved ??= new List<CastResolution>()).Add(resolution);
            }
            if (write < _pending.Count) _pending.RemoveRange(write, _pending.Count - write);
            return resolved ?? new List<CastResolution>();
        }

        /// <summary>判定帧：复查施法者存活 → 几何 → 服务端掷骰 → 扣血。返回 null 表示丢弃。</summary>
        private CastResolution? ResolveHits(PendingCast pending)
        {
            var caster = _store.GetPlayer(pending.CasterId);
            if (caster == null || !caster.IsAlive) return null;
            if (!CombatRules.TryGet(pending.Request.SkillId, out var entry)) return null; // 决策已校验，防御分支

            var candidates = new List<CombatCandidate>();
            foreach (var p in _store.All)
            {
                if (p.PlayerId == pending.CasterId) continue;
                if (pending.IsNpcCaster && (p.Kind != (byte)EntityKind.Player || !p.IsLoggedIn)) continue;
                candidates.Add(new CombatCandidate(p.PlayerId,
                    new Vec3(p.Position.X, p.Position.Y, p.Position.Z), p.IsAlive));
            }

            var hitIds = HitResolver.Resolve(
                new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z),
                caster.Rotation, pending.Request.TargetPlayerId, pending.Request.Shape, candidates);

            var resolution = new CastResolution
            {
                Accepted = true,
                IsNpcCaster = pending.IsNpcCaster,
                Broadcast = pending.Broadcast
            };
            foreach (var targetId in hitIds)
            {
                var target = _store.GetPlayer(targetId);
                if (target == null) continue;
                float damage = RollDamage(entry, out bool isCritical);
                target.ApplyDamage(damage);
                resolution.Hits.Add(new HitEntry
                {
                    TargetPlayerId = targetId,
                    Damage = damage,
                    IsCritical = isCritical,
                    TargetRemainingHp = target.Hp,
                    HitX = target.Position.X,
                    HitZ = target.Position.Z
                });
            }
            return resolution;
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
        public System.Collections.Generic.List<CombatResolver.CastResolution> DrainResolved(uint tick)
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
            var decision = _resolver.Decide(playerId.Value, request, _server.CurrentTick + 1);
            await BroadcastSwingAsync(playerId.Value, request, decision);
        }
    }
}
