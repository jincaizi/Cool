using Hotfix.GameSystems.Monster;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    /// <summary>
    /// P6 ④：单机怪物数值一致性断言——锁死"客户端资产与服务端表同值"，
    /// 防止再次出现"三源不一致"漂移（Slime 曾 600HP/0伤害 vs 服务端 100HP/10伤害）。
    /// </summary>
    public class MonsterConfigNumbersTests
    {
        private const string SlimePath = "Assets/Monstor/SlimeConfig.asset";
        private const string TurtlePath = "Assets/Monstor/TurtleShellConfig.asset";

        private static MonsterConfig Load(string path)
        {
            var config = AssetDatabase.LoadAssetAtPath<MonsterConfig>(path);
            Assert.IsNotNull(config, "怪物配置资产缺失: " + path);
            return config;
        }

        [Test]
        public void test_slime_damage_is_nonzero()
        {
            // Arrange
            var config = Load(SlimePath);

            // Assert（服务端 CombatRules 50001 = 10）
            Assert.Greater(config.AttackDamage.BaseDamage, 0f,
                "单机史莱姆伤害必须大于 0（曾为 0，导致单机毫无威胁）");
            Assert.AreEqual(10f, config.AttackDamage.BaseDamage, 0.001f,
                "单机史莱姆伤害须与服务端 CombatRules 50001 一致");
        }

        [Test]
        public void test_slime_hp_matches_server()
        {
            // Arrange
            var config = Load(SlimePath);

            // Assert（服务端 monster_config.json = 100）
            Assert.AreEqual(100f, config.MaxHP, 0.001f,
                "单机史莱姆血量须与服务端一致（曾为 600）");
        }

        [Test]
        public void test_turtle_damage_is_nonzero_and_hp_rescaled()
        {
            // Arrange
            var config = Load(TurtlePath);

            // Assert（服务端无此模板，按同量级定值 250/8）
            Assert.AreEqual(8f, config.AttackDamage.BaseDamage, 0.001f,
                "龟壳伤害须大于 0（曾为 2 且 _baseDamage 路径不同）");
            Assert.AreEqual(250f, config.MaxHP, 0.001f,
                "龟壳血量须压缩到同量级（曾为 1500），保留厚而钝定位");
        }

        [Test]
        public void test_both_monsters_have_knockback()
        {
            // Arrange
            var slime = Load(SlimePath);
            var turtle = Load(TurtlePath);

            // Assert（击退为 0 时 EnterHit 只进最轻的 Hit 态，验收要求可见击退）
            Assert.Greater(slime.AttackDamage.KnockbackForce, 0f, "史莱姆攻击须有击退");
            Assert.Greater(turtle.AttackDamage.KnockbackForce, 0f, "龟壳攻击须有击退");
        }
    }
}
