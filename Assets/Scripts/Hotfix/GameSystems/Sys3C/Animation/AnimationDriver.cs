using UnityEngine;
using Hotfix.GameSystems.Sys3C.Character;

namespace Hotfix.GameSystems.Sys3C.Animation
{
    /// <summary>
    /// Animator 参数驱动 — FSM 与 Animator 之间的桥梁
    /// 统一管理所有 Animator 参数设置
    /// </summary>
    public class AnimationDriver
    {
        private readonly Animator _animator;

        // 参数哈希缓存
        private static readonly int HASH_BaseState = Animator.StringToHash("BaseState");
        private static readonly int HASH_AttackState = Animator.StringToHash("AttackState");
        private static readonly int HASH_HitState = Animator.StringToHash("HitState");
        private static readonly int HASH_IsJumping = Animator.StringToHash("IsJumping");
        private static readonly int HASH_IsHit = Animator.StringToHash("IsHit");
        private static readonly int HASH_IsDead = Animator.StringToHash("IsDead");
        private static readonly int HASH_Attack = Animator.StringToHash("Attack");
        private static readonly int HASH_SkillQ = Animator.StringToHash("SkillQ");
        private static readonly int HASH_SkillR = Animator.StringToHash("SkillR");
        private static readonly int HASH_Hit = Animator.StringToHash("Hit");
        private static readonly int HASH_Death = Animator.StringToHash("Death");
        private static readonly int HASH_Blend = Animator.StringToHash("Blend");

        // Layer indices
        private const int BASE_LAYER_INDEX = 0;
        private const int ATTACK_LAYER_INDEX = 1;
        private const int HIT_LAYER_INDEX = 2;

        public AnimationDriver(Animator animator)
        {
            _animator = animator ?? throw new System.ArgumentNullException(nameof(animator));

            // 初始化层权重：Attack 和 Hit 层默认关闭
            // Base Layer 的 Locomotion Blend Tree 需要独立驱动腿部动画
            _animator.SetLayerWeight(ATTACK_LAYER_INDEX, 0f);
            _animator.SetLayerWeight(HIT_LAYER_INDEX, 0f);
        }

        /// <summary>
        /// 设置 Base Layer 状态
        /// </summary>
        public void SetBaseState(BaseState state)
        {
            _animator.SetInteger(HASH_BaseState, (int)state);
        }

        /// <summary>
        /// 获取当前 Base Layer 状态
        /// </summary>
        public BaseState GetBaseState()
        {
            return (BaseState)_animator.GetInteger(HASH_BaseState);
        }

        /// <summary>
        /// 设置 Hit Layer 状态
        /// </summary>
        public void SetHitState(FSM.HitState state)
        {
            _animator.SetInteger(HASH_HitState, (int)state);
        }

        /// <summary>
        /// 获取当前 Hit Layer 状态
        /// </summary>
        public FSM.HitState GetHitState()
        {
            return (FSM.HitState)_animator.GetInteger(HASH_HitState);
        }

        /// <summary>
        /// 设置 Blend 参数（驱动 Blend Tree 动画混合）
        /// </summary>
        /// <param name="blend">混合值：0=Idle, 0.5=Walk, 1=Run/Sprint</param>
        public void SetBlend(float blend)
        {
            _animator.SetFloat(HASH_Blend, blend);
        }

        /// <summary>
        /// 获取当前 Blend 值
        /// </summary>
        public float GetBlend()
        {
            return _animator.GetFloat(HASH_Blend);
        }

        /// <summary>
        /// 设置 Attack Layer 状态
        /// </summary>
        public void SetAttackState(AttackState state)
        {
            _animator.SetInteger(HASH_AttackState, (int)state);
        }

        /// <summary>
        /// 获取当前 Attack Layer 状态
        /// </summary>
        public AttackState GetAttackState()
        {
            return (AttackState)_animator.GetInteger(HASH_AttackState);
        }

        /// <summary>
        /// 设置跳跃状态
        /// </summary>
        public void SetIsJumping(bool isJumping)
        {
            _animator.SetBool(HASH_IsJumping, isJumping);
        }

        /// <summary>
        /// 设置受击状态
        /// </summary>
        public void SetIsHit(bool isHit)
        {
            _animator.SetBool(HASH_IsHit, isHit);
        }

        /// <summary>
        /// 触发普通攻击/连击
        /// </summary>
        public void TriggerAttack()
        {
            _animator.SetTrigger(HASH_Attack);
        }

        /// <summary>
        /// 触发技能Q
        /// </summary>
        public void TriggerSkillQ()
        {
            _animator.SetTrigger(HASH_SkillQ);
        }

        /// <summary>
        /// 触发技能R
        /// </summary>
        public void TriggerSkillR()
        {
            _animator.SetTrigger(HASH_SkillR);
        }

        /// <summary>
        /// 触发受击动画
        /// </summary>
        public void TriggerHit()
        {
            _animator.SetTrigger(HASH_Hit);
            _animator.SetBool(HASH_IsHit, true);
            _animator.SetInteger(HASH_HitState, (int)FSM.HitState.Hit);
        }

        /// <summary>
        /// 触发死亡动画
        /// </summary>
        public void TriggerDeath()
        {
            _animator.SetTrigger(HASH_Death);
            _animator.SetBool(HASH_IsDead, true);
            _animator.SetInteger(HASH_HitState, (int)FSM.HitState.Death);
        }

        /// <summary>
        /// 重置死亡状态
        /// </summary>
        public void ResetDeathState()
        {
            _animator.SetBool(HASH_IsDead, false);
        }

        /// <summary>
        /// 重置攻击触发器（用于动画结束后清理）
        /// </summary>
        public void ResetAttackTrigger()
        {
            _animator.ResetTrigger(HASH_Attack);
        }

        /// <summary>
        /// 重置技能Q触发器
        /// </summary>
        public void ResetSkillQTrigger()
        {
            _animator.ResetTrigger(HASH_SkillQ);
        }

        /// <summary>
        /// 重置技能R触发器
        /// </summary>
        public void ResetSkillRTrigger()
        {
            _animator.ResetTrigger(HASH_SkillR);
        }

        /// <summary>
        /// 重置受击触发器
        /// </summary>
        public void ResetHitTrigger()
        {
            _animator.ResetTrigger(HASH_Hit);
        }

        /// <summary>
        /// 设置 Attack Layer 权重
        /// </summary>
        public void SetAttackLayerWeight(float weight)
        {
            _animator.SetLayerWeight(ATTACK_LAYER_INDEX, weight);
        }

        /// <summary>
        /// 获取 Hit Layer 权重
        /// </summary>
        public float GetHitLayerWeight()
        {
            return _animator.GetLayerWeight(HIT_LAYER_INDEX);
        }

        /// <summary>
        /// 设置 Hit Layer 权重
        /// </summary>
        public void SetHitLayerWeight(float weight)
        {
            _animator.SetLayerWeight(HIT_LAYER_INDEX, weight);
        }
    }
}
