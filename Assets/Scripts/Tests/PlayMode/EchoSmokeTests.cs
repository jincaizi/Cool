using System;
using System.Collections;
using System.Net;
using System.Threading.Tasks;
using Cool.Protocol;
using KcpNet;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// P0 核心验收：Unity 客户端 ↔ Server3C 的 KCP over UDP 真连接 echo 往返
    /// 前置：服务器进程已在 127.0.0.1:8000 运行
    /// </summary>
    public class EchoSmokeTests
    {
        private sealed class SmokeResult
        {
            public bool Success;
            public string FailureReason = string.Empty;
            public EchoResponse? Response;
            public long SentClientTimestamp;
        }

        [UnityTest]
        public IEnumerator EchoRoundtrip_ThroughRealKcpSocket()
        {
            var runTask = RunEchoAsync();

            // ~30 秒墙钟等待上限（真实 socket IO 场景足够宽容；测试环境下帧率不受控，帧数上限不可靠）
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!runTask.IsCompleted && DateTime.UtcNow < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(runTask.IsCompleted, "echo roundtrip timed out after 30s");
            var result = runTask.Result;

            Assert.IsTrue(result.Success,
                $"echo failed: {result.FailureReason}");
            Assert.IsNotNull(result.Response, "no EchoResponse captured");

            Assert.AreEqual(result.SentClientTimestamp, result.Response!.ClientTimestamp,
                "server must echo ClientTimestamp verbatim");
            Assert.Greater(result.Response.ServerTimestamp, 0,
                "server timestamp semantics violated (spec §3)");
        }

        private static async Task<SmokeResult> RunEchoAsync()
        {
            var result = new SmokeResult();
            KcpClient? client = null;
            var completionSource = new TaskCompletionSource<EchoResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var options = new KcpOptions
                {
                    SendWindowSize = 32,
                    ReceiveWindowSize = 32,
                    NoDelay = true,
                    Interval = 10,
                    Mtu = 1400,
                    ConnectionTimeout = 30,
                    HeartbeatInterval = 5,
                    HeartbeatTimeout = 30,
                    MaxReconnectAttempts = 3,
                    BaseReconnectDelay = 1,
                    MaxReconnectDelay = 30,
                    UseExponentialBackoff = true,
                    SendBufferSize = 65536,
                    ReceiveBufferSize = 65536
                };
                // ConnectAsync 内部自启动接收循环（KcpClient.cs:59），只需订阅事件
                client = new KcpClient(
                    DateTime.UtcNow.Ticks,                 // sessionId（示例约定：临时值即可）
                    new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8000),
                    new KcpClientTransport(options, ConsoleLogger.Instance),
                    options,
                    ConsoleLogger.Instance,
                    DirectMessageExecutor.Instance);

                EventHandler<IMessage>? onReceived = null;
                onReceived = (_, message) =>
                {
                    if (message is EchoResponse echoResponse)
                    {
                        client.MessageReceived -= onReceived;
                        completionSource.TrySetResult(echoResponse);
                    }
                };
                client.MessageReceived += onReceived;

                await client.ConnectAsync("127.0.0.1", 8000);

                result.SentClientTimestamp = DateTime.UtcNow.Ticks;
                await client.SendAsync(new EchoRequest { ClientTimestamp = result.SentClientTimestamp }, MessageFlags.Reliable);

                result.Response = await AwaitWithTimeout(completionSource.Task, TimeSpan.FromSeconds(15));
                result.Success = result.Response != null;
                if (!result.Success) result.FailureReason = "no EchoResponse within 15s";
            }
            catch (Exception ex)
            {
                result.FailureReason = ex.Message;
            }
            finally
            {
                try
                {
                    if (client != null && client.IsConnected)
                        await client.CloseAsync("smoke-test-done");
                }
                catch { /* teardown 尽力而为 */ }
                client?.Dispose();
            }

            return result;
        }

        private static async Task<EchoResponse?> AwaitWithTimeout(Task<EchoResponse> task, TimeSpan timeout)
        {
            var finished = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            return finished == task && task.Status == TaskStatus.RanToCompletion
                ? await task.ConfigureAwait(false)
                : null;
        }
    }
}
