using System;
using System.Collections.Generic;
using Cool.Protocol;

namespace Cool.SyncCore
{
    /// <summary>
    /// P4 技能规则表（spec §5-P4：CombatRules 冷却/消耗/射程判定，双端同源）。
    /// 表值 = 客户端 PreRes/SkillsCfg 资产实测（2026-09-11）：
    ///   BA_Combo1 (10001) Single  range 2    cd 0s   dmg 50  crit 15%
    ///   BA_Heavy  (10002) Single  range 2.5  cd 0.5s dmg 90  crit 15%
    ///   SkillQ    (20001) Single  range 3.5  cd 6s   dmg 120 crit 20%
    ///   Spin R    (20002) Circle  radius 2.5 cd 5s   dmg 160 crit 25%
    /// 其余技能（投射物/蓄力/引导/大招）P4 不进表——服务器拒收为未知技能（决策 D-P4-1）。
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

        public SkillEntry(int skillId, int shapeType, float maxRange, float maxAreaRadius,
            float maxAngle, float cooldownSeconds, float baseDamage, float criticalRate,
            float hitDelaySeconds)
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
        }
    }

    public static class CombatRules
    {
        public const float CritMultiplier = 1.5f;
        public const float TickRateHz = 15f;

        private static readonly Dictionary<int, SkillEntry> Table = new Dictionary<int, SkillEntry>
        {
            // 玩家四条：hitDelay = 客户端动画资产的判定帧实测（ShapeBlock._hitboxTimings + CastTime）
            [10001] = new SkillEntry(skillId: 10001, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0f, baseDamage: 50f, criticalRate: 0.15f, hitDelaySeconds: 0.30f),
            [10002] = new SkillEntry(skillId: 10002, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0.5f, baseDamage: 90f, criticalRate: 0.15f, hitDelaySeconds: 0.35f),
            [20001] = new SkillEntry(skillId: 20001, shapeType: 0, maxRange: 3.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 6f, baseDamage: 120f, criticalRate: 0.2f, hitDelaySeconds: 0.30f),
            [20002] = new SkillEntry(skillId: 20002, shapeType: 1, maxRange: 2f, maxAreaRadius: 2.5f, maxAngle: 120f, cooldownSeconds: 5f, baseDamage: 160f, criticalRate: 0.25f, hitDelaySeconds: 0f),
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
            if (shape.Range > entry.MaxRange + 0.01f) return false;
            if (shape.AreaRadius > entry.MaxAreaRadius + 0.01f) return false;
            if (shape.Angle > entry.MaxAngle + 0.01f) return false;
            return true;
        }

        /// <summary>冷却换算为 tick 数（15Hz 基准，向上取整）；表值静态且非负，uint 转换安全</summary>
        public static uint CooldownTicks(SkillEntry entry) =>
            (uint)Math.Ceiling(entry.CooldownSeconds * TickRateHz);

        /// <summary>判定延迟换算为 tick 数（15Hz 基准，向上取整，与 CooldownTicks 同惯例）</summary>
        public static uint HitDelayTicks(SkillEntry entry) =>
            (uint)Math.Ceiling(entry.HitDelaySeconds * TickRateHz);
    }
}
