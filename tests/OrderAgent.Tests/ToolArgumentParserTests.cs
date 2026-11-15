using OrderAgent.Application.Tools;

namespace OrderAgent.Tests;

public sealed class ToolArgumentParserTests
{
    [Fact]
    public void TryParseOrderId_ValidIntegerPayload_ReturnsOrderId()
    {
        var ok = GetOrderToolArgumentParser.TryParseOrderId("""{"orderId":1042}""", out var orderId);

        Assert.True(ok);
        Assert.Equal(1042, orderId);
    }

    [Theory]
    [InlineData("""{"orderId":1042,"tenantId":"tenant-a"}""")]
    [InlineData("""{"orderId":0}""")]
    [InlineData("""{"orderId":-5}""")]
    [InlineData("""{"orderId":"1042"}""")]
    [InlineData("""{"orderId":null}""")]
    [InlineData("{bad json")]
    [InlineData("")]
    public void TryParseOrderId_InvalidOrUnexpectedProperties_ReturnsFalse(string rawJson)
    {
        var ok = GetOrderToolArgumentParser.TryParseOrderId(rawJson, out var orderId);

        Assert.False(ok);
        Assert.Equal(0, orderId);
    }
}
