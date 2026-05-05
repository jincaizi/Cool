using System;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Combat
{
    [Serializable]
    public class AttackHitboxData
    {
        public DamageData DamageData;
        public float KnockbackForce;
    }
}
