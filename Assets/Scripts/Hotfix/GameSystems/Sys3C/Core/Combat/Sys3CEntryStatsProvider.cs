using System;
using Hotfix.GameSystems.SyncLayer;
using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// T12 甲-3：HUD 的 <see cref="IPlayerStatsProvider"/> 实现——把 <see cref="Sys3CEntry"/> 的 HP
    /// 暴露给 PlayerHudPanel。此前 `PlayerHudPanel.Bind` 全项目零调用者，血条永远停在预制体烘焙值
    /// （7500/10000），面试官看不到任何"我在掉血"。
    ///
    /// HP 变化由 Sys3CEntry.Update 的脏检查推动（<see cref="RaiseHpChanged"/>）——不在每个扣血点插桩，
    /// 于是所有写入源（服务端权威伤害 / 怪物伤害 / 复活回满）都自动同步。
    /// 玩家没有 MP / buff 系统：如实报 0/0 与空列表（不塞假数据）。
    /// </summary>
    public sealed class Sys3CEntryStatsProvider : IPlayerStatsProvider
    {
        private readonly Sys3CEntry _entry;

        public Sys3CEntryStatsProvider(Sys3CEntry entry)
        {
            _entry = entry;
            string username = NetworkModeService.LoadUsername();
            Name = string.IsNullOrWhiteSpace(username) ? NetworkModeService.DefaultUsername : username;
        }

        public string Name { get; }
        public int Level => 1;
        public Sprite Portrait => null;
        public float HPPercent => _entry.MaxHp > 0f ? _entry.CurrentHp / _entry.MaxHp : 0f;
        public int CurrentHP => Mathf.CeilToInt(_entry.CurrentHp);
        public int MaxHP => Mathf.CeilToInt(_entry.MaxHp);
        public float MPPercent => 0f;
        public int CurrentMP => 0;
        public int MaxMP => 0;
        public BuffInfo[] ActiveBuffs => Array.Empty<BuffInfo>();

        public event Action<float, int, int> OnHPChanged;

        // 无 MP / buff 系统：这两组事件保留接口形状、永不出值。用显式访问器而不是
        // field-like event，免得编译器按"事件从未被使用"（CS0067）报一条无谓的警告。
        private Action<float, int, int> _mpChanged;
        private Action<BuffInfo[]> _buffsChanged;

        public event Action<float, int, int> OnMPChanged
        {
            add => _mpChanged += value;
            remove => _mpChanged -= value;
        }

        public event Action<BuffInfo[]> OnBuffsChanged
        {
            add => _buffsChanged += value;
            remove => _buffsChanged -= value;
        }

        /// <summary>HP 脏检查命中时由 Sys3CEntry 调用（主线程）——值现读，面板拿到的一定是最新血。</summary>
        public void RaiseHpChanged() => OnHPChanged?.Invoke(HPPercent, CurrentHP, MaxHP);
    }
}
