using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Core.WebBridge;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
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

    private class TestGameSessionService : IGameSessionService
    {
        public GameSessionState State { get; set; } = GameSessionState.Idle;
        public bool IsGameRunning => State == GameSessionState.Running;
        public System.Diagnostics.Process? CurrentProcess => null;

        public event EventHandler<GameSessionState>? StateChanged;
        public event EventHandler<string>? LogReceived;
        public event EventHandler<GameExitedEventArgs>? GameExited;
        public event EventHandler<System.Diagnostics.Process>? GameStarted;

        public Task<System.Diagnostics.Process> LaunchAsync(LaunchOptions? options = null, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> StopAsync(int timeoutMs = 5000) => Task.FromResult(true);
    }

    private class TestPresetService : IGraphicsPresetService
    {
        public string LastAppliedPresetKey { get; private set; } = string.Empty;

        public IReadOnlyDictionary<string, GraphicsPresetModel> GetPresets() => new Dictionary<string, GraphicsPresetModel>();
        public GraphicsPresetModel? GetPreset(string presetKey) => null;
        public string AutoDetectPreset() => "Medium";

        public PresetApplyResult ApplyPreset(string gameDir, string presetKey, bool persistConfig = true)
        {
            LastAppliedPresetKey = presetKey;
            return new PresetApplyResult
            {
                Success = true,
                PresetKey = presetKey,
                FinalRamMb = 4096,
                ChosenShader = "TestShader"
            };
        }
    }

    [Fact]
    public async Task Handle_ConfigGet_ReturnsConfigProperties()
    {
        var configService = new TestConfigService("C:\\test\\dir");
        configService.CurrentConfig.Nickname = "Steve";
        configService.CurrentConfig.RamMb = 4096;
        configService.CurrentConfig.HideLauncherWhilePlaying = false;
        configService.CurrentConfig.GraphicsPreset = "High";
        configService.CurrentConfig.VisualEffectsMode = "low";

        var sessionService = new TestGameSessionService();
        using var bridge = new WebBridge(null, sessionService, configService, null, _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_cfg_1","type":"command","name":"config.get"}""");

        Assert.NotNull(lastResponse);
        Assert.Equal("req_cfg_1", lastResponse.Id);
        Assert.Equal("config.get", lastResponse.Name);
        Assert.Null(lastResponse.Error);

        var json = System.Text.Json.JsonSerializer.Serialize(lastResponse.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Steve", root.GetProperty("nickname").GetString());
        Assert.Equal(4096, root.GetProperty("ramMb").GetInt32());
        Assert.False(root.GetProperty("hideLauncherWhilePlaying").GetBoolean());
        Assert.Equal("High", root.GetProperty("graphicsPreset").GetString());
        Assert.Equal("low", root.GetProperty("visualEffectsMode").GetString());
    }

    [Fact]
    public async Task Handle_ConfigSet_ValidKeys_UpdatesConfigAndReturnsUpdated()
    {
        var configService = new TestConfigService("C:\\test\\dir");
        var sessionService = new TestGameSessionService();
        using var bridge = new WebBridge(null, sessionService, configService, null, _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        // ramMb
        await bridge.ProcessRawMessageAsync("""{"id":"req_set_ram","type":"command","name":"config.set","payload":{"key":"ramMb","value":8192}}""");
        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        Assert.Equal(8192, configService.CurrentConfig.RamMb);

        // hideLauncherWhilePlaying
        await bridge.ProcessRawMessageAsync("""{"id":"req_set_hide","type":"command","name":"config.set","payload":{"key":"hideLauncherWhilePlaying","value":false}}""");
        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        Assert.False(configService.CurrentConfig.HideLauncherWhilePlaying);

        // visualEffectsMode
        await bridge.ProcessRawMessageAsync("""{"id":"req_set_fx","type":"command","name":"config.set","payload":{"key":"visualEffectsMode","value":"off"}}""");
        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        Assert.Equal("off", configService.CurrentConfig.VisualEffectsMode);
    }

    [Theory]
    [InlineData("unknownKey", "foo", "invalid_key")]
    [InlineData("ramMb", 1024, "invalid_value")] // below 2048
    [InlineData("ramMb", 99999999, "invalid_value")] // above physical RAM
    [InlineData("hideLauncherWhilePlaying", "not_a_bool", "invalid_value")]
    [InlineData("visualEffectsMode", "hyper", "invalid_value")]
    public async Task Handle_ConfigSet_InvalidInputs_ReturnsError(string key, object val, string expectedError)
    {
        var configService = new TestConfigService("C:\\test\\dir");
        var sessionService = new TestGameSessionService();
        using var bridge = new WebBridge(null, sessionService, configService, null, _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        string valJson = val is string s ? $"\"{s}\"" : val.ToString()!;
        string raw = "{\"id\":\"req_err\",\"type\":\"command\",\"name\":\"config.set\",\"payload\":{\"key\":\"" + key + "\",\"value\":" + valJson + "}}";

        await bridge.ProcessRawMessageAsync(raw);

        Assert.NotNull(lastResponse);
        Assert.Equal("req_err", lastResponse.Id);
        Assert.Equal(expectedError, lastResponse.Error);
    }

    [Fact]
    public async Task Handle_PresetApply_ValidKey_CallsServiceAndReturnsResult()
    {
        var configService = new TestConfigService("C:\\test\\dir");
        var sessionService = new TestGameSessionService();
        var presetService = new TestPresetService();
        using var bridge = new WebBridge(null, sessionService, configService, presetService, _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_preset","type":"command","name":"preset.apply","payload":{"presetKey":"Ultra"}}""");

        Assert.NotNull(lastResponse);
        Assert.Equal("req_preset", lastResponse.Id);
        Assert.Null(lastResponse.Error);
        Assert.Equal("Ultra", presetService.LastAppliedPresetKey);

        var json = System.Text.Json.JsonSerializer.Serialize(lastResponse.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
        Assert.Equal("Ultra", doc.RootElement.GetProperty("PresetKey").GetString());
    }
}
