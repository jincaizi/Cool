using System;
using NUnit.Framework;
using Cool.Protocol;

namespace Hotfix.GameSystems.Tests.Network
{
    /// <summary>
    /// 协议层回归测试（Unity 编辑器环境版本）
    /// 与 Server.Tests 的 CodecRoundtripTests 断言语义一致
    /// </summary>
    public class ProtocolRoundtripTests
    {
        [Test]
        public void Roundtrip_EchoResponse_PreservesFields()
        {
            var source = new EchoResponse
            {
                ClientTimestamp = 123456789L,
                ServerTimestamp = 987654321L
            };

            var encoded = MessageCodec.EncodeToArray(MessageId.EchoResponse, source);
            var (_, decoded) = MessageCodec.Decode(encoded);

            // Unity 内置 NUnit(3.5.1) 的 Assert.IsAssignableFrom<T> 返回 void，改用等价断言
            Assert.That(decoded, Is.InstanceOf<EchoResponse>());
            var response = (EchoResponse)decoded;
            Assert.AreEqual(source.ClientTimestamp, response.ClientTimestamp);
            Assert.AreEqual(source.ServerTimestamp, response.ServerTimestamp);
        }

        [Test]
        public void Every_MessageId_HasRegisteredType()
        {
            foreach (MessageId id in Enum.GetValues(typeof(MessageId)))
            {
                Assert.IsTrue(
                    MessageTypeRegistry.IsRegistered(id),
                    $"MessageId.{id} 未在 MessageTypeRegistry 注册");
            }
        }
    }
}
