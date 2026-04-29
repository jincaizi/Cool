using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Camera;

namespace Hotfix.GameSystems.Sys3C
{
    public class Sys3CEntry : MonoBehaviour
    {
        [Header("References")]
        public UnityEngine.CharacterController CharacterController;
        public Animator Animator;

        [Header("Settings")]
        public LayerMask GroundLayer;

        private Hotfix.GameSystems.Sys3C.Character.CharacterController _cc;
        private FSMManager _fsmManager;
        private SkillRegistry _skillRegistry;
        private HitManager _hitManager;
        private InputManager _inputManager;
        private ThirdPersonCameraController _camera;

        private void Start()
        {
            // 初始化组件
            _cc = new Hotfix.GameSystems.Sys3C.Character.CharacterController(transform, CharacterController, GroundLayer);

            // 创建 AnimationDriver
            var animationDriver = new Animation.AnimationDriver(Animator);

            _fsmManager = new FSMManager(_cc, Animator);
            _skillRegistry = new SkillRegistry();
            _hitManager = new HitManager(animationDriver);

            // 初始化输入
            _inputManager = GetComponent<InputManager>();
            if (_inputManager == null)
            {
                _inputManager = gameObject.AddComponent<InputManager>();
            }

            // 获取相机
            _camera = FindObjectOfType<ThirdPersonCameraController>();

            // 注册默认技能
            RegisterDefaultSkills();

            // 设置 StateMachineBehaviour 回调
            Animation.StateBehaviours.BaseStateBehaviour.SetCallback(animationDriver, HandleAnimationCallback);
            Animation.StateBehaviours.AttackStateBehaviour.SetCallback(animationDriver, HandleAnimationCallback);
            Animation.StateBehaviours.HitStateBehaviour.SetCallback(animationDriver, HandleHitAnimationCallback);

            Debug.Log("[Sys3CEntry] Initialized");
        }

        private void Update()
        {
            // 每帧更新输入管理器
            _inputManager.Update();

            // 处理输入
            HandleInput();

            // 读取输入获取移动命令
            Vector3 cameraForward = _camera != null ? _camera.transform.forward : Vector3.forward;
            var command = _inputManager.GetMoveCommand(cameraForward);

            // 更新各系统
            _cc.Update(command);
            _fsmManager.Update(Time.deltaTime);
            _skillRegistry.Update(Time.deltaTime);
        }

        private void HandleInput()
        {
            // 跳跃
            if (_inputManager.IsJumpPressed())
            {
                _cc.RequestJump();
            }

            // 攻击
            if (_inputManager.IsAttackPressed())
            {
                _fsmManager.RequestNormalAttack();
            }

            // 技能Q（普通攻击升级）
            if (_inputManager.IsSkill2Pressed())
            {
                TryUseSkill(SkillDefs.SkillQ);
            }

            // 技能R（大招）
            if (_inputManager.IsSkill3Pressed())
            {
                TryUseSkill(SkillDefs.SkillR);
            }
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

        private void HandleAnimationCallback(string stateName)
        {
            Debug.Log("[Sys3CEntry] AnimationCompleted: " + stateName);
        }

        private void HandleHitAnimationCallback(string stateName)
        {
            Debug.Log("[Sys3CEntry] HitAnimationCompleted: " + stateName);
            _hitManager.HandleHitCompleted(stateName);
        }
    }
}