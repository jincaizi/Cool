using System;
using System.Collections.Generic;
using Cool.Protocol;

namespace Cool.SyncCore
{
    /// <summary>投射物判定种类（服务端判定用；2026-10-18）。</summary>
    public enum ProjectileKind
    {
        LineAll = 0,            // 剑气：沿途全中（每目标一次）
        LineNearestSplash = 1,  // 火球：段内最近者 → 以其为中心溅射，后续段作废
        CircleAtPoint = 2,      // 蓄力火球 / 陨石：落点圆判定
    }

    /// <summary>投射物判定参数（表值来源见各表项注释）。</summary>
    public sealed class ProjectileSpec
    {
        public ProjectileKind Kind { get; }
        public float Length { get; }
        public float HalfWidth { get; }
        public float Radius { get; }
        public int Stages { get; }
        public float FirstDelaySeconds { get; }
        public float TotalFlightSeconds { get; }
        public float MaxImpactDistance { get; }
        public bool ChargeScales { get; }
        public float ChargeAreaMaxScale { get; }
        public float ChargeDamageMaxScale { get; }

        public ProjectileSpec(ProjectileKind kind, float length = 0f, float halfWidth = 0f, float radius = 0f,
            int stages = 1, float firstDelaySeconds = 0f, float totalFlightSeconds = 0f,
            float maxImpactDistance = 0f, bool chargeScales = false,
            float chargeAreaMaxScale = 1f, float chargeDamageMaxScale = 1f)
        {
            Kind = kind; Length = length; HalfWidth = halfWidth; Radius = radius;
            Stages = stages; FirstDelaySeconds = firstDelaySeconds; TotalFlightSeconds = totalFlightSeconds;
            MaxImpactDistance = maxImpactDistance; ChargeScales = chargeScales;
            ChargeAreaMaxScale = chargeAreaMaxScale; ChargeDamageMaxScale = chargeDamageMaxScale;
        }
    }

    /// <summary>
    /// P4 技能规则表（spec §5-P4：CombatRules 冷却/消耗/射程判定，双端同源）。
    /// 表值 = 客户端 PreRes/SkillsCfg 资产实测（2026-09-11）：
    ///   BA_Combo1 (10001) Single  range 2    cd 0s   dmg 50  crit 15%
    ///   BA_Heavy  (10002) Single  range 2.5  cd 0.5s dmg 90  crit 15%
    ///   SkillQ    (20001) Single  range 3.5  cd 6s   dmg 120 crit 20%
    ///   Spin R    (20002) Circle  radius 2.5 cd 5s   dmg 160 crit 25%
    /// 投射物/蓄力/大招自 2026-10-18 起进表（翻转 D-P4-1）；引导仍不进表，服务器拒收为未知技能。
    /// 消耗（mana）资产全 0——服务器无 mana 池（YAGNI，决策 D-P4-3）。
    /// 伤害公式（D4：战斗骰只有服务器掷）：BaseDamage × (crit ? 1.5 : 1)；波动 0 与
    /// 客户端 GameSettings.DamageFluctuation 默认对齐（改该值须同步此处）。
    /// </summary>
    public sealed class SkillEntry
    {
        // getter-only：仅经构造函数赋值，共享表状态不可变
        public int SkillId { get; }
        public int ShapeType { get; }        // HitShape.ShapeType（与客户端 TargetType 同值）
        public float MaxRange { get; }       // Single 距离上限
        public float MaxAreaRadius { get; }  // Circle 半径上限
        public float MaxAngle { get; }       // 锥形全角上限（与 ShapeBlock.Angle 同语义）
        public float CooldownSeconds { get; }
        public float BaseDamage { get; }
        public float CriticalRate { get; }
        /// <summary>判定帧延迟（秒，自技能激活起算）。来源 = 客户端动画资产的判定帧实测
        /// （<c>ShapeBlock._hitboxTimings</c> + CastTime；表注释逐条标注来源）。
        /// 服务端在 决策 tick + HitDelayTicks 处做几何判定。</summary>
        public float HitDelaySeconds { get; }

