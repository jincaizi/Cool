namespace KcpServer.Config
{
    /// <summary>
    /// AOI 大世界化服务器配置（spec §5-P3 带宽门禁载体；客户端同名的 Assets GameSettings
    /// 是显示配置 SO，与本类无冲突）。常量硬编码便于测试断言与部署一致性——
    /// 需线上可调时再迁配置表。
    /// </summary>
    public static class GlobalGameSettings
    {
        // —— AOI 参数（决策 D1-D3；与 AoiGrid/AoiBandScheduler 常量一致——改这里须同步）——
        public const float CellSize = 40f;
        public const float SightRadius = 80f;
        public const float LeaveRadius = 90f;
        public const float MidBandStart = 30f;
        public const float FarBandStart = 60f;

        // —— 分级频率（Hz；基准 tick 15Hz）——
        public const float NearHz = 15f;
        public const float MidHz = 8f;
        public const float FarHz = 4f;

        // —— 带宽门禁（B/s）：T5 实测校准——
        // 估算基准：单实体全字段快照 ≈33B（实测：49B 单实体包 − 16B 空包；60B 为规划裕量）；
        // 门禁 = 100 实体全近带最坏折算（100×15Hz×33B≈50KB/s）+ ~2× 余量
        // （静止实体 4Hz 保活使常态远低于上界；余量吸收后续字段增量的 60B 规划口径）
        public const float BandwidthBudgetBytesPerSec = 100_000f; // ≈ 100 KB/s
    }
}
