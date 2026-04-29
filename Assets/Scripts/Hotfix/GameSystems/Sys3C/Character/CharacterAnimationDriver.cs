using System;
using System.Linq;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>
    /// 角色动画驱动器 — 响应式模型
    /// 观察 CharacterData 状态变化，自动驱动 Animator 状态机
    /// 规则：CC 只改数据，Driver 只读数据并响应变化
    ///
    /// 驱动策略：
    /// - 统一使用 State 参数 (Int) 驱动 Base Layer 状态转换
    /// - 使用 AttackPhase 参数 (Int) + Attack Trigger 控制 Attack Layer 连击
    /// </summary>
    public class CharacterAnimationDriver
    {
        private readonly Animator _animator;

        // === Cached parameter hashes ===
        private static readonly int HASH_State = Animator.StringToHash("State");
        private static readonly int HASH_AttackPhase = Animator.StringToHash("AttackPhase");
        private static readonly int HASH_Attack = Animator.StringToHash("Attack");

        // === State values (必须与 Character3C.controller 中 State 参数值完全一致) ===
        // Idle=0, BattleIdle=1, Move=2, Run=3, JumpStart=4, JumpAir=5, JumpEnd=6, Death=7
        private const int STATE_IDLE = 0;
        private const int STATE_BATTLE_IDLE = 1;
        private const int STATE_MOVE = 2;
        private const int STATE_RUN = 3;
        private const int STATE_JUMP_START = 4;
        private const int STATE_JUMP_AIR = 5;
        private const int STATE_JUMP_END = 6;
        private const int STATE_DEATH = 7;

        // === Phase tracking ===
        private int _currentComboCount;
        private const int MAX_COMBO = 4;
        private int _lastNormalAttackIndex;

        // === 状态变化追踪 ===
        private CharacterState _prevState;
        private JumpPhase _prevJumpPhase;

        // 调试：记录最近一次 SetInteger 调用的参数和帧号
        private int _lastSetStateFrame;
        private int _lastSetStateValue;

        public CharacterAnimationDriver(Animator animator)
        {
            _animator = animator ?? throw new ArgumentNullException(nameof(animator));
            _prevState = CharacterState.Idle;
            _prevJumpPhase = JumpPhase.None;

            // 初始化 Animator 参数
            _animator.SetInteger(HASH_State, STATE_IDLE);
            _animator.SetInteger(HASH_AttackPhase, 0);

            // 打印 Attack Layer 信息
            if (_animator.layerCount >= 2)
            {
                var layer1 = _animator.GetLayerIndex("Attack Layer");
                if (layer1 >= 0)
                {
                    float weight = _animator.GetLayerWeight(layer1);
                    Debug.Log("[Driver] Attack Layer found: index=" + layer1 + ", weight=" + weight);

                    // 检查 AnyState 转换
                    AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(layer1);
                    Debug.Log("[Driver] Attack Layer current state: " + info.fullPathHash);
                }
                else
                {
                    Debug.LogError("[Driver] Attack Layer not found! Available layers:");
                    for (int i = 0; i < _animator.layerCount; i++)
                    {
                        Debug.Log("[Driver]   Layer " + i + ": " + _animator.GetLayerName(i));
                    }
                }
            }
            else
            {
                Debug.LogError("[Driver] Animator only has " + _animator.layerCount + " layers, need at least 2!");
            }
        }

        /// <summary>
        /// 每帧更新 — 检测 CharacterData 状态变化，响应式驱动动画
        /// </summary>
        public void Update(CharacterData data)
        {
            if (_animator == null)
            {
                Debug.LogError("[Driver] _animator is null!");
                return;
            }

            if (!_animator.isActiveAndEnabled)
            {
                Debug.LogWarning("[Driver] Animator not active/enabled");
                return;
            }

            // 检查 Animator 当前状态
            AnimatorStateInfo baseInfo = _animator.GetCurrentAnimatorStateInfo(0);
            bool isInTransition = _animator.IsInTransition(0);
            bool inJumpEndState = baseInfo.shortNameHash == Animator.StringToHash("JumpEnd");

            // 每 60 帧打印一次完整状态
            if (Time.frameCount % 60 == 0)
            {
                AnimationClip clip = baseInfo.shortNameHash != 0
                    ? _animator.GetCurrentAnimatorClipInfo(0)?.FirstOrDefault().clip
                    : null;
                string clipName = clip?.name ?? "null";

                int animatorState = _animator.GetInteger(HASH_State);
                Debug.Log("[Driver] === Frame " + Time.frameCount
                    + " | BaseStateHash=" + baseInfo.shortNameHash
                    + ", clip=" + clipName
                    + ", normalizedTime=" + baseInfo.normalizedTime.ToString("F2")
                    + " | AnimatorParam State=" + animatorState
                    + " (last SetInteger at frame " + _lastSetStateFrame + " = " + _lastSetStateValue + ")"
                    + " | data.State=" + data.State + ", JumpPhase=" + data.JumpPhase
                    + ", layerCount=" + _animator.layerCount);
            }

            // 1. 死亡优先级最高
            if (data.State == CharacterState.Death && _prevState != CharacterState.Death)
            {
                OnDeathEntered();
                _prevState = data.State;
                _prevJumpPhase = data.JumpPhase;
                return;
            }

            // 2. 跳跃阶段变化 → 驱动跳跃动画
            if (data.JumpPhase != _prevJumpPhase)
            {
                Debug.Log("[Driver] JumpPhase changed: " + _prevJumpPhase + " -> " + data.JumpPhase
                    + ", currentAnimState=" + baseInfo.shortNameHash);
                OnJumpPhaseChanged(_prevJumpPhase, data.JumpPhase);
                _prevJumpPhase = data.JumpPhase;
                _prevState = data.State;
                return;
            }

            // 3. 非跳跃期间，普通状态变化 → 驱动移动动画
            if (data.JumpPhase == JumpPhase.None && data.State != _prevState)
            {
                Debug.Log("[Driver] State changed: " + _prevState + " -> " + data.State);
                OnStateChanged(_prevState, data.State);
            }

            _prevState = data.State;

            // 4. 强制同步：只在非跳跃状态下同步移动状态
            // JumpEnd 状态的转换由动画 ExitTime 控制，避免状态冲突
            ForceSync(data);
        }

        /// <summary>
        /// 强制同步 Animator 参数
        /// </summary>
        private void ForceSync(CharacterData data)
        {
            if (_animator == null) return;

            // JumpEnd 期间不强制同步，让 ExitTime 转换完成
            if (data.JumpPhase == JumpPhase.End) return;

            int targetState;
            switch (data.JumpPhase)
            {
                case JumpPhase.Start:
                    targetState = STATE_JUMP_START;
                    break;
                case JumpPhase.Air:
                    targetState = STATE_JUMP_AIR;
                    break;
                case JumpPhase.End:
                    targetState = STATE_JUMP_END;
                    break;
                default:
                    targetState = CharacterStateToInt(data.State);
                    break;
            }

            int currentState = _animator.GetInteger(HASH_State);
            if (currentState != targetState || Time.frameCount % 60 == 0)
            {
                _animator.SetInteger(HASH_State, targetState);
                _lastSetStateFrame = Time.frameCount;
                _lastSetStateValue = targetState;
                Debug.Log("[Driver] ForceSync: SetInteger(" + targetState + ") at frame " + Time.frameCount
                    + " (prev animator State=" + currentState + ", data.State=" + data.State + ")");
            }
        }

        /// <summary>
        /// CharacterState 转换为 Animator State 参数值
        /// </summary>
        private int CharacterStateToInt(CharacterState state)
        {
            switch (state)
            {
                case CharacterState.Idle: return STATE_IDLE;
                case CharacterState.BattleIdle: return STATE_BATTLE_IDLE;
                case CharacterState.Move: return STATE_MOVE;
                case CharacterState.Run: return STATE_RUN;
                case CharacterState.Death: return STATE_DEATH;
                default: return STATE_IDLE;
            }
        }

        // ============================================
        // 响应式状态变化处理
        // ============================================

        private void OnJumpPhaseChanged(JumpPhase from, JumpPhase to)
        {
            int targetState;
            switch (to)
            {
                case JumpPhase.Start:
                    targetState = STATE_JUMP_START;
                    break;
                case JumpPhase.Air:
                    targetState = STATE_JUMP_AIR;
                    break;
                case JumpPhase.End:
                    targetState = STATE_JUMP_END;
                    break;
                default:
                    targetState = STATE_IDLE;
                    break;
            }
            Debug.Log("[Driver] OnJumpPhaseChanged: " + from + " -> " + to + ", SetInteger(State, " + targetState + ")");
            _animator.SetInteger(HASH_State, targetState);
            _lastSetStateFrame = Time.frameCount;
            _lastSetStateValue = targetState;
        }

        private void OnStateChanged(CharacterState from, CharacterState to)
        {
            int targetState;
            switch (to)
            {
                case CharacterState.Idle:
                    targetState = STATE_IDLE;
                    break;
                case CharacterState.BattleIdle:
                    targetState = STATE_BATTLE_IDLE;
                    break;
                case CharacterState.Move:
                    targetState = STATE_MOVE;
                    break;
                case CharacterState.Run:
                    targetState = STATE_RUN;
                    break;
                default:
                    targetState = STATE_IDLE;
                    break;
            }
            Debug.Log("[Driver] OnStateChanged: " + from + " -> " + to + ", SetInteger(State, " + targetState + ")");
            _animator.SetInteger(HASH_State, targetState);
            _lastSetStateFrame = Time.frameCount;
            _lastSetStateValue = targetState;
        }

        private void OnDeathEntered()
        {
            _animator.SetInteger(HASH_State, STATE_DEATH);
        }

        // ============================================
        // 攻击/技能 — 由输入直接驱动，不经 CharacterData
        // ============================================

        /// <summary>
        /// 普通攻击（Attack1/Attack2 交替）
        /// 通过 SetTrigger + SetInteger 触发 AnyState → AttackX 转换
        /// </summary>
        public void OnNormalAttack()
        {
            if (_animator == null)
            {
                Debug.LogError("[Driver] OnNormalAttack: _animator is null!");
                return;
            }
            _lastNormalAttackIndex = _lastNormalAttackIndex == 1 ? 2 : 1;
            Debug.Log("[Driver] OnNormalAttack: AttackPhase=" + _lastNormalAttackIndex);
            _animator.SetInteger(HASH_AttackPhase, _lastNormalAttackIndex);
            _animator.SetTrigger(HASH_Attack);
            Debug.Log("[Driver] SetAttack done, AttackPhase=" + _animator.GetInteger(HASH_AttackPhase)
                + ", layerCount=" + _animator.layerCount + ", baseState=" + _animator.GetCurrentAnimatorStateInfo(0).shortNameHash);
        }

        /// <summary>
        /// 2技能（Attack3）
        /// </summary>
        public void OnSkill2()
        {
            if (_animator == null)
            {
                Debug.LogError("[Driver] OnSkill2: _animator is null!");
                return;
            }
            Debug.Log("[Driver] OnSkill2: AttackPhase=3");
            _animator.SetInteger(HASH_AttackPhase, 3);
            _animator.SetTrigger(HASH_Attack);
        }

        /// <summary>
        /// 3技能（Attack4）
        /// </summary>
        public void OnSkill3()
        {
            if (_animator == null)
            {
                Debug.LogError("[Driver] OnSkill3: _animator is null!");
                return;
            }
            Debug.Log("[Driver] OnSkill3: AttackPhase=4");
            _animator.SetInteger(HASH_AttackPhase, 4);
            _animator.SetTrigger(HASH_Attack);
        }

        /// <summary>
        /// 攻击完成
        /// </summary>
        public void OnAttackComplete()
        {
            _animator.SetInteger(HASH_AttackPhase, 0);
            _currentComboCount = 0;
        }

        /// <summary>
        /// 尝试连击下一击
        /// </summary>
        public void TryComboNext()
        {
            if (_lastNormalAttackIndex < 2)
                OnNormalAttack();
        }

        /// <summary>
        /// 播放受击反应动画（外部事件驱动，不经 CharacterData）
        /// </summary>
        public void PlayHitReaction()
        {
            _animator.SetInteger(HASH_State, STATE_IDLE);
        }
    }
}