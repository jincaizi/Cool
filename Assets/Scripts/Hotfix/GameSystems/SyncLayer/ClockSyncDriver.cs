using System;
using Cool.Protocol;
using Cool.SyncCore;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 时钟同步采样调度（spec D3）：登录后突发 8 样本（每 200ms），此后每 10s 复校。
    /// 算法本体在共享 SyncCore（ClockSyncAlgorithm）。
    /// </summary>
    public sealed class ClockSyncDriver
    {
        private readonly ClockSyncAlgorithm _algorithm = new ClockSyncAlgorithm(8);
        private readonly Action<ClockSyncRequest> _sendRequest;
        private float _timer;
        private int _burstCount;
        private bool _burstDone;

        /// <summary>当前平滑偏移（ticks）</summary>
        public long SmoothedOffsetTicks => _algorithm.SmoothedOffsetTicks;

        public bool HasConverged => _algorithm.HasConverged;

        public ClockSyncDriver(Action<ClockSyncRequest> sendRequest)
        {
            _sendRequest = sendRequest;
        }

        public void StartBurst()
        {
            _burstCount = 0;
            _burstDone = false;
            _timer = 0f;
        }

        public void Tick(float deltaSeconds)
        {
            if (_burstDone && _timer > 0) { _timer -= deltaSeconds; return; }

            if (!_burstDone)
            {
                // 突发 8 样本
                _timer -= deltaSeconds;
                if (_timer <= 0f)
                {
                    _sendRequest(new ClockSyncRequest { ClientTimestamp = DateTime.UtcNow.Ticks });
                    _burstCount++;
                    _timer = 0.2f;
                    if (_burstCount >= 8)
                    {
                        _burstDone = true;
                        _timer = 10f; // 复校周期
                    }
                }
            }
            else
            {
                // 复校单样本
                _sendRequest(new ClockSyncRequest { ClientTimestamp = DateTime.UtcNow.Ticks });
                _timer = 10f;
            }
        }

        /// <summary>收到 ClockSyncResponse 时调用（返回 true 表示收敛达成）</summary>
        public bool OnResponse(ClockSyncResponse response)
        {
            _algorithm.AddSample(
                response.ClientTimestamp,
                response.ServerTimestamp,
                DateTime.UtcNow.Ticks);
            return _algorithm.HasConverged;
        }

        public long ToServerTime(long clientWallClockTicks)
            => _algorithm.ToServerTime(clientWallClockTicks);
    }
}
