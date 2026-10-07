using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using Hotfix.GameSystems.Sys3C.Skill;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Input;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Core;
using Hotfix.GameSystems.Sys3C.Core.Combat;
using Hotfix.GameSystems.Sys3C.Core.Events;
using Hotfix.GameSystems.Sys3C.Network;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Skills;
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

        [Header("Respawn")]
        [Tooltip("死亡后复活倒计时（秒）")]
        [SerializeField] private float _respawnDelay = 3f;
        [Tooltip("复活点（出生点）。零值 = 出生时按生成位置记录（D-P6-13：回初始生成点，不是死亡位置）")]
        [SerializeField] private Vector3 _spawnPoint;

        private float _respawnTimer;
        private bool _isDead;

        public float MaxHp => _maxHP;
        public float CurrentHp => _currentHP;
        public FSMManager Controller => _fsmManager;
        public bool IsDead => _isDead;
        public Vector3 SpawnPoint { get => _spawnPoint; set => _spawnPoint = value; }

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
        private Collider _bodyCollider;
        private IMovementPolicy _movementPolicy;

        /// <summary>本地角色控制器（网络宿主装配上报数据源用）</summary>
        public Hotfix.GameSystems.Sys3C.Character.CharacterController CoreController => _cc;

        /// <summary>技能协调器（P4 网络宿主装配战斗桥接入点）</summary>
        public SkillCoordinator SkillCoordinator => _skillCoordinator;

        /// <summary>移动策略注入面（网络宿主可替换为 NetMovementPolicy）</summary>
        public IMovementPolicy MovementPolicy { get => _movementPolicy; set => _movementPolicy = value; }

        private void Start()
        {
            _currentHP = _maxHP;

            // D-P6-13：复活回初始生成点。未显式配置时按出生位置记录
            // （PlayerSpawner 在出生点实例化，Start 时 transform.position 即出生点）。
            if (_spawnPoint == Vector3.zero)
                _spawnPoint = transform.position;

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

            _bodyCollider = GetComponent<Collider>();  // 玩家根节点：CharacterController 即 Collider，用于默认接触点

            _camera = FindObjectOfType<ThirdPersonCameraController>();
            if (_camera != null && _cc != null)
            {
                _camera.Target = transform;
                _camera.SnapToTarget();
            }

            _attackHandler = GetComponent<CharacterAttackHandler>();
            if (_attackHandler == null)
                _attackHandler = gameObject.AddComponent<CharacterAttackHandler>();

            // 宿主可在 Start 前预置 NetMovementPolicy（Awake 阶段）——未预置才落本地默认
            if (_movementPolicy == null)
                _movementPolicy = new LocalMovementPolicy(_cc);
        }

        private void Update()
        {
            TickRespawn(Time.deltaTime);

            _inputManager.Update();
            HandleInput();

            Vector3 cameraForward = _camera != null ? _camera.transform.forward : Vector3.forward;
            var command = _inputManager.GetMoveCommand(cameraForward);

            _movementPolicy.Update(command);

            // 蓄力中无移动输入时，角色朝向跟随相机（拖动鼠标调整抛投方向）
            if (_skillCoordinator.CurrentSubState == Skills.Definition.SkillSubState.Charging
                && _skillCoordinator.CanRotate()
                && !_inputManager.IsMoving())
            {
                Vector3 camFwd = _camera != null ? _camera.transform.forward : transform.forward;
                camFwd.y = 0f;
                if (camFwd.sqrMagnitude > 0.0001f)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation,
                        Quaternion.LookRotation(camFwd.normalized), 10f * Time.deltaTime);
                }
            }

            ApplyPushVector();
            _fsmManager.Update(Time.deltaTime);
            _skillCoordinator.Update(Time.deltaTime);
            _dashComponent.Update();

            // Dynamic movement/rotation control: respect skill settings
            _cc.LockRotation = !_skillCoordinator.CanRotate();
            // P6 ⑦：死亡也必须锁移动。StateCoordinator.CanMove 是全项目无消费者的死属性
            // （D-P6-6），不能指望它；这里显式并入 _isDead。
            _cc.LockMovement = _isDead || !_skillCoordinator.CanMove();
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
            // P6 ⑦：死亡期间不接受任何战斗输入（起手技能/普攻/跳跃）
            if (_isDead) return;

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

                if (_inputManager.IsSkillFireballPressed())
                {
                    int fireballId = GetSkillFireballId();
                    if (fireballId > 0)
                    {
                        var input = SkillInput.SkillToPosition(fireballId,
                            transform.position + transform.forward * 5f);
                        _skillCoordinator.HandleInput(input);
                    }
                }

                // 1键松开 → 蓄力抛投（火球）
                if (!_inputManager.IsSkillFireballHeld())
                {
                    _skillCoordinator.TryReleaseCharge(GetSkillFireballId());
                }

                // 2键按住蓄力，松开释放（蓄力炎爆）
                if (_inputManager.IsSkillChargeBlastPressed())
                {
                    int chargeId = GetSkillChargeBlastId();
                    if (chargeId > 0)
                    {
                        var input = SkillInput.SkillToPosition(chargeId,
                            transform.position + transform.forward * 5f);
                        _skillCoordinator.HandleInput(input);
                    }
                }
                if (!_inputManager.IsSkillChargeBlastHeld())
                {
                    _skillCoordinator.TryReleaseCharge(GetSkillChargeBlastId());
                }

                if (_inputManager.IsSkillMeteorPressed())
                {
                    int meteorId = (int)Skills.Definition.SkillID.Ultimate;
                    if (meteorId > 0)
                    {
                        var input = SkillInput.SkillToPosition(meteorId,
                            transform.position + transform.forward * 12f);
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

            // 先恢复攻击层权重与状态，最后再 SetTrigger——
            // 层权重为 0 时 SetTrigger 会被 Animator 静默丢弃（技能在清理回调栈内
            // 激活时层权重恰为 0，trigger 丢失会导致动画卡在上一状态循环）
            Animator.SetLayerWeight(1, 1f);
            Animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Attacking);

            if (!string.IsNullOrEmpty(_lastSkillTrigger))
            {
                Animator.SetTrigger(_lastSkillTrigger);
                UnityEngine.Debug.Log($"[Sys3CEntry] Skill activated: {skillData.SkillName} (id={skillData.SkillId}), trigger={_lastSkillTrigger}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[Sys3CEntry] Skill '{skillData.SkillName}' has no AnimatorTrigger set!");
            }
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
        private int GetSkillFireballId() => (int)SkillID.Fireball;
        private int GetSkillChargeBlastId() => (int)SkillID.ChargeBlast;

        /// <summary>
        /// P4 网络伤害入口（服务端权威——DamageResult 驱动）：绕过防御修正器直扣
        /// （PvP 格挡减伤服务端化是 P4 backlog，D-P4-8），受击表现/HitFSM/浮字事件复用
        /// 现有链路。怪物战斗（本地权威）不受影响。
        /// </summary>
        public void ApplyServerDamage(float damage, Vector3 hitDirection)
        {
            if (_currentHP <= 0) return;
            _currentHP = Mathf.Max(0f, _currentHP - damage);

            _fsmManager.HandleDamage(sourceId: -1, damage: damage, hitDirection: hitDirection,
                knockbackForce: 0f);
            if (_currentHP > 0)
            {
                EventBus.Emit(new PlayerTakeDamageEvent(
                    gameObject.GetInstanceID(),
                    transform.position + Vector3.up * 1.5f,
                    HitZonePart.Torso,
                    hitDirection,
                    Mathf.CeilToInt(damage),
                    Color.white));
            }
            if (_currentHP <= 0)
            {
                _skillCoordinator.ClearInputBuffer();
                _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
            }

            EvaluateLocalDeath();
        }

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection)
            => ((IDamageable)this).TakeDamage(data, hitDirection, HitContact.None);

        void IDamageable.TakeDamage(DamageBlock data, Vector3 hitDirection, HitContact contact)
        {
            if (_currentHP <= 0) return;

            float baseDamage = (data != null && data.CalculatedDamage > 0)
                ? data.CalculatedDamage
                : (data?.BaseDamage ?? 10f);

            // None → 方向重建（怪物 AttackShape 直调 2 参路径）
            HitContact resolved = contact.HasContact || _bodyCollider == null
                ? contact
                : HitContact.FromDirection(_bodyCollider, hitDirection);

            // 构建伤害上下文
            var ctx = new DamageContext
            {
                RawData = data,
                HitDirection = hitDirection,
                OverrideDamage = data?.CalculatedDamage ?? 0f,
                CurrentDamage = baseDamage,
                Contact = resolved
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
                    EvaluateLocalDeath();
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

            // 正常受击路由（非格挡时走 HitFSM，击退 × 部位倍率）
            if (!wasBlocked)
            {
                float kb = (data?.KnockbackForce ?? 0f) * HitZoneClassifier.KnockbackMultiplier(resolved.Part);
                _fsmManager.HandleDamage(sourceId: -1, damage: finalDamage, hitDirection: hitDirection,
                    knockbackForce: kb);

                // 玩家受击事件（死亡不发射——死亡判定在下方，用 _currentHP > 0 守卫）
                if (_currentHP > 0)
                {
                    EventBus.Emit(new PlayerTakeDamageEvent(
                        gameObject.GetInstanceID(),
                        resolved.HitPoint,
                        resolved.Part,
                        hitDirection,
                        Mathf.CeilToInt(finalDamage),
                        resolved.ElementColor
                    ));
                }
            }

            if (_currentHP <= 0)
            {
                _currentHP = 0;
                _skillCoordinator.ClearInputBuffer();
                _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
            }

            EvaluateLocalDeath();
        }

        /// <summary>
        /// P6 ⑦：单机本地死亡判定。仅单机模式生效——联网模式下玩家 HP 由服务端
        /// PlayerState 权威持有，客户端自作判断会与服务端打架（D-P6-18）。
        /// </summary>
        private void EvaluateLocalDeath()
        {
            if (IsOnlineMode()) return;      // 联网：交给服务端，见 D-P6-18
            if (_isDead) return;
            if (_currentHP > 0f) return;

            _isDead = true;
            _respawnTimer = _respawnDelay;

            // 走 HandleDeath（含 SetActiveLayer(Hit) + LockState），不走 EnterHit(Damage=9999)
            // ——后者是秒杀旁路，不执行 LockState，会留下"死亡但能动"的坏状态（D-P6-4）。
            _fsmManager.RequestDeath();

            // D-P6-22：**必须同时**让 HitFSM 进入 Death 态。
            // 否则受击剪辑播完时 HitFSM.Recover() 仍会执行（它只挡 HitState.Death，此刻不是），
            // 触发 OnHitComplete → HandleHitComplete → 把死亡锁清掉——死者被解锁。
            // 这与 D-P6-4 不冲突：被禁的是 EnterHit(Damage=9999) 那条秒杀旁路；
            // EnterDeath()（HitFSM.cs:244-251）是专门方法，会正确发出 DeathEvent。
            _fsmManager.HitFSM.EnterDeath();
        }

        private void TickRespawn(float deltaTime)
        {
            if (!_isDead) return;

            _respawnTimer -= deltaTime;
            if (_respawnTimer > 0f) return;

            _isDead = false;
            _currentHP = _maxHP;
            _fsmManager.HitFSM.Resurrect();
            _fsmManager.RequestResurrect();

            // 传送须先禁用 CharacterController：直接写 transform.position 会被它的内部位置
            // 在下一帧 Move() 时吃掉（实测：写 (5,0,5) 下一帧回到 (0,-0.05,0)，复活点丢失）。
            // 禁/启后再写是 Unity 的标准传送做法，实测生效。
            CharacterController.enabled = false;
            transform.position = _spawnPoint;
            CharacterController.enabled = true;
        }

        private static bool IsOnlineMode()
            => NetworkModeService.Load() == NetworkMode.Online;

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
