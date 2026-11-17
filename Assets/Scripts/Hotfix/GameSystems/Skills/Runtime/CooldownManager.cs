using System;
using System.Collections.Generic;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;

namespace Hotfix.GameSystems.Skills.Runtime
{
    /// <summary>
    /// 冷却管理器 - 管理所有技能的冷却状态
    /// </summary>
    public class CooldownManager
    {
        private readonly Dictionary<int, CooldownEntry> _cooldowns = new();
        private readonly List<int> _keysToRemove = new();

        /// <summary>
        /// 开始技能冷却
        /// </summary>
        public void StartCooldown(int skillId, float duration)
            => StartCooldown(skillId, duration, 0f);

        /// <summary>
        /// 按服务端权威已过时间开始冷却（剑气回荡迟到确认：Total=12、Remaining=10）。
        /// 原双参数调用等价于 elapsedSeconds = 0。
        /// </summary>
        public void StartCooldown(int skillId, float totalDuration, float elapsedSeconds)
        {
            if (!float.IsFinite(totalDuration) || totalDuration < 0f) return;
            float elapsed = float.IsFinite(elapsedSeconds)
                ? UnityEngine.Mathf.Max(0f, elapsedSeconds)
                : 0f;

            _cooldowns[skillId] = new CooldownEntry
            {
                StartTime = UnityEngine.Time.time - elapsed,
                Duration = totalDuration,
                Remaining = UnityEngine.Mathf.Max(0f, totalDuration - elapsed)
            };
        }

        /// <summary>
        /// 是否在冷却中
        /// </summary>
        public bool IsOnCooldown(int skillId)
        {
            if (!_cooldowns.TryGetValue(skillId, out var entry))
            {
                return false;
            }

            entry.Remaining = UnityEngine.Mathf.Max(0, entry.Duration - (UnityEngine.Time.time - entry.StartTime));
            return entry.Remaining > 0;
        }

        /// <summary>
        /// 获取剩余冷却时间
        /// </summary>
        public float GetRemainingCooldown(int skillId)
        {
            if (!_cooldowns.TryGetValue(skillId, out var entry))
            {
                return 0f;
            }

            return UnityEngine.Mathf.Max(0, entry.Duration - (UnityEngine.Time.time - entry.StartTime));
        }

        /// <summary>
        /// 获取归一化冷却进度 [0, 1]
        /// </summary>
        public float GetNormalizedCooldown(int skillId)
        {
            if (!_cooldowns.TryGetValue(skillId, out var entry))
            {
                return 1f;
            }

            float elapsed = UnityEngine.Time.time - entry.StartTime;
            return UnityEngine.Mathf.Clamp01(elapsed / entry.Duration);
        }

        /// <summary>
        /// 获取冷却完成进度 [1, 0]（用于UI倒计时）
        /// </summary>
        public float GetCooldownProgress(int skillId)
        {
            return 1f - GetNormalizedCooldown(skillId);
        }

        /// <summary>
        /// 缩短冷却时间
        /// </summary>
        public void ReduceCooldown(int skillId, float amount)
        {
            if (_cooldowns.TryGetValue(skillId, out var entry))
            {
                entry.Duration = UnityEngine.Mathf.Max(0, entry.Duration - amount);
            }
        }

        /// <summary>
        /// 缩短冷却百分比
        /// </summary>
        public void ReduceCooldownPercent(int skillId, float percent)
        {
            if (_cooldowns.TryGetValue(skillId, out var entry))
            {
                entry.Duration = UnityEngine.Mathf.Max(0, entry.Duration * (1f - percent));
            }
        }

        /// <summary>
        /// 重置冷却
        /// </summary>
        public void ResetCooldown(int skillId)
        {
            _cooldowns.Remove(skillId);
        }

        /// <summary>
        /// 清空所有冷却
        /// </summary>
        public void ClearAll()
        {
            _cooldowns.Clear();
        }

        /// <summary>
        /// 清空指定技能类别的冷却
        /// </summary>
        public void ClearByType(Definition.SkillType skillType, Func<int, Definition.SkillType> getSkillType)
        {
            var keysToClear = new List<int>();
            foreach (var kvp in _cooldowns)
            {
                if (getSkillType(kvp.Key) == skillType)
                {
                    keysToClear.Add(kvp.Key);
                }
            }

            foreach (var key in keysToClear)
            {
                _cooldowns.Remove(key);
            }
        }

        /// <summary>
        /// 每帧更新 - 清理已结束的冷却
        /// </summary>
        public void Update(float deltaTime)
        {
            _keysToRemove.Clear();

            foreach (var kvp in _cooldowns)
            {
                float remaining = kvp.Value.Duration - (UnityEngine.Time.time - kvp.Value.StartTime);
                if (remaining <= 0)
                {
                    _keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in _keysToRemove)
            {
                _cooldowns.Remove(key);
            }
        }

        /// <summary>
        /// 获取所有正在冷却的技能ID
        /// </summary>
        public IEnumerable<int> GetActiveCooldowns()
        {
            return _cooldowns.Keys;
        }

        /// <summary>
        /// 获取冷却信息（用于UI显示）
        /// </summary>
        public CooldownInfo GetCooldownInfo(int skillId)
        {
            if (!_cooldowns.TryGetValue(skillId, out var entry))
            {
                return new CooldownInfo { IsOnCooldown = false };
            }

            float elapsed = UnityEngine.Time.time - entry.StartTime;
            float remaining = entry.Duration - elapsed;

            return new CooldownInfo
            {
                IsOnCooldown = true,
                Remaining = UnityEngine.Mathf.Max(0, remaining),
                Total = entry.Duration,
                Progress = UnityEngine.Mathf.Clamp01(elapsed / entry.Duration)
            };
        }

        private struct CooldownEntry
        {
            public float StartTime;
            public float Duration;
            public float Remaining;
        }
    }

    /// <summary>
    /// 冷却信息（用于UI）
    /// </summary>
    public struct CooldownInfo
    {
        public bool IsOnCooldown;
        public float Remaining;
        public float Total;
        public float Progress;  // 0-1，完成度
    }
}