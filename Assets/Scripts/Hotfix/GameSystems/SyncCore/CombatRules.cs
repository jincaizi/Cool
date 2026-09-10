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
        public int SkillId;
        public int ShapeType;        // HitShape.ShapeType（与客户端 TargetType 同值）
        public float MaxRange;       // Single 距离上限
        public float MaxAreaRadius;  // Circle 半径上限
        public float MaxAngle;       // 锥形全角上限（与 ShapeBlock.Angle 同语义）
        public float CooldownSeconds;
        public float BaseDamage;
        public float CriticalRate;
    }

    public static class CombatRules
    {
        public const float CritMultiplier = 1.5f;
        public const float TickRateHz = 15f;

        private static readonly Dictionary<int, SkillEntry> Table = new Dictionary<int, SkillEntry>
        {
            [10001] = new SkillEntry { SkillId = 10001, ShapeType = 0, MaxRange = 2f, MaxAreaRadius = 0f, MaxAngle = 120f, CooldownSeconds = 0f, BaseDamage = 50f, CriticalRate = 0.15f },
            [10002] = new SkillEntry { SkillId = 10002, ShapeType = 0, MaxRange = 2.5f, MaxAreaRadius = 0f, MaxAngle = 120f, CooldownSeconds = 0.5f, BaseDamage = 90f, CriticalRate = 0.15f },
            [20001] = new SkillEntry { SkillId = 20001, ShapeType = 0, MaxRange = 3.5f, MaxAreaRadius = 0f, MaxAngle = 120f, CooldownSeconds = 6f, BaseDamage = 120f, CriticalRate = 0.2f },
            [20002] = new SkillEntry { SkillId = 20002, ShapeType = 1, MaxRange = 2f, MaxAreaRadius = 2.5f, MaxAngle = 120f, CooldownSeconds = 5f, BaseDamage = 160f, CriticalRate = 0.25f },
        };

        public static bool IsSynced(int skillId) => Table.ContainsKey(skillId);

        public static bool TryGet(int skillId, out SkillEntry entry)
        {
            if (Table.TryGetValue(skillId, out var found)) { entry = found; return true; }
            entry = null!;
            return false;
        }

        /// <summary>形状合法性（防篡改扩大判定范围）：类型必须一致，Range/AreaRadius/Angle 不得超表上限</summary>
        public static bool ValidateShape(HitShape shape, SkillEntry entry)
        {
            if (shape == null) return false;
            if (shape.ShapeType != entry.ShapeType) return false;
            if (shape.Range > entry.MaxRange + 0.01f) return false;
            if (shape.AreaRadius > entry.MaxAreaRadius + 0.01f) return false;
            if (shape.Angle > entry.MaxAngle + 0.01f) return false;
            return true;
        }

        /// <summary>冷却换算为 tick 数（15Hz 基准，向上取整）</summary>
        public static uint CooldownTicks(SkillEntry entry) =>
            (uint)Math.Ceiling(entry.CooldownSeconds * TickRateHz);
    }
}
