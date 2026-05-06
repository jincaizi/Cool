using System;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    public enum StatusEffectType
    {
        None = 0,
        Poison = 1,
        Bleed = 2,
        Slow = 3,
        Stun = 4,
    }

    [Serializable]
    public class AttackEffectConfig
    {
        public DamageData Damage;
        public float KnockbackForce;
        public float LaunchForce;
        public float StunDuration;
        public StatusEffectType Status;
        public float StatusDuration;
        public float StatusValue;
    }
}
