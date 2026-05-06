using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.Skills.Effect;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    using Definition = Hotfix.GameSystems.Skills.Definition;

    /// <summary>
    /// 技能注册表 — 管理技能配置和CD
    /// 同时支持旧的SkillConfig和新技能系统的SkillData
    /// </summary>
    public class SkillRegistry
    {
        // 旧系统
        private readonly Dictionary<string, SkillConfig> _skills = new Dictionary<string, SkillConfig>();
        private readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();

        // 新技能系统
        private readonly Dictionary<int, SkillData> _skillDataMap = new Dictionary<int, SkillData>();

        /// <summary>
        /// 技能数量
        /// </summary>
        public int SkillCount => _skillDataMap.Count + _skills.Count;

        /// <summary>
        /// 注册技能配置（旧系统）
        /// </summary>
        public void Register(SkillConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.SkillId))
            {
                return;
            }

            _skills[config.SkillId] = config;
            _cooldowns[config.SkillId] = 0f;
        }

        /// <summary>
        /// 注册多个技能
        /// </summary>
        public void RegisterRange(IEnumerable<SkillConfig> configs)
        {
            foreach (var config in configs)
            {
                Register(config);
            }
        }

        /// <summary>
        /// 注册新技能系统的SkillData
        /// </summary>
        public void RegisterSkillData(SkillData data)
        {
            if (data == null)
            {
                return;
            }

            _skillDataMap[data.SkillId] = data;
        }

        /// <summary>
        /// 注册多个SkillData
        /// </summary>
        public void RegisterSkillDataRange(IEnumerable<SkillData> skillDataList)
        {
            foreach (var data in skillDataList)
            {
                RegisterSkillData(data);
            }
        }

        /// <summary>
        /// 获取所有SkillData
        /// </summary>
        public IEnumerable<SkillData> GetAllSkills()
        {
            return _skillDataMap.Values;
        }

        /// <summary>
        /// 获取指定ID的SkillData
        /// </summary>
        public SkillData GetSkillData(int skillId)
        {
            return _skillDataMap.TryGetValue(skillId, out var data) ? data : null;
        }

        /// <summary>
        /// 获取基础攻击1的技能ID
        /// </summary>
        public int GetBasicAttack1Id()
        {
            foreach (var kvp in _skillDataMap)
            {
                if (kvp.Value.SkillType == Hotfix.GameSystems.Skills.Definition.SkillType.BasicAttack)
                    return kvp.Key;
            }
            return 10001; // 默认值
        }

        /// <summary>
        /// 获取技能Q的ID
        /// </summary>
        public int GetSkillQId()
        {
            foreach (var kvp in _skillDataMap)
            {
                if (kvp.Value.SkillType == Hotfix.GameSystems.Skills.Definition.SkillType.Special &&
                    kvp.Key.ToString().EndsWith("001")) // SkillQ
                    return kvp.Key;
            }
            return 20001; // 默认值
        }

        /// <summary>
        /// 获取技能R的ID
        /// </summary>
        public int GetSkillRId()
        {
            foreach (var kvp in _skillDataMap)
            {
                if (kvp.Value.SkillType == Hotfix.GameSystems.Skills.Definition.SkillType.Special &&
                    kvp.Key.ToString().EndsWith("002")) // SkillR
                    return kvp.Key;
            }
            return 20002; // 默认值
        }

        /// <summary>
        /// 检查技能是否可用（旧系统）
        /// </summary>
        public bool CanUse(string skillId, bool isGrounded)
        {
            if (!_skills.TryGetValue(skillId, out var config))
            {
                return false;
            }

            // 检查CD
            if (_cooldowns[skillId] > 0)
            {
                return false;
            }

            // 检查空中使用
            if (!isGrounded && !config.CanUseInAir)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 使用技能（旧系统 - 开始CD）
        /// </summary>
        public void Use(string skillId)
        {
            if (!_skills.ContainsKey(skillId))
            {
                return;
            }

            var config = _skills[skillId];
            if (config.Cooldown > 0)
            {
                _cooldowns[skillId] = config.Cooldown;
            }
        }

        /// <summary>
        /// 获取技能配置（旧系统）
        /// </summary>
        public SkillConfig GetConfig(string skillId)
        {
            return _skills.TryGetValue(skillId, out var config) ? config : null;
        }

        /// <summary>
        /// 获取技能CD剩余时间
        /// </summary>
        public float GetCooldownRemaining(string skillId)
        {
            return _cooldowns.TryGetValue(skillId, out var cd) ? cd : 0f;
        }

        /// <summary>
        /// 每帧更新CD
        /// </summary>
        public void Update(float deltaTime)
        {
            // 更新旧系统CD
            foreach (var skillId in _cooldowns.Keys.ToList())
            {
                if (_cooldowns[skillId] > 0)
                {
                    _cooldowns[skillId] = Mathf.Max(0, _cooldowns[skillId] - deltaTime);
                }
            }
        }
    }
}
