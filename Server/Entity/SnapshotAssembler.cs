using System.Collections.Generic;
using Cool.Protocol;

namespace KcpServer
{
    /// <summary>
    /// 快照组装器 —— 每 tick 由 TickLoop 调用：
    /// 脏实体全字段快照；静止实体按 beacon 间隔产出位置 beacon（spec D5）。
    /// </summary>
    public sealed class SnapshotAssembler
    {
        /// <summary>组装脏实体快照（清脏）；全静时返回空列表</summary>
        public List<EntitySnapshot> BuildDirtySnapshot(EntityStore store, uint tick)
        {
            var result = new List<EntitySnapshot>();
            foreach (var state in store.DrainDirty())
            {
                result.Add(ToSnapshot(state, EntityFieldMask.Position | EntityFieldMask.Rotation | EntityFieldMask.Speed, tick));
                state.LastSnapshotTick = tick;
            }
            return result;
        }

        /// <summary>组装静止实体 beacon（仅位置字段；距上次快照 ≥ beaconInterval 才产出）</summary>
        public List<EntitySnapshot> BuildBeaconSnapshot(EntityStore store, uint tick, uint beaconInterval)
        {
            var result = new List<EntitySnapshot>();
            foreach (var state in store.All)
            {
                if (state.IsDirty) continue; // 脏实体由 BuildDirtySnapshot 负责
                if (tick - state.LastSnapshotTick < beaconInterval) continue;

                result.Add(ToSnapshot(state, EntityFieldMask.Position, tick));
                state.LastSnapshotTick = tick;
            }
            return result;
        }

        private static EntitySnapshot ToSnapshot(PlayerState state, EntityFieldMask mask, uint tick)
        {
            var snap = new EntitySnapshot
            {
                PlayerId = state.PlayerId,
                FieldMask = (ushort)mask,
                ActionState = 0, // P1 恒 0；P2 客户端上报动作状态后填充
                Tick = tick
            };
            if ((mask & EntityFieldMask.Position) != 0)
            {
                snap.X = state.Position.X;
                snap.Y = state.Position.Y;
                snap.Z = state.Position.Z;
            }
            if ((mask & EntityFieldMask.Rotation) != 0)
                snap.Rotation = state.Rotation;
            if ((mask & EntityFieldMask.Speed) != 0)
                snap.Speed = state.Speed;
            return snap;
        }
    }
}
