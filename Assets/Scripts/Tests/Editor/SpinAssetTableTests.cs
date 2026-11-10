using Cool.SyncCore;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    /// <summary>
    /// B3：旋转（20002）的服务端重复判定时间参数必须与客户端真实资产一致。
    /// 首判 = 起手剪辑时长 + TickInterval（SkillStateMachine.UpdateSpinning 同源）。
    /// </summary>
    public class SpinAssetTableTests
    {
        private const string Path = "Assets/PreRes/SkillsCfg/Spin_SkillR.asset";

        [Test]
        public void test_spin_asset_matches_server_repeat_table()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SpinSkillData>(Path);
            Assert.IsNotNull(asset, $"资产缺失：{Path}");
            Assert.IsNotNull(asset.CastClip, "Spin_SkillR 必须有起手剪辑（首判时间依赖它）");

            Assert.IsTrue(CombatRules.TryGet(20002, out var entry));
            Assert.IsTrue(entry.Repeat.HasValue, "20002 必须在服务端表里配置重复判定");
            var repeat = entry.Repeat.Value;

            Assert.AreEqual(asset.CastClip.length + asset.TickInterval,
                repeat.FirstDelaySeconds, 0.001f, "首判 = 起手剪辑时长 + TickInterval");
            Assert.AreEqual(asset.TickInterval, repeat.IntervalSeconds, 0.001f);
            Assert.AreEqual(asset.MaxDuration, repeat.DurationSeconds, 0.001f);
            Assert.AreEqual(asset.MaxHitsPerTarget, repeat.MaxHitsPerTarget);
            Assert.AreEqual(asset.SkillId, 20002);
            Assert.AreEqual(asset.Damage.BaseDamage, entry.BaseDamage, 0.001f,
                "每段伤害口径：与单机资产一致（2026-11-10 评审决定）");
        }
    }
}
