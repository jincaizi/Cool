using System;

namespace KcpServer
{
    /// <summary>
    /// 玩家权威状态
    /// </summary>
    public sealed class PlayerState
    {
        public long PlayerId { get; }
        public string PlayerName { get; set; } = string.Empty;
        public string ZoneId { get; set; } = string.Empty;
        public Vector3 Position { get; internal set; }
        public float Rotation { get; internal set; }
        public float Speed { get; internal set; }
        public long LastUpdateTimestamp { get; internal set; }
        public uint LastAcknowledgedSequence { get; internal set; }
        public uint LastReceivedSequence { get; internal set; }
        public Vector3 LastValidPosition { get; internal set; }
        public long LastValidTimestamp { get; internal set; }

        /// <summary>脏标记：位置等同步字段在最近一次快照后发生变化（主循环独占访问）</summary>
        public bool IsDirty { get; internal set; }

        /// <summary>上次进入快照的 tick（beacon 判定用）</summary>
        public uint LastSnapshotTick { get; internal set; }
        public bool IsLoggedIn { get; internal set; }
        public DateTime LastActiveTime { get; internal set; }

        public PlayerState(long playerId)
        {
            PlayerId = playerId;
            Position = Vector3.zero;
            LastValidPosition = Vector3.zero;
            LastActiveTime = DateTime.UtcNow;
        }

        public void UpdatePosition(Vector3 position, float rotation, float speed, long timestamp, uint sequence)
        {
            Position = position;
            Rotation = rotation;
            Speed = speed;
            LastUpdateTimestamp = timestamp;
            LastReceivedSequence = sequence;
            LastActiveTime = DateTime.UtcNow;
            IsDirty = true;
        }

        public void AcknowledgeSequence(uint sequence)
        {
            LastAcknowledgedSequence = sequence;
        }

        public void RecordValidPosition(Vector3 position, long timestamp)
        {
            LastValidPosition = position;
            LastValidTimestamp = timestamp;
        }
    }
}
