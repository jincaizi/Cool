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

        /// <summary>施放裁决结果（输出载体）</summary>
        public sealed class CastResolution
        {
            public bool Accepted;
            public string RejectReason = string.Empty;
            public CastBroadcast? Broadcast;
            public readonly List<HitEntry> Hits = new List<HitEntry>();
        }

        /// <summary>校验 + 裁决 + 状态变更（冷却记录/扣血）。广播发送由 CastHandler 负责。</summary>
        public CastResolution ResolveCast(long playerId, CastRequest request, uint tick)
        {
            var resolution = new CastResolution();
            var caster = _store.GetPlayer(playerId);
            if (caster == null) return Reject(resolution, "caster-not-found");
            if (!caster.IsAlive) return Reject(resolution, "caster-dead");
            if (!CombatRules.TryGet(request.SkillId, out var entry))
                return Reject(resolution, "unknown-skill");
            if (!CombatRules.ValidateShape(request.Shape, entry))
                return Reject(resolution, "shape-tampered");
            if (!caster.IsCastReady(request.SkillId, tick, CombatRules.CooldownTicks(entry)))
                return Reject(resolution, "cooldown");

            // 候选（P4：全部在线实体 − caster；P5 D-P5-3 扩展）：
            //  玩家施法 → 玩家 + 怪物（PvE 挥击自动目标按锥内最近者自然命中怪物——
            //    跨界误伤玩家 backlog 项落地；bot 压测实体同 P4 语义不排除）
            //  怪物施法 → 仅已登录玩家（怪物互不打；bot 不可被怪物命中）
            bool isNpcCaster = caster.Kind == (byte)EntityKind.Npc;
            var candidates = new List<CombatCandidate>();
            foreach (var p in _store.All)
            {
                if (p.PlayerId == playerId) continue;
                if (isNpcCaster && (p.Kind != (byte)EntityKind.Player || !p.IsLoggedIn)) continue;
                candidates.Add(new CombatCandidate(p.PlayerId,
                    new Vec3(p.Position.X, p.Position.Y, p.Position.Z), p.IsAlive));
            }
            var hitIds = HitResolver.Resolve(
                new Vec3(caster.Position.X, caster.Position.Y, caster.Position.Z),
                caster.Rotation, request.TargetPlayerId, request.Shape, candidates);

            // 冷却记录在裁决后（D-P4-5：空挥也冷却——与客户端激活即冷却一致）
            caster.RecordCast(request.SkillId, tick);

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

            resolution.Accepted = true;
            resolution.Broadcast = new CastBroadcast
            {
                SkillId = request.SkillId,
                CasterPlayerId = playerId,
                TargetPlayerId = request.TargetPlayerId,
                CasterX = caster.Position.X,
                CasterY = caster.Position.Y,
                CasterZ = caster.Position.Z,
                CasterRotation = caster.Rotation,
                Shape = request.Shape,
                ServerTimestamp = DateTime.UtcNow.Ticks
            };
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

        /// <summary>裁决入口（P5 怪物施法复用——怪物无 session，Server3C tick 内直调）</summary>
        public CombatResolver.CastResolution Resolve(long casterId, CastRequest request, uint tick)
            => _resolver.ResolveCast(casterId, request, tick);

        public async Task HandleCastAsync(KcpServerSession session, CastRequest request)
        {
            long? playerId = _server.GetPlayerId(session);
            if (!playerId.HasValue)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: session={session.SessionId} skill={request.SkillId} reason=not-logged-in");
                return;
            }

            var resolution = _resolver.ResolveCast(playerId.Value, request, _server.CurrentTick);
            if (!resolution.Accepted)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] cast rejected: player={playerId.Value} skill={request.SkillId} reason={resolution.RejectReason}");
                return;
            }

            // CastBroadcast → caster + caster AOI 视野内观察者（第三方看到技能表现；AoiGrid 现成语义）
            var castRecipients = new HashSet<long> { playerId.Value };
            foreach (var p in _server.GetVisiblePlayers(playerId.Value))
                castRecipients.Add(p.PlayerId);
            foreach (var recipientId in castRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.CastBroadcast, resolution.Broadcast!);
            }

            // DamageResult → caster + 每个在线受击者（双端伤害数字一致性载体；AOE 打包）
            if (resolution.Hits.Count > 0)
            {
                var result = new DamageResult
                {
                    SourcePlayerId = playerId.Value,
                    SkillId = request.SkillId,
                    ServerTimestamp = DateTime.UtcNow.Ticks,
                    Hits = resolution.Hits
                };
                var damageRecipients = new HashSet<long> { playerId.Value };
                foreach (var hit in resolution.Hits)
                    damageRecipients.Add(hit.TargetPlayerId);
                foreach (var recipientId in damageRecipients)
                {
                    var s = _server.GetSession(recipientId);
                    if (s != null)
                        await _server.SendToSessionAsync(s, MessageId.DamageResult, result);
                }
            }
        }

        /// <summary>
        /// P5 怪物施放广播（D-P5-4）：CastBroadcast → 怪物 AOI 视野内在线观察者
        /// （第三方看技能表现）；DamageResult → 每个受击玩家（怪物无 session，caster 收件人跳过）。
        /// 拒绝不广播（怪物被拒 = 服务器内部逻辑错误——审计日志 + 计数）。
        /// </summary>
        public async Task HandleNpcCastAsync(long npcId, CombatResolver.CastResolution resolution)
        {
            if (!resolution.Accepted || resolution.Broadcast == null)
            {
                RejectedCasts++;
                _logger.LogWarning($"[Combat] npc cast rejected: npc={npcId} reason={resolution.RejectReason}");
                return;
            }

            var castRecipients = _server.GetObserversOf(npcId);
            foreach (var recipientId in castRecipients)
            {
                var s = _server.GetSession(recipientId);
                if (s != null)
                    await _server.SendToSessionAsync(s, MessageId.CastBroadcast, resolution.Broadcast);
            }

            if (resolution.Hits.Count > 0)
            {
                var result = new DamageResult
                {
                    SourcePlayerId = npcId,
                    SkillId = resolution.Broadcast.SkillId,
                    ServerTimestamp = DateTime.UtcNow.Ticks,
                    Hits = resolution.Hits
                };
                foreach (var hit in resolution.Hits)
                {
                    var s = _server.GetSession(hit.TargetPlayerId);
                    if (s != null)
                        await _server.SendToSessionAsync(s, MessageId.DamageResult, result);
                }
            }
        }
    }
}
