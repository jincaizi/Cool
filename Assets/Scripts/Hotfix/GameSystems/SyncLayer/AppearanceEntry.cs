using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 外观目录的一条（Inspector 可编辑）。**不能换成 `Dictionary`**：Unity 不序列化字典，
    /// 场景里配的值会静默丢光（与 `ClientNetworkHost.NpcPrefabEntry` 同一条教训）。
    /// </summary>
    [Serializable]
    public class AppearanceEntry
    {
        /// <summary>外观 id（0 = 默认外观：真人玩家在协议带上外观 id 之前走它）</summary>
        public int Id;

        /// <summary>近档（Full）：完整模型预制体。留空 ⇒ 该条目退化为**占位胶囊**
        /// （registry 在 `fullPrefab == null` 时早退到胶囊占位并直接返回——`Proxy` 预制体
        /// 在这条早退路径上不生效；"纯代理条目该不该用 `Proxy`"是未决的语义问题，见 backlog）</summary>
        public GameObject? Full;

        /// <summary>远档（Proxy）代理预制体。**留空 = 运行时生成胶囊**——将来美术产出
        /// billboard/impostor 时填进来即可，换档机制一行都不用改</summary>
        public GameObject? Proxy;
    }

    /// <summary>外观解析（纯函数，便于单测）：实体 → 预制体。</summary>
    public static class AppearanceResolver
    {
        /// <summary>默认外观 id（真人）</summary>
        public const int DefaultId = 0;

        /// <summary>
        /// bot 的外观条脚下标（`EntityKind.PlayerBot`）。
        ///
        /// **乘法哈希后取模，不是 `playerId % count`**：bot id 是连续分配的，取模会让
        /// 男女规律交替（0,1,0,1…），一眼假。哈希保证**确定性**——同一个 bot 每次进视野
        /// 长得一样（随机选会换脸）。
        /// </summary>
        public static int BotIndex(long playerId, int entryCount)
        {
            if (entryCount <= 1) return 0;
            ulong mixed = (ulong)playerId * 2654435761UL;   // Knuth 乘法混合
            return (int)((mixed >> 16) % (ulong)entryCount);
        }

        /// <summary>
        /// 按 id 查表（真人走这条：`Id = DefaultId`）。未接线 / 未命中 ⇒ null，调用方走胶囊降级。
        /// </summary>
        public static AppearanceEntry? FindId(IReadOnlyList<AppearanceEntry>? entries, int id)
        {
            if (entries == null) return null;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].Id == id) return entries[i];
            return null;
        }

        /// <summary>取第 index 条（bot 走这条：索引由 <see cref="BotIndex"/> 算出；越界返回 null）</summary>
        public static AppearanceEntry? EntryAt(IReadOnlyList<AppearanceEntry>? entries, int index)
        {
            if (entries == null || index < 0 || index >= entries.Count) return null;
            return entries[index];
        }
    }
}
