namespace Cool.Protocol
{
    /// <summary>
    /// 消息 ID 枚举 —— wire 格式的数值契约，**值一旦发布不可更改**
    /// （spec §3：只加不删）。分段约定：1-99 系统 / 100-199 位置 /
    /// 200-299 聊天 / 300-399 战斗 / 1000-1999 远程同步与房间 / 2000-2099 NPC。
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

        // ========== 战斗 (300-399) ==========
        /// <summary>施法请求（P4 追加；客户端 → 服务器）</summary>
        CastRequest = 300,

        /// <summary>施法广播（P4 追加；服务器 → caster 及视野观察者）</summary>
        CastBroadcast = 301,

        /// <summary>伤害结果（P4 追加；服务器 → caster + 受击者）</summary>
        DamageResult = 302,

        /// <summary>复活通知（服务器 → **被复活者本人**；D-N4）</summary>
        RespawnNotice = 303,

        /// <summary>取消施法（客户端 → 服务器；B3 旋转多段：取消/换命后丢弃未到期判定段）</summary>
        CancelCastRequest = 304,

        /// <summary>换装请求（客户端 → 服务器；属性剑气 v1）</summary>
        EquipWeaponRequest = 305,

        /// <summary>换装确认（服务器 → 客户端；含权威装备版本与武器摘要）</summary>
        EquipWeaponConfirm = 306,

        /// <summary>施法确认（服务器 → 施法者本人；按 ClientCastSeq 幂等重发）</summary>
        CastConfirm = 307,

        /// <summary>剑气权威发射描述（服务器 → 施法者与同场景观察者）</summary>
        ProjectileLaunchBroadcast = 308,

        /// <summary>剑气提前终止（服务器 → 已收到发射描述的会话）</summary>
        ProjectileStopBroadcast = 309,

        /// <summary>取消未提交施法（服务器 → 施法者与观察者；Plan 02）</summary>
        CastCancelBroadcast = 310,

        /// <summary>战斗投影信封（服务器 → 观察者；Plan 02 可靠完整投影）</summary>
        CombatStateUpdate = 311,

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
        NpcAnimSync = 2003,
        NpcActionEvent = 2004
    }
}
