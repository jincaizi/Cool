using System;
using System.Collections.Concurrent;
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
    ///  - 入站：CastBroadcast → OnCastBroadcast 事件（P4 表现占位：宿主日志）；
    ///    DamageResult → 本地玩家走 onDamageToLocalPlayer 注入（宿主接 Sys3CEntry.ApplyServerDamage，
    ///    含格挡标志——宿主据此走格挡受击表现而不是普通受击），
    ///    远程实体直接 Emit PlayerTakeDamageEvent——现有 DisplayEventBridge/FloatText 链自动飘字。
    /// 线程模型：adapter 回调线程只入队，Update（主线程）消费——与 ClientNetworkManager 同纪律。
    /// </summary>
    public sealed class CombatNetworkBridge
    {
        private readonly INetworkClient _client;
        private readonly Func<long> _localPlayerId;
        private readonly RemoteEntityRegistry _registry;
        /// <summary>本地受击注入点：targetId / damage / isCritical / 方向 / 服务端权威余血（T12 甲-2）/
        /// 是否被格挡（服务端裁决——宿主据此走格挡受击表现）</summary>
        private readonly Action<long, float, bool, Vector3, float, bool>? _onDamageToLocalPlayer;
        private readonly ConcurrentQueue<IMessage> _incoming = new ConcurrentQueue<IMessage>();

        /// <summary>收到其他玩家施法（表现接线点；P4 宿主仅日志占位——远程实体为胶囊）</summary>
        public event Action<CastBroadcast>? OnCastBroadcast;

        /// <summary>收到伤害结果（扣血/飘字已内部处理；测试与上层观察用）</summary>
        public event Action<DamageResult>? OnDamageResult;

        public CombatNetworkBridge(INetworkClient client, Func<long> localPlayerId,
            RemoteEntityRegistry registry, Action<long, float, bool, Vector3, float, bool>? onDamageToLocalPlayer = null)
        {
            _client = client;
            _localPlayerId = localPlayerId;
            _registry = registry;
            _onDamageToLocalPlayer = onDamageToLocalPlayer;
        }

        /// <summary>
        /// 技能激活/蓄力释放 → 表内技能发施法请求（非表技能静默忽略——本地表现不受影响，D-P4-1）。
        /// 落点（CircleAtPoint 类：20004/30001）与蓄力进度随包上线；服务端校验有限性与距离上限。
        /// </summary>
        public void NotifySkillCast(int skillId, HitShape shape,
            Vector3? impactPoint = null, float chargeProgress = 0f)
        {
            if (!Cool.SyncCore.CombatRules.IsSynced(skillId)) return;
            var request = new CastRequest
            {
                SkillId = skillId,
                TargetPlayerId = 0, // D-P4-2：客户端不锁定远程目标
                Shape = shape,
                ChargeProgress = chargeProgress
            };
            if (impactPoint.HasValue)
            {
                request.ImpactX = impactPoint.Value.x;
                request.ImpactY = impactPoint.Value.y;
                request.ImpactZ = impactPoint.Value.z;
            }
            _client.Send(MessageId.CastRequest, request);
        }

        /// <summary>网络线程回调入口（宿主接线 adapter.MessageReceived）——只入队</summary>
        public void OnMessage(MessageId messageId, IMessage message)
        {
            if (messageId == MessageId.CastBroadcast || messageId == MessageId.DamageResult)
                _incoming.Enqueue(message);
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
                }
            }
        }

        private void ApplyDamageResult(DamageResult result)
        {
            long localId = _localPlayerId();
            foreach (var hit in result.Hits)
            {
                // P4：协议无方向字段——飘字/受击不依赖朝向（方向字段留待命中细节阶段，偏差记录）
                if (hit.TargetPlayerId == localId)
                {
                    // T12 甲-2：权威余血随包下传（协议早已有 TargetRemainingHp，此前零消费方）——
                    // 宿主用它对齐本地 HP，不再只看本地减法。
                    // IsBlocked 随同一回调下传：宿主走格挡受击表现（受击层既有 DefendHit 态）
                    _onDamageToLocalPlayer?.Invoke(hit.TargetPlayerId, hit.Damage, hit.IsCritical,
                        Vector3.zero, hit.TargetRemainingHp, hit.IsBlocked);
                    continue;
                }
                // 远程实体：被格挡时沿用既有飘字链路（Damage 已是减免后的值）；远程格挡动画
                // 无同步通道（表现层只做本地玩家，P4 backlog——见格挡减伤交付报告）
                var view = _registry.GetView(hit.TargetPlayerId);
                if (view == null) continue; // 视野外无视图（AOI 下不应发生——防御跳过）
                EventBus.Emit(new PlayerTakeDamageEvent(
                    view.GameObject.GetInstanceID(),
                    view.Position + Vector3.up * 1.5f,
                    HitZonePart.Torso,
                    Vector3.zero,
                    Mathf.CeilToInt(hit.Damage),
                    Color.white));
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
            => FromShapeBlock(GetShape(data) ?? new ShapeBlock());
    }
}
