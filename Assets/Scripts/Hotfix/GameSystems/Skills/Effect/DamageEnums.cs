using UnityEngine;

namespace Hotfix.GameSystems.Skills.Effect
{
    public enum DamageType
    {
        // 被护甲减少
        [Tooltip("被护甲减少")]
        Physical,
        // 被魔法抗性减少
        [Tooltip("被魔法抗性减少")]
        Magic,
        // 无视所有防御
        [Tooltip("无视所有防御")]
        True
    }

    public enum AttributeType
    {
        AttackPower,
        SpellPower,
        Health,
        Defense,
        Resistance,
        CriticalRate,
        CriticalDamage,
        Speed,
    }

    public enum ModifierType
    {
        // 加一个固定值: final = base + value
        [Tooltip("加一个固定值: final = base + value")]
        Flat,
        // 加基础的百分比: final = base * (1 + value)
        [Tooltip("加基础的百分比: final = base * (1 + value)")]
        PercentAdd,
        // 乘以最终值: final = final * (1 + value)
        [Tooltip("乘以最终值: final = final * (1 + value)")]
        PercentMult
    }
}
