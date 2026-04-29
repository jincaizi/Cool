using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Input;

namespace Hotfix.GameSystems.Sys3C
{
    public class Sys3CEntry : MonoBehaviour
    {
        [Header("References")]
        public UnityEngine.CharacterController CharacterController;
        public Animator Animator;

        [Header("Settings")]
        public LayerMask GroundLayer;

        private CharacterController _cc;
        private FSMManager _fsmManager;
        private SkillRegistry _skillRegistry;
        private HitManager _hitManager;
        private InputManager _inputManager;

        private void Start()
        {
            // 初始化组件
            _cc = new CharacterController(transform, CharacterController, GroundLayer);
            _fsmManager = new FSMManager(_cc, Animator);
            _skillRegistry = new SkillRegistry();
            _hitManager = new HitManager(Animator);

            // 注册默认技能
            RegisterDefaultSkills();

            // 设置 StateMachineBehaviour 回调
            Animation.StateBehaviours.BaseStateBehaviour.SetCallback(_fsmManager.OnAnimationCompleted);
            Animation.StateBehaviours.AttackStateBehaviour.SetCallback(_fsmManager.OnAnimationCompleted);
            Animation.StateBehaviours.HitStateBehaviour.SetCallback(_hitManager.OnHitCompleted);

            // 初始化输入
            _inputManager = GetComponent<InputManager>();
            if (_inputManager != null)
            {
                _inputManager.OnJumpPressed += () => _cc.RequestJump();
                _inputManager.OnAttackPressed += () => _fsmManager.RequestNormalAttack();
                _inputManager.OnSkillQPressed += () => TryUseSkill(SkillDefs.SkillQ);
                _inputManager.OnSkillRPressed += () => TryUseSkill(SkillDefs.SkillR);
            }

            Debug.Log("[Sys3CEntry] Initialized");
        }

        private void Update()
        {
            // 读取输入
            var command = _inputManager?.GetMoveCommand() ?? default;

            // 更新各系统
            _cc.Update(command);
            _fsmManager.Update(Time.deltaTime);
            _skillRegistry.Update(Time.deltaTime);
        }

        private void TryUseSkill(string skillId)
        {
            if (_skillRegistry.CanUse(skillId, _cc.IsGrounded))
            {
                _skillRegistry.Use(skillId);

                switch (skillId)
                {
                    case SkillDefs.SkillQ:
                        _fsmManager.RequestSkillQ();
                        break;
                    case SkillDefs.SkillR:
                        _fsmManager.RequestSkillR();
                        break;
                }
            }
        }

        private void RegisterDefaultSkills()
        {
            // 从 Resources 加载技能配置
            var configs = Resources.LoadAll<Skill.SkillConfig>("Skills");
            _skillRegistry.RegisterRange(configs);

            Debug.Log("[Sys3CEntry] Registered " + configs.Length + " skills");
        }
    }
}