        /// <summary>投射物判定参数；null = 非投射物技能（既有瞬时技能）。</summary>
        public ProjectileSpec? Projectile { get; }

        public SkillEntry(int skillId, int shapeType, float maxRange, float maxAreaRadius,
            float maxAngle, float cooldownSeconds, float baseDamage, float criticalRate,
            float hitDelaySeconds, ProjectileSpec? projectile = null)
        {
            SkillId = skillId;
            ShapeType = shapeType;
            MaxRange = maxRange;
            MaxAreaRadius = maxAreaRadius;
            MaxAngle = maxAngle;
            CooldownSeconds = cooldownSeconds;
            BaseDamage = baseDamage;
            CriticalRate = criticalRate;
            HitDelaySeconds = hitDelaySeconds;
            Projectile = projectile;
        }
    }

    public static class CombatRules
    {
        public const float CritMultiplier = 1.5f;
        public const float TickRateHz = 15f;

        /// <summary>米制字段与角度字段共用的浮点容差：只吸收序列化/运算抖动，不容纳 1cm 级的范围放大。</summary>
        private const float ShapeEpsilon = 0.001f;

        // —— PvP 格挡减伤（2026-10-17；服务端裁决，客户端表现复用既有 DefendHit 链路）——
        // 表值自持（与判定延迟同一作风）：防御中且攻击者落在正面 BlockHalfAngleDeg 锥内 ⇒
        // 伤害 × BlockDamageScale；每格挡一次扣 ShieldDurabilityPerBlock 耐久，归零后不再减伤。

        /// <summary>格挡后伤害倍率（格挡成功 ⇒ damage × 该值）</summary>
        public const float BlockDamageScale = 0.3f;

        /// <summary>格挡锥半角（度）：受击者朝向与"受击者→攻击者"方向夹角 ≤ 半角 ⇒ 格挡命中。
        /// **半角**语义（锥全角 120°）——与 HitShape.Angle（全角）不同，勿混</summary>
        public const float BlockHalfAngleDeg = 60f;

        /// <summary>盾耐久上限（服务端权威 PlayerState.ShieldDurability 的初值；登录/复活回满）。
        /// 与客户端本地盾（CharacterController._shieldDurability 上限 50）**不同值域**：本地盾服务于
        /// 单机权威战斗，服务端盾服务联网裁决，两者不互抄</summary>
        public const float ShieldMaxDurability = 100f;

        /// <summary>每次格挡消耗的耐久（100 / 25 = 4 次格挡后耗尽）</summary>
        public const float ShieldDurabilityPerBlock = 25f;

