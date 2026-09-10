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

        public SkillEntry(int skillId, int shapeType, float maxRange, float maxAreaRadius,
            float maxAngle, float cooldownSeconds, float baseDamage, float criticalRate)
        {
            SkillId = skillId;
            ShapeType = shapeType;
            MaxRange = maxRange;
            MaxAreaRadius = maxAreaRadius;
            MaxAngle = maxAngle;
            CooldownSeconds = cooldownSeconds;
            BaseDamage = baseDamage;
            CriticalRate = criticalRate;
        }
    }

    public static class CombatRules
    {
        public const float CritMultiplier = 1.5f;
        public const float TickRateHz = 15f;

        private static readonly Dictionary<int, SkillEntry> Table = new Dictionary<int, SkillEntry>
        {
            [10001] = new SkillEntry(skillId: 10001, shapeType: 0, maxRange: 2f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0f, baseDamage: 50f, criticalRate: 0.15f),
            [10002] = new SkillEntry(skillId: 10002, shapeType: 0, maxRange: 2.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 0.5f, baseDamage: 90f, criticalRate: 0.15f),
            [20001] = new SkillEntry(skillId: 20001, shapeType: 0, maxRange: 3.5f, maxAreaRadius: 0f, maxAngle: 120f, cooldownSeconds: 6f, baseDamage: 120f, criticalRate: 0.2f),
            [20002] = new SkillEntry(skillId: 20002, shapeType: 1, maxRange: 2f, maxAreaRadius: 2.5f, maxAngle: 120f, cooldownSeconds: 5f, baseDamage: 160f, criticalRate: 0.25f),
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
    }
}
