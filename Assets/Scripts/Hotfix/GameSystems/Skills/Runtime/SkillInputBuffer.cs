using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 技能输入缓冲 - 处理预输入和指令队列
    /// </summary>
    public class SkillInputBuffer
    {
        private readonly Queue<BufferedCommand> _commandQueue = new();
        private float _bufferWindow = 0.15f;   // 150ms缓冲窗口
        private float _maxBufferTime = 0.5f;   // 最大缓冲保留时间
        private int _maxQueueSize = 3;          // 最大缓冲数量

        /// <summary>
        /// 入队技能命令
        /// </summary>
        public void Enqueue(SkillInput input, float timestamp)
        {
            // 清理过期命令
            CleanupExpiredCommands(timestamp);

            // 检查队列是否已满
            if (_commandQueue.Count >= _maxQueueSize)
            {
                // 移除最旧的命令
                _commandQueue.Dequeue();
            }

            // 检查是否已有相同技能缓冲 - 复制到列表中检查
            var existingCommands = new System.Collections.Generic.List<BufferedCommand>(_commandQueue);
            foreach (var existing in existingCommands)
            {
                if (existing.Input.SkillId == input.SkillId)
                {
                    // 找到相同技能，移除旧命令
                    var tempQueue = new Queue<BufferedCommand>();
                    while (_commandQueue.Count > 0)
                    {
                        var cmd = _commandQueue.Dequeue();
                        if (cmd.Input.SkillId != input.SkillId)
                        {
                            tempQueue.Enqueue(cmd);
                        }
                    }
                    while (tempQueue.Count > 0)
                    {
                        _commandQueue.Enqueue(tempQueue.Dequeue());
                    }
                    break;
                }
            }

            _commandQueue.Enqueue(new BufferedCommand
            {
                Input = input,
                InputTime = timestamp
            });
        }

        /// <summary>
        /// 查看但不出队
        /// </summary>
        public bool TryPeek(out BufferedCommand command)
        {
            CleanupExpiredCommands(Time.time);

            if (_commandQueue.Count > 0)
            {
                command = _commandQueue.Peek();
                return true;
            }

            command = default;
            return false;
        }

        /// <summary>
        /// 查看特定技能但不消费
        /// </summary>
        public bool TryPeekSkill(int skillId, out BufferedCommand command)
        {
            CleanupExpiredCommands(Time.time);

            foreach (var cmd in _commandQueue)
            {
                if (cmd.Input.SkillId == skillId)
                {
                    command = cmd;
                    return true;
                }
            }

            command = default;
            return false;
        }

        /// <summary>
        /// 消费并出队
        /// </summary>
        public bool TryConsume(out BufferedCommand command)
        {
            if (TryPeek(out command))
            {
                _commandQueue.Dequeue();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 消费特定技能（如果有）
        /// </summary>
        public bool TryConsumeSkill(int skillId, out BufferedCommand command)
        {
            var tempQueue = new Queue<BufferedCommand>();
            bool found = false;
            command = default;

            while (_commandQueue.Count > 0)
            {
                var cmd = _commandQueue.Dequeue();
                if (!found && cmd.Input.SkillId == skillId)
                {
                    command = cmd;
                    found = true;
                }
                else
                {
                    tempQueue.Enqueue(cmd);
                }
            }

            // 恢复剩余命令
            while (tempQueue.Count > 0)
            {
                _commandQueue.Enqueue(tempQueue.Dequeue());
            }

            return found;
        }

        /// <summary>
        /// 清空缓冲
        /// </summary>
        public void Clear()
        {
            _commandQueue.Clear();
        }

        /// <summary>
        /// 清空特定技能的缓冲
        /// </summary>
        public void ClearSkill(int skillId)
        {
            var tempQueue = new Queue<BufferedCommand>();
            while (_commandQueue.Count > 0)
            {
                var cmd = _commandQueue.Dequeue();
                if (cmd.Input.SkillId != skillId)
                {
                    tempQueue.Enqueue(cmd);
                }
            }

            while (tempQueue.Count > 0)
            {
                _commandQueue.Enqueue(tempQueue.Dequeue());
            }
        }

        /// <summary>
        /// 获取缓冲数量
        /// </summary>
        public int Count
        {
            get
            {
                CleanupExpiredCommands(Time.time);
                return _commandQueue.Count;
            }
        }

        /// <summary>
        /// 检查命令是否在有效缓冲窗口内
        /// </summary>
        public bool IsWithinBufferWindow(BufferedCommand command)
        {
            return Time.time - command.InputTime <= _bufferWindow;
        }

        /// <summary>
        /// 设置缓冲窗口大小
        /// </summary>
        public void SetBufferWindow(float seconds)
        {
            _bufferWindow = Mathf.Clamp(seconds, 0.05f, 1f);
        }

        /// <summary>
        /// 设置最大缓冲数量
        /// </summary>
        public void SetMaxQueueSize(int size)
        {
            _maxQueueSize = Mathf.Clamp(size, 1, 10);
        }

        private void CleanupExpiredCommands(float currentTime)
        {
            var tempQueue = new Queue<BufferedCommand>();

            while (_commandQueue.Count > 0)
            {
                var cmd = _commandQueue.Dequeue();
                if (currentTime - cmd.InputTime <= _maxBufferTime)
                {
                    tempQueue.Enqueue(cmd);
                }
            }

            while (tempQueue.Count > 0)
            {
                _commandQueue.Enqueue(tempQueue.Dequeue());
            }
        }
    }

    /// <summary>
    /// 缓冲的命令
    /// </summary>
    public struct BufferedCommand
    {
        public SkillInput Input;
        public float InputTime;

        /// <summary>
        /// 获取命令的年龄（秒）
        /// </summary>
        public float Age => Time.time - InputTime;
    }

    /// <summary>
    /// 技能输入
    /// </summary>
    public struct SkillInput
    {
        public int SkillId;
        public Vector3 TargetPosition;
        public int TargetEntityId;
        public Vector3 InputDirection;
        public bool IsRangedSkill;
        public bool IsCharging;

        /// <summary>
        /// 创建普攻输入
        /// </summary>
        public static SkillInput BasicAttack(int skillId, Vector3 direction)
        {
            return new SkillInput
            {
                SkillId = skillId,
                InputDirection = direction,
                IsRangedSkill = false,
                IsCharging = false
            };
        }

        /// <summary>
        /// 创建技能输入（目标位置）
        /// </summary>
        public static SkillInput SkillToPosition(int skillId, Vector3 position)
        {
            return new SkillInput
            {
                SkillId = skillId,
                TargetPosition = position,
                IsRangedSkill = true,
                IsCharging = false
            };
        }

        /// <summary>
        /// 创建技能输入（目标单位）
        /// </summary>
        public static SkillInput SkillToTarget(int skillId, int targetId, Vector3 targetPosition)
        {
            return new SkillInput
            {
                SkillId = skillId,
                TargetEntityId = targetId,
                TargetPosition = targetPosition,
                IsRangedSkill = true,
                IsCharging = false
            };
        }

        /// <summary>
        /// 创建蓄力技能输入
        /// </summary>
        public static SkillInput ChargingSkill(int skillId, Vector3 direction)
        {
            return new SkillInput
            {
                SkillId = skillId,
                InputDirection = direction,
                IsRangedSkill = true,
                IsCharging = true
            };
        }
    }
}