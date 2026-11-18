using System;
using System.Collections.Generic;
using Cool.Protocol;
using Cool.SyncCore;
using KcpServer.Monster;

namespace KcpServer
{
    /// <summary>
    /// Plan 04：GameLoop 预 tick（网络包消费之前）的权威状态推进——
    /// 解除到期修饰（风先失效）→ 结算到期灼烧跳 → 发布 <see cref="DamageResult"/> →
    /// 推进下一跳/移除末跳 → 死亡立即清状态。
    /// 主循环线程独占（R2），无锁；下行发送经 <see cref="CastHandler"/>。
    /// </summary>
    public sealed class StatusTickService
    {
        private readonly EntityStore _store;
        private readonly CastHandler _cast;
        private readonly Func<MonsterModule?> _monsters;
        private readonly ElementalCatalogDefinition _catalog;

        /// <param name="monsters">延迟查怪物模块：Server3C 构造顺序里 EnableMonsters 晚于本服务，
        /// 且未启用怪物时必须为空。</param>
        public StatusTickService(EntityStore store, CastHandler cast,
            Func<MonsterModule?> monsters, ElementalCatalogDefinition catalog)
        {
            _store = store;
            _cast = cast;
            _monsters = monsters;
            _catalog = catalog;
        }

        public void Tick(uint tick)
        {
            var monsters = _monsters();
            foreach (var entity in _store.All)
            {
                if (entity.Statuses == null) continue;
                entity.Statuses.RemoveExpired(tick);
                var due = new List<ActiveStatus>();
                entity.Statuses.Runtime.CollectDueDots(tick, due);
                foreach (var status in due)
                {
                    if (!entity.IsAlive) { entity.Statuses.ClearAll(tick); break; }
                    // PvE（目标 NPC 或来源 NPC）吃状态表易伤倍率；PvP 玩家目标不放大灼烧。
                    bool pvp = entity.Kind == (byte)EntityKind.Player && !IsNpcSource(status.SourceId);
                    float wind = pvp ? 1f : entity.Statuses.Runtime.IncomingDamageMultiplier(
                        pvp: false, _catalog.Pvp.WindVulnerabilityMultiplier);
                    float damage = status.Definition.DamagePerTick * wind;
                    entity.ApplyDamage(damage);
                    if (entity.Kind == (byte)EntityKind.Npc)
                        monsters?.OnDamageTaken(entity.PlayerId, status.SourceId, damage, MonsterDamageKind.Dot);

                    var hit = new HitEntry
                    {
                        TargetPlayerId = entity.PlayerId,
                        Damage = damage,
                        TargetRemainingHp = entity.Hp,
                        CastId = status.SourceCastId,
                        DamageKind = DamageKind.Dot,
                        TargetLifeId = entity.LifeId,
                        EventSeq = entity.AllocateEventSeq(),
                        EventId = _cast.AllocateEventId(),
                        CombatVersion = entity.CombatVersion
                    };
                    _ = _cast.BroadcastDotDamageAsync(status.SourceId, status.SourceSkillId, hit);
                    entity.Statuses.AdvanceDot(status, tick);
                    if (!entity.IsAlive) entity.Statuses.ClearAll(tick);
                }
                entity.Statuses.RemoveExpiredDots(tick);
            }
        }

        private bool IsNpcSource(long sourceId)
        {
            var source = _store.GetPlayer(sourceId);
            return source != null && source.Kind == (byte)EntityKind.Npc;
        }
    }
}
