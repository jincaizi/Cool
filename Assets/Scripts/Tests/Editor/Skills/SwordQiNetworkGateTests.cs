using System.Reflection;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Data;
using Hotfix.GameSystems.Skills.Events;
using Hotfix.GameSystems.Skills.Runtime;
using NUnit.Framework;
using UnityEditor;

namespace Hotfix.GameSystems.Tests.Skills
{
    public class SwordQiNetworkGateTests
    {
        [Test]
        public void Network_Gate_Suppresses_Local_SwordQi_Spawn()
        {
            var heavy = AssetDatabase.LoadAssetAtPath<InstantSkillData>(
                "Assets/PreRes/SkillsCfg/BA_Heavy.asset");
            Assert.IsNotNull(heavy, "BA_Heavy.asset 未找到");
            Assert.IsNotNull(heavy!.SwordQi, "BA_Heavy 必须配置 SwordQi");

            bool spawned = false;
            void OnSpawned(SwordQiSpawnedEvent _) => spawned = true;
            EventBus.Subscribe<SwordQiSpawnedEvent>(OnSpawned);
            NetworkSwordQiGate.SuppressLocalSpawn = true;
            try
            {
                // owner 故意传 null：gate 必须在触碰 owner.Stats 之前提前返回。
                var executor = new SkillExecutor(null, heavy);
                var method = typeof(SkillExecutor).GetMethod("OnSwordQiFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(method, "SkillExecutor.OnSwordQiFrame 反射目标失效");
                method!.Invoke(executor, new object[] { 0 });
            }
            finally
            {
                NetworkSwordQiGate.SuppressLocalSpawn = false;
                EventBus.Unsubscribe<SwordQiSpawnedEvent>(OnSpawned);
            }

            Assert.IsFalse(spawned, "联机 gate 开启时本地不得生成剑气/发 SwordQiSpawnedEvent");
        }
    }
}
