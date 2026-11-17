using UnityEngine;

namespace Hotfix.GameSystems.Skills.Data
{
    /// <summary>
    /// 「剑气回荡」技能资产。攻击半径/目标上限/剑气数等共享参数在 SyncCore
    /// `SwordQiEchoRules.Spec`；本资产只承载确认等待与表现接线。
    /// </summary>
    [CreateAssetMenu(fileName = "SwordQiEchoSkill", menuName = "Game/Skills/Sword Qi Echo")]
    public class SwordQiEchoSkillData : SkillData
    {
        [Tooltip("等待服务端确认的上限（秒）；资产可调")]
        [SerializeField] private float _confirmationTimeoutSeconds = 3f;

        public float ConfirmationTimeoutSeconds => _confirmationTimeoutSeconds;

        private void OnValidate()
        {
            _skillType = Definition.SkillType.Instant;
            _canBeInterruptedByMovement = false;
            _confirmationTimeoutSeconds = Mathf.Max(0.1f, _confirmationTimeoutSeconds);
        }
    }
}
