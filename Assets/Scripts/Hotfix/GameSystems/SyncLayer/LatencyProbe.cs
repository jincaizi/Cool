using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Cool.Protocol;

namespace Hotfix.GameSystems.SyncLayer
{
    /// <summary>
    /// 应用层 RTT 探针：每 1 秒发一条 Heartbeat（服务端原样回显），按 Sequence 匹配回包并做 EMA。
    ///
    /// 线程模型：<see cref="Tick"/> 只在主线程调用；<see cref="OnResponse"/> 可在任意线程调用，
    /// 它在回调线程读一次 <see cref="Stopwatch"/> 时间戳后把 (回包, 到达时刻) 入队。
    /// 回包时间**不能等主线程下一帧再读**——那会把 0~1 帧的等待算进 RTT，60fps 下凭空多出约 16ms。
    /// 序列匹配、EMA、超时判定仍在主线程完成。
    ///
    /// 时钟：默认 Stopwatch（单调、线程安全、不受 Time.timeScale 影响——打击停顿会把 timeScale 设为 0）。
    /// 测试注入假时钟。
    /// 依赖传输层把 Heartbeat 上抛到 INetworkClient.MessageReceived；KCP 客户端在内部消费 Heartbeat，接 KCP 时 RttMs 恒为 null。
    /// </summary>
    internal sealed class LatencyProbe
    {
        internal const double ProbeIntervalSeconds = 1.0;
        internal const double TimeoutSeconds = 3.0;

        private readonly Action<Heartbeat> _send;
        private readonly Func<double> _clock;
        private readonly ConcurrentQueue<(Heartbeat heartbeat, double receivedAt)> _responses =
            new ConcurrentQueue<(Heartbeat heartbeat, double receivedAt)>();

        private uint _sequence;
        private uint _pendingSequence;
        private bool _hasPending;
        private double _pendingSendTime;
        private double _lastResponseTime;
        private double _lastSendTime;
        private bool _hasSample;
        private double _smoothedMs;

        internal LatencyProbe(Action<Heartbeat> send, Func<double>? clock = null)
        {
            _send = send;
            _clock = clock ?? DefaultClock;
            _lastSendTime = -ProbeIntervalSeconds;   // 首个 Tick 立即发包
        }

        private static double DefaultClock()
            => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        /// <summary>平滑后的 RTT（毫秒）；无样本或超时后为 null。</summary>
        internal int? RttMs => _hasSample ? (int)Math.Round(_smoothedMs) : (int?)null;

        /// <summary>网络回调线程调用：读一次到达时刻并入队，其余留主线程。</summary>
        internal void OnResponse(Heartbeat heartbeat)
            => _responses.Enqueue((heartbeat, _clock()));

        /// <summary>主线程每帧调用（仅 InWorld）：消费回包、判超时、按节拍发包。</summary>
        internal void Tick()
        {
            double now = _clock();

            while (_responses.TryDequeue(out var response))
                Accept(response.heartbeat, response.receivedAt);

            // 1 秒节拍会不断覆盖“待回包”，所以超时按“最后一次成功回包”计时。
            if (_hasSample && now - _lastResponseTime > TimeoutSeconds)
            {
                _hasSample = false;
                _smoothedMs = 0.0;
            }

            if (now - _lastSendTime < ProbeIntervalSeconds) return;

            _lastSendTime = now;
            _sequence++;
            _pendingSequence = _sequence;
            _hasPending = true;
            _pendingSendTime = now;
            _send(new Heartbeat
            {
                ClientTimestamp = DateTime.UtcNow.Ticks,   // 协议字段既有语义，仅作诊断
                Sequence = _sequence
            });
        }

        private void Accept(Heartbeat response, double receivedAt)
        {
            if (!_hasPending || response.Sequence != _pendingSequence) return;

            _hasPending = false;
            double rttMs = (receivedAt - _pendingSendTime) * 1000.0;
            if (rttMs < 0.0) return;   // 时钟回跳：丢弃

            _smoothedMs = _hasSample ? _smoothedMs * 0.7 + rttMs * 0.3 : rttMs;
            _hasSample = true;
            _lastResponseTime = receivedAt;
        }
    }
}
