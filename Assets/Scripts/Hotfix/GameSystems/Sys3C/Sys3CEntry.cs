using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.Skills.Runtime;
using Hotfix.GameSystems.Monster;

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
        private bool _canFireHeavy;
        private DefendModifier _defendModifier;
        private DefendConfig _defendConfig = DefendConfig.Default;

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

            _defendModifier = new DefendModifier(_defendConfig, transform, () => _cc.IsDefending);

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
            _skillCoordinator.OnLightAttackCompleted += () =>
            {
                if (_inputManager.IsAttackHeld())
                {
                    _canFireHeavy = true;
                }
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
            _cc.MoveSpeedScale = _skillCoordinator.GetMoveSpeedMultiplier();

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
                // Charging → Execution: do NOT cleanup here.
                // Damage is dealt during Execution phase via hitbox frames.
                // Animation callback (AttackStateBehaviour) handles cleanup at 95%.
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
            // 防御处理（按住右键举盾，松开放下）
            if (_inputManager.IsDefendHeld())
            {
                if (_fsmManager.CanDefend && _cc.TryEnterDefend())
                {
                    _fsmManager.EnterDefend();
                }
            }
            else
            {
                if (_cc.IsDefending)
                {
                    _cc.TryExitDefend();
                    _fsmManager.ExitDefend();
                }
            }

            if (_inputManager.IsJumpPressed())
            {
                _cc.RequestJump();
            }

            // 防御期间禁止攻击和技能
            if (!_cc.IsDefending)
            {
                // Press → light attack. Complete + held → canFireHeavy. Release → fire heavy.
                if (_inputManager.IsAttackJustPressed())
                {
                    _skillCoordinator.HandleLightAttack();
                    _canFireHeavy = false;
                }

                float attackDuration = _inputManager.GetAttackReleaseDuration();
                if (attackDuration >= 0f && _canFireHeavy)
                {
                    _skillCoordinator.HandleHeavyFire();
                    _canFireHeavy = false;
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
            // 持续型技能（蓄力/引导/旋转）的动画完成不代表技能结束，忽略回调
            if (_skillCoordinator.IsAnimationCompletionIgnored())
                return;

            // 立即设 AttackState=0 让 Animator 切回 Idle，不等 Cleanup 走完
            Animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Idle);
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

            float baseDamage = (data != null && data.CalculatedDamage > 0)
                ? data.CalculatedDamage
                : (data?.BaseDamage ?? 10f);

            // 构建伤害上下文
            var ctx = new DamageContext
            {
                RawData = data,
                HitDirection = hitDirection,
                OverrideDamage = data?.CalculatedDamage ?? 0f,
                CurrentDamage = baseDamage
            };

            // 防御修正器介入
            var result = _defendModifier?.Modify(ref ctx) ?? new DamageResult
            {
                FinalDamage = baseDamage,
                ShouldKnockback = true,
                ReactLevel = HitReactLevel.Flinch
            };

            float finalDamage = result.FinalDamage;
            bool wasBlocked = result.ReactLevel == HitReactLevel.None;

            // 防御未被触发（背面受击或不在防御状态）→ 退出防御
            if (_cc.IsDefending && !wasBlocked)
            {
                _cc.TryExitDefend();
                _fsmManager.ExitDefend();
            }

            // 格挡成功 → 扣耐久 + 播 DefendHit
            if (_cc.IsDefending && wasBlocked)
            {
                float absorbed = baseDamage - finalDamage;
                bool broken = _cc.AbsorbDamage(absorbed);
                if (broken)
                {
                    _cc.OnShieldBreak();
                    _fsmManager.HandleShieldBreak(new HitData
                    {
                        Damage = finalDamage,
                        HitDirection = hitDirection
                    });
                    // 扣血（盾破时穿透伤害仍要扣除）
                    _currentHP -= finalDamage;
                    if (_currentHP <= 0)
                    {
                        _currentHP = 0;
                        _skillCoordinator.ClearInputBuffer();
                        _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
                    }
                    return;
                }

                _fsmManager.HitFSM.EnterDefendHit(new HitData
                {
                    Damage = finalDamage,
                    HitDirection = hitDirection
                });
                _fsmManager.Coordinator.SetActiveLayer(LayerType.Hit);
            }

            // 扣血
            _currentHP -= finalDamage;

            // 正常受击路由（非格挡时走 HitFSM）
            if (!wasBlocked)
            {
                _fsmManager.HandleDamage(sourceId: -1, damage: finalDamage, hitDirection: hitDirection,
                    knockbackForce: data?.KnockbackForce ?? 0);
            }

            if (_currentHP <= 0)
            {
                _currentHP = 0;
                _skillCoordinator.ClearInputBuffer();
                _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
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
