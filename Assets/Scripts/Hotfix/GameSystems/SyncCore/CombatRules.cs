using System;
using System.Collections.Generic;
using Cool.Protocol;

namespace Cool.SyncCore
{
    /// <summary>投射物判定种类（服务端判定用；2026-10-18）。</summary>
    public enum ProjectileKind
    {
        LineAll = 0,            // 剑气：沿途全中（每目标一次）
        LineNearestSplash = 1,  // 旧火球线形判定：段内最近者 → 以其为中心溅射，后续段作废。
                                // 20003 已改 CircleAtPoint，当前表无此项 ⇒ ResolveNearestStage 为死路径（保留兼容）
        CircleAtPoint = 2,      // 火球 / 蓄力火球 / 陨石：落点圆判定
    }

    /// <summary>投射物判定参数（表值来源见各表项注释）。</summary>
    public sealed class ProjectileSpec
    {
        public ProjectileKind Kind { get; }
        public float Length { get; }
        public float HalfWidth { get; }
        public float Radius { get; }
        public int Stages { get; }
        public float TotalFlightSeconds { get; }
        public float MaxImpactDistance { get; }
        public bool ChargeScales { get; }
        public float ChargeAreaMaxScale { get; }
        public float ChargeDamageMaxScale { get; }

        /// <summary>投射物之外是否**同时**保留挥击伤害实例（10002 = true）。
        /// 单机里 BA_Heavy 的 0.35s 挥击与剑气是两个独立伤害实例；投射物分支若把它吞掉，
        /// 近身目标只会吃一半伤害。20003/20004/30001 单机无挥击 ⇒ 保持 false（否则多算）。</summary>
        public bool KeepsSwing { get; }

        /// <summary>飞行速度（m/s）；仅 CircleAtPoint 用于换算飞行延迟。0 = 无飞行延迟。</summary>
        public float Speed { get; }

        public ProjectileSpec(ProjectileKind kind, float length = 0f, float halfWidth = 0f, float radius = 0f,
            int stages = 1, float totalFlightSeconds = 0f,
            float maxImpactDistance = 0f, bool chargeScales = false,
            float chargeAreaMaxScale = 1f, float chargeDamageMaxScale = 1f,
            bool keepsSwing = false, float speed = 0f)
        {
            Kind = kind; Length = length; HalfWidth = halfWidth; Radius = radius;
            Stages = stages; TotalFlightSeconds = totalFlightSeconds;
            MaxImpactDistance = maxImpactDistance; ChargeScales = chargeScales;
            ChargeAreaMaxScale = chargeAreaMaxScale; ChargeDamageMaxScale = chargeDamageMaxScale;
            KeepsSwing = keepsSwing; Speed = speed;
        }
    }

