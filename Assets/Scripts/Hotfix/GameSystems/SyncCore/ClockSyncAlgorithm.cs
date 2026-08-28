using System.Collections.Generic;

namespace Cool.SyncCore
{
    /// <summary>
    /// 时钟同步算法（spec D3：min-filter + 一阶低通）——纯 C#，零引擎依赖，双端共享。
    /// 语义：OffsetTicks = 服务器时钟 − 客户端墙钟（正值 = 服务器更快）。
    /// 样本模型（t 均为 ticks）：
    ///   t1 = 客户端发送时刻（墙钟）
    ///   t2 = 服务器收到并回显时刻（服务器时钟）
    ///   t3 = 客户端收到响应时刻（墙钟）
    ///   offset = (t2 − t1 + t2 − t3) / 2
    ///   rtt = t3 − t1
    /// 非线程安全：单实例供单消费者（每客户端一个 ClockSyncDriver）；跨线程读需外部同步。
    /// </summary>
    public sealed class ClockSyncAlgorithm
    {
        private readonly int _windowSize;
        private readonly List<Sample> _samples = new List<Sample>();

        /// <summary>当前最优偏移（ticks；min-RTT 样本的偏移，可负）</summary>
        public long OffsetTicks { get; private set; }

        /// <summary>平滑后偏移（一阶低通，alpha=0.3）</summary>
        public long SmoothedOffsetTicks { get; private set; }

        /// <summary>是否已收敛（窗口填满）</summary>
        public bool HasConverged => _samples.Count >= _windowSize;

        public ClockSyncAlgorithm(int windowSize = 8)
        {
            _windowSize = windowSize;
        }

        public void AddSample(long clientSendTicks, long serverEchoTicks, long clientReceiveTicks)
        {
            long rtt = clientReceiveTicks - clientSendTicks;
            if (rtt <= 0) return; // 时钟回跳/乱序样本：拒绝（min-filter 会偏爱最小 RTT）

            long offset = serverEchoTicks - clientSendTicks - rtt / 2;

            _samples.Add(new Sample { Rtt = rtt, Offset = offset });

            // 滑窗溢出（先剔除再扫描：窗口外样本不参与 min-filter）
            if (_samples.Count > _windowSize)
                _samples.RemoveAt(0);

            // min-filter：滑窗内取 RTT 最小样本的偏移
            Sample best = _samples[0];
            foreach (var s in _samples)
            {
                if (s.Rtt < best.Rtt) best = s;
            }
            OffsetTicks = best.Offset;

            // 一阶低通（初值直接采纳）
            SmoothedOffsetTicks = _samples.Count == 1
                ? best.Offset
                : SmoothedOffsetTicks + (best.Offset - SmoothedOffsetTicks) * 3 / 10;
        }

        /// <summary>客户端墙钟 → 服务器时钟（ticks）</summary>
        public long ToServerTime(long clientWallClockTicks)
        {
            return clientWallClockTicks + OffsetTicks;
        }

        private struct Sample
        {
            public long Rtt;
            public long Offset;
        }
    }
}
