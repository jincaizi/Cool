using System.Collections.Generic;
using Hotfix.GameSystems.Sys3C.Animation;
using Hotfix.GameSystems.Sys3C.Character;
using Hotfix.GameSystems.Sys3C.FSM;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 远程真人角色的动画驱动（N5b）。挂在实例化出来的角色上，是**视图侧唯一允许运行的组件**。
    ///
    /// 为什么是 MonoBehaviour：项目规则禁止在非 MonoBehaviour 上 `GetComponent`，而
    /// `RemoteEntityView` 是普通类——拿 Animator、停装备都只能在组件这一侧做。
    ///
    /// **表现仲裁器**：快照只喂"状态"（`Dead` / `IsDefending`），挥砍与受击是事件
    /// （`PlayAttack` / `PlayHit`），由 <see cref="Tick"/> 计时释放。优先级：
    /// Falling/Down &gt; Hit &gt; Attack &gt; GettingUp &gt; Defend &gt; Locomotion。
    ///
    /// 为什么快照不再直接写受击层：玩家快照的 `ActionState` 服务端恒 0（D-P5-5），
    /// 若快照每 tick 都按 `ActionState` 重写，会把事件驱动的受击表现当场清掉。
    /// 死亡也一样：`Dead` 是**状态**不是边沿事件（D-N1/D-N2），重复 `dead=true` 不得重置死亡计时。
    ///
    /// 死亡序列：Death（倒下）→ Down（躺卧，霸体）→ 复活快照 → GetUp → None。
    /// Death/Down 期间攻击/受击一律不接（与 `HitFSM.HasSuperArmor` 一致）；GetUp 可被打断。
    /// </summary>
    public sealed class RemoteEntityAnimator : MonoBehaviour
    {
        /// <summary>标称走/跑速度（m/s）——**必须与本地 CharacterController.MoveSpeed/SprintSpeed 一致**，
        /// 由 `RemoteEntityAnimatorLocomotionTests` 的守护用例钉住。</summary>
        public const float NominalWalkSpeed = 5f;
        public const float NominalSprintSpeed = 8f;
        /// <summary>走/跑档的分界（两个标称速度的中点）</summary>
        public const float WalkRunSplitSpeed = 6.5f;
        /// <summary>低于这个速度算静止（避免浮点噪声把 Idle 抖成走）</summary>
        public const float IdleSpeedThreshold = 0.1f;
        /// <summary>步频下限——速度趋 0 时比例为 0 会把动画冻成静止帧</summary>
        public const float MinLocomotionSpeed = 0.3f;
        /// <summary>步频上限（防御：反推速度已被钳过，这里只做兜底）</summary>
        public const float MaxLocomotionSpeed = 1.5f;

        /// <summary>倒地剪辑的兜底时长：超过它就从 Death 换到 Down（真实剪辑长度在 PlayMode 验收）。</summary>
        private const float DeathFallDuration = 0.7f;
        /// <summary>起身占位时长：到期把 Hit 层交还给 Base 层。</summary>
        private const float GetUpDuration = 0.5f;

        /// <summary>最近一次仲裁出的控制器 `HitState` 值（观察面——测试与调试读数用；
        /// 没有可驱动的 Animator 时也照样记录仲裁结果）。</summary>
        public int LastHitState { get; private set; } = (int)HitState.None;

        /// <summary>最近一次写出的混合值（观察面——测试与调试读数用，与 LastHitState 同惯例）</summary>
        public float LastBlend { get; private set; }
        /// <summary>最近一次写出的步频倍率（观察面）</summary>
        public float LastLocomotionSpeed { get; private set; } = 1f;
        /// <summary>最近一次写出的 Base 层状态（观察面）</summary>
        public int LastBaseState { get; private set; }

        private enum DeathPhase
        {
            None,
            Falling,
            Down,
            GettingUp
        }

        private Animator? _animator;

        // 能力检测（ResolveAnimator 时缓存）：美术预制体可能缺参数/层，缺了就安全降级不写，
        // 否则每次写入都会刷 `Parameter '…' does not exist` / `layer index out of range` 错误日志。
        private bool _hasAttackState;
        private bool _hasHitState;
        private bool _hasDefending;
        private int _layerCount;
        private readonly HashSet<int> _availableParams = new HashSet<int>();

        private bool _dead;
        private bool _defending;
        private float _hitRemaining;
        private bool _hitBlocked;
        private float _attackRemaining;
        private float _deathRemaining;
        private DeathPhase _deathPhase;
        private string? _attackTrigger;
        private bool _attackTriggerApplied;

        private int _lastLocomotionKey = int.MinValue;   // 去重：参数没变不重写

        private void Awake()
        {
            ResolveAnimator();

            // 远程实体是"别人权威状态的视图"——本地行为一条都不许跑。
            // 不停的话，角色预制体上那些**本地玩家的**装备会照着别人的角色跑一遍：
            //   · `Sys3CEntry.Start` 会 `FindObjectOfType<ThirdPersonCameraController>()` 再把
            //     相机 Target 抢到这个远程角色身上（本地镜头跟着别人跑，演示直接崩），
            //     还会挂 `InputManager`（本地输入驱赶远程角色）、给 HUD 换血条、
            //     向 `PhysicsRegistry` 注册成"本地攻击打得到"的实体；
            //   · `CharacterAttackHandler.Start` / `MeleeWeapon.Update` 会拿命中形状去打本地怪；
            //   · 根上的 `UnityEngine.CharacterController` 会把本地玩家挡在远程角色身上。
            // 停用发生在同一帧的 Start/Update 之前（Instantiate 后同步执行），所以上面这些一个都不会跑。
            foreach (var behaviour in GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != this)
                    behaviour.enabled = false;

            // 碰撞体同理：远程位置由快照插值写，不能让 Unity 物理在里面插一脚
            foreach (var c in GetComponentsInChildren<Collider>(true))
                c.enabled = false;

            // 归零 Attack / Hit / Defend 层权重（原因见 ResetLayerWeights 的注释）
            ResetLayerWeights();
        }

        private void OnEnable()
        {
            // `m_KeepAnimatorStateOnDisable=0`：禁用期间 Animator 已重置回资产默认状态
            // （含层权重/参数），重新启用时按仲裁器锁存的状态当场重放。
            Reapply();
        }

        private void OnDisable()
        {
            // 短暂表现不跨禁用存活；死亡阶段/防守状态是状态，保留给 Reapply。
            _hitRemaining = 0f;
            _attackRemaining = 0f;
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// `Attack` / `Hit` / `Defend` 三层权重归零。控制器资产（`Character3C.controller`）里
        /// 四层 `m_DefaultWeight` 依次是 Base=1 / Attack=1 / Hit=1 / **Defend=0**——本地玩家靠
        /// `FSMManager` 构造时把 Attack/Hit/Defend 归零；远程没有 FSMManager，不归零的话
        /// Attack 层会一直以权重 1 压着 Base 层（远程角色看起来是"攻击待机"而不是站着）。
        ///
        /// **升档时也必须调**：角色预制体的 `m_KeepAnimatorStateOnDisable` 是 0 —— Animator 在
        /// 禁用期间会重置回资产的默认状态（含层权重），所以 Proxy → Full **重新启用**既有
        /// FullRoot 时光靠 <see cref="InvalidateDedup"/> 不够（它管的是参数写入去重，不管权重）。
        /// 惰性实例化路径（新建 avatar）走 `Awake`，本来就归零。
        /// </summary>
        public void ResetLayerWeights()
        {
            if (_animator == null) return;

            WriteLayerWeight(AnimHashes.AttackLayerIndex, 0f);
            WriteLayerWeight(AnimHashes.HitLayerIndex, 0f);
            WriteLayerWeight(AnimHashes.DefendLayerIndex, 0f);
        }

        /// <summary>
        /// 解析被驱动的 Animator，并缓存"这个控制器能承受哪些写入"。
        /// 为什么独立成 <c>internal</c> 方法而不是塞进 <c>Awake</c>：
        /// EditMode 下 <c>AddComponent</c> **不会**触发生命周期（除非组件挂 <c>[ExecuteAlways]</c>），
        /// 测试要直接调它才能拿到驱动（与 `NpcEntityAnimator.ResolveAnimator` 同惯例）。
        /// </summary>
        internal void ResolveAnimator()
        {
            _animator = GetComponent<Animator>();

            // 没有控制器的 Animator 既没有层也没有参数——对它写权重/参数只会刷
            // `Animator is not playing an AnimatorController` **错误**日志。视作"无可驱动"。
            // 空壳控制器（layerCount == 0）同理：SetInteger 会抛 NullReferenceException。
            if (_animator != null
                && (_animator.runtimeAnimatorController == null || _animator.layerCount == 0))
                _animator = null;

            CacheCapabilities();
        }

        private void CacheCapabilities()
        {
            _availableParams.Clear();
            _hasAttackState = false;
            _hasHitState = false;
            _hasDefending = false;
            _layerCount = 0;

            if (_animator == null) return;

            // 远程实体是"别人权威状态的视图"——根位移绝不能由动画驱动。
            _animator.applyRootMotion = false;

            _layerCount = _animator.layerCount;
            foreach (var parameter in _animator.parameters)
            {
                int hash = parameter.nameHash;
                _availableParams.Add(hash);
                if (hash == AnimHashes.AttackState) _hasAttackState = true;
                else if (hash == AnimHashes.HitState) _hasHitState = true;
                else if (hash == AnimHashes.IsDefending) _hasDefending = true;
            }
        }

        /// <summary>
        /// 快照驱动的**状态**：只认 `Dead`。玩家快照的 `ActionState` 服务端恒 0（D-P5-5），
        /// 受击/攻击是事件（<see cref="PlayHit"/> / <see cref="PlayAttack"/>），快照不得改写它们。
        ///
        /// **每次快照都调**（状态不是边沿事件：观察者在视野内时最多 4 tick ≈264ms 才保活重发），
        /// 重复值在这里去重——尤其 `dead=true` 重复到达时绝不能把死亡计时重置回 Death。
        /// </summary>
        internal void ApplySnapshotState(bool dead)
        {
            if (dead == _dead) return;   // 重复快照绝不重置计时

            _dead = dead;

            // 生死翻转：进行中的短暂表现一律作废
            _hitRemaining = 0f;
            _attackRemaining = 0f;

            if (dead)
            {
                _deathPhase = DeathPhase.Falling;
                _deathRemaining = DeathFallDuration;
            }
            else
            {
                _deathPhase = DeathPhase.GettingUp;
                _deathRemaining = GetUpDuration;
            }

            RefreshLayers();
        }

        /// <summary>事件驱动的挥砍。Death/Down 霸体期间忽略；GettingUp 非霸体，会被打断。</summary>
        internal void PlayAttack(string trigger, float duration)
        {
            if (_dead || _deathPhase == DeathPhase.Down) return;
            if (_deathPhase == DeathPhase.GettingUp) _deathPhase = DeathPhase.None;

            _attackRemaining = Mathf.Max(0.1f, duration);
            _attackTrigger = trigger;
            _attackTriggerApplied = false;
            RefreshLayers();
        }

        /// <summary>
        /// 事件驱动的受击。Death/Down 霸体期间忽略；GettingUp 非霸体，会被打断。
        /// 受击**打断攻击**：本地 HitFSM 的受击不会恢复被打断的动作，远程保持一致；
        /// 因此这里清掉攻击计时，受击结束后自然回到格挡/行走（而不是继续挥砍）。
        /// </summary>
        internal void PlayHit(bool blocked, float duration)
        {
            if (_dead || _deathPhase == DeathPhase.Down) return;
            if (_deathPhase == DeathPhase.GettingUp) _deathPhase = DeathPhase.None;

            _hitRemaining = Mathf.Max(0.1f, duration);
            _hitBlocked = blocked;
            _attackRemaining = 0f;
            RefreshLayers();
        }

        /// <summary>快照驱动的**状态**：格挡姿态。每 tick 都带；不变时不重刷层。</summary>
        internal void SetDefending(bool defending)
        {
            if (_defending == defending) return;
            _defending = defending;
            RefreshLayers();
        }

        /// <summary>推进短暂表现计时；阶段切换时重算层权重。</summary>
        internal void Tick(float deltaTime)
        {
            bool hitWasActive = _hitRemaining > 0f;
            bool attackWasActive = _attackRemaining > 0f;

            if (_attackRemaining > 0f) _attackRemaining = Mathf.Max(0f, _attackRemaining - deltaTime);
            if (_hitRemaining > 0f) _hitRemaining = Mathf.Max(0f, _hitRemaining - deltaTime);
            if (_deathRemaining > 0f) _deathRemaining = Mathf.Max(0f, _deathRemaining - deltaTime);

            if (_deathPhase == DeathPhase.Falling && _deathRemaining <= 0f)
            {
                _deathPhase = DeathPhase.Down;
                RefreshLayers();
            }
            else if (_deathPhase == DeathPhase.GettingUp && _deathRemaining <= 0f)
            {
                _deathPhase = DeathPhase.None;
                RefreshLayers();
            }
            else if ((hitWasActive && _hitRemaining <= 0f) || (attackWasActive && _attackRemaining <= 0f))
            {
                RefreshLayers();   // 受击/攻击到期：交还给下一优先级
            }
        }

        /// <summary>
        /// 按优先级把当前仲裁结果写进控制器：Falling/Down &gt; Hit &gt; Attack &gt; GettingUp
        /// &gt; Defend &gt; Locomotion。幂等——随时可重放（升档/启用）。
        /// </summary>
        private void RefreshLayers()
        {
            if (_deathPhase == DeathPhase.Falling)
                WriteHitOnly(HitState.Death);
            else if (_deathPhase == DeathPhase.Down)
                WriteHitOnly(HitState.Down);
            else if (_hitRemaining > 0f)
                WriteHitOnly(_hitBlocked ? HitState.DefendHit : HitState.Hit);
            else if (_attackRemaining > 0f)
                WriteAttackOnly();
            else if (_deathPhase == DeathPhase.GettingUp)
                WriteHitOnly(HitState.GetUp);
            else
                WriteLocomotionLayers();
        }

        private void WriteHitOnly(HitState hitState)
        {
            LastHitState = (int)hitState;   // 仲裁结果——没有 Animator 也照样记下来
            if (_animator == null) return;

            WriteLayerWeight(AnimHashes.HitLayerIndex, 1f);
            WriteLayerWeight(AnimHashes.AttackLayerIndex, 0f);
            WriteLayerWeight(AnimHashes.DefendLayerIndex, 0f);
            if (_hasHitState) _animator.SetInteger(AnimHashes.HitState, (int)hitState);

            ResetAttackTrigger();
        }

        private void WriteAttackOnly()
        {
            LastHitState = (int)HitState.None;
            if (_animator == null) return;

            WriteLayerWeight(AnimHashes.HitLayerIndex, 0f);
            WriteLayerWeight(AnimHashes.DefendLayerIndex, 0f);
            WriteLayerWeight(AnimHashes.AttackLayerIndex, 1f);
            if (_hasAttackState) _animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Attacking);

            // 只在本次攻击开始时触发一次：后续刷新（受击结束/防守快照/重复快照）不得重复点。
            if (!_attackTriggerApplied
                && !string.IsNullOrEmpty(_attackTrigger)
                && _availableParams.Contains(Animator.StringToHash(_attackTrigger)))
            {
                _animator.SetTrigger(_attackTrigger);
                _attackTriggerApplied = true;
            }
        }

        private void WriteLocomotionLayers()
        {
            LastHitState = (int)HitState.None;
            if (_animator == null) return;

            WriteLayerWeight(AnimHashes.HitLayerIndex, 0f);
            WriteLayerWeight(AnimHashes.AttackLayerIndex, 0f);
            if (_hasAttackState) _animator.SetInteger(AnimHashes.AttackState, (int)AttackState.Idle);
            WriteLayerWeight(AnimHashes.DefendLayerIndex, _defending ? 1f : 0f);
            if (_hasDefending) _animator.SetBool(AnimHashes.IsDefending, _defending);

            ResetAttackTrigger();
        }

        private void ResetAttackTrigger()
        {
            if (string.IsNullOrEmpty(_attackTrigger)) return;
            if (_animator != null && _availableParams.Contains(Animator.StringToHash(_attackTrigger)))
                _animator.ResetTrigger(_attackTrigger);
            _attackTriggerApplied = false;
        }

        /// <summary>层能力检测：控制器层数不足时（美术预制体/占位控制器）跳过写入，避免错误日志。</summary>
        private void WriteLayerWeight(int layerIndex, float weight)
        {
            if (layerIndex < _layerCount) _animator!.SetLayerWeight(layerIndex, weight);
        }

        /// <summary>
        /// 升档/重新启用时按锁存状态重放。
        /// · 死亡中：Down 保持躺卧；还没倒完的重新播 Death（Animator 已被重置）；
        /// · 复活中：不重播 GetUp（半程起身动画重放会像"诈尸"），直接回 Locomotion；
        /// · 其余：按 Defend/Locomotion 重新写一遍。
        /// 短暂表现计时清零——禁用期间它们不应继续倒数。
        /// </summary>
        internal void Reapply()
        {
            _hitRemaining = 0f;
            _attackRemaining = 0f;

            if (_dead)
            {
                if (_deathPhase != DeathPhase.Down)
                {
                    _deathPhase = DeathPhase.Falling;
                    _deathRemaining = DeathFallDuration;   // 重新播倒下（升档时 Animator 已被重置）
                }
            }
            else if (_deathPhase == DeathPhase.GettingUp)
            {
                _deathPhase = DeathPhase.None;             // 升档/OnEnable 不重播 GetUp
                _deathRemaining = 0f;
            }

            RefreshLayers();
        }

        /// <summary>
        /// 行走动画（design §4 行走动画映射；速度由 registry 从渲染位移反推后传入）。
        ///
        /// 三个参数的分工照抄本地契约（`FSMManager.UpdateBlendParameter` / `CharacterController`）：
        ///   · `Blend` = Idle/Walk/Sprint 混合值（0 / 0.5 / 1）；
        ///   · `LocomotionSpeed` = **步频倍率** = 速度 ÷ 该档标称速度 —— 不按比例调就是脚步溜冰；
        ///   · `BaseState` = 0(Idle) / 7(Locomotion)。
        /// </summary>
        public void ApplyLocomotion(float speed)
        {
            float blend = speed < IdleSpeedThreshold ? 0f
                : speed < WalkRunSplitSpeed ? 0.5f
                : 1f;

            float nominal = blend >= 1f ? NominalSprintSpeed : NominalWalkSpeed;
            float rate = blend <= 0f
                ? 1f                                                   // 静止：写 1（写 0 会冻住 idle）
                : Mathf.Clamp(speed / nominal, MinLocomotionSpeed, MaxLocomotionSpeed);

            int baseState = blend <= 0f
                ? (int)BaseState.Idle
                : (int)BaseState.Locomotion;

            LastBlend = blend;
            LastLocomotionSpeed = rate;
            LastBaseState = baseState;

            int key = Mathf.RoundToInt(blend * 1000f) * 100_000 + Mathf.RoundToInt(rate * 1000f) * 10 + baseState;
            if (key == _lastLocomotionKey) return;
            _lastLocomotionKey = key;

            if (_animator == null) return;   // 无控制器：只记观察值（与仲裁器同款守卫）

            _animator.SetFloat(AnimHashes.Blend, blend);
            _animator.SetFloat(AnimHashes.LocomotionSpeed, rate);
            _animator.SetInteger(AnimHashes.BaseState, baseState);
        }

        /// <summary>
        /// 让去重游标失效：下一次写入必定落到 Animator。
        /// **升档时必须调用**——角色预制体的 `m_KeepAnimatorStateOnDisable` 是 0（禁用即重置状态），
        /// 从 Proxy 档升回 Full 档时 Animator 已回到默认状态，去重游标会把"该重写"误判成"没变"。
        /// 仲裁器的层写入本身不做去重；这里只影响 locomotion 参数写入。
        /// </summary>
        public void InvalidateDedup()
        {
            _lastLocomotionKey = int.MinValue;
        }
    }
}
