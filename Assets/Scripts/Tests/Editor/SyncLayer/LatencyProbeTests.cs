using System.Collections.Generic;
using Cool.Protocol;
using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;

namespace Hotfix.GameSystems.Tests.SyncLayer
{
    /// <summary>
    /// 应用层 RTT 探针。时钟由测试注入：所有时间断言都是确定的毫秒数，
    /// 不依赖真实时间（否则 1 秒节拍/3 秒超时测试会又慢又抖）。
    /// </summary>
    public class LatencyProbeTests
    {
        private double _now;
        private readonly List<Heartbeat> _sent = new List<Heartbeat>();
        private LatencyProbe _probe = null!;

        [SetUp]
        public void SetUp()
        {
            _now = 0.0;
            _sent.Clear();
            _probe = new LatencyProbe(hb => _sent.Add(hb), () => _now);
        }

        [Test]
        public void test_latency_probe_first_tick_sends_immediately()
        {
            _probe.Tick();

            Assert.AreEqual(1, _sent.Count, "进世界后的首个 Tick 必须立即发第一条 Heartbeat");
        }

        [Test]
        public void test_latency_probe_sends_at_one_second_cadence()
        {
            _probe.Tick();          // t=0，发第 1 条
            _now = 0.9;
            _probe.Tick();

            Assert.AreEqual(1, _sent.Count, "1 秒节拍内不得重复发包");

            _now = 1.0;
            _probe.Tick();

            Assert.AreEqual(2, _sent.Count, "满 1 秒必须发下一条");
        }

        [Test]
        public void test_latency_probe_matching_response_reports_round_trip()
        {
            _probe.Tick();                                  // t=0，seq=1
            _now = 0.040;                                   // 回包 40ms 后到达
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();

            Assert.AreEqual(40, _probe.RttMs);
        }

        [Test]
        public void test_latency_probe_ignores_stale_sequence()
        {
            _probe.Tick();                                  // seq=1
            _now = 1.0;
            _probe.Tick();                                  // seq=2，覆盖待回包
            _now = 1.1;
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();

            Assert.IsNull(_probe.RttMs, "旧 Sequence 的回包不能产生读数");
        }

        [Test]
        public void test_latency_probe_smooths_samples_with_ema()
        {
            _probe.Tick();
            _now = 0.100;
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();
            Assert.AreEqual(100, _probe.RttMs);

            _now = 1.0;
            _probe.Tick();                                  // 发 seq=2
            _now = 1.200;
            _probe.OnResponse(new Heartbeat { Sequence = _sent[1].Sequence });
            _probe.Tick();

            Assert.AreEqual(130, _probe.RttMs, "EMA: 100*0.7 + 200*0.3 = 130");
        }

        [Test]
        public void test_latency_probe_clears_reading_after_three_seconds_without_response()
        {
            _probe.Tick();
            _now = 0.040;
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();
            Assert.AreEqual(40, _probe.RttMs);

            _now = 3.041;                                   // 距最后一次成功回包 3.001s
            _probe.Tick();

            Assert.IsNull(_probe.RttMs, "超过 3 秒无匹配回包必须显示 --");
        }

        [Test]
        public void test_latency_probe_accepts_zero_round_trip()
        {
            _probe.Tick();
            _probe.OnResponse(new Heartbeat { Sequence = _sent[0].Sequence });
            _probe.Tick();

            Assert.AreEqual(0, _probe.RttMs, "同帧回包是合法的 0ms，不是 null");
        }
    }
}
