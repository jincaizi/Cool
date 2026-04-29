using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Skill
{
    /// <summary>
    /// 技能注册表 — 管理技能配置和CD
    /// </summary>
    public class SkillRegistry
    {
        private readonly Dictionary<string, SkillConfig> _skills = new Dictionary<string, SkillConfig>();
        private readonly Dictionary<string, float> _cooldowns = new Dictionary<string, float>();

        /// <summary>
        /// 注册技能配置
        /// </summary>
        public void Register(SkillConfig config)
        {
            if (config == null || string.IsNullOrEmpty(config.SkillId))
            {
                Debug.LogError("[SkillRegistry] Invalid skill config");
                return;
            }

            _skills[config.SkillId] = config;
            _cooldowns[config.SkillId] = 0f;
            Debug.Log("[SkillRegistry] Registered skill: " + config.SkillId);
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
        /// 检查技能是否可用
        /// </summary>
        public bool CanUse(string skillId, bool isGrounded)
        {
            if (!_skills.TryGetValue(skillId, out var config))
            {
                Debug.LogWarning("[SkillRegistry] Skill not found: " + skillId);
                return false;
            }

            // 检查CD
            if (_cooldowns[skillId] > 0)
            {
                Debug.Log("[SkillRegistry] Skill on cooldown: " + skillId + ", remaining: " + _cooldowns[skillId]);
                return false;
            }

            // 检查空中使用
            if (!isGrounded && !config.CanUseInAir)
            {
                Debug.Log("[SkillRegistry] Skill cannot be used in air: " + skillId);
                return false;
            }

            return true;
        }

        /// <summary>
        /// 使用技能（开始CD）
        /// </summary>
        public void Use(string skillId)
        {
            if (!_skills.ContainsKey(skillId))
            {
                Debug.LogError("[SkillRegistry] Skill not registered: " + skillId);
                return;
            }

            var config = _skills[skillId];
            if (config.Cooldown > 0)
            {
                _cooldowns[skillId] = config.Cooldown;
                Debug.Log("[SkillRegistry] Used skill " + skillId + ", CD: " + config.Cooldown + "s");
            }
        }

        /// <summary>
        /// 获取技能配置
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
            foreach (var key in _cooldowns.Keys)
            {
                if (_cooldowns[key] > 0)
                {
                    _cooldowns[key] = Mathf.Max(0, _cooldowns[key] - deltaTime);
                }
            }
        }
    }
}
