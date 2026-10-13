using System.Collections.Generic;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>外观目录（design §4 外观解析顺序）：查表 + bot 的确定性哈希选型。</summary>
    public class AppearanceCatalogTests
    {
        private static List<AppearanceEntry> TwoEntries()
        {
            var male = new GameObject("MaleDouble");
            var female = new GameObject("FemaleDouble");
            return new List<AppearanceEntry>
            {
                new AppearanceEntry { Id = 0, Full = male },
                new AppearanceEntry { Id = 1, Full = female },
            };
        }

        [Test]
        public void EntryAt_Returns_The_Matching_Entry()
        {
            var entries = TwoEntries();
            try
            {
                Assert.AreEqual("MaleDouble", AppearanceResolver.EntryAt(entries, 0)!.Full!.name);
                Assert.AreEqual("FemaleDouble", AppearanceResolver.EntryAt(entries, 1)!.Full!.name);
            }
            finally { foreach (var e in entries) Object.DestroyImmediate(e.Full); }
        }

        [Test]
        public void EntryAt_Miss_Returns_Null_For_The_Caller_To_Degrade()
        {
            var entries = TwoEntries();
            try
            {
                Assert.IsNull(AppearanceResolver.EntryAt(entries, 99), "越界必须返回 null（调用方走胶囊降级）");
                Assert.IsNull(AppearanceResolver.EntryAt(entries, -1), "负数同样安全");
                Assert.IsNull(AppearanceResolver.EntryAt(null, 0), "未接线同样返回 null");
            }
            finally { foreach (var e in entries) Object.DestroyImmediate(e.Full); }
        }

        [Test]
        public void FindId_Locates_The_Default_Player_Appearance()
        {
            var entries = TwoEntries();
            try
            {
                Assert.AreEqual("MaleDouble", AppearanceResolver.FindId(entries, 0)!.Full!.name);
                Assert.IsNull(AppearanceResolver.FindId(entries, 99), "没有该 Id 的条目 ⇒ null（调用方降级）");
                Assert.IsNull(AppearanceResolver.FindId(null, 0), "未接线 ⇒ null");
            }
            finally { foreach (var e in entries) Object.DestroyImmediate(e.Full); }
        }

        [Test]
        public void Bot_Appearance_Is_Deterministic()
        {
            // 同一个 bot 每次进视野必须长得一样——随机选会"换脸"
            for (long id = 1; id <= 50; id++)
                Assert.AreEqual(AppearanceResolver.BotIndex(id, 2), AppearanceResolver.BotIndex(id, 2),
                    $"bot {id} 的外观索引必须稳定");
        }

        [Test]
        public void Bot_Appearance_Spreads_Across_Entries()
        {
            // 连续 id 必须打散：直接取模会得到 0,1,0,1…（一眼假），乘法哈希不会
            var seen = new HashSet<int>();
            for (long id = 1; id <= 20; id++) seen.Add(AppearanceResolver.BotIndex(id, 2));
            Assert.AreEqual(2, seen.Count, "连续 id 应覆盖全部条目");

            int firstHalf = 0;
            for (long id = 1; id <= 10; id++) firstHalf += AppearanceResolver.BotIndex(id, 2);
            Assert.That(firstHalf, Is.InRange(3, 7), "前 10 个 id 不该全部落在同一条目");
        }

        [Test]
        public void Bot_Index_Handles_Degenerate_Catalogs()
        {
            Assert.AreEqual(0, AppearanceResolver.BotIndex(7, 0), "空目录不崩（返回 0，调用方走降级）");
            Assert.AreEqual(0, AppearanceResolver.BotIndex(7, 1), "只有一条 ⇒ 恒 0（统一外观）");
        }
    }
}
