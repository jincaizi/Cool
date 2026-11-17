using System;

namespace Hotfix.GameSystems.Sys3C.Character
{
    /// <summary>本人控制投影的纯状态合并：版本门 + 倍率钳制 + “变为锁”边沿。</summary>
    public sealed class LocalControlState
    {
        public uint Version { get; private set; }
        public float SpeedScale { get; private set; } = 1f;
        public bool ActionLock { get; private set; }

        /// <summary>应用服务端 ControlSummary；旧版本忽略。返回本次是否从“未锁”跨到“锁”。</summary>
        public bool Apply(float speedScale, bool actionLock, uint version)
        {
            if (version < Version) return false;
            bool becameLocked = actionLock && !ActionLock;
            Version = version;
            SpeedScale = float.IsFinite(speedScale) ? Math.Clamp(speedScale, 0f, 4f) : 0f;
            ActionLock = actionLock;
            return becameLocked;
        }
    }
}
