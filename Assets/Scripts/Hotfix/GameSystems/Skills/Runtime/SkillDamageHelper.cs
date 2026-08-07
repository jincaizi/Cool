using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 技能伤害上下文填充：SkillExecutor.ApplyDamage 与 SwordQiProjectile 共用，
    /// 保证 SkillId/ComboIndex/CalculatedDamage/击退 语义一致。
    /// </summary>
    public static class SkillDamageHelper
    {
        public static void FillDamageContext(DamageBlock damage, int skillId, int comboIndex,
                                             EffectBlock effect, IEffectStats stats)
        {
            if (damage == null) return;

            damage.SkillId = skillId;
            damage.ComboIndex = comboIndex;
            damage.CalculatedDamage = damage.CalculateFinalDamage(stats);

            // 与 ApplyDamage 原逻辑一致：EffectBlock 击退 > 0 时覆盖 DamageBlock 默认值
            if (effect != null && effect.KnockbackForce > 0)
                damage.KnockbackForce = effect.KnockbackForce;
        }
    }
}
