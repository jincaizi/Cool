using Cysharp.Threading.Tasks;
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
using Hotfix.GameSystems.Stamina;
using Hotfix.GameSystems.Nameplate;

namespace Hotfix.GameSystems.Sys3C
{
    public class Sys3CEntry : MonoBehaviour, IDamageable, IPlayerHpSource
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

        private Sys3CEntryStatsProvider _statsProvider;
        private float _hudHp = float.NaN;

        /// <summary>
        /// T12 乙-3：服务端判定本地玩家阵亡（联网态权威余血归零）。宿主（ClientNetworkHost）
        /// 据此弹出阵亡提示并返回选人界面——重登会重建服务端 PlayerState，即满血复活。
        /// </summary>
        public event System.Action OnServerDeath;

        public float MaxHp => _maxHP;
        public float CurrentHp => _currentHP;
        public float HpPercent => _maxHP > 0f ? _currentHP / _maxHP : 0f;
        public FSMManager Controller => _fsmManager;
        public bool IsDead => _isDead;

        /// <summary>HUD 数据源（甲-3）：Start 建立并绑到 PlayerHudPanel；测试与宿主只读</summary>
        public Sys3CEntryStatsProvider StatsProvider => _statsProvider;

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

        [Header("Stamina")]
        [SerializeField] private float _maxStamina = 100f;
        [SerializeField] private float _sprintStaminaPerSecond = 20f;
        [SerializeField] private float _defendStaminaPerSecond = 10f;
        [SerializeField] private float _staminaRegenPerSecond = 100f / 3f;
        [SerializeField, Tooltip("力竭后的回复延迟（秒）")]
        private float _staminaRegenDelay = 0.75f;
        [SerializeField, Tooltip("未力竭时的回复延迟（秒）")]
        private float _staminaShortRegenDelay = 0.2f;
        [SerializeField, Range(0f, 1f)] private float _staminaExhaustRecoverRatio = 0.2f;
        [SerializeField, Tooltip("每次成功格挡额外消耗的精力")]
        private float _blockStaminaCost = 15f;

        private StaminaSystem _stamina;
        private bool _defendStaminaLatch;
        private bool _staminaBound;

        public StaminaSystem Stamina => _stamina;

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
            _stamina = new StaminaSystem(new StaminaConfig
            {
                Max = _maxStamina,
                SprintDrainPerSecond = _sprintStaminaPerSecond,
                DefendDrainPerSecond = _defendStaminaPerSecond,
                RegenPerSecond = _staminaRegenPerSecond,
                RegenDelay = _staminaRegenDelay,
                ShortRegenDelay = _staminaShortRegenDelay,
                ExhaustRecoverRatio = _staminaExhaustRecoverRatio
            });

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

            _skillCoordinator = new SkillCoordinator(new SkillOwnerProxy(
                transform,
                _cc.StatsAdapter,
                _cc.StatusControllerAdapter,
                _cc.PhysicsSystemAdapter,
                _cc.Heal));
            _skillCoordinator.SetDashComponent(_dashComponent);
            _skillCoordinator.SetStamina(_stamina);
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

            // T12 甲-3：HUD 绑定（PlayerHudPanel.Bind 此前全项目零调用者 ⇒ 血条是死的）。
            // 阶段 2：改成"等就绪信号"而不是"假设面板已注册"。今天面板是同步加载的，
            // 这个 await 立刻返回；阶段 4 换成 Addressables 之后面板会真的异步加载 ——
            // 那时若还假设 Start 时就绪，HUD 会静默绑不上（血条死在预制体烘焙值上，
            // 即 T12 修掉的那个缺陷）。放在一个独立方法里是因为 Start 本身必须保持同步。
            _statsProvider = new Sys3CEntryStatsProvider(this);
            BindHudWhenReady().Forget();
            _hudHp = _currentHP;

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
            TryBindStaminaUi();

            // T12 甲-3：HP 脏检查（全项目唯一读数点）——服务端伤害/怪物伤害/复活回满
            // 都是写 _currentHP，这里统一推动 HUD 血条，不必在每个扣血点插桩。
            if (_statsProvider != null && !Mathf.Approximately(_hudHp, _currentHP))
            {
                _hudHp = _currentHP;
                _statsProvider.RaiseHpChanged();
            }

