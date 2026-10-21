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

    private class TestBridgeSkinService : ISkinService
    {
        public bool IsValid { get; set; } = true;
        public string? ValidationError { get; set; }
        public bool UploadSuccess { get; set; } = true;
        public string? UploadError { get; set; }
        public string? LastUploadedModel { get; private set; }
        public string? LastUploadedSkinPath { get; private set; }
        public string? LastSyncedSkinPath { get; private set; }

        public SkinValidationResult ValidateSkinFile(string? filePath)
            => new(IsValid, IsValid ? null : (ValidationError ?? "Invalid skin file"), IsValid ? 64 : 0, IsValid ? 64 : 0);

        public System.Windows.Media.ImageSource LoadSkinImage(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractHeadAvatar(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractFrontSkinPreview(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractBackSkinPreview(string? skinPath) => null!;

        public Task SyncSkinToGameAsync(string? skinPath, string nickname, string gameDir, CancellationToken cancellationToken = default)
        {
            LastSyncedSkinPath = skinPath;
            return Task.CompletedTask;
        }

        public Task ResetToDefaultSteveAsync(string nickname, string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SkinUploadResult> UploadSkinToLobbyApiAsync(string? skinPath, string nickname, string model, string? ownerToken, string? baseUrl = null, CancellationToken cancellationToken = default)
        {
            LastUploadedSkinPath = skinPath;
            LastUploadedModel = model;
            return Task.FromResult(new SkinUploadResult(UploadSuccess, UploadSuccess ? "tok_test_owner" : null, UploadError));
        }

        public Task<System.Windows.Media.ImageSource> GetAvatarForPlayerAsync(string nickname, CancellationToken cancellationToken = default) => Task.FromResult<System.Windows.Media.ImageSource>(null!);
        public Task<(System.Windows.Media.ImageSource SkinTexture, bool IsSlim)> GetSkinTextureForPlayerAsync(string nickname, CancellationToken cancellationToken = default) => Task.FromResult<(System.Windows.Media.ImageSource, bool)>((null!, false));
        public void EnsureCustomSkinLoaderConfig(string gameDir) { }
        public void ClearCustomSkinLoaderCache(string gameDir) { }
    }

    [Fact]
    public async Task Handle_SkinGet_ReturnsConfigData()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        configService.CurrentConfig.Nickname = "AlexUser";
        configService.CurrentConfig.SkinModel = "slim";
        configService.CurrentConfig.SkinOriginalName = "my_cool_skin.png";
        configService.CurrentConfig.SkinPath = ""; // No file on disk -> dataUrl: null

        var sessionService = new TestGameSessionService();
        using var bridge = new WebBridge(null, sessionService, configService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_sg","type":"command","name":"skin.get"}""");

        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        var json = System.Text.Json.JsonSerializer.Serialize(lastResponse.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("AlexUser", doc.RootElement.GetProperty("nickname").GetString());
        Assert.Equal("slim", doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("my_cool_skin.png", doc.RootElement.GetProperty("fileName").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doc.RootElement.GetProperty("dataUrl").ValueKind);
    }

    [Fact]
    public async Task Handle_SkinUpload_InvalidFormat_ReturnsError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var skinService = new TestBridgeSkinService();
        using var bridge = new WebBridge(null, sessionService, configService, skinService: skinService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        // Base64 of plain text "not a png"
        string notPngB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Hello World!"));
        await bridge.ProcessRawMessageAsync("{\"id\":\"req_up1\",\"type\":\"command\",\"name\":\"skin.upload\",\"payload\":{\"fileName\":\"test.png\",\"base64Data\":\"" + notPngB64 + "\"}}");

        Assert.NotNull(lastResponse);
        Assert.Equal("invalid_format", lastResponse.Error);
    }

    [Fact]
    public async Task Handle_SkinUpload_TooLarge_ReturnsError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var skinService = new TestBridgeSkinService();
        using var bridge = new WebBridge(null, sessionService, configService, skinService: skinService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        // 1MB + 10 bytes dummy byte array
        byte[] large = new byte[1024 * 1024 + 10];
        string largeB64 = Convert.ToBase64String(large);
        await bridge.ProcessRawMessageAsync("{\"id\":\"req_up2\",\"type\":\"command\",\"name\":\"skin.upload\",\"payload\":{\"fileName\":\"large.png\",\"base64Data\":\"" + largeB64 + "\"}}");

        Assert.NotNull(lastResponse);
        Assert.Equal("file_too_large", lastResponse.Error);
    }

    [Fact]
    public async Task Handle_SkinUpload_LobbyApiError_ReturnsOkWithUploadError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var skinService = new TestBridgeSkinService
        {
            UploadSuccess = false,
            UploadError = "lobby_api_down"
        };
        using var bridge = new WebBridge(null, sessionService, configService, skinService: skinService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        // Valid 8-byte PNG header + dummy data
        byte[] fakePng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        string pngB64 = Convert.ToBase64String(fakePng);
        await bridge.ProcessRawMessageAsync("{\"id\":\"req_up3\",\"type\":\"command\",\"name\":\"skin.upload\",\"payload\":{\"fileName\":\"custom.png\",\"base64Data\":\"" + pngB64 + "\"}}");

        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        var json = System.Text.Json.JsonSerializer.Serialize(lastResponse.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("lobby_api_down", doc.RootElement.GetProperty("uploadError").GetString());
    }

    [Fact]
    public async Task Handle_SkinSetModel_InvalidValue_ReturnsError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        using var bridge = new WebBridge(null, sessionService, configService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_sm1","type":"command","name":"skin.setModel","payload":{"model":"unknown"}}""");

        Assert.NotNull(lastResponse);
        Assert.Equal("invalid_value", lastResponse.Error);
    }

    [Fact]
    public async Task Handle_SkinSetModel_ValidValue_UpdatesConfigAndReturnsOk()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        configService.CurrentConfig.SkinModel = "default";
        var sessionService = new TestGameSessionService();
        var skinService = new TestBridgeSkinService();
        using var bridge = new WebBridge(null, sessionService, configService, skinService: skinService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_sm2","type":"command","name":"skin.setModel","payload":{"model":"slim"}}""");

        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        Assert.Equal("slim", configService.CurrentConfig.SkinModel);
        var json = System.Text.Json.JsonSerializer.Serialize(lastResponse.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("slim", doc.RootElement.GetProperty("model").GetString());
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
    private class FakeFriendService : IFriendService
    {
        public bool IsRegistered { get; set; } = true;
        public string? CurrentUserId { get; set; } = "user_1";
        public string? CurrentFriendCode { get; set; } = "AURA-1234-5678";
        public IReadOnlyList<FriendPresenceItem> CachedFriends { get; set; } = Array.Empty<FriendPresenceItem>();
        public bool IsFriendsTabActive { get; set; }
        public bool IsInLobby { get; set; }
        public string? CurrentLobbyCode { get; set; }
        public string? CurrentHostToken { get; set; }
        public bool IsGameRunning { get; set; }

        public event Action<SyncResponse>? SyncUpdated;
        public event Action<IReadOnlyList<FriendPresenceItem>>? FriendsListUpdated;
        public event Action<IncomingInviteItem>? InviteReceived;
        public event Action<FriendRequestItem>? FriendRequestReceived;

        public void TriggerFriendsListUpdated(IReadOnlyList<FriendPresenceItem> items) => FriendsListUpdated?.Invoke(items);
        public bool HasFriendsListUpdatedSubscribers => FriendsListUpdated != null;
        public bool HasRequestReceivedSubscribers => FriendRequestReceived != null;
        public bool HasInviteReceivedSubscribers => InviteReceived != null;

        public void UpdateLobbyState(bool isInLobby, string? lobbyCode) { }
        public void Start() { }
        public void Stop() { }
        public Task EnsureRegisteredAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SyncResponse?> SyncNowAsync(CancellationToken cancellationToken = default) => Task.FromResult<SyncResponse?>(null);

        public Task<(bool Success, string? ErrorMessage)> SendFriendRequestAsync(string friendCode, CancellationToken cancellationToken = default)
        {
            if (friendCode == "AURA-0000")
            {
                return Task.FromResult((false, (string?)"Игрок с таким кодом не найден"));
            }
            return Task.FromResult((true, (string?)null));
        }

        public Task<bool> RespondFriendRequestAsync(string fromId, bool accept, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> RemoveFriendAsync(string friendId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<(bool Success, string? InviteId, string? ErrorMessage)> SendInviteAsync(string friendId, string lobbyCode, string? hostToken, CancellationToken cancellationToken = default) => Task.FromResult((true, (string?)"inv_1", (string?)null));
        public Task<(bool Success, string? ErrorMessage)> ChangeNicknameAsync(string newNick, CancellationToken cancellationToken = default) => Task.FromResult((true, (string?)null));
        public Task<(bool Success, string? LobbyCode, string? ErrorMessage)> RespondInviteAsync(string inviteId, bool accept, CancellationToken cancellationToken = default) => Task.FromResult((true, (string?)"lob_1", (string?)null));
        public Task ReportOfflineAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendProfileSnapshotAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<UserProfileSnapshotDto?> GetFriendProfileAsync(string friendId, CancellationToken cancellationToken = default) => Task.FromResult<UserProfileSnapshotDto?>(null);
        public void Dispose() { }
    }

    [Fact]
    public async Task Handle_FriendsAdd_InvalidCode_ReturnsErrorInResponse()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var friendService = new FakeFriendService();

        using var bridge = new WebBridge(null, sessionService, configService, friendService: friendService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_fa","type":"command","name":"friends.add","payload":{"code":"AURA-0000"}}""");

        Assert.NotNull(lastResponse);
        Assert.Null(lastResponse.Error);
        var json = System.Text.Json.JsonSerializer.Serialize(lastResponse.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("Игрок с таким кодом не найден", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public void Handle_FriendsListUpdated_SendsPresenceEvent()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var friendService = new FakeFriendService();

        using var bridge = new WebBridge(null, sessionService, configService, friendService: friendService, logger: _ => {});

        BridgeMessage? lastEvent = null;
        bridge.MessagePosted += msg => lastEvent = msg;

        friendService.TriggerFriendsListUpdated(new List<FriendPresenceItem>
        {
            new FriendPresenceItem { Id = "f_1", Nick = "BestFriend", Online = true, Status = "playing", LobbyCode = "LOBBY1" }
        });

        Assert.NotNull(lastEvent);
        Assert.Equal("event", lastEvent.Type);
        Assert.Equal("friends.presence", lastEvent.Name);
        var json = System.Text.Json.JsonSerializer.Serialize(lastEvent.Payload);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var friendsArr = doc.RootElement.GetProperty("friends");
        Assert.Equal(1, friendsArr.GetArrayLength());
        Assert.Equal("BestFriend", friendsArr[0].GetProperty("nick").GetString());
        Assert.Equal("playing", friendsArr[0].GetProperty("status").GetString());
        Assert.Equal("LOBBY1", friendsArr[0].GetProperty("lobbyCode").GetString());
    }

    [Fact]
    public void Dispose_UnsubscribesFromFriendServiceEvents()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        var friendService = new FakeFriendService();

        var bridge = new WebBridge(null, sessionService, configService, friendService: friendService, logger: _ => {});
        Assert.True(friendService.HasFriendsListUpdatedSubscribers);
        Assert.True(friendService.HasRequestReceivedSubscribers);
        Assert.True(friendService.HasInviteReceivedSubscribers);

        bridge.Dispose();

        Assert.False(friendService.HasFriendsListUpdatedSubscribers);
        Assert.False(friendService.HasRequestReceivedSubscribers);
        Assert.False(friendService.HasInviteReceivedSubscribers);
    }

    [Fact]
    public async Task Handle_GameSetVersion_InvalidVersion_ReturnsError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();

        using var bridge = new WebBridge(null, sessionService, configService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_v_err","type":"command","name":"game.setVersion","payload":{"version":"1.19.4"}}""");

        Assert.NotNull(lastResponse);
        Assert.Equal("invalid_value", lastResponse.Error);
    }

    [Fact]
    public async Task Handle_GameSetVersion_WhileBusy_ReturnsError()
    {
        var configService = new TestConfigService(@"C:\test\dir");
        var sessionService = new TestGameSessionService();
        sessionService.State = GameSessionState.Launching;

        using var bridge = new WebBridge(null, sessionService, configService, logger: _ => {});

        BridgeMessage? lastResponse = null;
        bridge.MessagePosted += msg => lastResponse = msg;

        await bridge.ProcessRawMessageAsync("""{"id":"req_v_busy","type":"command","name":"game.setVersion","payload":{"version":"1.21.1"}}""");

        Assert.NotNull(lastResponse);
        Assert.Equal("busy", lastResponse.Error);
    }

    [Fact]
    public async Task Handle_CacheClear_DeletesOldCrashReportAndLogGz_KeepsFreshCrashReport()
    {
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aura_test_cache_" + Guid.NewGuid().ToString("N"));
        string instanceDir = System.IO.Path.Combine(tempDir, "instances", "1.20.1");
        string logsDir = System.IO.Path.Combine(instanceDir, "logs");
        string crashDir = System.IO.Path.Combine(instanceDir, "crash-reports");

        System.IO.Directory.CreateDirectory(logsDir);
        System.IO.Directory.CreateDirectory(crashDir);

        try
        {
            // 1. *.log.gz file (should be deleted)
            string logGzFile = System.IO.Path.Combine(logsDir, "2026-10-01-1.log.gz");
            System.IO.File.WriteAllBytes(logGzFile, new byte[100]);

            // 2. Fresh crash report (modified today - should NOT be deleted)
            string freshCrash = System.IO.Path.Combine(crashDir, "crash-2026-10-21.txt");
            System.IO.File.WriteAllBytes(freshCrash, new byte[200]);

            // 3. Old crash report (modified 10 days ago - should be deleted)
            string oldCrash = System.IO.Path.Combine(crashDir, "crash-2026-10-10.txt");
            System.IO.File.WriteAllBytes(oldCrash, new byte[300]);
            System.IO.File.SetLastWriteTimeUtc(oldCrash, DateTime.UtcNow.AddDays(-10));

            var configService = new TestConfigService(tempDir);
            configService.CurrentConfig.SelectedVersion = "1.20.1";
            var sessionService = new TestGameSessionService();
            var launchService = new TestBridgeLaunchService
            {
                InstanceDirToReturn = instanceDir
            };

            using var bridge = new WebBridge(null, sessionService, configService, launchService: launchService, logger: _ => {});

            BridgeMessage? lastResponse = null;
            bridge.MessagePosted += msg => lastResponse = msg;

            await bridge.ProcessRawMessageAsync("""{"id":"req_cc","type":"command","name":"cache.clear"}""");

            Assert.NotNull(lastResponse);
            Assert.Null(lastResponse.Error);

            var json = System.Text.Json.JsonSerializer.Serialize(lastResponse.Payload);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            long freed = doc.RootElement.GetProperty("freedBytes").GetInt64();
            Assert.Equal(400, freed); // 100 + 300

            Assert.False(System.IO.File.Exists(logGzFile));
            Assert.False(System.IO.File.Exists(oldCrash));
            Assert.True(System.IO.File.Exists(freshCrash));
        }
        finally
        {
            try { System.IO.Directory.Delete(tempDir, true); } catch { }
        }
    }

    private class FakeLobbySession : ILobbySessionService
    {
        public LobbySessionRole Role { get; set; }
        public LobbySessionState State { get; set; }
        public string? CurrentLobbyCode { get; set; }
        public string? CurrentHostToken { get; set; }
        public string? CurrentTunnelAddress { get; set; }
        public int? CurrentTunnelPingMs { get; set; }
        public string? HostNick { get; set; }
        public string? LastError { get; set; }
        public string? StatusHint { get; set; }
        public bool HasPersonalTunnel { get; set; }
        public IReadOnlyList<LobbyPlayerItemDto> Players { get; set; } = Array.Empty<LobbyPlayerItemDto>();
        public IReadOnlyDictionary<string, PlayerModSyncInfo>? ModSync { get; set; }

        public event EventHandler<LobbySessionState>? StateChanged;
        public event EventHandler<LobbyStatusDto>? LobbyUpdated;
        public event EventHandler<string>? TunnelAddressReady;
        public event EventHandler<int>? TunnelPingUpdated;
        public event EventHandler<string>? PlayerKicked;
        public event EventHandler<string>? ErrorOccurred;

        public string? CreatedBy { get; private set; }
        public bool JoinResult { get; set; } = true;
        public bool HasSubscribers => StateChanged != null || LobbyUpdated != null || TunnelPingUpdated != null || PlayerKicked != null || ErrorOccurred != null;

        public void RaiseState(LobbySessionState s) { State = s; StateChanged?.Invoke(this, s); }
        public void RaiseKicked(string code) => PlayerKicked?.Invoke(this, code);

        public Task<string?> CreateLobbyAsync(string hostName, CancellationToken cancellationToken = default)
        {
            CreatedBy = hostName;
            Role = LobbySessionRole.Host;
            State = LobbySessionState.WaitingForWorld;
            CurrentLobbyCode = "AURA-1111-2222";
            CurrentHostToken = "host_tok";
            HostNick = hostName;
            Players = new[] { new LobbyPlayerItemDto(hostName, true) };
            return Task.FromResult<string?>(CurrentLobbyCode);
        }

        public Task<string?> ClaimPersonalTunnelAsync(Action<string>? onBrowserUrl = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<bool> JoinByCodeAsync(string code, string guestNick, CancellationToken cancellationToken = default)
        {
            if (!JoinResult)
            {
                State = LobbySessionState.Error;
                LastError = "Лобби не найдено";
                return Task.FromResult(false);
            }
            Role = LobbySessionRole.Guest;
            State = LobbySessionState.Joining;
            CurrentLobbyCode = code;
            return Task.FromResult(true);
        }

        public Task OpenWorldAsync(int? localPort = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ConnectToGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> KickPlayerAsync(string playerNick, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task LeaveLobbyAsync(CancellationToken cancellationToken = default)
        {
            Role = LobbySessionRole.None;
            State = LobbySessionState.Idle;
            CurrentLobbyCode = null;
            Players = Array.Empty<LobbyPlayerItemDto>();
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private static System.Text.Json.JsonElement PayloadOf(BridgeMessage msg)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(msg.Payload);
        return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task Handle_LobbyGet_WithoutService_ReturnsError()
    {
        using var bridge = new WebBridge(null, new TestGameSessionService(), new TestConfigService(@"C:\test\dir"), logger: _ => {});
        BridgeMessage? last = null;
        bridge.MessagePosted += m => last = m;

        await bridge.ProcessRawMessageAsync("""{"id":"r1","type":"command","name":"lobby.get"}""");

        Assert.NotNull(last);
        Assert.Equal("lobby_service_unavailable", last.Error);
    }

    [Fact]
    public async Task Handle_LobbyCreate_UsesConfigNickname_ReturnsSnapshot()
    {
        var lobby = new FakeLobbySession();
        using var bridge = new WebBridge(null, new TestGameSessionService(), new TestConfigService(@"C:\test\dir"), lobbySession: lobby, logger: _ => {});
        BridgeMessage? last = null;
        bridge.MessagePosted += m => last = m;

        await bridge.ProcessRawMessageAsync("""{"id":"r2","type":"command","name":"lobby.create"}""");

        Assert.Equal("TestPlayer", lobby.CreatedBy);
        Assert.NotNull(last);
        Assert.Null(last.Error);
        var p = PayloadOf(last);
        Assert.Equal("host", p.GetProperty("role").GetString());
        Assert.Equal("waitingForWorld", p.GetProperty("state").GetString());
        Assert.Equal("AURA-1111-2222", p.GetProperty("code").GetString());
        Assert.Equal(1, p.GetProperty("players").GetArrayLength());
    }

    [Fact]
    public async Task Handle_LobbyCreate_WhenAlreadyInLobby_ReturnsError()
    {
        var lobby = new FakeLobbySession { Role = LobbySessionRole.Guest };
        using var bridge = new WebBridge(null, new TestGameSessionService(), new TestConfigService(@"C:\test\dir"), lobbySession: lobby, logger: _ => {});
        BridgeMessage? last = null;
        bridge.MessagePosted += m => last = m;

        await bridge.ProcessRawMessageAsync("""{"id":"r3","type":"command","name":"lobby.create"}""");

        Assert.Equal("already_in_lobby", last!.Error);
        Assert.Null(lobby.CreatedBy);
    }

    [Fact]
    public async Task Handle_LobbyJoin_EmptyCode_And_Failure_ReturnErrors()
    {
        var lobby = new FakeLobbySession { JoinResult = false };
        using var bridge = new WebBridge(null, new TestGameSessionService(), new TestConfigService(@"C:\test\dir"), lobbySession: lobby, logger: _ => {});
        var posted = new List<BridgeMessage>();
        bridge.MessagePosted += m => posted.Add(m);

        await bridge.ProcessRawMessageAsync("""{"id":"r4","type":"command","name":"lobby.join","payload":{"code":"  "}}""");
        await bridge.ProcessRawMessageAsync("""{"id":"r5","type":"command","name":"lobby.join","payload":{"code":"aura-9999-0000"}}""");

        Assert.Equal("invalid_code", posted[0].Error);
        Assert.Equal("Лобби не найдено", posted[1].Error);
    }

    [Fact]
    public async Task Handle_LobbyJoin_UppercasesCode()
    {
        var lobby = new FakeLobbySession();
        using var bridge = new WebBridge(null, new TestGameSessionService(), new TestConfigService(@"C:\test\dir"), lobbySession: lobby, logger: _ => {});
        BridgeMessage? last = null;
        bridge.MessagePosted += m => last = m;

        await bridge.ProcessRawMessageAsync("""{"id":"r6","type":"command","name":"lobby.join","payload":{"code":"aura-1234-abcd"}}""");

        Assert.Null(last!.Error);
        Assert.Equal("AURA-1234-ABCD", lobby.CurrentLobbyCode);
        Assert.Equal("guest", PayloadOf(last).GetProperty("role").GetString());
    }

    [Fact]
    public async Task Handle_FriendsInvite_NotHost_ReturnsNotInLobby()
    {
        var lobby = new FakeLobbySession();
        using var bridge = new WebBridge(null, new TestGameSessionService(), new TestConfigService(@"C:\test\dir"),
            friendService: new FakeFriendService(), lobbySession: lobby, logger: _ => {});
        BridgeMessage? last = null;
        bridge.MessagePosted += m => last = m;

        await bridge.ProcessRawMessageAsync("""{"id":"r7","type":"command","name":"friends.invite","payload":{"friendId":"f_1"}}""");

        Assert.Equal("not_in_lobby", last!.Error);
    }

    [Fact]
    public async Task Handle_FriendsRespondInvite_Accept_JoinsLobby()
    {
        var lobby = new FakeLobbySession();
        using var bridge = new WebBridge(null, new TestGameSessionService(), new TestConfigService(@"C:\test\dir"),
            friendService: new FakeFriendService(), lobbySession: lobby, logger: _ => {});
        BridgeMessage? last = null;
        bridge.MessagePosted += m => last = m;

        await bridge.ProcessRawMessageAsync("""{"id":"r8","type":"command","name":"friends.respondInvite","payload":{"inviteId":"inv_1","accept":true}}""");

        var p = PayloadOf(last!);
        Assert.True(p.GetProperty("joined").GetBoolean());
        Assert.Equal(LobbySessionRole.Guest, lobby.Role);
        Assert.Equal("lob_1", lobby.CurrentLobbyCode);
    }

    [Fact]
    public void LobbyEvents_AreForwarded_AndDisposeUnsubscribes()
    {
        var lobby = new FakeLobbySession();
        var bridge = new WebBridge(null, new TestGameSessionService(), new TestConfigService(@"C:\test\dir"), lobbySession: lobby, logger: _ => {});
        var posted = new List<BridgeMessage>();
        bridge.MessagePosted += m => posted.Add(m);

        lobby.RaiseState(LobbySessionState.Active);
        lobby.RaiseKicked("AURA-1");

        Assert.Equal("lobby.updated", posted[0].Name);
        Assert.Equal("active", PayloadOf(posted[0]).GetProperty("state").GetString());
        Assert.Equal("lobby.kicked", posted[1].Name);

        bridge.Dispose();
        Assert.False(lobby.HasSubscribers);
    }
}