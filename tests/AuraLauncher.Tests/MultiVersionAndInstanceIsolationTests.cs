using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.ViewModels;
using Xunit;

namespace AuraLauncher.Tests;

public class MultiVersionAndInstanceIsolationTests : IDisposable
{
    private readonly string _testRoot;

    public MultiVersionAndInstanceIsolationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "AuraMultiVerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch { }
    }

    [Theory]
    [InlineData("1.20.1", "1.20.1", "manifest.json", 17, "java-runtime-gamma", "0.19.5")]
    [InlineData("1.21.1", "1.21.1", "manifest-1.21.1.json", 21, "java-runtime-delta", "0.19.5")]
    [InlineData("  1.21.1  ", "1.21.1", "manifest-1.21.1.json", 21, "java-runtime-delta", "0.19.5")]
    [InlineData("invalid", "1.20.1", "manifest.json", 17, "java-runtime-gamma", "0.19.5")]
    [InlineData(null, "1.20.1", "manifest.json", 17, "java-runtime-gamma", "0.19.5")]
    public void GameVersionProfiles_Metadata_MatchesExpectations(
        string? inputVersion,
        string expectedNormalized,
        string expectedManifest,
        int expectedMinJava,
        string expectedJavaComponent,
        string expectedFabric)
    {
        var normalized = GameVersionProfiles.NormalizeVersion(inputVersion);
        Assert.Equal(expectedNormalized, normalized);
        Assert.Equal(expectedManifest, GameVersionProfiles.GetManifestFileName(normalized));
        Assert.Equal(expectedMinJava, GameVersionProfiles.GetMinJavaVersion(normalized));
        Assert.Equal(expectedJavaComponent, GameVersionProfiles.GetJavaRuntimeComponent(normalized));
        Assert.Equal(expectedFabric, GameVersionProfiles.GetDefaultFabricLoader(normalized));
    }

    [Fact]
    public void EnsureInstanceMigrated_MovesLegacyFilesToInstances120()
    {
        var rootDir = Path.Combine(_testRoot, "minecraft_root");
        var legacyMods = Path.Combine(rootDir, "mods");
        var legacyConfig = Path.Combine(rootDir, "config");
        var legacySaves = Path.Combine(rootDir, "saves");
        var legacyOptions = Path.Combine(rootDir, "options.txt");

        Directory.CreateDirectory(legacyMods);
        Directory.CreateDirectory(legacyConfig);
        Directory.CreateDirectory(legacySaves);
        File.WriteAllText(Path.Combine(legacyMods, "testmod.jar"), "dummy jar");
        File.WriteAllText(Path.Combine(legacyConfig, "settings.json"), "{ \"foo\": \"bar\" }");
        File.WriteAllText(Path.Combine(legacySaves, "world1.txt"), "world data");
        File.WriteAllText(legacyOptions, "fov:70");

        // Act
        FabricGameLaunchService.EnsureInstanceMigrated(rootDir);

        // Assert
        var instance120 = Path.Combine(rootDir, "instances", "1.20.1");
        Assert.True(Directory.Exists(instance120));
        Assert.True(File.Exists(Path.Combine(instance120, "mods", "testmod.jar")));
        Assert.True(File.Exists(Path.Combine(instance120, "config", "settings.json")));
        Assert.True(File.Exists(Path.Combine(instance120, "saves", "world1.txt")));
        Assert.True(File.Exists(Path.Combine(instance120, "options.txt")));

        // Root should no longer contain legacy mods
        Assert.False(Directory.Exists(legacyMods));
    }

    [Fact]
    public void ResolveInstanceDirectory_SeparatesVersionFolders()
    {
        var configService = new InMemoryConfigService(new LauncherConfig
        {
            GameDir = _testRoot,
            SelectedVersion = "1.20.1"
        });
        var launchService = new FabricGameLaunchService(configService);

        var dir120 = launchService.ResolveInstanceDirectory(_testRoot, "1.20.1");
        var dir121 = launchService.ResolveInstanceDirectory(_testRoot, "1.21.1");

        Assert.Equal(Path.Combine(_testRoot, "instances", "1.20.1"), dir120);
        Assert.Equal(Path.Combine(_testRoot, "instances", "1.21.1"), dir121);
        Assert.NotEqual(dir120, dir121);
    }

    [Fact]
    public async Task SettingsViewModel_SwitchingVersion_UpdatesConfigAndRaisesNotifications()
    {
        var configService = new InMemoryConfigService(new LauncherConfig
        {
            GameDir = _testRoot,
            SelectedVersion = "1.20.1"
        });
        var skinService = new SkinService();
        var vm = new SettingsViewModel(configService, skinService);

        Assert.True(vm.IsVersion120);
        Assert.False(vm.IsVersion121);
        Assert.Equal("1.20.1", vm.SelectedVersion);

        // Switch to 1.21.1
        vm.IsVersion121 = true;

        Assert.False(vm.IsVersion120);
        Assert.True(vm.IsVersion121);
        Assert.Equal("1.21.1", vm.SelectedVersion);
        Assert.Equal("1.21.1", configService.CurrentConfig.SelectedVersion);

        // Switch back to 1.20.1
        vm.IsVersion120 = true;

        Assert.True(vm.IsVersion120);
        Assert.False(vm.IsVersion121);
        Assert.Equal("1.20.1", vm.SelectedVersion);
        Assert.Equal("1.20.1", configService.CurrentConfig.SelectedVersion);
    }

    [Fact]
    public void OverviewViewModel_SpecLine_ReflectsSelectedVersion()
    {
        var configService = new InMemoryConfigService(new LauncherConfig
        {
            GameDir = _testRoot,
            SelectedVersion = "1.20.1"
        });
        var launchService = new FabricGameLaunchService(configService);
        var overview = new OverviewViewModel(configService, launchService);

        overview.RefreshStats();
        Assert.Contains("1.20.1", overview.SpecLine);

        // Change version in config
        configService.CurrentConfig.SelectedVersion = "1.21.1";
        overview.RefreshStats();

        Assert.Contains("1.21.1", overview.SpecLine);
        Assert.Contains("1.21.1", overview.VersionInfo);
    }

    [Fact]
    public void ResolveTargetInstanceDir_SeparatesVersionFolders()
    {
        var dir120 = PackUpdateService.ResolveTargetInstanceDir(_testRoot, "1.20.1");
        var dir121 = PackUpdateService.ResolveTargetInstanceDir(_testRoot, "1.21.1");

        Assert.Equal(Path.Combine(_testRoot, "instances", "1.20.1"), dir120);
        Assert.Equal(Path.Combine(_testRoot, "instances", "1.21.1"), dir121);
        Assert.NotEqual(dir120, dir121);
    }

    [Fact]
    public async Task PackUpdateService_WhenManifestMinecraftMatchesSelectedVersion_DoesNotFailVersionCheck()
    {
        var configService = new InMemoryConfigService(new LauncherConfig
        {
            GameDir = _testRoot,
            SelectedVersion = "1.21.1"
        });
        var stateFile = Path.Combine(_testRoot, "state.json");
        var handler = new MockHttpMessageHandler
        {
            Handler = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"name\":\"Aura 1.21.1\",\"version\":\"1.0.0\",\"minecraft\":\"1.21.1\",\"files\":[]}")
            })
        };
        using var client = new HttpClient(handler);
        using var service = new PackUpdateService(configService, client, stateFile);

        var result = await service.CheckAndApplyAsync();
        Assert.NotEqual("Сборка для другой версии Minecraft", result.Message);
    }

    private class MockDiscordRpcService : IDiscordRpcService
    {
        public string? LastWorldName { get; private set; }
        public DateTime? LastStartTime { get; private set; }
        public string? LastGameVersion { get; private set; }
        public bool InLauncher { get; private set; }

        public void Initialize() { }
        public void SetInLauncher() => InLauncher = true;
        public void SetInLobby(int playerCount, string? hostName = null, string? lobbyCode = null) { }
        public void SetPlayingGame(string? worldName = null, DateTime? startTime = null, string? gameVersion = null)
        {
            LastWorldName = worldName;
            LastStartTime = startTime;
            LastGameVersion = gameVersion;
            InLauncher = false;
        }
        public void UpdateSettings() { }
        public void Dispose() { }
    }

    private class MockGameLaunchService : IGameLaunchService
    {
        public LauncherConfig? CapturedConfig { get; private set; }
        public bool IsGameRunning => false;
        public System.Diagnostics.Process? CurrentGameProcess => null;
        public event EventHandler<int>? GameExited;

        public string ResolveMinecraftDirectory(string? customPath = null) => customPath ?? "C:\\aura";
        public string ResolveInstanceDirectory(string? customPath = null, string? version = null) =>
            Path.Combine(customPath ?? "C:\\aura", "instances", version ?? "1.20.1");
        public string? ResolveJavaRuntime(string gameDir, int minMajorVersion = 17) => "javaw.exe";
        public bool CheckEnvironmentInstalled(string gameDir, string? version = null) => true;
        public System.Diagnostics.Process? FindRunningGameProcess(string gameDir) => null;
        public Task<bool> StopGameProcessAsync(int timeoutMs = 6000) => Task.FromResult(true);

        public Task EnsureInstalledAsync(
            string gameDir,
            IProgress<InstallProgressReport>? progress = null,
            Action<string>? onLogReceived = null,
            CancellationToken cancellationToken = default,
            string? version = null) => Task.CompletedTask;

        public Task<System.Diagnostics.Process> LaunchGameAsync(
            LauncherConfig config,
            Action<string>? onLogReceived = null,
            Action<int, string>? onGameExited = null,
            IProgress<InstallProgressReport>? installProgress = null,
            CancellationToken cancellationToken = default)
        {
            CapturedConfig = config;
            return Task.FromResult<System.Diagnostics.Process>(null!);
        }
    }

    private class MockPackUpdateService : IPackUpdateService
    {
        public Task<PackUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default, bool forceFullCheck = false)
        {
            return Task.FromResult(new PackUpdateResult(PackUpdateStatus.UpToDate, "Сборка актуальна"));
        }
    }

    private class MockServerListSyncService : IServerListSyncService
    {
        public Task SyncServersAsync(string gameDir, IReadOnlyList<ManifestServerEntry>? manifestServers, string? customStateFilePath = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpsertActiveLobbyServerAsync(string gameDir, string tunnelAddress, string? hostName = null, string? lobbyCode = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RemoveActiveLobbyServerAsync(string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private class MockSkinService : ISkinService
    {
        public SkinValidationResult ValidateSkinFile(string? filePath) => new(true);
        public System.Windows.Media.ImageSource LoadSkinImage(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractHeadAvatar(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractFrontSkinPreview(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractBackSkinPreview(string? skinPath) => null!;
        public Task SyncSkinToGameAsync(string? skinPath, string nickname, string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetToDefaultSteveAsync(string nickname, string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SkinUploadResult> UploadSkinToLobbyApiAsync(string? skinPath, string nickname, string model, string? ownerToken, string? baseUrl = null, CancellationToken cancellationToken = default) => Task.FromResult(new SkinUploadResult(true));
        public Task<System.Windows.Media.ImageSource> GetAvatarForPlayerAsync(string nickname, CancellationToken cancellationToken = default) => Task.FromResult<System.Windows.Media.ImageSource>(null!);
        public Task<(System.Windows.Media.ImageSource SkinTexture, bool IsSlim)> GetSkinTextureForPlayerAsync(string nickname, CancellationToken cancellationToken = default) => Task.FromResult<(System.Windows.Media.ImageSource, bool)>((null!, false));
        public void EnsureCustomSkinLoaderConfig(string gameDir) { }
        public void ClearCustomSkinLoaderCache(string gameDir) { }
    }

    [Fact]
    public async Task GameSessionService_SnapshotsVersion_ImmuneToLaterConfigMutation()
    {
        var config = new LauncherConfig
        {
            GameDir = _testRoot,
            Nickname = "TestUser",
            SelectedVersion = "1.21.1"
        };
        var configService = new InMemoryConfigService(config);
        var launchService = new MockGameLaunchService();
        var discordRpc = new MockDiscordRpcService();
        var sessionService = new GameSessionService(
            launchService,
            new MockPackUpdateService(),
            new MockServerListSyncService(),
            new MockSkinService(),
            configService,
            discordRpc);

        // Mutating config right before or during launch
        var launchTask = sessionService.LaunchAsync();
        // User changes version in UI while launch is in flight:
        config.SelectedVersion = "1.20.1";

        await launchTask;

        Assert.Equal("1.21.1", sessionService.RunningGameVersion);
        Assert.Equal("1.21.1", discordRpc.LastGameVersion);
        Assert.NotNull(launchService.CapturedConfig);
        Assert.Equal("1.21.1", launchService.CapturedConfig.SelectedVersion);
    }
}
