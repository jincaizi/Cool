using System;
using Cool.Protocol;
using Xunit;

namespace Cool.Protocol.Tests;

public class RegistryCompletenessTests
{
    /// <summary>
    /// 规则层约束：每个 MessageId 枚举成员必须有对应类型注册，
    /// 否则线上收到该消息会被静默丢失
    /// </summary>
    [Fact]
    public void Every_MessageId_HasRegisteredType()
    {
        foreach (MessageId id in Enum.GetValues(typeof(MessageId)))
        {
            Assert.True(
                MessageTypeRegistry.IsRegistered(id),
                $"MessageId.{id} 未在 MessageTypeRegistry 注册");
        }
    }

    [Fact]
    public void EchoPair_Ids_DoNotCollide()
    {
        Assert.NotEqual((ushort)MessageId.EchoRequest, (ushort)MessageId.EchoResponse);
    }
}