        private static readonly Dictionary<int, SkillEntry> Table = new Dictionary<int, SkillEntry>
        {
            // 玩家四条：hitDelay = 客户端动画资产的判定帧实测（ShapeBlock._hitboxTimings + CastTime）
            [10001] = new SkillEntry(skillId: 10001, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0f, baseDamage: 50f, criticalRate: 0.15f, hitDelaySeconds: 0.30f),
            [10002] = new SkillEntry(skillId: 10002, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0.5f, baseDamage: 90f, criticalRate: 0.15f, hitDelaySeconds: 0.35f, projectile: new ProjectileSpec(ProjectileKind.LineAll, length: 12f, halfWidth: 2f, stages: 3, firstDelaySeconds: 0.35f, totalFlightSeconds: 12f / 18f)),
            [20001] = new SkillEntry(skillId: 20001, shapeType: 0, maxRange: 3.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 6f, baseDamage: 120f, criticalRate: 0.2f, hitDelaySeconds: 0.30f),
            [20002] = new SkillEntry(skillId: 20002, shapeType: 1, maxRange: 2f, maxAreaRadius: 2.5f, maxAngle: 120f, cooldownSeconds: 5f, baseDamage: 160f, criticalRate: 0.25f, hitDelaySeconds: 0f),
            // 2026-10-18：投射物/蓄力进表（翻转 D-P4-1）。形状上限必须容纳客户端真实形状。
            [20003] = new SkillEntry(20003, shapeType: 0, maxRange: 0f, maxAreaRadius: 0f, maxAngle: 0f,
                cooldownSeconds: 3f, baseDamage: 20f, criticalRate: 0f, hitDelaySeconds: 0f,
                projectile: new ProjectileSpec(ProjectileKind.LineNearestSplash,
                    length: 15f, halfWidth: 0.6f, radius: 1.5f, stages: 3,
                    firstDelaySeconds: 0f, totalFlightSeconds: 15f / 18f)),
            [20004] = new SkillEntry(20004, shapeType: 1, maxRange: 2f, maxAreaRadius: 2f, maxAngle: 120f,
                cooldownSeconds: 6f, baseDamage: 40f, criticalRate: 0f, hitDelaySeconds: 0f,
                projectile: new ProjectileSpec(ProjectileKind.CircleAtPoint, radius: 2f,
                    maxImpactDistance: 30f, chargeScales: true,
                    chargeAreaMaxScale: 1.5f, chargeDamageMaxScale: 1.5f)),
            [30001] = new SkillEntry(30001, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f,
                cooldownSeconds: 25f, baseDamage: 80f, criticalRate: 0f, hitDelaySeconds: 0.8f,
                projectile: new ProjectileSpec(ProjectileKind.CircleAtPoint, radius: 4f, maxImpactDistance: 30f)),
            // 怪物四条：**占位 0.35s**（人类裁定 D-8）。NPC 攻击剪辑无动画事件、判定帧未实测
            //（DogKnight Attack01 时长 0.833s@30fps ⇒ 0.35s ≈ 42% 处，合理但未验证）。
            // 实测后逐条替换：见 docs/superpowers/specs/2026-10-16-hit-judgment-timing-design.md R3。
            // P5 怪物技能（表值 = Server/Config/monster_config.json 实测 2026-09-12：
            //   Slime atk range 1.5 / Wolf atk range 2.0；伤害 Slime 10 / Wolf 15——旧 AiComponent 硬编码 10
            //   从未生效（死代码+攻击缺陷），P5 首次生效即差异化（D-P5-2 平衡选择，偏差记录）；
            //   cd 1s 与旧 SkillSystem 同值；怪物无暴击）
            [50001] = new SkillEntry(skillId: 50001, shapeType: 0, maxRange: 1.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1f, baseDamage: 10f, criticalRate: 0f, hitDelaySeconds: 0.35f),
            [50002] = new SkillEntry(skillId: 50002, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1f, baseDamage: 15f, criticalRate: 0f, hitDelaySeconds: 0.35f),
            // 2026-10-12 新怪（spec §3）：t3 骑士精英 / t4 蜘蛛 Boss。
            // maxRange 必须 ≥ monster_config.json 的 attackRange——填小了怪物在攻击距离静默冻结
            //（MonsterModuleTests.Template_AttackRange_Fits_Its_Combat_Rule 守这条）。
            [50003] = new SkillEntry(skillId: 50003, shapeType: 0, maxRange: 2.2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1.5f, baseDamage: 30f, criticalRate: 0f, hitDelaySeconds: 0.35f),
            [50004] = new SkillEntry(skillId: 50004, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 2f, baseDamage: 20f, criticalRate: 0f, hitDelaySeconds: 0.35f),
        };

        public static bool IsSynced(int skillId) => Table.ContainsKey(skillId);

        public static bool TryGet(int skillId, out SkillEntry entry)
        {
            if (Table.TryGetValue(skillId, out var found)) { entry = found; return true; }
            entry = null!;
            return false;
        }