            _inputManager.Update();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (UnityEngine.Input.GetKeyDown(KeyCode.H))
                Heal(30f);
#endif
            _inputManager.SprintAllowed = _stamina.CanStartConsuming;
            // P6 ③：相机旋转接线——HandleRotationInput 此前全项目零调用者，相机角度永远停在初始值。
            // 键鼠/手柄的观察输入由 InputManager 的 adapter 提供（PC 形态无需触屏适配）。
            if (_camera != null)
                _camera.HandleRotationInput(_inputManager.GetCameraRotationInput());
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
            UpdateStamina(Time.deltaTime);
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
        }

        private void HandleInput()
        {
            // P6 ⑦：死亡期间不接受任何战斗输入（起手技能/普攻/跳跃）
            if (_isDead) return;

            // 防御处理（按住右键举盾，松开放下）
            if (_inputManager.IsDefendHeld())
            {
                if (!_defendStaminaLatch && _stamina.CanStartConsuming
                    && _fsmManager.CanDefend && _cc.TryEnterDefend())
                {
                    _fsmManager.EnterDefend();
                }
                else if (!_stamina.CanStartConsuming)
                {
                    _defendStaminaLatch = true;
                }
            }
            else
            {
                _defendStaminaLatch = false;
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

        private void TryBindStaminaUi()
        {
            if (_staminaBound) return;
            var display = EntityDisplayManager.Instance;
            if (display == null) return;

            display.BindStamina(transform, _stamina, () => !_isDead && isActiveAndEnabled);
            _staminaBound = true;
        }

        private void UpdateStamina(float deltaTime)
        {
            if (_isDead)
            {
                _stamina.Tick(deltaTime);
                return;
            }

            var sub = _skillCoordinator.CurrentSubState;
            bool sustained = sub == SkillSubState.Charging
                          || sub == SkillSubState.Channeling
                          || sub == SkillSubState.Spinning;

            float staminaDt = float.IsFinite(deltaTime)
                ? Mathf.Clamp(deltaTime, 0f, 0.1f)
                : 0f;

            if (!sustained && !_stamina.IsExhausted)
            {
                if (_cc.IsDefending && !_cc.StatusControllerAdapter.IsStunned)
                {
                    if (!_stamina.TryConsume(_defendStaminaPerSecond * staminaDt))
                        ForceExitDefendByExhaustion();
                }
                else if (!_cc.IsDefending
                         && !_cc.StatusControllerAdapter.IsStunned
                         && _cc.Data.BaseState == BaseState.Sprint
                         && _cc.Data.IsGrounded
                         && !_cc.LockMovement
                         && _fsmManager.Coordinator.ActiveLayer != LayerType.Hit)
                {
                    _stamina.TryConsume(_sprintStaminaPerSecond * staminaDt);
                }
            }

            _stamina.Tick(deltaTime);
        }

        private void ForceExitDefendByExhaustion()
        {
            _defendStaminaLatch = true;
            if (!_cc.IsDefending) return;
            _cc.TryExitDefend();
            _fsmManager.ExitDefend();
        }

        /// <summary>成功格挡的离散精力成本（与持盾每秒消耗叠加）。
        /// 以服务端 IsBlocked 为准；精力不足也扣，随后由 ForceExit 收盾。</summary>
        private void ConsumeBlockStamina()
        {
            if (_stamina == null) return;
            if (!_stamina.TryConsume(_blockStaminaCost))
                ForceExitDefendByExhaustion();
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
            // 死亡瞬间（死亡伤害同时打断了技能）不能在这里把移动锁清掉一帧——
            // 保留 _isDead 的锁，其余情况与旧行为一致。
            _cc.LockMovement = _isDead;
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
        /// P4 网络伤害入口（服务端权威——DamageResult 驱动）：服务端已裁决的伤害直扣
        /// （格挡减伤 D-P4-8 已服务端化：<paramref name="isBlocked"/> 为 true 时服务端已把
        /// 伤害乘过 <c>CombatRules.BlockDamageScale</c>，本地不再复算），受击表现/HitFSM/浮字事件
        /// 复用现有链路。怪物战斗（本地权威）不受影响。
        ///
        /// T12 甲-2：<paramref name="serverRemainingHp"/> 是协议里已有的权威余血
        /// （<c>HitEntry.TargetRemainingHp</c>）。传了就**以它为真相**、不在本地做减法——这消灭两个真实缺陷：
        /// ①本地 HP 归零后永久早退（服务端也不会再打死者 ⇒ 此后永远静默）；
        /// ②本地减法与服务端不一致时飘字/HUD 对不上（尤其致死那一击）。
        /// 不传（单机测试路径 / 无该字段的旧服务端）退回本地减法。
        ///
        /// 格挡分支（<paramref name="isBlocked"/>）：走**格挡受击**表现——复用单机格挡路径
        /// （<c>IDamageable.TakeDamage</c>）同款挂点 <c>HitFSM.EnterDefendHit</c> + Hit 层激活，
        /// 不新增状态/动画参数；浮字不发（与单机格挡一致：格挡成功本就跳过
        /// <c>PlayerTakeDamageEvent</c>）。格挡的 15 点精力成本在本分支扣除（以服务端 IsBlocked 为准）。
        ///
        /// <paramref name="sourceIsPlayer"/>：来源是玩家或 bot（服务端 <c>DamageResult.SourceKind</c>）。
        /// 仅非格挡、非致死分支据此发 <c>LocalPlayerHitByPlayerEvent</c>（屏幕闪屏）；怪物来源不闪。
        /// </summary>
        public void ApplyServerDamage(float damage, Vector3 hitDirection, float? serverRemainingHp = null,
            bool isBlocked = false, bool sourceIsPlayer = false)
        {
            if (serverRemainingHp.HasValue)
            {
                // 已死且服务端仍判死 → 没有新表现可做；否则以权威余血纠正本地血量
                //（服务端复活/重登后回血时，本地的毒化 0 血必须被纠回来，不能永久早退）。
                if (_currentHP <= 0f && serverRemainingHp.Value <= 0f) return;
                _currentHP = Mathf.Max(0f, serverRemainingHp.Value);
            }
            else
            {
                if (_currentHP <= 0) return;
                _currentHP = Mathf.Max(0f, _currentHP - damage);
            }

            if (isBlocked)
            {
                _fsmManager.HitFSM.EnterDefendHit(new HitData
                {
                    Damage = damage,
                    HitDirection = hitDirection
                });
                _fsmManager.Coordinator.SetActiveLayer(LayerType.Hit);

                ConsumeBlockStamina();

                if (_currentHP <= 0)
                {
                    _skillCoordinator.ClearInputBuffer();
                    _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
                }

                EvaluateLocalDeath();
                return;
            }

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
            if (sourceIsPlayer && _currentHP > 0)
            {
                EventBus.Emit(new LocalPlayerHitByPlayerEvent(gameObject.GetInstanceID()));
            }
            if (_currentHP <= 0)
            {
                _skillCoordinator.ClearInputBuffer();
                _skillCoordinator.InterruptCurrentSkill(Skills.Definition.InterruptionSource.Stun);
            }

            EvaluateLocalDeath();
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || _currentHP <= 0f) return; // 死亡不可治疗；负数/零忽略
            if (_currentHP >= _maxHP) return;              // 已满/超上限：治疗不应把血量拉低
            float before = _currentHP;
            _currentHP = Mathf.Min(_maxHP, _currentHP + amount);
            float actual = _currentHP - before;
            if (actual > 0f)
                EventBus.Emit(new PlayerHealedEvent(gameObject.GetInstanceID(), actual));
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

            // 格挡成功 → 扣精 + 播 DefendHit
            if (_cc.IsDefending && wasBlocked)
            {
                ConsumeBlockStamina();
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
        /// 死亡判定。单机走本地判死 + 3 秒本地复活；联网只**镜像**服务端权威结论
        /// （HP 来自 HitEntry.TargetRemainingHp——甲-2），复活交给"回选人重登"（乙-3）。
        ///
        /// D-P6-18/D-P6-15 的边界没有被越过：联网下仍然不做本地 HP 自算、不自行判死，
        /// 只有服务端说"余血 0"才进这个分支；本地 TickRespawn 在联网态被短路，
        /// 免得出现"客户端站起来了、服务端还当他死了"（施法被 caster-dead 全拒）的分叉。
        /// </summary>
        private void EvaluateLocalDeath()
        {
            if (_isDead) return;
            if (_currentHP > 0f) return;

            _cc.TryExitDefend();
            _fsmManager.ExitDefend();

            if (IsOnlineMode())
            {
                _isDead = true;
                _fsmManager.RequestDeath();
                _fsmManager.HitFSM.EnterDeath();
                UnityEngine.Debug.LogWarning("[Sys3CEntry] 服务端判定本地玩家阵亡（权威余血归零）");
                OnServerDeath?.Invoke();
                return;
            }

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

        /// <summary>
        /// 服务端说复活了（D-N4/D-5）。客户端**不自行判活**——只有收到这条才起身。
        /// 位置由客户端自己摆：服务端不拥有玩家位置（D-N5）。
        /// </summary>
        public void ApplyServerRespawn(float hp)
        {
            _currentHP = hp;
            _stamina.Refill();
            if (_inputManager != null) _inputManager.ResetSprintLatch();
            _cc.TryExitDefend();
            _fsmManager.ExitDefend();
            _defendStaminaLatch = false;
            _isDead = false;
            _fsmManager.HitFSM.EnterRespawn();
            // 死亡锁（StateCoordinator.HandleDeath 的 LockState + LockLayer(Base)）必须一起解除，
            // 否则人虽然站起来了却动不了——单机 TickRespawn 一直是这么配对的。
            _fsmManager.RequestResurrect();
            TeleportToSpawnPoint();
        }

        private void TickRespawn(float deltaTime)
        {
            // T12 乙-3：联网态的复活不归客户端（D-P6-15）——否则客户端会满血站起来，
            // 而服务端仍判死（施法全被 caster-dead 拒绝）。复活走服务端的 RespawnNotice
            //（ApplyServerRespawn）。
            if (IsOnlineMode()) return;
            if (!_isDead) return;

            _respawnTimer -= deltaTime;
            if (_respawnTimer > 0f) return;

            _isDead = false;
            _currentHP = _maxHP;
            _stamina.Refill();
            if (_inputManager != null) _inputManager.ResetSprintLatch();
            _cc.TryExitDefend();
            _fsmManager.ExitDefend();
            _defendStaminaLatch = false;
            // N4：死亡剪辑播完后 HitFSM 停在 **Down**（躺卧）而不是 Death；
            // 旧路径用的 Resurrect() 只认 Death，在这里会静默不生效 ⇒ 复活者永远躺着，
            // 且 Down 的霸体让之后每一次命中都被优先级拒掉。EnterRespawn 覆盖 Down 与 Death。
            _fsmManager.HitFSM.EnterRespawn();
            _fsmManager.RequestResurrect();
            TeleportToSpawnPoint();
        }

        /// <summary>
        /// 传送回出生点。单机 3 秒复活与服务端复活**共用这一处**落位逻辑。
        /// 传送须先禁用 CharacterController：直接写 transform.position 会被它的内部位置
        /// 在下一帧 Move() 时吃掉（实测：写 (5,0,5) 下一帧回到 (0,-0.05,0)，复活点丢失）。
        /// 禁/启后再写是 Unity 的标准传送做法，实测生效。
        /// </summary>
        private void TeleportToSpawnPoint()
        {
            CharacterController.enabled = false;
            transform.position = _spawnPoint;
            CharacterController.enabled = true;
        }

        private static bool IsOnlineMode()
            => NetworkModeService.Load() == NetworkMode.Online;

        /// <summary>
        /// 等 UI 就绪再绑 HUD。用显式判空而不是 ?.：UnityEngine.Object 的伪 null 不认 null 传播
        ///（UIManager 是 DontDestroyOnLoad 的，跨场景时可能已被销毁）。
        /// </summary>
        private async Cysharp.Threading.Tasks.UniTask BindHudWhenReady()
        {
            var uiManager = Hotfix.GameSystems.UI.UIManager.Instance;
            if (uiManager == null) return;

            await uiManager.WhenReady();

            var hud = uiManager.GetPanel<Hotfix.GameSystems.UI.PlayerHudPanel>();
            if (hud == null)
            {
                UnityEngine.Debug.LogError("Sys3CEntry: PlayerHudPanel 未注册，HUD 不会更新");
                return;
            }

            hud.Bind(_statsProvider);
            uiManager.ShowAlwaysAsync<Hotfix.GameSystems.UI.PlayerHudPanel>().Forget();   // 回选人时被收起过，进世界要再显示
        }

        private void OnDestroy()
        {
            EntityDisplayManager.Instance?.UnbindStamina(transform);
            PhysicsRegistry.Instance.Unregister(this);

            // T12 甲-3：本地角色没了（阵亡/断线回选人/换角色）→ 收起 HUD 并解绑。
            // **但「本地角色没了」不等于「这个实例被销毁」**：确认角色重建玩家时，新玩家已经
            // Start（并显示 HUD），旧玩家的帧末销毁才跑到这里 —— 无条件收起会把新玩家的 HUD
            // 一起收掉，且再没有别人显示它（2026-10-17 实测：HUD 永久消失）。
            // 判据因此改成状态：场上还有「已启动的本地角色」就把 HUD 交给它。
            if (_statsProvider == null) return;
            if (HasOtherStartedLocalEntry()) return;
            var uiManager = Hotfix.GameSystems.UI.UIManager.Instance;
            if (uiManager == null) return;
            var hud = uiManager.GetPanel<Hotfix.GameSystems.UI.PlayerHudPanel>();
            if (hud != null)
            {
                hud.Unbind();
                uiManager.HideAlwaysAsync<Hotfix.GameSystems.UI.PlayerHudPanel>().Forget();
            }
        }

        /// <summary>
        /// 场上是否还有**别的**已启动的本地角色。
        ///
        /// 判据必须是「已启动」（`_statsProvider != null`）：只有 Start 跑完的 entry 才绑过 HUD，
        /// 也才会在 Start 里重新显示 HUD。`enabled` 单独不够 —— 远端头像预制体自带的
        /// `Sys3CEntry` 是 `enabled=false`（bot 视图），而 `FindObjectsOfType` **会返回
        /// 「激活对象上的禁用组件」**（本项目已在 `MobileInputBinder.PickLocalEntry` 记录过这个
        /// 陷阱）。两者叠加：`enabled` 挡 bot 视图，`_statsProvider` 兜住「enabled 但 Start
        /// 还没跑完」的窗口。
        /// </summary>
        private bool HasOtherStartedLocalEntry()
        {
            foreach (var entry in FindObjectsOfType<Sys3CEntry>())
            {
                if (entry == this) continue;
                if (!entry.enabled) continue;
                if (entry._statsProvider == null) continue;
                return true;
            }
            return false;
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
        private readonly IEffectStats _stats;
        private readonly IStatusController _statusController;
        private readonly IPhysicsSystem _physicsSystem;
        private readonly System.Action<float> _heal;

        public Transform transform => _transform;
        public IEffectStats Stats => _stats;
        public IShieldSystem ShieldSystem => null;
        public IPhysicsSystem PhysicsSystem => _physicsSystem;
        public IStatusController StatusController => _statusController;

        public void Heal(float amount) => _heal?.Invoke(amount);

        public void ApplyDamageOverTime(float amount) { }

        public SkillOwnerProxy(
            Transform t,
            IEffectStats stats,
            IStatusController statusController,
            IPhysicsSystem physicsSystem,
            System.Action<float> heal)
        {
            _transform = t;
            _stats = stats;
            _statusController = statusController;
            _physicsSystem = physicsSystem;
            _heal = heal;
        }
    }
}
