namespace Cool.Protocol
{
    /// <summary>
    /// 消息 ID 枚举 —— wire 格式的数值契约，**值一旦发布不可更改**
    /// （spec §3：只加不删）。分段约定：1-99 系统 / 100-199 位置 /
    /// 200-299 聊天 / 1000-1999 远程同步与房间 / 2000-2099 NPC。
    /// </summary>
    public enum MessageId : ushort
    {
        // ========== 系统 (1-99) ==========
        LoginRequest = 1,
        LoginResponse = 2,
        Heartbeat = 3,
        Kick = 4,
        DisconnectRequest = 5,
        ShutdownNotification = 6,

        /// <summary>P0 验收载体：回环往返</summary>
        EchoRequest = 7,
        EchoResponse = 8,

        /// <summary>时钟同步请求（v1 追加）</summary>
        ClockSyncRequest = 9,

        /// <summary>时钟同步响应（v1 追加）</summary>
        ClockSyncResponse = 10,

        // ========== 位置同步 (100-199) ==========
        PositionSyncRequest = 100,
        PositionSyncResponse = 101,

        /// <summary>世界快照广播（v1 追加；服务器 → 客户端，15Hz）</summary>
        WorldSnapshot = 110,

        // ========== 聊天 (200-299) ==========
        ChatMessage = 200,
        BroadcastMessage = 201,

        // ========== 远程同步与房间 (1000-1999) ==========
        RemotePlayerSync = 1000,
        PlayerEnterRoom = 1001,
        PlayerLeaveRoom = 1002,
        RoomSync = 1003,

        // ========== NPC (2000-2099) ==========
        NpcSpawn = 2000,
        NpcDespawn = 2001,
        NpcPosSync = 2002,
        NpcAnimSync = 2003
    }
}
