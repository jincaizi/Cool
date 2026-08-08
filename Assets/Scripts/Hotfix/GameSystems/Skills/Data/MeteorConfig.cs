using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    [CreateAssetMenu(fileName = "MeteorConfig", menuName = "Game/Skills/Meteor Config")]
    public class MeteorConfig : ScriptableObject
    {
        [Header("=== Meteor ===")]
        [Tooltip("陨石预制体（根物体需含 MeteorStrike 组件；子物体预警红圈在落地时关闭）")]
        [SerializeField] private GameObject _prefab;
        public GameObject Prefab => _prefab;

        [Tooltip("预警持续时间(秒)，之后落地爆炸")]
        [SerializeField] private float _warnDuration = 0.8f;
        public float WarnDuration => _warnDuration;

        [Tooltip("落地爆炸半径(米)，检测中心在落点 +1m 脚踝高度")]
        [SerializeField] private float _explosionRadius = 4f;
        public float ExplosionRadius => _explosionRadius;

        [Tooltip("伤害倍率 (1.0 = 继承技能全额伤害)")]
        [SerializeField] private float _damagePercent = 1f;
        public float DamagePercent => _damagePercent;

        [Tooltip("落地瞬间播放的一次性爆炸特效（自动销毁，不走池）")]
        [SerializeField] private GameObject _impactVFX;
        public GameObject ImpactVFX => _impactVFX;

        [Tooltip("命中击退等效果（KnockbackForce > 0 时覆盖技能默认）")]
        [SerializeField] private EffectBlock _effect;
        public EffectBlock Effect => _effect;
    }
}
