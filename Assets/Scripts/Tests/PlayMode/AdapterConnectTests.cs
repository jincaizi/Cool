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
