using System.Collections;
using System.Threading.Tasks;
using Cool.Protocol;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>P1 验收前置：adapter 连服务器 → 时钟同步握手往返</summary>
    public class AdapterConnectTests
    {
        private sealed class SmokeResult
        {
            public bool Success;
            public string FailureReason = string.Empty;
            public ClockSyncResponse? Response;
        }

        [UnityTest]
        public IEnumerator Adapter_Connects_And_ClockSync_Roundtrips()
        {
            var runTask = RunAsync();
            const int maxFrames = 1800; // 30s 挂钟
            int frames = 0;
            while (!runTask.IsCompleted && frames < maxFrames)
            {
                frames++;
                yield return null;
            }

            Assert.IsTrue(runTask.IsCompleted, "adapter test timed out");
            var result = runTask.Result;
            Assert.IsTrue(result.Success, $"adapter failed: {result.FailureReason}");
            Assert.IsNotNull(result.Response, "no ClockSyncResponse captured");
            Assert.Greater(result.Response.ServerTimestamp, 0,
                "server timestamp semantics violated (spec §3)");
        }

        [UnityTest]
        public IEnumerator Connect_To_Invalid_Host_Fails_With_Error()
        {
            // 实测（console 探针）：本 KCP 栈客户端无握手等待——UDP connect 到未监听端口
            // 立即成功（connected=true、无 Error 事件；ICMP 不可达在驱动循环内被吞掉），
            // 故 closed-port 负路径无法按字面断言；改用非法 host（IPAddress.Parse 抛
            // FormatException）确定性快速失败，覆盖同一适配器失败路径：Error 事件 + 返回 false。
            var runTask = Task.Run(async () =>
            {
                using var adapter = new KcpNetClientAdapter();
                bool errorRaised = false;
                adapter.Error += _ => errorRaised = true;
                var connected = await adapter.ConnectAsync("not-an-ip", 8000);
                return (connected, errorRaised);
            });

            int frames = 0;
            while (!runTask.IsCompleted && frames < 1800) { frames++; yield return null; }

            Assert.IsTrue(runTask.IsCompleted, "connect-fail test timed out");
            var (connected, errorRaised) = runTask.Result;
            Assert.IsFalse(connected, "connect to invalid host must fail");
            Assert.IsTrue(errorRaised, "Error event must fire on connect failure");
        }

        private static async Task<SmokeResult> RunAsync()
        {
            var result = new SmokeResult();
            using var adapter = new KcpNetClientAdapter();
            var tcs = new TaskCompletionSource<ClockSyncResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            adapter.MessageReceived += (id, msg) =>
            {
                if (id == MessageId.ClockSyncResponse && msg is ClockSyncResponse response)
                    tcs.TrySetResult(response);
            };

            try
            {
                var connected = await adapter.ConnectAsync("127.0.0.1", 8000);
                if (!connected) { result.FailureReason = "connect failed"; return result; }

                adapter.Send(MessageId.ClockSyncRequest, new ClockSyncRequest
                {
                    ClientTimestamp = System.DateTime.UtcNow.Ticks
                });

                var done = await Task.WhenAny(tcs.Task, Task.Delay(10000));
                if (done == tcs.Task && tcs.Task.Status == TaskStatus.RanToCompletion)
                {
                    result.Response = tcs.Task.Result;
                    result.Success = true;
                }
                else
                {
                    result.FailureReason = "no ClockSyncResponse within 10s";
                }
            }
            catch (System.Exception ex)
            {
                result.FailureReason = ex.Message;
            }

            return result;
        }
    }
}
