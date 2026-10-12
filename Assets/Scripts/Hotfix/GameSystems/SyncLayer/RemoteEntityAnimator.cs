using Cool.Protocol;
using Hotfix.GameSystems.Sys3C.Animation;
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
        /// <summary>最近一次快照映射出的控制器 `HitState` 值（观察面——测试与调试读数用）</summary>
        public int LastHitState { get; private set; } = (int)HitState.None;

        private Animator? _animator;
        private int _lastKey = -1;   // -1 不在取值域内 ⇒ 首个快照必定应用一次

        private void Awake()
        {
            _animator = GetComponent<Animator>();

            // 没有控制器的 Animator 既没有层也没有参数——对它写权重/参数只会刷
            // `Animator is not playing an AnimatorController` **错误**日志。视作"无可驱动"。
            if (_animator != null && _animator.runtimeAnimatorController == null)
                _animator = null;

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

            if (_animator == null) return;

            // 控制器资产里 Attack / Hit 两层的 `m_DefaultWeight` 是 **1**，本地玩家靠
            // `FSMManager` 构造时把它们归零；远程没有 FSMManager，不归零的话 Attack 层
            // 会一直以权重 1 压着 Base 层（远程角色看起来是"攻击待机"而不是站着）。
            _animator.SetLayerWeight(AnimHashes.AttackLayerIndex, 0f);
            _animator.SetLayerWeight(AnimHashes.HitLayerIndex, 0f);
            _animator.SetLayerWeight(AnimHashes.DefendLayerIndex, 0f);
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
