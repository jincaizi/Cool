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
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
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
                        detectionRadius = 15,
                        visionAngle = 90,
                        attackRange = 2.0f,
                        patrolRadius = 10,
                        skills = new List<string> { "Attack" }
                    }
                }
            };
        }
    }
}
