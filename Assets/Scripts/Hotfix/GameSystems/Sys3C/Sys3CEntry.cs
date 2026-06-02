using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
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
                UnityEngine.Debug.LogError("[Sys3CEntry] CharacterController is null!");
                return;
            }
            if (Animator == null)
            {
                UnityEngine.Debug.LogError("[Sys3CEntry] Animator is null!");
                return;
            }

            _cc = new Hotfix.GameSystems.Sys3C.Character.CharacterController(
                transform, CharacterController, GroundLayer);

            _fsmManager = new FSMManager(_cc, Animator);
            _fsmManager.OnAttackAnimationCompleted += HandleAttackAnimationCompleted;

            _dashComponent = new SkillDashComponent(CharacterController, transform);

            _skillCoordinator = new SkillCoordinator(new SkillOwnerProxy(transform));
            _skillCoordinator.SetDashComponent(_dashComponent);
            _skillCoordinator.OnSkillActivated += HandleSkillActivated;
            _skillCoordinator.OnTargetHit += (target) =>
            {
                if (target is IDamageable damageable)
                    _attackHandler.SelectTarget(damageable);
            };

            // Register skills from Inspector-assigned SkillData assets
            int skillCount = 0;
            foreach (var skill in _characterSkills)
            {
                if (skill != null)
                {
                    _skillCoordinator.RegisterSkill(skill);
                    skillCount++;
                }
            }

            if (skillCount == 0)
            {
                UnityEngine.Debug.LogError("[Sys3CEntry] No SkillData assigned to _characterSkills. " +
                               "Create assets via Create > Game > Skills > Skill Data and assign them in the Inspector.");
            }
            else
            {
                UnityEngine.Debug.Log($"[Sys3CEntry] Skills registered: {skillCount}");
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
            ApplyPushVector();
            _fsmManager.Update(Time.deltaTime);
            _skillCoordinator.Update(Time.deltaTime);
            _dashComponent.Update();

            // Dynamic movement/rotation control: respect skill settings
            _cc.LockRotation = !_skillCoordinator.CanRotate();
            _cc.LockMovement = !_skillCoordinator.CanMove();

            // Detect when skill leaves looping state (Charging/Channeling → Execution)
            var curSubState = _skillCoordinator.CurrentSubState;
            bool wasLooping = _prevSkillSubState == Skills.Definition.SkillSubState.Charging ||
                              _prevSkillSubState == Skills.Definition.SkillSubState.Channeling;
            bool stillLooping = curSubState == Skills.Definition.SkillSubState.Charging ||
                                curSubState == Skills.Definition.SkillSubState.Channeling;
            if (wasLooping && !stillLooping && _lastSkillTrigger != null)
            {
                if (_prevSkillSubState == Skills.Definition.SkillSubState.Channeling)
                {
                    // Channeling end: looping anims have exit-time that delays
                    // CleanupSkillAnimation. All damage was dealt during ticks, so
                    // immediately clean up to avoid a 0.5-1s movement freeze.
                    CleanupSkillAnimation();
                }
                else
                {
                    // Charging end: use the same full cleanup path
                    CleanupSkillAnimation();
                }
            }
            _prevSkillSubState = curSubState;

            // Safety: cleanup animation if skill ended without firing animation callback
            // (e.g. executor cleaned up externally)
            if (_skillCoordinator.CurrentSkill == null && _lastSkillTrigger != null)
            {
                CleanupSkillAnimation();
            }

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
                    _skillCoordinator.HandleInput(input);
                }
            }

            if (_inputManager.IsSkill2Pressed())
            {
                int skillQId = GetSkillQId();
                if (skillQId > 0)
                {
                    var input = SkillInput.SkillToPosition(skillQId, transform.position + transform.forward * 5f);
                    _skillCoordinator.HandleInput(input);
                }
            }

            if (_inputManager.IsSkill3Pressed())
            {
                int skillRId = GetSkillRId();
                if (skillRId > 0)
                {
                    var input = SkillInput.SkillToPosition(skillRId, transform.position + transform.forward * 5f);
                    _skillCoordinator.HandleInput(input);
                }
            }

            if (_inputManager.IsSkill3Released())
            {
                var executor = _skillCoordinator.CurrentSkill;
                if (executor != null && executor.CurrentSubState == Skills.Definition.SkillSubState.Charging)
                {
                    executor.ReleaseCharge();
                }
            }
        }

        private string _lastSkillTrigger;
        private bool _cleanupInProgress;
        private Skills.Definition.SkillSubState _prevSkillSubState;

        // Push-away from monster collision
        private Vector3 _pushVector;
        private float _pushForce = 0.08f;

        private void HandleSkillActivated(SkillData skillData)
        {
            // 连段时先清除旧的触发器，确保新触发器能正确被 Animator 消费
            if (!string.IsNullOrEmpty(_lastSkillTrigger))
            {
                Animator.ResetTrigger(_lastSkillTrigger);
            }

            _lastSkillTrigger = skillData.AnimatorTrigger;

            _fsmManager.Coordinator.SetAttackLayerActive();
            _cc.LockRotation = true;
            _cc.LockMovement = true;

            if (!string.IsNullOrEmpty(_lastSkillTrigger))
            {
                Animator.SetTrigger(_lastSkillTrigger);
                UnityEngine.Debug.Log($"[Sys3CEntry] Skill activated: {skillData.SkillName} (id={skillData.SkillId}), trigger={_lastSkillTrigger}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[Sys3CEntry] Skill '{skillData.SkillName}' has no AnimatorTrigger set!");
            }

            Animator.SetLayerWeight(1, 1f);
            Animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Attacking);
        }

        private void HandleAttackAnimationCompleted()
        {
            var subState = _skillCoordinator.CurrentSubState;
            if (subState == Skills.Definition.SkillSubState.Charging ||
                subState == Skills.Definition.SkillSubState.Channeling)
                return;

            CleanupSkillAnimation();
        }

        private void CleanupSkillAnimation()
        {
            if (_cleanupInProgress) return;
            _cleanupInProgress = true;

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

            _skillCoordinator.CurrentSkill?.ForceComplete();

            _fsmManager.Coordinator.UnlockAndReturnToBase();
            _cleanupInProgress = false;
        }

        private int GetBasicAttackSkillId() => (int)SkillID.LightAttack;
        private int GetSkillQId() => (int)SkillID.SkillQ;
        private int GetSkillRId() => (int)SkillID.SkillR;

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
        {
            if (_currentHP <= 0) return;

            float damage = data != null ? data.BaseDamage : 10f;
            _currentHP -= damage;
            UnityEngine.Debug.Log($"[Player] Took {damage} damage, HP: {_currentHP}/{_maxHP}");

            _fsmManager.HandleDamage(sourceId: -1, damage: damage, hitDirection: hitDirection);

            if (_currentHP <= 0)
            {
                _currentHP = 0;
                UnityEngine.Debug.Log("[Player] Died!");
            }
        }

        private void OnDestroy()
        {
            PhysicsRegistry.Instance.Unregister(this);
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            var damageable = hit.collider.GetComponent<IDamageable>();
            if (damageable == null || hit.collider.gameObject == gameObject) return;

            Vector3 toPlayer = transform.position - hit.collider.transform.position;
            toPlayer.y = 0;
            if (toPlayer.sqrMagnitude < 0.01f)
                toPlayer = -hit.moveDirection;
            _pushVector += toPlayer.normalized * _pushForce;
        }

        private void ApplyPushVector()
        {
            if (_pushVector.sqrMagnitude < 0.0001f) return;

            CharacterController.Move(_pushVector);
            _pushVector = Vector3.zero;
        }
    }

    /// <summary>
    /// Minimal IEffectTarget proxy for SkillCoordinator.
    /// Full implementation deferred until combat system integration.
    /// </summary>
    internal sealed class SkillOwnerProxy : IEffectTarget
    {
        private readonly Transform _transform;
        public Transform transform => _transform;
        public IEffectStats Stats => null;
        public IShieldSystem ShieldSystem => null;
        public IPhysicsSystem PhysicsSystem => null;
        public IStatusController StatusController => null;
        public void Heal(float amount) { }

        public SkillOwnerProxy(Transform t) { _transform = t; }
    }
}
