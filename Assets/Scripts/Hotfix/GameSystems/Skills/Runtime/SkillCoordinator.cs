using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;
using Hotfix.GameSystems.SyncLayer;
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
        private bool _chargeReleaseReported;  // 本次施放是否已上报过蓄力释放

        // 事件
        public event Action<SkillData> OnSkillActivated;
        public event Action<int, SkillSubState> OnSkillStateChanged;
        public event Action<int, float> OnCooldownUpdate;  // skillId, progress
        public event Action<IEffectTarget> OnTargetHit;
        public event Action OnLightAttackCompleted;
        /// <summary>蓄力释放（松开按钮或蓄满自动释放）：skillData / 释放进度 [0,1] / 释放瞬间落点。
        /// 联网上报用——蓄力技能在 OnSkillActivated 时不发 CastRequest，改为在这里发（取消蓄力不触发）</summary>
        public event Action<SkillData, float, Vector3> OnChargeReleased;

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
        /// 蓄力技能：松开按钮提前释放（由输入层在键松开时调用）。
        /// 仅当指定技能正在蓄力时生效。
        /// </summary>
        public bool TryReleaseCharge(int skillId)
        {
            if (_currentSkill != null
                && _currentSkill.SkillId == skillId
                && _currentSkill.CurrentSubState == SkillSubState.Charging)
            {
                // 进度必须在 Release 之前取（Release 会把状态切到 Execution，GetChargeProgress 归 0）
                float progress = _currentSkill.GetChargeProgress();
                if (!_currentSkill.Release())
                    return false;
                ReportChargeReleased(progress);
                return true;
            }
            return false;
        }

        /// <summary>上报一次蓄力释放（同一施放只报一次；取消蓄力走 Interrupt，不经过这里）</summary>
        private void ReportChargeReleased(float progress)
        {
            if (_chargeReleaseReported) return;
            _chargeReleaseReported = true;
            OnChargeReleased?.Invoke(_currentSkill.Data, progress, _currentSkill.TargetPositionOrOwner);
        }

        /// <summary>
        /// 处理技能输入
        /// </summary>
        public void HandleInput(SkillInput input)
        {
            // R 同键取消特例：旋转期间再按同技能键 = 取消（必须位于冷却检查之前，
            // 因为旋转中技能自身处于冷却；永不入缓冲，防延迟重施放）
            if (_currentSkill != null
                && _currentSkill.CurrentSubState == SkillSubState.Spinning
                && input.SkillId == _currentSkill.SkillId)
            {
                if (_currentSkill.CanCancel())
                    _currentSkill.Cancel();
                return;
            }

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
        /// 重击（竖劈）——松手即放，仅在无活跃技能时激活
        /// </summary>
        public void HandleHeavyFire()
        {
            int skillId = (int)Definition.SkillID.HeavyAttack;
            if (!_skillDatabase.TryGetValue(skillId, out var skillData))
                return;

            if (_currentSkill != null && _currentSkill.IsActive)
                return;

            if (_cooldownManager.IsOnCooldown(skillId))
                return;

            if (!HasEnoughResources(skillData))
                return;

            var input = SkillInput.BasicAttack(skillId, _owner.transform.forward);
            TryActivateSkill(skillId, input);
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
            executor.SetAttackDirection(input.InputDirection);
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
            _chargeReleaseReported = false;   // 每次新施放复位（连击链/连续施放）

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
                SkillSubState.Spinning => false,
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
            bool wasCharging = _currentSkill != null
                && _currentSkill.CurrentSubState == SkillSubState.Charging;

            // 更新当前技能
            _currentSkill?.Update(deltaTime);

            // 蓄满自动释放：状态机在 MaxChargeTime 处直接 Charging → Execution（不经过
            // TryReleaseCharge）——联网上报同样需要这次"释放时刻"的 CastRequest，进度按满计。
            // 判定必须用 SkillCastReporter.IsChargeable（与激活侧同源）：20003 火球是
            // ProjectileSkillData（MaxChargeTime>0），只认 ChargedSkillData 会让它一条都发不出
            if (wasCharging && !_chargeReleaseReported
                && _currentSkill != null
                && _currentSkill.CurrentSubState == SkillSubState.Execution
                && SkillCastReporter.IsChargeable(_currentSkill.Data))
            {
                ReportChargeReleased(1f);
            }

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

        /// <summary>
        /// 清空输入缓冲（死亡等不可施放场景，防止缓冲技能被意外触发）
        /// </summary>
        public void ClearInputBuffer()
        {
            _inputBuffer.Clear();
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
            if (skillId == (int)Definition.SkillID.LightAttack)
                OnLightAttackCompleted?.Invoke();
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
                    InstantSkillData => true,
                    _ => false
                },
                SkillSubState.Recovery => _currentSkill.Data is ChargedSkillData
                                       || _currentSkill.Data is ComboSkillData
                                       || _currentSkill.Data is InstantSkillData,
                SkillSubState.Channeling =>
                    (_currentSkill.Data as ChanneledSkillData)?.CanMoveWhileChanneling ?? false,
                SkillSubState.Charging => _currentSkill.Data switch
                {
                    ChargedSkillData c => c.CanMoveWhileCharging,
                    ProjectileSkillData p => p.CanMoveWhileCharging,
                    _ => false
                },
                SkillSubState.Spinning => true,
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
                SkillSubState.Charging => _currentSkill.Data switch
                {
                    ChargedSkillData c => c.CanRotateWhileCharging,
                    ProjectileSkillData p => p.CanRotateWhileCharging,
                    _ => true
                },
                SkillSubState.Spinning => true,
                _ => false
            };
        }

        /// <summary>
        /// 当前移动速度倍率（旋转期间按配置减速，其余为1）
        /// </summary>
        public float GetMoveSpeedMultiplier()
        {
            if (_currentSkill != null
                && _currentSkill.CurrentSubState == SkillSubState.Spinning
                && _currentSkill.Data is SpinSkillData spin)
            {
                return spin.MoveSpeedMultiplier;
            }
            return 1f;
        }

        /// <summary>
        /// 持续型技能（蓄力/引导/旋转）期间，攻击动画的完成回调应被忽略——
        /// 这些技能的起手/循环动画结束不代表技能结束（否则 SkillR_start 95% 回调会强制完成技能）
        /// </summary>
        public bool IsAnimationCompletionIgnored()
        {
            return CurrentSubState switch
            {
                SkillSubState.Charging => true,
                SkillSubState.Channeling => true,
                SkillSubState.Spinning => true,
                _ => false
            };
        }
    }
}