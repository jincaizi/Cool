using Cool.Protocol;
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
    /// 状态映射见 `Apply`（D-N6：离散状态不插值；同一条视图上的位置仍然走插值）。
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

        /// <summary>最近一次快照映射出的控制器 `HitState` 值（观察面——测试与调试读数用）</summary>
        public int LastHitState { get; private set; } = (int)HitState.None;

        /// <summary>最近一次写出的混合值（观察面——测试与调试读数用，与 LastHitState 同惯例）</summary>
        public float LastBlend { get; private set; }
        /// <summary>最近一次写出的步频倍率（观察面）</summary>
        public float LastLocomotionSpeed { get; private set; } = 1f;
        /// <summary>最近一次写出的 Base 层状态（观察面）</summary>
        public int LastBaseState { get; private set; }

        private Animator? _animator;
        private int _lastKey = -1;   // -1 不在取值域内 ⇒ 首个快照必定应用一次

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

        /// <summary>
        /// `Attack` / `Hit` / `Defend` 三层权重归零。控制器资产里这三层的 `m_DefaultWeight`
        /// 是 **1**，本地玩家靠 `FSMManager` 构造时把它们归零；远程没有 FSMManager，不归零的话
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

            _animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 0f);
            _animator.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);
            _animator.SetLayerWeight(AnimHashes.DefendLayerIndex, 0f);
        }

        /// <summary>
        /// 解析被驱动的 Animator。为什么独立成 <c>internal</c> 方法而不是塞进 <c>Awake</c>：
        /// EditMode 下 <c>AddComponent</c> **不会**触发生命周期（除非组件挂 <c>[ExecuteAlways]</c>），
        /// 测试要直接调它才能拿到驱动（与 `NpcEntityAnimator.ResolveAnimator` 同惯例）。
        /// </summary>
        internal void ResolveAnimator()
        {
            _animator = GetComponent<Animator>();

            // 没有控制器的 Animator 既没有层也没有参数——对它写权重/参数只会刷
            // `Animator is not playing an AnimatorController` **错误**日志。视作"无可驱动"。
            if (_animator != null && _animator.runtimeAnimatorController == null)
                _animator = null;
        }

        /// <summary>
        /// 快照状态 → Animator。**每次快照都调**（状态不是边沿事件）：服务端对已在视野内的
        /// 观察者最多 4 tick（≈264ms）才保活重发一次，只在"翻位"时切动画会漏掉那一段——
        /// 这里靠 `_lastKey` 去重，仍是"读状态"而不是"读变化"。
        /// </summary>
        internal void Apply(byte actionState, bool dead)
        {
            int key = (dead ? 256 : 0) | actionState;
            if (key == _lastKey) return;
            _lastKey = key;

            int hitState = MapToHitState(actionState, dead);
            LastHitState = hitState;   // 映射结果——没有可驱动的 Animator 也照样记下来

            if (_animator == null) return;

            bool active = hitState != (int)HitState.None;

            // 0 表示"没有受击/死亡状态"——把 Hit 层权重收回，交还给 Base 层
            _animator.SetLayerWeight(AnimHashes.HitLayerIndex, active ? 1f : 0f);
            if (active) _animator.SetInteger(AnimHashes.HitState, hitState);
        }

        private int _lastLocomotionKey = int.MinValue;   // 去重：参数没变不重写

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

            if (_animator == null) return;   // 无控制器：只记观察值（与 Apply 同款守卫）

            _animator.SetFloat(AnimHashes.Blend, blend);
            _animator.SetFloat(AnimHashes.LocomotionSpeed, rate);
            _animator.SetInteger(AnimHashes.BaseState, baseState);
        }

        /// <summary>
        /// 让去重游标失效：下一次写入必定落到 Animator。
        /// **升档时必须调用**——角色预制体的 `m_KeepAnimatorStateOnDisable` 是 0（禁用即重置状态），
        /// 从 Proxy 档升回 Full 档时 Animator 已回到默认状态，去重游标会把"该重写"误判成"没变"。
        /// </summary>
        public void InvalidateDedup()
        {
            _lastKey = -1;                 // 受击层去重游标（Apply 用）
            _lastLocomotionKey = int.MinValue;
        }

        /// <summary>
        /// 线上 `ActionState`（`NpcAnimationState`，D-N3：玩家与 NPC 共用同一栏）→ 控制器
        /// `HitState` 整型参数。**两套枚举取值同名不同序，不能直接透传**：
        /// 线上 Hit=5/Down=7/GetUp=8/Death=6，控制器（= 本地 `HitState`）Hit=1/Down=5/GetUp=6/Death=7。
        /// 透传会让"死亡"落到控制器的"起身"上。
        ///
        /// `Dead`（D-N1/D-N2，每次快照都带）优先级高于 `ActionState`：玩家侧的 `ActionState`
        /// 服务端恒 0（D-P5-5 只做了怪物侧），远程真人的死亡**只能**从这一位读出来。
        /// </summary>
        private static int MapToHitState(byte actionState, bool dead)
        {
            if (dead) return (int)HitState.Death;

            switch ((NpcAnimationState)actionState)
            {
                case NpcAnimationState.Hit: return (int)HitState.Hit;
                case NpcAnimationState.Down: return (int)HitState.Down;
                case NpcAnimationState.GetUp: return (int)HitState.GetUp;
                case NpcAnimationState.Death: return (int)HitState.Death;
                // Idle/Walk/Run/Attack/Skill：都不是受击态，Hit 层不该抬起来
                default: return (int)HitState.None;
            }
        }
    }
}
