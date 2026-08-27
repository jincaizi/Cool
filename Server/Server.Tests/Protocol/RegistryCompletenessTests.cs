using System;
using System.Linq;
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
    public void Registry_Maps_Are_Consistent()
    {
        var ids = (MessageId[])Enum.GetValues(typeof(MessageId));
        var forward = ids.ToDictionary(id => id, id => MessageTypeRegistry.GetMessageType(id)!);

        // 正向：每个 id 都能取到类型
        Assert.All(forward, kv => Assert.NotNull(kv.Value));
        // 反向：每个类型都能取回原 id
        foreach (var kv in forward)
            Assert.Equal(kv.Key, MessageTypeRegistry.GetMessageId(kv.Value));

        // id 数值无重复（枚举手滑防线）
        Assert.Equal(ids.Length, ids.Select(i => (ushort)i).Distinct().Count());
    }
}