        /// <summary>
        /// 形状合法性（防篡改扩大判定范围）：类型必须一致，Range/AreaRadius/Angle 不得超表上限、
        /// 不得为负；非有限值（NaN/Infinity）一律拒绝——防 NaN 绕过（NaN 比较恒 false，穿透全部上限检查）。
        /// </summary>
        public static bool ValidateShape(HitShape shape, SkillEntry entry)
        {
            if (shape == null) return false;
            if (!float.IsFinite(shape.Range) || !float.IsFinite(shape.Angle) || !float.IsFinite(shape.AreaRadius)) return false;
            if (shape.Range < 0f || shape.AreaRadius < 0f || shape.Angle < 0f) return false;
            if (shape.ShapeType != entry.ShapeType) return false;
            if (shape.Range > entry.MaxRange + ShapeEpsilon) return false;
            if (shape.AreaRadius > entry.MaxAreaRadius + ShapeEpsilon) return false;
            if (shape.Angle > entry.MaxAngle + ShapeEpsilon) return false;
            return true;
        }

        /// <summary>落点校验（仅 CircleAtPoint）。返回 null = 通过，否则为拒绝原因。</summary>
        public static string? ValidateProjectileRequest(CastRequest request, SkillEntry entry,
            float casterX, float casterZ)
        {
            var spec = entry.Projectile;
            if (spec == null || spec.Kind != ProjectileKind.CircleAtPoint) return null;
            if (!float.IsFinite(request.ImpactX) || !float.IsFinite(request.ImpactY)
                || !float.IsFinite(request.ImpactZ)) return "impact-not-finite";
            float dx = request.ImpactX - casterX;
            float dz = request.ImpactZ - casterZ;
            if (Math.Sqrt(dx * dx + dz * dz) > spec.MaxImpactDistance + 0.01f) return "impact-too-far";
            return null;
        }

        /// <summary>蓄力进度钳制：非有限 → 0，>1 → 1，负 → 0。</summary>
        public static float ClampChargeProgress(float raw)
            => float.IsFinite(raw) ? Math.Clamp(raw, 0f, 1f) : 0f;

        /// <summary>冷却换算为 tick 数（15Hz 基准，向上取整）；表值静态且非负，uint 转换安全</summary>
        public static uint CooldownTicks(SkillEntry entry) =>
            (uint)Math.Ceiling(entry.CooldownSeconds * TickRateHz);

        /// <summary>判定延迟换算为 tick 数（15Hz 基准，向上取整，与 CooldownTicks 同惯例）</summary>
        public static uint HitDelayTicks(SkillEntry entry) =>
            (uint)Math.Ceiling(entry.HitDelaySeconds * TickRateHz);

        /// <summary>
        /// 格挡几何（纯 XZ 平面，零 UnityEngine 依赖）：受击者朝向（yaw 语义与 HitResolver 一致：
        /// yaw=0 → +Z，sin/cos 展开天然 wrap 安全）与"受击者→攻击者"方向的夹角 ≤
        /// BlockHalfAngleDeg 即格挡命中。<paramref name="dx"/>/<paramref name="dz"/> =
        /// 攻击者相对受击者的偏移（caster − defender）。
        /// 零距离退化视为格挡——沿用 HitResolver 的退化护栏惯例（D-P4-2a）：贴脸时方向向量趋零、
        /// 归一化不可靠，不做此修正会让"贴着受击者打"永远无法格挡（命中侧是同一护栏的反面）。
        /// **信任面**：IsDefending 由客户端上报、yaw 也来自客户端上报的位置同步——本方法只做几何
        /// 判定，与既有"客户端权威位置 + 服务端部分校验"同一哲学，不是防作弊闸门。
        /// </summary>
        public static bool IsBlocked(float defenderYawDeg, float dx, float dz)
        {
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist < 0.0001f) return true; // 零距离退化（阈值与 HitResolver.InCone 同款）
            float forwardX = (float)Math.Sin(defenderYawDeg * Math.PI / 180.0);
            float forwardZ = (float)Math.Cos(defenderYawDeg * Math.PI / 180.0);
            float halfCos = (float)Math.Cos(BlockHalfAngleDeg * Math.PI / 180.0);
            return (forwardX * dx + forwardZ * dz) / dist >= halfCos;
        }
    }
}
