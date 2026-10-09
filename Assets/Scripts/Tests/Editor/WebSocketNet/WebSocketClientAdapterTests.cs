using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cool.Protocol;
using NUnit.Framework;
using WebSocketNet;

namespace GameSys.EditorTests
{
    /// <summary>
    /// Task 3：WebSocketClientAdapter 的协议接线 —— 用假通道验证
    /// "编码 → 发送" 与 "收到 → 解码 → 事件" 两条链路，以及连接状态与错误上抛。
    /// 真实 WS 往返由 T4（浏览器）与 T5（集成）覆盖，不在此处。
    /// 注：Unity 内置 NUnit（test-framework 1.1.33）不接受 async Task 测试方法
    /// （"Method has non-void return value"）——假通道返回已完成的 Task，
    /// 阻塞式 GetResult 立即返回，语义与 await 等价。
    /// </summary>
    public class WebSocketClientAdapterTests
    {
        private sealed class FakeChannel : IWebSocketChannel
        {
            public readonly List<byte[]> Sent = new List<byte[]>();
            public bool IsConnected { get; private set; }
            public string? LastUri;
            public string? CloseReason;

            public event Action<byte[]>? MessageReceived;
            public event Action<string>? Closed;
            public event Action<Exception>? Error;

            public Task<bool> ConnectAsync(string uri)
            {
                LastUri = uri;
                IsConnected = true;
                return Task.FromResult(true);
            }

            public Task CloseAsync(string reason)
            {
                CloseReason = reason;
                IsConnected = false;
                return Task.CompletedTask;
            }

            public Task SendAsync(byte[] data, int offset, int length)
            {
                var copy = new byte[length];
                Array.Copy(data, offset, copy, 0, length);
                Sent.Add(copy);
                return Task.CompletedTask;
            }

            // 测试用驱动器
            public void Deliver(byte[] frame) => MessageReceived?.Invoke(frame);

            // 收到对端 Close 帧后不应再算已连接 —— 真实现（DotNetWebSocketChannel）的
            // IsConnected 判的是 State == Open，对端关闭后即为 false，假通道须同款建模
            public void RaiseClosed(string reason)
            {
                IsConnected = false;
                Closed?.Invoke(reason);
            }

            public void RaiseError(Exception ex) => Error?.Invoke(ex);

            public void Dispose() { }
        }

        [Test]
        public void test_connect_builds_ws_uri_from_host_and_port()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);

            // Act
            bool ok = adapter.ConnectAsync("127.0.0.1", 8000).GetAwaiter().GetResult();

            // Assert
            Assert.IsTrue(ok);
            Assert.AreEqual("ws://127.0.0.1:8000/", channel.LastUri);
            Assert.IsTrue(adapter.IsConnected);
        }

        [Test]
        public void test_send_encodes_message_onto_one_frame()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            adapter.ConnectAsync("127.0.0.1", 8000).GetAwaiter().GetResult();
            var echo = new EchoRequest { ClientTimestamp = 12345L };

            // Act
            adapter.Send(MessageId.EchoRequest, echo);

            // Assert（一条消息 = 一帧，且能被 MessageCodec 解回来）
            Assert.AreEqual(1, channel.Sent.Count, "一条消息应只产生一帧（D-W9）");
            Assert.IsTrue(MessageCodec.TryDecode(channel.Sent[0], out var decoded));
            Assert.AreEqual(MessageId.EchoRequest, decoded.messageId);
            Assert.AreEqual(12345L, ((EchoRequest)decoded.message).ClientTimestamp);

            // flags 必须编码进 wire 第 1 字节（MessageCodec 默认 None=0，适配器默认 Reliable=4）
            // ——丢掉 flags 转发这条即红
            Assert.AreEqual((byte)MessageFlags.Reliable, channel.Sent[0][0],
                "适配器应把 flags 透传给 MessageCodec（默认 Reliable），而非用 None");
        }

        [Test]
        public void test_received_frame_raises_message_received_with_decoded_id()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            adapter.ConnectAsync("127.0.0.1", 8000).GetAwaiter().GetResult();

            MessageId? seenId = null;
            IMessage? seenMsg = null;
            adapter.MessageReceived += (id, msg) => { seenId = id; seenMsg = msg; };

            var payload = MessageCodec.EncodeToArray(
                MessageId.EchoResponse,
                new EchoResponse { ClientTimestamp = 12345L, ServerTimestamp = 987654321L },
                MessageFlags.Reliable);

            // Act
            channel.Deliver(payload);

            // Assert
            Assert.AreEqual(MessageId.EchoResponse, seenId);
            var response = (EchoResponse)seenMsg!;
            Assert.AreEqual(12345L, response.ClientTimestamp);
            Assert.AreEqual(987654321L, response.ServerTimestamp);
        }

        [Test]
        public void test_malformed_frame_is_dropped_without_raising()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            adapter.ConnectAsync("127.0.0.1", 8000).GetAwaiter().GetResult();

            bool raised = false;
            Exception? errored = null;
            adapter.MessageReceived += (_, _) => raised = true;
            adapter.Error += ex => errored = ex;

            // Act（一段不是合法编码的字节）
            channel.Deliver(new byte[] { 0xFF, 0x00, 0x01 });

            // Assert（不崩、不上抛假消息；解码失败要留痕但不能打断接收循环）
            Assert.IsFalse(raised, "畸形帧不得伪装成一条消息");
            Assert.IsNotNull(errored, "畸形帧应经由 Error 事件留痕，而不是静默吞掉");
        }

        [Test]
        public void test_channel_closed_raises_disconnected()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            adapter.ConnectAsync("127.0.0.1", 8000).GetAwaiter().GetResult();

            string? reason = null;
            adapter.Disconnected += r => reason = r;

            // Act
            channel.RaiseClosed("server shutdown");

            // Assert
            Assert.AreEqual("server shutdown", reason);
            Assert.IsFalse(adapter.IsConnected);
        }

        [Test]
        public void test_send_when_not_connected_is_dropped_silently()
        {
            // Arrange（未连接）
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);

            // Act
            adapter.Send(MessageId.EchoRequest, new EchoRequest { ClientTimestamp = 1L });

            // Assert（与 KcpNetClientAdapter 的既有语义一致：未连接时静默丢弃）
            Assert.AreEqual(0, channel.Sent.Count);
        }

        [Test]
        public void test_disconnect_forwards_reason_to_channel()
        {
            // Arrange
            var channel = new FakeChannel();
            var adapter = new WebSocketClientAdapter(channel);
            adapter.ConnectAsync("127.0.0.1", 8000).GetAwaiter().GetResult();

            // Act
            adapter.DisconnectAsync("bye").GetAwaiter().GetResult();

            // Assert（reason 必须透传到通道——退回 Task.CompletedTask 占位实现这条即红）
            Assert.AreEqual("bye", channel.CloseReason);
            Assert.IsFalse(adapter.IsConnected);
        }
    }
}
