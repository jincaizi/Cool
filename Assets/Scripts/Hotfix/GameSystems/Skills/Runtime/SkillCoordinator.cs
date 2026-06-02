using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 技能协调器 - 管理角色所有技能的释放、冷却、优先级
    /// 与FSM协调器交互，处理技能与基础层/受击层的协调
    /// </summary>
    public class SkillCoordinator
    {
        private readonly IEffectTarget _owner;
        private readonly Dictionary<int, SkillData> _skillDatabase;
        private readonly Dictionary<int, SkillExecutor> _activeExecutors;
        private readonly CooldownManager _cooldownManager;
        private readonly SkillInputBuffer _inputBuffer;
        private readonly SkillInterruptionMatrix _interruptionMatrix;
        private IDashComponent _dashComponent;

        // 当前正在执行的技能
        private SkillExecutor _currentSkill;
        private SkillExecutor _queuedSkill;  // 预输入的技能

        // 事件
        public event Action<SkillData> OnSkillActivated;
        public event Action<int, SkillSubState> OnSkillStateChanged;
        public event Action<int, float> OnCooldownUpdate;  // skillId, progress
        public event Action<IEffectTarget> OnTargetHit;

        // 属性
        public SkillExecutor CurrentSkill => _currentSkill;
        public SkillSubState CurrentSubState => _currentSkill?.CurrentSubState ?? SkillSubState.None;
        public bool IsSkillActive => _currentSkill != null && _currentSkill.IsActive;
        public bool IsCasting => CurrentSubState == SkillSubState.Casting ||
                                  CurrentSubState == SkillSubState.Charging ||
                                  CurrentSubState == SkillSubState.Channeling;

        public SkillCoordinator(IEffectTarget owner)
        {
            _owner = owner;
            _skillDatabase = new Dictionary<int, SkillData>();
            _activeExecutors = new Dictionary<int, SkillExecutor>();
            _cooldownManager = new CooldownManager();
            _inputBuffer = new SkillInputBuffer();
            _interruptionMatrix = new SkillInterruptionMatrix();
        }

        /// <summary>
        /// 注册技能数据
        /// </summary>
        public void RegisterSkill(SkillData data)
        {
            if (data != null)
            {
                _skillDatabase[data.SkillId] = data;
            }
        }

        /// <summary>
        /// 注册多个技能数据
        /// </summary>
        public void RegisterSkills(IEnumerable<SkillData> skillDataList)
        {
            foreach (var data in skillDataList)
            {
                RegisterSkill(data);
            }
        }

        /// <summary>
        /// 获取技能数据
        /// </summary>
        public SkillData GetSkillData(int skillId)
        {
            return _skillDatabase.TryGetValue(skillId, out var data) ? data : null;
        }

        public void SetDashComponent(IDashComponent dashComponent)
        {
            _dashComponent = dashComponent;
        }

        /// <summary>
        /// 处理技能输入
        /// </summary>
        public void HandleInput(SkillInput input)
        {
            // 检查技能是否存在
            if (!_skillDatabase.TryGetValue(input.SkillId, out var skillData))
            {
                return;
            }

            // 检查冷却
            if (_cooldownManager.IsOnCooldown(input.SkillId))
            {
                // 冷却中，存入缓冲
                _inputBuffer.Enqueue(input, UnityEngine.Time.time);
                return;
            }

            // 检查资源
            if (!HasEnoughResources(skillData))
            {
                return;
            }

            // 当前无技能执行，直接释放
            if (_currentSkill == null || !_currentSkill.IsActive)
            {
                TryActivateSkill(input.SkillId, input);
            }
            else
            {
                // 有技能执行，检查是否可取消/链接
                if (CanChainSkill(input.SkillId))
                {
                    // 尝试取消当前技能
                    TryCancelCurrentSkill(InterruptionSource.AnotherSkill);
                    TryActivateSkill(input.SkillId, input);
                }
                else
                {
                    // 存入缓冲，等待当前技能结束
                    _inputBuffer.Enqueue(input, UnityEngine.Time.time);
                }
            }
        }

        /// <summary>
        /// 轻击（横劈）——仅当无技能执行或在可取消窗口内才激活
        /// </summary>
        public void HandleLightAttack()
        {
            int skillId = (int)Definition.SkillID.LightAttack;
            if (!_skillDatabase.TryGetValue(skillId, out var skillData))
                return;

            // 有技能正在执行，检查是否在可取消窗口
            if (_currentSkill != null && _currentSkill.IsActive)
            {
                if (!IsInCancelableWindow())
                    return;
                _currentSkill.ForceComplete();
            }

            // 检查冷却
            if (_cooldownManager.IsOnCooldown(skillId))
                return;

            if (!HasEnoughResources(skillData))
                return;

            var input = SkillInput.BasicAttack(skillId, _owner.transform.forward);
            TryActivateSkill(skillId, input);
        }

        /// <summary>
        /// 重击（竖劈蓄力）——开始蓄力
        /// </summary>
        public void HandleHeavyAttack()
        {
            int skillId = (int)Definition.SkillID.HeavyAttack;
            if (!_skillDatabase.TryGetValue(skillId, out var skillData))
                return;

            // 已在蓄力中，跳过
            if (_currentSkill != null && _currentSkill.IsActive
                && _currentSkill.CurrentSubState == SkillSubState.Charging)
                return;

            if (_cooldownManager.IsOnCooldown(skillId))
                return;

            if (!HasEnoughResources(skillData))
                return;

            var input = SkillInput.ChargingSkill(skillId, _owner.transform.forward);
            TryActivateSkill(skillId, input);
        }

        /// <summary>
        /// 释放重击蓄力
        /// </summary>
        public void HandleHeavyRelease()
        {
            if (_currentSkill != null && _currentSkill.IsActive
                && _currentSkill.CurrentSubState == SkillSubState.Charging)
            {
                _currentSkill.ReleaseCharge();
            }
        }

        private bool IsInCancelableWindow()
        {
            if (_currentSkill == null) return true;

            var clip = _currentSkill.Data.GetMainAnimationClip();
            if (clip == null) return true;

            float totalDuration = clip.length;
            if (totalDuration <= 0f) return true;

            return _currentSkill.Data switch
            {
                ComboSkillData combo => combo.IsInCancelableWindow(_currentSkill.ElapsedTime, totalDuration),
                _ => false
            };
        }

        /// <summary>
        /// 尝试激活技能
        /// </summary>
        private bool TryActivateSkill(int skillId, SkillInput input = default)
        {
            if (!_skillDatabase.TryGetValue(skillId, out var skillData))
            {
                return false;
            }

            // 检查优先级（如果有当前技能）
            if (_currentSkill != null && _currentSkill.IsActive)
            {
                if (skillData.InterruptionPriority < _currentSkill.Data.InterruptionPriority)
                {
                    return false;
                }

                // 检查打断矩阵
                if (!_interruptionMatrix.CanBeInterrupted(_currentSkill.Data, InterruptionSource.AnotherSkill))
                {
                    return false;
                }
            }

            // 创建执行器
            var executor = new SkillExecutor(_owner, skillData, _interruptionMatrix);
            if (_dashComponent != null)
            {
                executor.SetDashComponent(_dashComponent);
            }
            executor.SetTargetPosition(input.TargetPosition);
            if (input.TargetEntityId > 0)
            {
                // 设置目标单位（需要通过ID查找）
                // 这里需要根据实际情况实现
            }

            // 注册事件
            executor.OnSkillCompleted += () => OnExecutorCompleted(skillId);
            executor.OnSkillInterrupted += (source) => OnExecutorInterrupted(skillId, source);
            executor.OnTargetHit += (target) => OnTargetHit?.Invoke(target);

            _activeExecutors[skillId] = executor;
            _currentSkill = executor;

            // 扣除资源
            ConsumeResources(skillData);

            // 启动冷却
            _cooldownManager.StartCooldown(skillId, skillData.Cooldown);

            // 尝试开始释放
            if (!executor.TryStart())
            {
                // 可能已经在冷却中
                return false;
            }

            // 通知事件
            OnSkillActivated?.Invoke(skillData);

            return true;
        }

        /// <summary>
        /// 能否链接到另一个技能
        /// </summary>
        private bool CanChainSkill(int nextSkillId)
        {
            if (_currentSkill == null || !_currentSkill.IsActive)
                return true;

            var nextData = GetSkillData(nextSkillId);
            if (nextData == null) return false;

            var currentState = _currentSkill.CurrentSubState;
            switch (currentState)
            {
                case SkillSubState.Execution:
                case SkillSubState.HitConfirm:
                    return false;
                case SkillSubState.Cancelled:
                case SkillSubState.Completed:
                    return true;
            }

            if (currentState == SkillSubState.Recovery)
                return nextData.CanCancelIntoBasicAttack || nextData.CanCancelIntoOtherSkill;

            return currentState switch
            {
                SkillSubState.Casting => nextData is InstantSkillData,
                SkillSubState.Channeling => nextData is InstantSkillData
                    && (_currentSkill.Data is ChanneledSkillData ch && ch.CanMoveWhileChanneling),
                SkillSubState.Charging => false,
                _ => false
            };
        }

        /// <summary>
        /// 尝试取消当前技能
        /// </summary>
        private bool TryCancelCurrentSkill(InterruptionSource source)
        {
            if (_currentSkill != null && _currentSkill.IsActive)
            {
                return _currentSkill.TryInterrupt(source);
            }
            return true;  // 没有活动技能，视为成功
        }

        /// <summary>
        /// 中断当前技能
        /// </summary>
        public void InterruptCurrentSkill(InterruptionSource source)
        {
            TryCancelCurrentSkill(source);
        }

        /// <summary>
        /// 处理伤害中断
        /// </summary>
        public void OnDamageTaken(float damage, DamageType damageType)
        {
            if (_currentSkill != null && _currentSkill.IsActive)
            {
                _currentSkill.TryInterrupt(InterruptionSource.DamageTaken);
            }

            // 处理缓冲输入
            ProcessInputBuffer();
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public void Update(float deltaTime)
        {
            // 更新当前技能
            _currentSkill?.Update(deltaTime);

            // 更新冷却管理器
            _cooldownManager.Update(deltaTime);

            // 处理缓冲输入
            ProcessInputBuffer();

            // 发送冷却更新事件
            foreach (var kvp in _activeExecutors)
            {
                float progress = _cooldownManager.GetNormalizedCooldown(kvp.Key);
                if (progress < 1f)
                {
                    OnCooldownUpdate?.Invoke(kvp.Key, progress);
                }
            }
        }

        private void ProcessInputBuffer()
        {
            while (_inputBuffer.TryPeek(out var buffered))
            {
                // 检查冷却是否已好
                if (!_cooldownManager.IsOnCooldown(buffered.Input.SkillId))
                {
                    _inputBuffer.TryConsume(out _);

                    // 检查资源
                    var skillData = GetSkillData(buffered.Input.SkillId);
                    if (skillData != null && HasEnoughResources(skillData))
                    {
                        // 当前无技能执行或可取消
                        if (_currentSkill == null || !_currentSkill.IsActive)
                        {
                            TryActivateSkill(buffered.Input.SkillId, buffered.Input);
                        }
                        else if (CanChainSkill(buffered.Input.SkillId))
                        {
                            TryCancelCurrentSkill(InterruptionSource.AnotherSkill);
                            TryActivateSkill(buffered.Input.SkillId, buffered.Input);
                        }
                        else
                        {
                            // 仍然无法释放，重新缓冲
                            _inputBuffer.Enqueue(buffered.Input, UnityEngine.Time.time);
                            break;
                        }
                    }
                }
                else
                {
                    break;  // 还在冷却，停止处理
                }
            }
        }

        private void OnExecutorCompleted(int skillId)
        {
            CleanupExecutor(skillId);
        }

        private void OnExecutorInterrupted(int skillId, InterruptionSource source)
        {
            CleanupExecutor(skillId);
        }

        private void CleanupExecutor(int skillId)
        {
            if (_activeExecutors.TryGetValue(skillId, out var executor))
            {
                executor.OnSkillCompleted -= () => OnExecutorCompleted(skillId);
                executor.OnSkillInterrupted -= (source) => OnExecutorInterrupted(skillId, source);
                _activeExecutors.Remove(skillId);
            }

            if (_currentSkill?.SkillId == skillId)
            {
                _currentSkill = null;

                // 尝试执行队列中的技能
                ProcessInputBuffer();
            }
        }

        private bool HasEnoughResources(SkillData skillData)
        {
            // TODO: 检查玩家资源（魔法、耐力等）
            // 这里应该从角色属性中获取
            return true;
        }

        private void ConsumeResources(SkillData skillData)
        {
            // TODO: 扣除玩家资源
        }

        /// <summary>
        /// 获取技能冷却信息
        /// </summary>
        public CooldownInfo GetCooldownInfo(int skillId)
        {
            return _cooldownManager.GetCooldownInfo(skillId);
        }

        /// <summary>
        /// 是否在安全施法状态（可以切换目标等）
        /// </summary>
        public bool IsInSafeCastState()
        {
            return _currentSkill?.CurrentSubState switch
            {
                SkillSubState.Casting => true,
                SkillSubState.Channeling =>
                    (_currentSkill.Data as ChanneledSkillData)?.CanMoveWhileChanneling ?? false,
                SkillSubState.Charging => true,
                _ => false
            };
        }

        /// <summary>
        /// 是否可以移动
        /// </summary>
        public bool CanMove()
        {
            if (_currentSkill == null || !_currentSkill.IsActive)
                return true;

            return _currentSkill.CurrentSubState switch
            {
                SkillSubState.Casting => true,
                SkillSubState.Execution => _currentSkill.Data switch
                {
                    ComboSkillData combo => combo.EnableMovement,
                    ChargedSkillData => true,
                    _ => false
                },
                SkillSubState.Recovery => _currentSkill.Data is ChargedSkillData
                                       || _currentSkill.Data is ComboSkillData,
                SkillSubState.Channeling =>
                    (_currentSkill.Data as ChanneledSkillData)?.CanMoveWhileChanneling ?? false,
                SkillSubState.Charging =>
                    (_currentSkill.Data as ChargedSkillData)?.CanMoveWhileCharging ?? false,
                _ => false
            };
        }

        /// <summary>
        /// 是否可以转向
        /// </summary>
        public bool CanRotate()
        {
            if (_currentSkill == null || !_currentSkill.IsActive)
                return true;

            return _currentSkill.CurrentSubState switch
            {
                SkillSubState.Casting => true,
                SkillSubState.Execution => _currentSkill.Data is ChargedSkillData,
                SkillSubState.Recovery => _currentSkill.Data is ChargedSkillData,
                SkillSubState.Charging =>
                    (_currentSkill.Data as ChargedSkillData)?.CanRotateWhileCharging ?? true,
                _ => false
            };
        }
    }
}