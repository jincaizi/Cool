using Xunit;

namespace Cool.Protocol.Tests;

public class FoundationTests
{
    [Fact]
    public void ProtocolVersion_Is_1()
    {
        Assert.Equal(1, ProtocolConstants.ProtocolVersion);
    }

    [Fact]
    public void ProtoBufVersion_Is_Locked()
    {
        Assert.Equal("3.2.56", ProtocolConstants.ProtoBufVersion);
    }

    [Fact]
    public void HeaderSize_Is_Five()
    {
        Assert.Equal(5, ProtocolConstants.HeaderSize);
    }
}
