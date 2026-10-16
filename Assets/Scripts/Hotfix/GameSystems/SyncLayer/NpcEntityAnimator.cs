using Cool.Protocol;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 怪物动画驱动（spec §5）。两个驱动源分工明确：
    ///   · **快照**（在 renderAt 处采样）→ Idle / Walk / Run / Hit / Death
    ///   · **施法广播**（排程到点）→ 每次挥砍播一次攻击，变体由时间戳推
    ///
    /// 为什么需要忙位（规则见 spec §5.2）：AnyState 转移每帧评估，交战中服务端**全程**
    /// 保持 ActionState=Attack，若不屏蔽 locomotion 写入，已经播上的挥砍会被当场打断。
    ///
    /// 前提：怪物预制体是纯美术（无脚本、无碰撞体）。本组件**不做** RemoteEntityAnimator
    /// 那套「禁用实例上其它 MonoBehaviour」——谁往怪物预制体里加脚本，谁自己处理。
    /// </summary>
    public sealed class NpcEntityAnimator : MonoBehaviour
    {
        public const int StateIdle = 0;
        public const int StateWalk = 1;
        public const int StateRun = 2;
        public const int StateAttacking = 3;
        public const int StateHit = 4;
        public const int StateDeath = 5;

        private static readonly int MonsterStateParam = Animator.StringToHash("MonsterState");
        private static readonly int AttackVariantParam = Animator.StringToHash("AttackVariant");
        private static readonly int AttackTrigger = Animator.StringToHash("Attack");

        private Animator? _animator;
        private int _lastWritten = int.MinValue;   // 去重：状态没变不重写参数

        private bool _busy;              // 已起手：确认前屏蔽一切写入，确认后屏蔽到时间轴离开攻击窗口
        /// <summary>已触发挥砍，等时间轴证实服务端进了攻击窗口。
        /// **已知前提**：这依赖「服务端在排队施法的同一次 BT 动作里就置了 `ActionState = Attack`」
        /// （`MonsterBtActions.Attack` 确实如此）。若哪天改成"先施法、稍后置状态"，忙位不会解除 ——
        /// `ApplyState` 里的 Hit/Death 分支是唯一兜底。别让它悄悄失效。</summary>
        private bool _awaitingConfirm;

        private void Awake()
        {
            ResolveAnimator();
        }

        /// <summary>
        /// 解析被驱动的 Animator。用 <c>GetComponentInChildren</c>：本组件由 registry 在实例化后
        /// AddComponent 到**预制体根**，而模型自带的 Animator 可能在子节点上（美术资源的常见结构）。
        /// 这里在 MonoBehaviour 内部取组件是合规的——registry 是普通类，项目规则禁止它在非
        /// MonoBehaviour 上下文里 GetComponent。
        ///
        /// 为什么独立成 <c>internal</c> 方法而不是塞进 <c>Awake</c>：EditMode 下 <c>AddComponent</c>
        /// **不会**触发生命周期（除非组件挂 <c>[ExecuteAlways]</c>），测试要直接调它才能拿到驱动；
        /// 任务 5 的端到端用例同样复用这一处。语义上 <c>internal</c> 就是"供程序集内使用"，
        /// 比反射稳。
        ///
        /// 前提：宿主预制体的 Animator 控制器需带 <c>MonsterState</c> / <c>AttackVariant</c> / <c>Attack</c>
        /// 三个参数（由 <c>NpcAnimator_*</c> 系列控制器提供）。缺任一参数——含空壳控制器与美术包自带
        /// 词表的控制器（如 <c>Slime</c> / <c>TurtleShell</c> 只有 AIState / Attack / AttackIndex / Hit /
        /// Death / Speed…，没有 MonsterState）——都会被安全降级为"只渲染、不动画"。
        /// </summary>
        internal void ResolveAnimator()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null) return;

            // 「不可驱动」统一在此降级为 null，Write / PlayAttack 只认 _animator == null（保护集中在
            // 一处，多处判断会漂移）：
            //   · 没有控制器的 Animator——写参数只会刷错误日志；
            //   · 空壳控制器（layerCount == 0）——SetInteger 会抛 NullReferenceException；
            //   · 控制器词表缺驱动要写的参数——`Parameter 'Hash …' does not exist` 每帧刷屏
            //     （玩家实测：Slime/TurtleShell 接进来后 171 条 MonsterState 警告；此类美术预制体被
            //     接进 NPC 视图属于预期情形——资源方还没配 NpcAnimator 词表）。
            if (_animator.runtimeAnimatorController == null
                || _animator.layerCount == 0
                || !HasDriverParameters(_animator))
                _animator = null;
        }

        /// <summary>控制器是否带驱动要写的三个参数（MonsterState / AttackVariant / Attack）。
        /// 读的是控制器资产数据，与宿主 GameObject 的激活状态无关。</summary>
        private static bool HasDriverParameters(Animator animator)
        {
            bool monsterState = false, attackVariant = false, attack = false;
            foreach (var p in animator.parameters)
            {
                int hash = p.nameHash;
                if (hash == MonsterStateParam) monsterState = true;
                else if (hash == AttackVariantParam) attackVariant = true;
                else if (hash == AttackTrigger) attack = true;
            }
            return monsterState && attackVariant && attack;
        }

        /// <summary>快照状态（由调用方在 renderAt 处采样后传入）。规则 2/3/4。</summary>
        public void ApplyState(byte actionState, bool dead)
        {
            int state = MapState(actionState, dead);
            if (state < 0) return;                       // Skill/Down/GetUp：忽略

            if (_awaitingConfirm)
            {
                if (state == StateAttacking || state == StateHit || state == StateDeath)
                    _awaitingConfirm = false;            // 服务端确认进入攻击窗口（或被打断）
                else
                    return;                              // 确认前一律不写：广播可能先于快照到达
            }

            if (_busy)
            {
                if (state == StateAttacking) return;     // 攻击窗口内保持 3，别让 locomotion 转移打断挥砍
                _busy = false;                           // 服务端离开攻击窗口 → 释放
            }

            Write(state);
        }

        /// <summary>服务端裁定的一次挥砍（排程到点后调用）。规则 1。</summary>
        public void PlayAttack(int variant)
        {
            _busy = true;
            _awaitingConfirm = true;
            Write(StateAttacking);                       // 先写值再打 Trigger：广播与快照到达顺序不保证
            if (_animator == null) return;
            _animator.SetInteger(AttackVariantParam, variant);
            _animator.SetTrigger(AttackTrigger);
        }

        private void Write(int state)
        {
            if (state == _lastWritten) return;
            _lastWritten = state;
            if (_animator == null) return;
            _animator.SetInteger(MonsterStateParam, state);
        }

        /// <summary>线上 ActionState → 控制器参数值。返回 -1 表示"不写、保持当前"。</summary>
        public static int MapState(byte actionState, bool dead)
        {
            if (dead) return StateDeath;                 // Dead 位优先（与真人侧同惯例）
            switch ((NpcAnimationState)actionState)
            {
                case NpcAnimationState.Idle:   return StateIdle;
                case NpcAnimationState.Walk:   return StateWalk;
                case NpcAnimationState.Run:    return StateRun;
                case NpcAnimationState.Attack: return StateAttacking;
                case NpcAnimationState.Hit:    return StateHit;
                case NpcAnimationState.Death:  return StateDeath;
                default:                       return -1;   // Skill / Down / GetUp
            }
        }
    }
}
