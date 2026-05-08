using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Runtime;

namespace Hotfix.GameSystems.Sys3C
{
    public class Sys3CEntry : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [SerializeField] private float _maxHP = 100f;
        private float _currentHP;

        bool IDamageable.IsAlive => _currentHP > 0;
        Transform IDamageable.Transform => transform;

        [Header("References")]
        public UnityEngine.CharacterController CharacterController;
        public Animator Animator;

        [Header("Settings")]
        public LayerMask GroundLayer;

        [Header("Skills")]
        [SerializeField] private SkillData[] _characterSkills;

        private Hotfix.GameSystems.Sys3C.Character.CharacterController _cc;
        private FSMManager _fsmManager;
        private SkillCoordinator _skillCoordinator;
        private SkillDashComponent _dashComponent;
        private InputManager _inputManager;
        private ThirdPersonCameraController _camera;
        private CharacterAttackHandler _attackHandler;

        private void Start()
        {
            _currentHP = _maxHP;
            PhysicsRegistry.Instance.Register(this, EntityType.Player);

            if (CharacterController == null)
            {
                Debug.LogError("[Sys3CEntry] CharacterController is null!");
                return;
            }
            if (Animator == null)
            {
                Debug.LogError("[Sys3CEntry] Animator is null!");
                return;
            }

            _cc = new Hotfix.GameSystems.Sys3C.Character.CharacterController(
                transform, CharacterController, GroundLayer);

            _fsmManager = new FSMManager(_cc, Animator);

            _dashComponent = new SkillDashComponent(CharacterController, transform);

            _skillCoordinator = new SkillCoordinator(null);
            _skillCoordinator.SetDashComponent(_dashComponent);
            _skillCoordinator.OnSkillActivated += HandleSkillActivated;

            foreach (var skill in _characterSkills)
            {
                if (skill != null)
                    _skillCoordinator.RegisterSkill(skill);
            }

            _inputManager = GetComponent<InputManager>();
            if (_inputManager == null)
                _inputManager = gameObject.AddComponent<InputManager>();

            _camera = FindObjectOfType<ThirdPersonCameraController>();
            if (_camera != null && _cc != null)
            {
                _camera.Target = transform;
                _camera.SnapToTarget();
            }

            _attackHandler = GetComponent<CharacterAttackHandler>();
            if (_attackHandler == null)
                _attackHandler = gameObject.AddComponent<CharacterAttackHandler>();
        }

        private void Update()
        {
            _inputManager.Update();
            HandleInput();

            Vector3 cameraForward = _camera != null ? _camera.transform.forward : Vector3.forward;
            var command = _inputManager.GetMoveCommand(cameraForward);

            _cc.Update(command);
            _fsmManager.Update(Time.deltaTime);
            _skillCoordinator.Update(Time.deltaTime);
            _dashComponent.Update();

            // Detect skill completion for animation cleanup
            bool isSkillActive = _skillCoordinator.IsSkillActive;
            if (_wasSkillActive && !isSkillActive)
            {
                CleanupSkillAnimation();
            }
            _wasSkillActive = isSkillActive;

            if (_camera != null)
                _camera.Update();
        }

        private void HandleInput()
        {
            if (_inputManager.IsJumpPressed())
            {
                _cc.RequestJump();
            }

            if (_inputManager.IsAttackPressed())
            {
                int attackId = GetBasicAttackSkillId();
                if (attackId > 0)
                {
                    var input = SkillInput.BasicAttack(attackId, transform.forward);
                    _skillCoordinator.HandleBasicAttackInput(input);
                    _fsmManager.Coordinator.SetAttackLayerActive();
                }
            }

            if (_inputManager.IsSkill2Pressed())
            {
                int skillQId = GetSkillQId();
                if (skillQId > 0)
                {
                    var input = SkillInput.SkillToPosition(skillQId, transform.position + transform.forward * 5f);
                    _skillCoordinator.HandleInput(input);
                    _fsmManager.Coordinator.SetAttackLayerActive();
                    _cc.LockRotation = true;
                    _cc.LockMovement = true;
                }
            }

            if (_inputManager.IsSkill3Pressed())
            {
                int skillRId = GetSkillRId();
                if (skillRId > 0)
                {
                    var input = SkillInput.SkillToPosition(skillRId, transform.position + transform.forward * 5f);
                    _skillCoordinator.HandleInput(input);
                    _fsmManager.Coordinator.SetAttackLayerActive();
                    _cc.LockRotation = true;
                }
            }

            if (_inputManager.IsSkill3Released())
            {
                if (_skillCoordinator.CurrentSkill != null &&
                    _skillCoordinator.CurrentSkill.CurrentSubState == Skills.Definition.SkillSubState.Charging)
                {
                    _skillCoordinator.CurrentSkill.ReleaseCharge();
                }
            }
        }

        private string _lastSkillTrigger;
        private bool _wasSkillActive;

        private void HandleSkillActivated(SkillData skillData)
        {
            _lastSkillTrigger = skillData.AnimatorTrigger;

            if (!string.IsNullOrEmpty(_lastSkillTrigger))
            {
                Animator.SetTrigger(_lastSkillTrigger);
            }

            Animator.SetLayerWeight(1, 1f);
            Animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Attacking);
        }

        private void CleanupSkillAnimation()
        {
            if (!string.IsNullOrEmpty(_lastSkillTrigger))
            {
                Animator.ResetTrigger(_lastSkillTrigger);
                _lastSkillTrigger = null;
            }

            Animator.ResetTrigger(AnimHashes.Attack);
            Animator.SetLayerWeight(1, 0f);
            Animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Idle);
            _cc.LockMovement = false;
            _cc.LockRotation = false;
            _fsmManager.Coordinator.UnlockAndReturnToBase();
        }

        private int GetBasicAttackSkillId()
        {
            foreach (var skill in _characterSkills)
            {
                if (skill != null && skill.SkillType == Skills.Definition.SkillType.BasicAttack)
                    return skill.SkillId;
            }
            return 0;
        }

        private int GetSkillQId()
        {
            foreach (var skill in _characterSkills)
            {
                if (skill != null && skill.SkillType == Skills.Definition.SkillType.Special)
                    return skill.SkillId;
            }
            return 0;
        }

        private int GetSkillRId()
        {
            bool foundFirst = false;
            foreach (var skill in _characterSkills)
            {
                if (skill != null && skill.SkillType == Skills.Definition.SkillType.Special)
                {
                    if (!foundFirst) { foundFirst = true; continue; }
                    return skill.SkillId;
                }
            }
            return 0;
        }

        void IDamageable.TakeDamage(DamageData data, Vector3 hitDirection)
        {
            if (_currentHP <= 0) return;

            float damage = data != null ? data.BaseDamage : 10f;
            _currentHP -= damage;
            Debug.Log($"[Player] Took {damage} damage, HP: {_currentHP}/{_maxHP}");

            _fsmManager.HandleDamage(sourceId: -1, damage: damage, hitDirection: hitDirection);

            if (_currentHP <= 0)
            {
                _currentHP = 0;
                Debug.Log("[Player] Died!");
            }
        }

        private void OnDestroy()
        {
            PhysicsRegistry.Instance.Unregister(this);
        }
    }
}
