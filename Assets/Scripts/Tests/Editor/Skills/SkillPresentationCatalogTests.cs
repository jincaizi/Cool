using System.Linq;
using Hotfix.GameSystems.Skills.Data;
using NUnit.Framework;
using UnityEditor;

namespace GameSys.EditorTests
{
    public class SkillPresentationCatalogTests
    {
        private static readonly int[] SyncedPlayerSkills = { 10001, 10002, 20001, 20002, 20003, 20004, 30001 };

        [Test]
        public void Catalog_Contains_All_Seven_Synced_Player_Skills()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SkillPresentationCatalog>(
                "Assets/PreRes/SkillsCfg/SkillPresentationCatalog.asset");
            Assert.IsNotNull(catalog, "SkillPresentationCatalog.asset 未创建");

            foreach (var id in SyncedPlayerSkills)
            {
                Assert.IsTrue(catalog!.TryGet(id, out var data), "目录缺少技能 " + id);
                Assert.IsFalse(string.IsNullOrEmpty(data!.AnimatorTrigger), "技能 " + id + " 缺 AnimatorTrigger");
            }
            Assert.AreEqual(SyncedPlayerSkills.Length, catalog!.Skills.Count,
                "目录只应收录 7 个同步玩家技能（怪物技能不在客户端目录）");
        }
    }
}
