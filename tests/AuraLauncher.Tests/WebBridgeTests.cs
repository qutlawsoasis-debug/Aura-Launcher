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

        public virtual PresetApplyResult ApplyPreset(string gameDir, string presetKey, bool persistConfig = true)
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
        using var bridge = new WebBridge(null, sessionService, configService, null, null, null, logger: _ => {});

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
        using var bridge = new WebBridge(null, sessionService, configService, null, null, null, logger: _ => {});

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
        using var bridge = new WebBridge(null, sessionService, configService, null, null, null, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        string valJson = val is string s ? $"\"{s}\"" : val.ToString()!;
        string raw = "{\"id\":\"req_err\",\"type\":\"command\",\"name\":\"config.set\",\"payload\":{\"key\":\"" + key + "\",\"value\":" + valJson + "}}";

        await bridge.ProcessRawMessageAsync(raw);

        Assert.NotNull(lastResponse);
        Assert.Equal("req_err", lastResponse.Id);
        Assert.Equal(expectedError, lastResponse.Error);
    }

    private class TestWorkshopService : IWorkshopService
    {
        public List<ShaderPackItem> Shaders { get; set; } = new();
        public List<ModItem> Mods { get; set; } = new();
        public string ActiveShader { get; set; } = "OFF";

        public Task<List<WorldSaveItem>> GetWorldSavesAsync(string gameDir) => Task.FromResult(new List<WorldSaveItem>());
        public Task<string> CreateWorldBackupAsync(WorldSaveItem world) => Task.FromResult("");
        public Task ExportWorldToZipAsync(WorldSaveItem world, string destinationZipPath) => Task.CompletedTask;
        public Task<string> ImportWorldFromZipAsync(string gameDir, string sourceZipPath) => Task.FromResult("");
        public Task CreateAutoBackupLatestWorldAsync(string gameDir, int maxAutoBackupsPerWorld = 3) => Task.CompletedTask;
        public Task RestoreWorldBackupAsync(WorldSaveItem world, WorldBackupItem backup) => Task.CompletedTask;
        public Task DeleteWorldBackupAsync(WorldBackupItem backup) => Task.CompletedTask;
        public Task<bool> DeleteWorldAsync(WorldSaveItem world) => Task.FromResult(true);

        public Task<List<ModItem>> GetModsAsync(string gameDir) => Task.FromResult(Mods);
        public bool ToggleMod(ModItem mod, bool enable)
        {
            mod.IsEnabled = enable;
            if (enable && mod.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                mod.FileName = mod.FileName[..^9];
            }
            else if (!enable && !mod.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                mod.FileName += ".disabled";
            }
            return true;
        }

        public Task<List<ShaderPackItem>> GetShaderPacksAsync(string gameDir) => Task.FromResult(Shaders);
        public void SetActiveShaderPack(string gameDir, string shaderFileName)
        {
            ActiveShader = shaderFileName;
            foreach (var s in Shaders)
            {
                s.IsActive = string.Equals(s.FileName, shaderFileName, StringComparison.OrdinalIgnoreCase);
            }
        }

        public Task<List<ScreenshotItem>> GetScreenshotsAsync(string gameDir) => Task.FromResult(new List<ScreenshotItem>());
        public bool DeleteScreenshot(ScreenshotItem item) => true;
    }

    private class TestPackUpdateService : IPackUpdateService
    {
        public bool ShouldDelay { get; set; }
        public TaskCompletionSource<bool>? DelayTcs { get; set; }

        public async Task<PackUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default, bool forceFullCheck = false)
        {
            if (ShouldDelay && DelayTcs != null)
            {
                await DelayTcs.Task;
            }

            progress?.Report(new DownloadProgressReport { Percentage = 50, StatusText = "Скачивание" });
            progress?.Report(new DownloadProgressReport { Percentage = 100, StatusText = "Готово" });

            return new PackUpdateResult(PackUpdateStatus.Updated, "Обновлено 2 файла", 2);
        }
    }

    [Fact]
    public async Task Handle_WorkshopList_ReturnsShadersAndMods()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var workshopService = new TestWorkshopService
        {
            Shaders = new List<ShaderPackItem>
            {
                new() { Name = "Sildurs", FileName = "Sildurs.zip", IsActive = true }
            },
            Mods = new List<ModItem>
            {
                new() { FileName = "sodium.jar", DisplayName = "Sodium", Version = "0.5.8", IsEnabled = true }
            }
        };

        using var bridge = new WebBridge(null, sessionService, configService, null, workshopService, null, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_wl","type":"command","name":"workshop.list"}""");

        Assert.NotNull(lastResponse);
        Assert.Equal("req_wl", lastResponse.Id);
        Assert.Null(lastResponse.Error);

        var json = System.Text.Json.JsonSerializer.Serialize(lastResponse.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var shaders = doc.RootElement.GetProperty("shaders");
        var mods = doc.RootElement.GetProperty("mods");

        Assert.Equal(1, shaders.GetArrayLength());
        Assert.Equal("Sildurs.zip", shaders[0].GetProperty("fileName").GetString());
        Assert.True(shaders[0].GetProperty("isActive").GetBoolean());

        Assert.Equal(1, mods.GetArrayLength());
        Assert.Equal("sodium.jar", mods[0].GetProperty("fileName").GetString());
        Assert.True(mods[0].GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Handle_WorkshopSetShader_InvalidShader_ReturnsError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var workshopService = new TestWorkshopService
        {
            Shaders = new List<ShaderPackItem>
            {
                new() { Name = "Sildurs", FileName = "Sildurs.zip", IsActive = false }
            }
        };

        using var bridge = new WebBridge(null, sessionService, configService, null, workshopService, null, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_ss","type":"command","name":"workshop.setShader","payload":{"fileName":"NonExistent.zip"}}""");

        Assert.NotNull(lastResponse);
        Assert.Equal("req_ss", lastResponse.Id);
        Assert.Equal("invalid_shader", lastResponse.Error);
    }

    [Fact]
    public async Task Handle_WorkshopToggleMod_ModNotFound_ReturnsError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var workshopService = new TestWorkshopService();

        using var bridge = new WebBridge(null, sessionService, configService, null, workshopService, null, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_tm","type":"command","name":"workshop.toggleMod","payload":{"fileName":"missing.jar","enabled":false}}""");

        Assert.NotNull(lastResponse);
        Assert.Equal("req_tm", lastResponse.Id);
        Assert.Equal("mod_not_found", lastResponse.Error);
    }

    [Fact]
    public async Task Handle_PackSync_WhenAlreadySyncing_ReturnsBusyError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var delayTcs = new TaskCompletionSource<bool>();
        var packUpdateService = new TestPackUpdateService
        {
            ShouldDelay = true,
            DelayTcs = delayTcs
        };

        using var bridge = new WebBridge(null, sessionService, configService, null, null, packUpdateService, logger: _ => {});

        var responses = new List<BridgeMessage>();
        bridge.MessagePosted += msg => responses.Add(msg);

        // First call starts background sync
        await bridge.ProcessRawMessageAsync("""{"id":"req_sync1","type":"command","name":"pack.sync"}""");

        // Second call should immediately get rejected with busy
        await bridge.ProcessRawMessageAsync("""{"id":"req_sync2","type":"command","name":"pack.sync"}""");

        var busyResp = responses.FirstOrDefault(r => r.Id == "req_sync2");
        Assert.NotNull(busyResp);
        Assert.Equal("busy", busyResp.Error);

        delayTcs.SetResult(true);
    }

    [Fact]
    public async Task Handle_PresetApply_ValidKey_CallsServiceAndReturnsResult()
    {
        var configService = new TestConfigService("C:\\test\\dir");
        var sessionService = new TestGameSessionService();
        var presetService = new TestPresetService();
        using var bridge = new WebBridge(null, sessionService, configService, presetService, null, null, logger: _ => {});

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

    private class TestBridgeLaunchService : IGameLaunchService
    {
        public string InstanceDirToReturn { get; set; } = @"C:\custom\instance\dir";
        public bool IsGameRunning => false;
        public System.Diagnostics.Process? CurrentGameProcess => null;
        public event EventHandler<int>? GameExited { add { } remove { } }
        public Task<bool> StopGameProcessAsync(int timeoutMs = 6000) => Task.FromResult(true);
        public string ResolveMinecraftDirectory(string? customPath = null) => customPath ?? @"C:\test";
        public string ResolveInstanceDirectory(string? customPath = null, string? version = null) => InstanceDirToReturn;
        public string? ResolveJavaRuntime(string gameDir, int minMajorVersion = 17) => null;
        public bool CheckEnvironmentInstalled(string gameDir, string? version = null) => true;
        public System.Diagnostics.Process? FindRunningGameProcess(string gameDir) => null;
        public Task EnsureInstalledAsync(string gameDir, IProgress<InstallProgressReport>? progress = null, Action<string>? onLogReceived = null, CancellationToken cancellationToken = default, string? version = null) => Task.CompletedTask;
        public Task<System.Diagnostics.Process> LaunchGameAsync(LauncherConfig config, Action<string>? onLogReceived = null, Action<int, string>? onGameExited = null, IProgress<InstallProgressReport>? installProgress = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class TestPresetServiceWithDir : TestPresetService
    {
        public string? LastGameDir { get; private set; }

        public override PresetApplyResult ApplyPreset(string gameDir, string presetKey, bool persistConfig = true)
        {
            LastGameDir = gameDir;
            return base.ApplyPreset(gameDir, presetKey, persistConfig);
        }
    }

    [Fact]
    public async Task Handle_InstanceDir_IsUsedFromLaunchService()
    {
        var configService = new TestConfigService(@"C:\root\.aura");
        var sessionService = new TestGameSessionService();
        var presetService = new TestPresetServiceWithDir();
        var launchService = new TestBridgeLaunchService { InstanceDirToReturn = @"C:\root\.aura\instances\1.20.1" };

        using var bridge = new WebBridge(null, sessionService, configService, presetService, null, null, launchService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_pdir","type":"command","name":"preset.apply","payload":{"presetKey":"High"}}""");

        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        Assert.Equal(@"C:\root\.aura\instances\1.20.1", presetService.LastGameDir);
    }

    [Fact]
    public async Task Handle_WorkshopToggleMod_DisableThenReEnable_ByBaseName()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var modItem = new ModItem
        {
            FileName = "testmod.jar",
            DisplayName = "Test Mod",
            IsEnabled = true
        };
        var workshopService = new TestWorkshopService
        {
            Mods = new List<ModItem> { modItem }
        };

        using var bridge = new WebBridge(null, sessionService, configService, null, workshopService, null, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        // 1. Disable mod using original name
        await bridge.ProcessRawMessageAsync("""{"id":"req_dis","type":"command","name":"workshop.toggleMod","payload":{"fileName":"testmod.jar","enabled":false}}""");
        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        Assert.False(modItem.IsEnabled);
        Assert.Equal("testmod.jar.disabled", modItem.FileName);

        // 2. Re-enable mod using base name ("testmod.jar")
        await bridge.ProcessRawMessageAsync("""{"id":"req_en","type":"command","name":"workshop.toggleMod","payload":{"fileName":"testmod.jar","enabled":true}}""");
        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        Assert.True(modItem.IsEnabled);
        Assert.Equal("testmod.jar", modItem.FileName);

        // 3. Disable again, then re-enable using actual current filename ("testmod.jar.disabled")
        await bridge.ProcessRawMessageAsync("""{"id":"req_dis2","type":"command","name":"workshop.toggleMod","payload":{"fileName":"testmod.jar","enabled":false}}""");
        Assert.False(modItem.IsEnabled);
        Assert.Equal("testmod.jar.disabled", modItem.FileName);

        await bridge.ProcessRawMessageAsync("""{"id":"req_en2","type":"command","name":"workshop.toggleMod","payload":{"fileName":"testmod.jar.disabled","enabled":true}}""");
        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        Assert.True(modItem.IsEnabled);
        Assert.Equal("testmod.jar", modItem.FileName);
    }
}
