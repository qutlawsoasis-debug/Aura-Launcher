using AuraLauncher.Core.WebBridge;
using Xunit;

namespace AuraLauncher.Tests;

public class WebBridgeTests
{
    [Fact]
    public void Parse_ValidJsonObjectString_ReturnsMessage()
    {
        string json = """{"id":"req_1","type":"command","name":"game.launch","payload":{"demo":true}}""";

        var (msg, error) = BridgeMessageParser.Parse(json);

        Assert.Null(error);
        Assert.NotNull(msg);
        Assert.Equal("req_1", msg.Id);
        Assert.Equal("command", msg.Type);
        Assert.Equal("game.launch", msg.Name);
    }

    [Fact]
    public void Parse_JsonEncodedString_ReturnsMessage()
    {
        // When JS sends postMessage(JSON.stringify(msg)), WebMessageAsJson arrives as a JSON string literal:
        // "\"{\\\"id\\\":\\\"req_2\\\",\\\"type\\\":\\\"command\\\",\\\"name\\\":\\\"window.action\\\"}\""
        string encoded = "\"{\\\"id\\\":\\\"req_2\\\",\\\"type\\\":\\\"command\\\",\\\"name\\\":\\\"window.action\\\"}\"";

        var (msg, error) = BridgeMessageParser.Parse(encoded);

        Assert.Null(error);
        Assert.NotNull(msg);
        Assert.Equal("req_2", msg.Id);
        Assert.Equal("command", msg.Type);
        Assert.Equal("window.action", msg.Name);
    }

    [Fact]
    public void Parse_CaseInsensitiveFields_ReturnsMessage()
    {
        string json = """{"ID":"req_3","TYPE":"COMMAND","NAME":"game.stop"}""";

        var (msg, error) = BridgeMessageParser.Parse(json);

        Assert.Null(error);
        Assert.NotNull(msg);
        Assert.Equal("req_3", msg.Id);
        Assert.Equal("COMMAND", msg.Type);
        Assert.Equal("game.stop", msg.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Parse_EmptyOrNullString_ReturnsErrorWithoutException(string? input)
    {
        var (msg, error) = BridgeMessageParser.Parse(input);

        Assert.Null(msg);
        Assert.NotNull(error);
        Assert.Equal("empty_message", error);
    }

    [Theory]
    [InlineData("{invalid json")]
    [InlineData("not a json at all")]
    [InlineData("{id: 123}")]
    public void Parse_MalformedJson_ReturnsErrorWithoutException(string input)
    {
        var (msg, error) = BridgeMessageParser.Parse(input);

        Assert.Null(msg);
        Assert.NotNull(error);
    }
}
