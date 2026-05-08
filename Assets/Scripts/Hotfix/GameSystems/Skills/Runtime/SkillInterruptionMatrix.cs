using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 技能打断矩阵 - 定义哪些技能可被哪些行为打断
    /// </summary>
    public class SkillInterruptionMatrix
    {
        // 默认打断规则表
        private static readonly Dictionary<SkillType, Dictionary<InterruptionSource, bool>> _defaultRules = new()
        {
            {
                SkillType.Combo, new Dictionary<InterruptionSource, bool>
                {
                    { InterruptionSource.MovementInput, true },      // 可被移动取消
                    { InterruptionSource.BasicAttack, false },       // 普攻之间不能互断
                    { InterruptionSource.AnotherSkill, false },     // 普攻期间不可放技能
                    { InterruptionSource.DamageTaken, false },      // 霸体保护
                    { InterruptionSource.Stun, true },              // 眩晕可打断
                    { InterruptionSource.RollDodge, true },         // 可被翻滚取消
                    { InterruptionSource.Parry, true },             // 可被招架
                    { InterruptionSource.TimeOut, true }            // 超时自动结束
                }
            },
            {
                SkillType.Instant, new Dictionary<InterruptionSource, bool>
                {
                    { InterruptionSource.MovementInput, false },
                    { InterruptionSource.BasicAttack, false },
                    { InterruptionSource.AnotherSkill, false },
                    { InterruptionSource.DamageTaken, true },
                    { InterruptionSource.Stun, true },
                    { InterruptionSource.RollDodge, false },
                    { InterruptionSource.Parry, true },
                    { InterruptionSource.TimeOut, true }
                }
            },
            {
                SkillType.Charged, new Dictionary<InterruptionSource, bool>
                {
                    { InterruptionSource.MovementInput, false },
                    { InterruptionSource.BasicAttack, false },
                    { InterruptionSource.AnotherSkill, false },
                    { InterruptionSource.DamageTaken, true },
                    { InterruptionSource.Stun, true },
                    { InterruptionSource.RollDodge, false },
                    { InterruptionSource.Parry, true },
                    { InterruptionSource.TimeOut, true }
                }
            },
            {
                SkillType.Channeled, new Dictionary<InterruptionSource, bool>
                {
                    { InterruptionSource.MovementInput, false },
                    { InterruptionSource.BasicAttack, false },
                    { InterruptionSource.AnotherSkill, false },
                    { InterruptionSource.DamageTaken, true },
                    { InterruptionSource.Stun, true },
                    { InterruptionSource.RollDodge, false },
                    { InterruptionSource.Parry, true },
                    { InterruptionSource.TimeOut, true }
                }
            },
            {
                SkillType.Projectile, new Dictionary<InterruptionSource, bool>
                {
                    { InterruptionSource.MovementInput, false },
                    { InterruptionSource.BasicAttack, false },
                    { InterruptionSource.AnotherSkill, false },
                    { InterruptionSource.DamageTaken, true },
                    { InterruptionSource.Stun, true },
                    { InterruptionSource.RollDodge, false },
                    { InterruptionSource.Parry, true },
                    { InterruptionSource.TimeOut, true }
                }
            },
            {
                SkillType.Ultimate, new Dictionary<InterruptionSource, bool>
                {
                    { InterruptionSource.MovementInput, false },
                    { InterruptionSource.BasicAttack, false },
                    { InterruptionSource.AnotherSkill, false },
                    { InterruptionSource.DamageTaken, false },      // 大招霸体
                    { InterruptionSource.Stun, false },            // 不受控制
                    { InterruptionSource.RollDodge, false },
                    { InterruptionSource.Parry, false },
                    { InterruptionSource.TimeOut, true }
                }
            },
            {
                SkillType.Passive, new Dictionary<InterruptionSource, bool>
                {
                    // 被动技能不会被任何东西打断
                }
            },
            {
                SkillType.Item, new Dictionary<InterruptionSource, bool>
                {
                    { InterruptionSource.MovementInput, false },
                    { InterruptionSource.DamageTaken, true },
                    { InterruptionSource.Stun, true },
                    { InterruptionSource.TimeOut, true }
                }
            }
        };

        // 自定义打断规则（技能特定）
        private readonly Dictionary<int, Dictionary<InterruptionSource, bool>> _customRules = new();

        /// <summary>
        /// 检查技能是否可被特定来源打断
        /// </summary>
        public bool CanBeInterrupted(SkillData skillData, InterruptionSource source)
        {
            if (skillData == null) return false;

            // 优先检查技能自定义规则
            if (_customRules.TryGetValue(skillData.SkillId, out var customRule))
            {
                if (customRule.TryGetValue(source, out var canInterrupt))
                {
                    return canInterrupt;
                }
            }

            // 使用默认规则
            if (_defaultRules.TryGetValue(skillData.SkillType, out var typeRules))
            {
                if (typeRules.TryGetValue(source, out var result))
                {
                    return result;
                }
            }

            // 未定义的行为默认不可打断
            return false;
        }

        /// <summary>
        /// 检查技能在特定子状态下是否可被打断
        /// </summary>
        public bool CanBeInterruptedInState(SkillData skillData, SkillSubState subState, InterruptionSource source)
        {
            // 某些状态不允许被打断
            switch (subState)
            {
                case SkillSubState.Execution:
                    // 执行阶段通常不可被打断
                    return false;

                case SkillSubState.Recovery:
                    // 收招阶段可以被任何来源打断
                    return true;

                case SkillSubState.Cancelled:
                case SkillSubState.Completed:
                    return false;

                default:
                    return CanBeInterrupted(skillData, source);
            }
        }

        /// <summary>
        /// 设置技能自定义打断规则
        /// </summary>
        public void SetCustomRule(int skillId, InterruptionSource source, bool canInterrupt)
        {
            if (!_customRules.ContainsKey(skillId))
            {
                _customRules[skillId] = new Dictionary<InterruptionSource, bool>();
            }

            _customRules[skillId][source] = canInterrupt;
        }

        /// <summary>
        /// 设置技能多个自定义打断规则
        /// </summary>
        public void SetCustomRules(int skillId, params (InterruptionSource source, bool canInterrupt)[] rules)
        {
            foreach (var (source, canInterrupt) in rules)
            {
                SetCustomRule(skillId, source, canInterrupt);
            }
        }

        /// <summary>
        /// 清除技能自定义规则
        /// </summary>
        public void ClearCustomRule(int skillId)
        {
            _customRules.Remove(skillId);
        }

        /// <summary>
        /// 清除所有自定义规则
        /// </summary>
        public void ClearAllCustomRules()
        {
            _customRules.Clear();
        }

        /// <summary>
        /// 获取所有可打断当前技能的行为
        /// </summary>
        public List<InterruptionSource> GetValidInterruptions(SkillData skillData)
        {
            var result = new List<InterruptionSource>();

            if (skillData == null) return result;

            foreach (InterruptionSource source in Enum.GetValues(typeof(InterruptionSource)))
            {
                if (source != InterruptionSource.None && CanBeInterrupted(skillData, source))
                {
                    result.Add(source);
                }
            }

            return result;
        }

        /// <summary>
        /// 获取技能被打断的优先级（用于多个打断源同时触发时）
        /// </summary>
        public int GetInterruptionPriority(InterruptionSource source)
        {
            return source switch
            {
                InterruptionSource.Stun => 100,          // 最高优先级
                InterruptionSource.DamageTaken => 90,
                InterruptionSource.Parry => 80,
                InterruptionSource.AnotherSkill => 70,
                InterruptionSource.RollDodge => 60,
                InterruptionSource.BasicAttack => 50,
                InterruptionSource.MovementInput => 40,
                InterruptionSource.TimeOut => 10,
                _ => 0
            };
        }

        /// <summary>
        /// 创建全局打断请求
        /// </summary>
        public InterruptionRequest CreateRequest(SkillData skillData, SkillSubState subState, InterruptionSource source)
        {
            return new InterruptionRequest
            {
                SkillData = skillData,
                SubState = subState,
                Source = source,
                Priority = GetInterruptionPriority(source),
                CanInterrupt = CanBeInterruptedInState(skillData, subState, source)
            };
        }
    }

    /// <summary>
    /// 打断请求
    /// </summary>
    public struct InterruptionRequest
    {
        public SkillData SkillData;
        public SkillSubState SubState;
        public InterruptionSource Source;
        public int Priority;
        public bool CanInterrupt;
    }

    /// <summary>
    /// 打断结果
    /// </summary>
    public enum InterruptionResult
    {
        Interrupted,       // 成功打断
        NotInterruptible,  // 不可打断
        WrongState,       // 状态不允许打断
        PriorityTooLow    // 优先级太低
    }
}