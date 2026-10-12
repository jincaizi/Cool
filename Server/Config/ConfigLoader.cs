using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KcpServer.Config
{
    public class ConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // P5：MonsterData 全 public 字段——System.Text.Json 默认只反序列化属性，
            // 不开此项则整表静默落 0（templateId=0 → 怪物配置全失）——T2 实测修复
            IncludeFields = true
        };

        public static MonsterConfig LoadMonsterConfig(string path)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"Monster config not found: {path}, using defaults");
                return CreateDefaultConfig();
            }

            try
            {
                string json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<MonsterConfig>(json, JsonOptions);
                return config ?? CreateDefaultConfig();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to load monster config: {e.Message}, using defaults");
                return CreateDefaultConfig();
            }
        }

        public static Dictionary<int, MonsterData> LoadMonsterTemplates(string configPath)
        {
            var config = LoadMonsterConfig(configPath);
            var dict = new Dictionary<int, MonsterData>();

            foreach (var monster in config.monsters)
            {
                dict[monster.templateId] = monster;
            }

            return dict;
        }

        private static MonsterConfig CreateDefaultConfig()
        {
            return new MonsterConfig
            {
                monsters = new List<MonsterData>
                {
                    new MonsterData
                    {
                        templateId = 1,
                        name = "Slime",
                        hp = 100,
                        moveSpeed = 2.0f,
                        bodyRadius = 0.5f,          // 与 monster_config.json / 客户端球体占位同值（见 MonsterData.bodyRadius）
                        detectionRadius = 8,
                        visionAngle = 120,
                        attackRange = 1.5f,
                        patrolRadius = 5,
                        skills = new List<string> { "Attack" }
                    },
                    new MonsterData
                    {
                        templateId = 2,
                        name = "Wolf",
                        hp = 150,
                        moveSpeed = 3.5f,
                        bodyRadius = 0.5f,
                        detectionRadius = 15,
                        visionAngle = 90,
                        attackRange = 2.0f,
                        patrolRadius = 10,
                        skills = new List<string> { "Attack" }
                    },
                    new MonsterData
                    {
                        templateId = 3,
                        name = "DogKnight",
                        hp = 300,
                        moveSpeed = 2.8f,
                        bodyRadius = 0.88f,
                        detectionRadius = 18,
                        visionAngle = 120,
                        attackRange = 2.2f,
                        patrolRadius = 6,
                        skills = new List<string> { "Attack" }
                    },
                    new MonsterData
                    {
                        templateId = 4,
                        name = "SpiderBoss",
                        hp = 1500,
                        moveSpeed = 3.2f,
                        bodyRadius = 1.66f,
                        detectionRadius = 25,
                        visionAngle = 140,
                        attackRange = 2.5f,
                        patrolRadius = 12,
                        skills = new List<string> { "Attack" }
                    }
                }
            };
        }
    }
}
