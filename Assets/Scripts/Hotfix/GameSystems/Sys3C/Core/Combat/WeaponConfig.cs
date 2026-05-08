using Hotfix.GameSystems.Skills.Data;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    [CreateAssetMenu(menuName = "Game/Weapon/Config")]
    public class WeaponConfig : ScriptableObject
    {
        [Header("Basic")]
        public string WeaponId;
        public WeaponType WeaponType;

        [Header("Attack")]
        public ShapeBlock AttackShape;
        public AttackEffectConfig[] Effects;
        public float AttackSpeed = 1f;

        [Header("Skills")]
        public string[] SkillIds;
    }
}