    /// <summary>攻击体积的垂直范围（相对施法者脚底，米）。null = 不做高度过滤（既有 XZ 语义）。</summary>
    public readonly struct AttackHeightSpec
    {
        public float Min { get; }
        public float Max { get; }

        public AttackHeightSpec(float min, float max)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>重复判定参数（旋转技能：一次施法按固定间隔多次判定）。</summary>
    /// <remarks>
    /// 时间轴与客户端 <c>SpinSkillData</c> 一致：首判 = 起手剪辑时长 + TickInterval，
    /// 之后每 TickInterval 一段，直到 MaxDuration；每目标独立计数。
    /// 服务端按 <see cref="CombatRules.TickRateHz"/> 向上取整（0.2s = 3 tick；首判 0.4s = 6 tick）。
    /// </remarks>
    public readonly struct RepeatSpec
    {
        public float FirstDelaySeconds { get; }
        public float IntervalSeconds { get; }
        public float DurationSeconds { get; }
        public int MaxHitsPerTarget { get; }

        public RepeatSpec(float firstDelaySeconds, float intervalSeconds,
            float durationSeconds, int maxHitsPerTarget)
        {
            FirstDelaySeconds = firstDelaySeconds;
            IntervalSeconds = intervalSeconds;
            DurationSeconds = durationSeconds;
            MaxHitsPerTarget = maxHitsPerTarget;
        }

        /// <summary>从决策 tick 起算的各段判定 tick（升序，含首段）。</summary>
        public uint[] BuildStageTicks(uint firstTick)
        {
            float span = DurationSeconds - FirstDelaySeconds;
            int count = span < 0f || IntervalSeconds <= 0f
                ? 1
                : 1 + (int)Math.Floor(span / IntervalSeconds + 0.0001f);
            var ticks = new uint[count];
            for (int k = 0; k < count; k++)
                ticks[k] = firstTick + (uint)Math.Ceiling(
                    (FirstDelaySeconds + k * IntervalSeconds) * CombatRules.TickRateHz);
            return ticks;
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

        /// <summary>重复判定参数；null = 单段技能。旋转（20002）用。</summary>
        public RepeatSpec? Repeat { get; }

        /// <summary>攻击体积垂直范围；null = 不做高度过滤（投射物/AOE 等未纳入本批的技能）。</summary>
        public AttackHeightSpec? AttackHeight { get; }

        public SkillEntry(int skillId, int shapeType, float maxRange, float maxAreaRadius,
            float maxAngle, float cooldownSeconds, float baseDamage, float criticalRate,
            float hitDelaySeconds, ProjectileSpec? projectile = null, RepeatSpec? repeat = null,
            AttackHeightSpec? attackHeight = null)
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
            Repeat = repeat;
            AttackHeight = attackHeight;
        }
    }

    public static class CombatRules
    {
        public const float CritMultiplier = 1.5f;
        public const float TickRateHz = 15f;

        /// <summary>玩家受击体积高度（米，脚底起算）；B1 高度重叠判定用。</summary>
        public const float PlayerBodyHeight = 1.8f;

        /// <summary>地面近战攻击体积（相对施法者脚底，米）：跳起后脚底超过 1.2m 即脱离该体积。
        /// 第一批只给玩家基础近战与怪物地面近战；投射物/AOE 保持纯 XZ 语义。</summary>
        public static readonly AttackHeightSpec GroundMelee = new AttackHeightSpec(-0.3f, 1.2f);

        /// <summary>米制字段与角度字段共用的浮点容差：只吸收序列化/运算抖动，不容纳 1cm 级的范围放大。</summary>
        private const float ShapeEpsilon = 0.001f;

        // —— PvP 格挡减伤（2026-10-17；服务端裁决，客户端表现复用既有 DefendHit 链路）——
        // 防御中且攻击者落在正面 BlockHalfAngleDeg 锥内 ⇒ 伤害 × BlockDamageScale。
        // 盾资源只由客户端精力控制（2026-10-21 删除耐久）：服务端不持有盾耐久，
        // 只做姿态 + 几何判断。

        /// <summary>格挡后伤害倍率（格挡成功 ⇒ damage × 该值）</summary>
        public const float BlockDamageScale = 0.3f;

        /// <summary>格挡锥半角（度）：受击者朝向与"受击者→攻击者"方向夹角 ≤ 半角 ⇒ 格挡命中。
        /// **半角**语义（锥全角 120°）——与 HitShape.Angle（全角）不同，勿混</summary>
        public const float BlockHalfAngleDeg = 60f;

        private static readonly Dictionary<int, SkillEntry> Table = new Dictionary<int, SkillEntry>
        {
            // 玩家四条：hitDelay = 客户端动画资产的判定帧实测（ShapeBlock._hitboxTimings + CastTime）
            [10001] = new SkillEntry(skillId: 10001, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0f, baseDamage: 50f, criticalRate: 0.15f, hitDelaySeconds: 0.30f, attackHeight: GroundMelee),
            // 10002：单机 BA_Heavy 的 0.35s 挥击（[0.35]）+ 剑气是两个独立伤害实例 ⇒ keepsSwing: true。
            // 高度只作用于挥击实例（ResolveSwing）；剑气走 ResolveLineAll，保持纯 XZ。
            [10002] = new SkillEntry(skillId: 10002, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0.5f, baseDamage: 90f, criticalRate: 0.15f, hitDelaySeconds: 0.35f, projectile: new ProjectileSpec(ProjectileKind.LineAll, length: 12f, halfWidth: 2f, stages: 3, totalFlightSeconds: 12f / 18f, keepsSwing: true), attackHeight: GroundMelee),
            [20001] = new SkillEntry(skillId: 20001, shapeType: 0, maxRange: 3.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 6f, baseDamage: 120f, criticalRate: 0.2f, hitDelaySeconds: 0.30f),
            // 20002：单机 Spin_SkillR 时间轴 = 起手剪辑 0.2s（Attack04_Start_SwordAndShield，FBX 0–6 帧）
            // + 0.2s 首判间隔，之后每 0.2s 一段，MaxDuration 3s，每目标最多 5 段（资产值，2026-11-10 实测）。
            [20002] = new SkillEntry(skillId: 20002, shapeType: 1, maxRange: 2f, maxAreaRadius: 2.5f, maxAngle: 120f, cooldownSeconds: 5f, baseDamage: 160f, criticalRate: 0.25f, hitDelaySeconds: 0f,
                repeat: new RepeatSpec(firstDelaySeconds: 0.4f, intervalSeconds: 0.2f, durationSeconds: 3f, maxHitsPerTarget: 5)),
            // 2026-10-18：投射物/蓄力进表（翻转 D-P4-1）。形状上限必须容纳客户端真实形状。
            [20003] = new SkillEntry(20003, shapeType: 0, maxRange: 0f, maxAreaRadius: 0f, maxAngle: 0f,
                cooldownSeconds: 3f, baseDamage: 20f, criticalRate: 0f, hitDelaySeconds: 0f,
                projectile: new ProjectileSpec(ProjectileKind.CircleAtPoint,
                    radius: 1.5f, maxImpactDistance: 15f, speed: 18f)),
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
            [50001] = new SkillEntry(skillId: 50001, shapeType: 0, maxRange: 1.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1f, baseDamage: 10f, criticalRate: 0f, hitDelaySeconds: 0.35f, attackHeight: GroundMelee),
            [50002] = new SkillEntry(skillId: 50002, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1f, baseDamage: 15f, criticalRate: 0f, hitDelaySeconds: 0.35f, attackHeight: GroundMelee),
            // 2026-10-12 新怪（spec §3）：t3 骑士精英 / t4 蜘蛛 Boss。
            // maxRange 必须 ≥ monster_config.json 的 attackRange——填小了怪物在攻击距离静默冻结
            //（MonsterModuleTests.Template_AttackRange_Fits_Its_Combat_Rule 守这条）。
            [50003] = new SkillEntry(skillId: 50003, shapeType: 0, maxRange: 2.2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 1.5f, baseDamage: 30f, criticalRate: 0f, hitDelaySeconds: 0.35f, attackHeight: GroundMelee),
            [50004] = new SkillEntry(skillId: 50004, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 2f, baseDamage: 20f, criticalRate: 0f, hitDelaySeconds: 0.35f, attackHeight: GroundMelee),
            // P2 怪物多技能（2026-11-03）：DogKnight 突进斩 / SpiderBoss 范围技（血量 50% 以下启用）
            [50005] = new SkillEntry(50005, shapeType: 0, maxRange: 6f, maxAreaRadius: 0f, maxAngle: 60f,
                cooldownSeconds: 4f, baseDamage: 25f, criticalRate: 0f, hitDelaySeconds: 0.35f, attackHeight: GroundMelee),
            [50006] = new SkillEntry(50006, shapeType: 1, maxRange: 0f, maxAreaRadius: 4f, maxAngle: 0f,
                cooldownSeconds: 5f, baseDamage: 40f, criticalRate: 0f, hitDelaySeconds: 0.35f),
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

        /// <summary>CircleAtPoint 投射物的飞行 tick 数（距离 / 速度 × 15Hz，向上取整）。</summary>
        public static uint ProjectileTravelTicks(ProjectileSpec? spec,
            float casterX, float casterZ, float impactX, float impactZ)
        {
            if (spec == null || spec.Kind != ProjectileKind.CircleAtPoint || spec.Speed <= 0f) return 0u;
            if (!float.IsFinite(casterX) || !float.IsFinite(casterZ)
                || !float.IsFinite(impactX) || !float.IsFinite(impactZ)) return 0u;

            float dx = impactX - casterX;
            float dz = impactZ - casterZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!(distance > 0f) || !float.IsFinite(distance)) return 0u;

            double ticks = Math.Ceiling(distance / spec.Speed * TickRateHz);
            return ticks >= uint.MaxValue ? uint.MaxValue : (uint)ticks;
        }

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
