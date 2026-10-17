using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 本地技能 → CastRequest 的上报策略。
    ///
    /// **可蓄力技能改为释放时上报**：激活时落点/伤害都还没定，提前发会让服务端按蓄力起始
    /// 位置判定、且"取消蓄力"仍产生一次幽灵伤害与冷却。判定必须与
    /// <c>SkillStateMachine._isCharged</c> 同源——`ChargedSkillData` **或** `MaxChargeTime > 0`
    /// 的 `ProjectileSkillData`（20003 火球资产是 2s）。
    ///
    /// 抽成静态类是为了让"激活不发 / 释放恰好一条 / 取消不发"可在 EditMode 直接断言
    /// （ClientNetworkHost 的接线方法依赖完整宿主环境，单测够不到）。
    /// </summary>
    public static class SkillCastReporter
    {
        /// <summary>可蓄力：ChargedSkillData 或 MaxChargeTime &gt; 0 的投射物（与状态机同源）</summary>
        public static bool IsChargeable(SkillData data)
            => data is ChargedSkillData
               || (data is ProjectileSkillData projectile && projectile.MaxChargeTime > 0f);

        /// <summary>
        /// 激活时上报：非蓄力技能立即发；30001 带落点（零值兜底与 SkillExecutor.SpawnMeteor
        /// 逐字一致：施法者位置 + 前向 × 12）。
        /// </summary>
        public static void ReportActivation(CombatNetworkBridge bridge, SkillData data,
            Vector3 targetPosition, Transform owner)
        {
            if (bridge == null || data == null || IsChargeable(data)) return;

            Vector3? impactPoint = null;
            if (data.SkillId == (int)SkillID.Ultimate)
            {
                Vector3 point = targetPosition;
                if (point.sqrMagnitude < 0.0001f)
                    point = owner.position + owner.forward * 12f;
                impactPoint = point;
            }

            // 10002/20003 不传落点：服务端沿施法者朝向做线形判定
            bridge.NotifySkillCast(data.SkillId, HitShapeConverter.FromSkillData(data), impactPoint);
        }

        /// <summary>释放时上报：可蓄力技能发一次（落点 + 进度）；取消蓄力不会走到这里</summary>
        public static void ReportChargeRelease(CombatNetworkBridge bridge, SkillData data,
            float chargeProgress, Vector3 impactPoint)
        {
            if (bridge == null || data == null || !IsChargeable(data)) return;

            // 只有 ChargedSkillData 按落点做圆判定；20003 是线形判定（沿施法者朝向），不传落点
            Vector3? impact = data is ChargedSkillData ? impactPoint : (Vector3?)null;
            bridge.NotifySkillCast(data.SkillId, HitShapeConverter.FromSkillData(data), impact, chargeProgress);
        }
    }
}
