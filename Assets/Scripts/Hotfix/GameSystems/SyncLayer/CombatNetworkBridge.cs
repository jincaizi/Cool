using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// P4 战斗网络桥（spec §5-P4 客户端侧）：
    ///  - 出站：本地技能激活 → 表内技能发 CastRequest（形状随包上线，服务端校验上限）。
    ///    本地表现即时播放不变（预测反馈）；伤害数字等服务端结果（D-P4-8）。
    ///  - 入站：CastBroadcast → OnCastBroadcast 事件（远程玩家施法表现由 RemoteCastPresenter
    ///    消费；宿主同时把 NPC 广播路由给 registry 的挥砍排程）；
    ///    DamageResult → 本地玩家走 onDamageToLocalPlayer 注入（宿主接 Sys3CEntry.ApplyServerDamage，
    ///    含格挡标志——宿主据此走格挡受击表现而不是普通受击）；
    ///    远程视图同步 Targetable 权威余血，远程真人追加受击动画（PlayHit）；
    ///    远程 **Npc** 视图 Emit MonsterTakeDamageEvent（走怪物红字/暴击橙），
    ///    远程 **玩家/bot** 视图 Emit PlayerTakeDamageEvent（走玩家灰字），
    ///    被格挡时改发 BlockSucceededEvent（格挡飘字 + 火花），不再飘伤害数字；
    ///    由 DisplayEventBridge/FloatText 链自动飘字。
    ///    `DamageKind=Dot` 单独分流：只对齐权威余血 + 飘字（本地走 onDotDamageToLocalPlayer，
    ///    远程 Npc 发 MonsterDotDamageEvent、玩家发 PlayerTakeDamageEvent）——不得触发
    ///    受击动画/格挡/击退/命中特效/震屏（DoT 每跳都跳受击硬直是错的）。
    /// 线程模型：adapter 回调线程只入队，Update（主线程）消费——与 ClientNetworkManager 同纪律。
    /// </summary>
    public sealed class CombatNetworkBridge
    {
        private readonly INetworkClient _client;
        private readonly Func<long> _localPlayerId;
        private readonly RemoteEntityRegistry _registry;
        /// <summary>本地受击注入点：targetId / damage / isCritical / 方向 / 服务端权威余血（T12 甲-2）/
        /// 是否被格挡（服务端裁决——宿主据此走格挡受击表现）/
        /// 来源是否玩家或 bot（DamageResult.SourceKind——宿主据此区分玩家闪屏与怪物攻击）</summary>
        private readonly Action<long, float, bool, Vector3, float, bool, bool>? _onDamageToLocalPlayer;
        /// <summary>本地 DoT 跳伤注入点：targetId / damage / 服务端权威余血——只更新血量与飘字，
        /// 不触发受击 FSM/格挡/击退/闪屏（宿主接 Sys3CEntry.ApplyDotDamage）</summary>
        private readonly Action<long, float, float>? _onDotDamageToLocalPlayer;
        private readonly ConcurrentQueue<IMessage> _incoming = new ConcurrentQueue<IMessage>();
        private readonly ClientEquipmentState _equipment;
        private readonly Dictionary<int, (uint seq, ulong castId, uint lifeId)> _acceptedCasts = new();
        private uint _nextEquipSeq;
        private uint _nextCastSeq;

        /// <summary>收到其他玩家施法（表现接线点；P4 宿主仅日志占位——远程实体为胶囊）</summary>
        public event Action<CastBroadcast>? OnCastBroadcast;

        /// <summary>收到伤害结果（扣血/飘字已内部处理；测试与上层观察用）</summary>
        public event Action<DamageResult>? OnDamageResult;

        /// <summary>本机施放的 LineAll 剑气被格挡吸收（旧服务端兼容；新服务端走 ProjectileStopBroadcast）</summary>
        public event Action<Vector3>? OnLineProjectileStopped;

        /// <summary>权威装备状态（按 EquipmentRevision 合并）。</summary>
        public ClientEquipmentState Equipment => _equipment;

        /// <summary>本连接已发出的最大换装序号（角色重建时随装备摘要一起保留）。</summary>
        public uint NextEquipSeq => _nextEquipSeq;

        /// <summary>装备状态实际更新（接受或较新的拒绝摘要）。</summary>
        public event Action<EquipWeaponConfirm>? OnEquipmentChanged;

        /// <summary>每个换装确认（含拒绝）——宿主用于关闭待确认态/恢复选中行。</summary>
        public event Action<EquipWeaponConfirm>? OnEquipResult;

        public event Action<CastConfirm>? OnCastConfirm;
        public event Action<ProjectileLaunchBroadcast>? OnProjectileLaunch;
        public event Action<ProjectileStopBroadcast>? OnProjectileStop;

        /// <summary>前摇/持续施法被服务端取消（已发射的投射物继续飞行）</summary>
        public event Action<CastCancelBroadcast>? OnCastCancel;

        /// <summary>Npc 动作阶段广播（客户端动作存储 + 取消表现）</summary>
        public event Action<NpcActionEvent>? OnNpcAction;

        /// <summary>战斗投影更新（版本合并 + 活动施法表现）</summary>
        public event Action<CombatStateUpdate>? OnCombatStateUpdate;

        public CombatNetworkBridge(INetworkClient client, Func<long> localPlayerId,
            RemoteEntityRegistry registry,
            Action<long, float, bool, Vector3, float, bool, bool>? onDamageToLocalPlayer = null,
            Action<long, float, float>? onDotDamageToLocalPlayer = null,
            ClientEquipmentState? equipment = null, uint nextEquipSeq = 0)
        {
            _client = client;
            _localPlayerId = localPlayerId;
            _registry = registry;
            _onDamageToLocalPlayer = onDamageToLocalPlayer;
            _onDotDamageToLocalPlayer = onDotDamageToLocalPlayer;
            // 连接生命周期的装备摘要可跨角色重建复用；新会话由宿主传 null。
            _equipment = equipment ?? new ClientEquipmentState();
            _nextEquipSeq = nextEquipSeq;
        }

        /// <summary>发送换装请求（会话内序号 + 已确认版本）；返回发送的序号，0 表示未发送。</summary>
        public uint RequestEquip(long weaponInstanceId)
        {
            if (weaponInstanceId == 0) return 0;
            uint seq = ++_nextEquipSeq;
            _client.Send(MessageId.EquipWeaponRequest, new EquipWeaponRequest
            {
                ClientEquipSeq = seq,
                WeaponInstanceId = weaponInstanceId,
                KnownEquipmentRevision = _equipment.Revision
            });
            return seq;
        }

        /// <summary>
        /// 技能激活/蓄力释放 → 表内技能发施法请求（非表技能静默忽略——本地表现不受影响，D-P4-1）。
        /// 落点（CircleAtPoint 类：20004/30001）与蓄力进度随包上线；服务端校验有限性与距离上限。
        /// </summary>
        public void NotifySkillCast(int skillId, HitShape shape,
            Vector3? impactPoint = null, float chargeProgress = 0f, long targetId = 0)
        {
            if (!Cool.SyncCore.CombatRules.IsSynced(skillId)) return;
            var request = new CastRequest
            {
                SkillId = skillId,
                TargetPlayerId = targetId, // 默认 0 = 不锁定远程目标（D-P4-2）
                Shape = shape,
                ChargeProgress = chargeProgress,
                ClientCastSeq = ++_nextCastSeq,
                EquipmentRevision = _equipment.Revision,
                CasterLifeId = _equipment.LifeId
            };
            if (impactPoint.HasValue)
            {
                request.ImpactX = impactPoint.Value.x;
                request.ImpactY = impactPoint.Value.y;
                request.ImpactZ = impactPoint.Value.z;
            }
            _client.Send(MessageId.CastRequest, request);
        }

        /// <summary>取消施法（B3：旋转多段取消后，服务端丢弃未到期判定段）。</summary>
        public void NotifySkillCancel(int skillId)
        {
            if (!Cool.SyncCore.CombatRules.IsSynced(skillId)) return;
            _client.Send(MessageId.CancelCastRequest, new CancelCastRequest { SkillId = skillId });
        }

        /// <summary>
        /// B5：查询某技能最近一次被服务端接受的 CastId（位移上报携带）。
        /// 生命代次不匹配返回 false——复活/重建后的旧 CastId 不得复用。
        /// </summary>
        public bool TryGetAcceptedCastId(int skillId, uint lifeId, out ulong castId)
        {
            if (skillId > 0 && _acceptedCasts.TryGetValue(skillId, out var entry)
                && entry.lifeId == lifeId)
            {
                castId = entry.castId;
                return true;
            }
            castId = 0;
            return false;
        }

        private void RememberAcceptedCast(CastConfirm confirm)
        {
            if (!confirm.Accepted || confirm.ClientCastSeq == 0) return;
            if (_acceptedCasts.TryGetValue(confirm.SkillId, out var last)
                && confirm.ClientCastSeq < last.seq) return;
            _acceptedCasts[confirm.SkillId] = (confirm.ClientCastSeq, confirm.CastId, confirm.CasterLifeId);
        }

        /// <summary>网络线程回调入口（宿主接线 adapter.MessageReceived）——只入队</summary>
        public void OnMessage(MessageId messageId, IMessage message)
        {
            switch (messageId)
            {
                case MessageId.CastBroadcast:
                case MessageId.DamageResult:
                case MessageId.EquipWeaponConfirm:
                case MessageId.CastConfirm:
                case MessageId.ProjectileLaunchBroadcast:
                case MessageId.ProjectileStopBroadcast:
                case MessageId.CastCancelBroadcast:
                case MessageId.NpcActionEvent:
                case MessageId.CombatStateUpdate:
                    _incoming.Enqueue(message);
                    break;
            }
        }

        /// <summary>主线程消费（宿主每帧调用）</summary>
        public void Update()
        {
            while (_incoming.TryDequeue(out var message))
            {
                switch (message)
                {
                    case CastBroadcast broadcast:
                        OnCastBroadcast?.Invoke(broadcast);
                        break;
                    case DamageResult result:
                        ApplyDamageResult(result);
                        OnDamageResult?.Invoke(result);
                        break;
                    case EquipWeaponConfirm equip:
                        if (_equipment.TryApply(equip))
                            OnEquipmentChanged?.Invoke(equip);
                        OnEquipResult?.Invoke(equip);
                        break;
                    case CastConfirm castConfirm:
                        RememberAcceptedCast(castConfirm);
                        OnCastConfirm?.Invoke(castConfirm);
                        break;
                    case ProjectileLaunchBroadcast launch:
                        OnProjectileLaunch?.Invoke(launch);
                        break;
                    case ProjectileStopBroadcast stop:
                        OnProjectileStop?.Invoke(stop);
                        break;
                    case CastCancelBroadcast cancel:
                        OnCastCancel?.Invoke(cancel);
                        break;
                    case NpcActionEvent npcAction:
                        OnNpcAction?.Invoke(npcAction);
                        break;
                    case CombatStateUpdate combatUpdate:
                        OnCombatStateUpdate?.Invoke(combatUpdate);
                        break;
                }
            }
        }

        private void ApplyDamageResult(DamageResult result)
        {
            long localId = _localPlayerId();
            foreach (var hit in result.Hits)
            {
                // DoT 跳伤：只更新权威血量与飘字。绝不走受击 FSM/格挡/击退/PlayHit/
                // MonsterTakeDamageEvent（后两者会带出命中粒子/震屏），也不发红屏事件。
                if (hit.DamageKind == DamageKind.Dot)
                {
                    if (hit.TargetPlayerId == localId)
                    {
                        var localView = _registry.GetView(hit.TargetPlayerId);
                        localView?.Targetable?.SetVitals(hit.TargetRemainingHp, localView.MaxHp,
                            hit.TargetRemainingHp <= 0f);
                        _onDotDamageToLocalPlayer?.Invoke(hit.TargetPlayerId, hit.Damage,
                            hit.TargetRemainingHp);
                        continue;
                    }

                    var dotView = _registry.GetView(hit.TargetPlayerId);
                    if (dotView == null) continue; // 视野外无视图（与直接命中同款防御跳过）

                    Vector3 dotHitPos = dotView.Position + Vector3.up * 1.5f;
                    dotView.Targetable?.SetVitals(hit.TargetRemainingHp, dotView.MaxHp,
                        hit.TargetRemainingHp <= 0f);
                    if (dotView.Kind == (byte)EntityKind.Npc)
                    {
                        // 怪物 DoT 走专用事件：只飘红字，不触发顿帧/震屏/命中粒子。
                        EventBus.Emit(new MonsterDotDamageEvent(
                            dotView.GameObject.GetInstanceID(), dotHitPos,
                            Mathf.CeilToInt(hit.Damage)));
                    }
                    else
                    {
                        EventBus.Emit(new PlayerTakeDamageEvent(
                            dotView.GameObject.GetInstanceID(), dotHitPos, HitZonePart.Torso,
                            Vector3.zero, Mathf.CeilToInt(hit.Damage), Color.white));
                    }
                    continue;
                }

                // P4：协议无方向字段——飘字/受击不依赖朝向（方向字段留待命中细节阶段，偏差记录）
                if (hit.TargetPlayerId == localId)
                {
                    // T12 甲-2：权威余血随包下传（协议早已有 TargetRemainingHp，此前零消费方）——
                    // 宿主用它对齐本地 HP，不再只看本地减法。
                    // IsBlocked 随同一回调下传：宿主走格挡受击表现（受击层既有 DefendHit 态）；
                    // SourceKind 随包下传：显式白名单，只有 Player/PlayerBot 才算玩家来源；
                    // 未知 kind（旧服务端默认 0 以外的值/未来新增）一律按非玩家处理（fail-closed，不闪屏）。
                    bool sourceIsPlayer = result.SourceKind == (int)EntityKind.Player
                                          || result.SourceKind == (int)EntityKind.PlayerBot;
                    _onDamageToLocalPlayer?.Invoke(hit.TargetPlayerId, hit.Damage, hit.IsCritical,
                        Vector3.zero, hit.TargetRemainingHp, hit.IsBlocked, sourceIsPlayer);
                    continue;
                }
                // 远程实体：被格挡时改发表现事件（飘字/火花），不再出减免后的伤害数字；
                // 远程真人的格挡受击动画已由上面的 PlayHit(IsBlocked) 触发（DefendHit）
                var view = _registry.GetView(hit.TargetPlayerId);
                if (view == null) continue; // 视野外无视图（AOI 下不应发生——防御跳过）

                int viewId = view.GameObject.GetInstanceID();
                Vector3 hitPos = view.Position + Vector3.up * 1.5f;

                // 服务端权威余血 → 远程视图 Targetable（名牌血条/受击飘字共用）。
                // 本地玩家不走这里（上面已分流到 ApplyServerDamage）。
                view.Targetable?.SetVitals(hit.TargetRemainingHp, view.MaxHp, hit.TargetRemainingHp <= 0f);
                // 远程真人的受击表现（格挡走 DefendHit）：此前只有本地玩家有受击动画
                if (view.Kind == (byte)EntityKind.Player)
                    view.Animator?.PlayHit(hit.IsBlocked, 0.5f);

                // 玩家/bot 专用；Npc 永不格挡，守卫防未来服务端给 Npc 置 IsBlocked 时吞掉红字
                if (hit.IsBlocked && view.Kind != (byte)EntityKind.Npc)
                {
                    EventBus.Emit(new BlockSucceededEvent(
                        view.GameObject.GetInstanceID(),
                        view.Position + Vector3.up * 1.2f,
                        hitPos));
                    continue;
                }

                if (view.Kind == (byte)EntityKind.Npc)
                {
                    // 怪物走 MonsterTakeDamageEvent：DisplayEventBridge 用它选红字/暴击橙，
                    // 不能复用玩家灰字（2026-10-18 修复：此前玩家打怪整片灰字）。
                    EventBus.Emit(new MonsterTakeDamageEvent(
                        viewId, hitPos, Vector3.zero,
                        Mathf.CeilToInt(hit.Damage), hit.IsCritical,
                        result.SkillId, 1, HitZonePart.Torso, Color.white,
                        result.SourcePlayerId == localId));
                }
                else
                {
                    EventBus.Emit(new PlayerTakeDamageEvent(
                        viewId, hitPos, HitZonePart.Torso, Vector3.zero,
                        Mathf.CeilToInt(hit.Damage), Color.white));
                }
            }

            // 新服务端通过 ProjectileStopBroadcast 按 CastId 停点；旧路径只服务 CastId=0 的旧消息。
            bool authoritativeCast = result.Hits != null && result.Hits.Count > 0
                                     && result.Hits[0].CastId != 0;
            if (!authoritativeCast
                && result.ProjectileStopped
                && result.SourcePlayerId == localId
                && Cool.SyncCore.CombatRules.TryGet(result.SkillId, out var stopEntry)
                && stopEntry.Projectile?.Kind == Cool.SyncCore.ProjectileKind.LineAll)
            {
                OnLineProjectileStopped?.Invoke(new Vector3(result.StopX, 0f, result.StopZ));
            }
        }
    }

    /// <summary>ShapeBlock → HitShape 协议映射（判定几何两端同源——数值直传，枚举值同）</summary>
    public static class HitShapeConverter
    {
        public static HitShape FromShapeBlock(ShapeBlock block) => new HitShape
        {
            ShapeType = (int)block.TargetType,
            Range = block.Range,
            Angle = block.Angle,
            AngleStart = block.AngleStart,
            AngleEnd = block.AngleEnd,
            InnerRadius = block.InnerRadius,
            InnerAngle = block.InnerAngle,
            AreaRadius = block.AreaRadius
        };

        /// <summary>SkillData → ShapeBlock（与 SkillExecutor.GetShape 同款取形模式）。
        /// <c>ProjectileSkillData</c> 无 ShapeBlock，客户端发零形状是刻意的；对应表项
        /// maxRange/maxAngle=0 ⇒ 校验通过且不产生挥击判定（10002 的挥击由 KeepsSwing 单独表达）。</summary>
        public static ShapeBlock? GetShape(SkillData data)
            => (data as ComboSkillData)?.Shape
                ?? (data as InstantSkillData)?.Shape
                ?? (data as ChargedSkillData)?.Shape
                ?? (data as ChanneledSkillData)?.Shape
                ?? (data as SpinSkillData)?.Shape;

        public static HitShape FromSkillData(SkillData data)
        {
            var block = GetShape(data);
            // 无形状技能（ProjectileSkillData）必须发**零形状**：服务端表项 maxRange/maxAngle=0，
            // 任何非零值都会被判 shape-tampered。而 `new ShapeBlock()` 的字段默认值不是零
            // （Range=2/Angle=120/…），2026-11-01 浏览器 E2E 里火球正是因此被服务端拒掉。
            return block == null ? new HitShape() : FromShapeBlock(block);
        }
    }
}
