using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Velopack;
using Xunit;

namespace AuraLauncher.Tests;

public class TestLaunchService : IGameLaunchService
{
    public bool IsGameRunning { get; set; }
    public Process? CurrentGameProcess => null;

    public event EventHandler<int>? GameExited { add { } remove { } }

    public string ResolveMinecraftDirectory(string? customPath = null) => customPath ?? "C:\\AuraTest";
    public string? ResolveJavaRuntime(string gameDir) => null;
    public bool CheckEnvironmentInstalled(string gameDir) => true;
    public Process? FindRunningGameProcess(string gameDir) => null;

    public Task EnsureInstalledAsync(
        string gameDir,
        IProgress<InstallProgressReport>? progress = null,
        Action<string>? onLogReceived = null,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<Process> LaunchGameAsync(
        LauncherConfig config,
        Action<string>? onLogReceived = null,
        Action<int, string>? onGameExited = null,
        IProgress<InstallProgressReport>? installProgress = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

public class LauncherUpdateServiceTests
{
    static LauncherUpdateServiceTests()
    {
        try
        {
            VelopackApp.Build().Run();
        }
        catch { }
    }

    [Fact]
    public async Task CheckAndApplyAsync_WhenNotInstalled_ReturnsNotInstalled()
    {
        // Arrange
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService { IsGameRunning = false };
        var service = new LauncherUpdateService(
            configService, 
            launchService, 
            isInstalledOverride: false);

        // Act
        var result = await service.CheckAndApplyAsync();

        // Assert
        Assert.Equal(LauncherUpdateStatus.NotInstalled, result.Status);
        Assert.Contains("портативном", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAndApplyAsync_WhenGameRunning_ReturnsSkipped()
    {
        // Arrange
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService { IsGameRunning = true };
        var service = new LauncherUpdateService(
            configService, 
            launchService, 
            isInstalledOverride: true);

        // Act
        var result = await service.CheckAndApplyAsync();

        // Assert
        Assert.Equal(LauncherUpdateStatus.Skipped, result.Status);
        Assert.Contains("Игра запущена", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAndApplyAsync_WhenSourceFails_ReturnsFailed()
    {
        // Arrange: передаем заведомо несуществующий путь в качестве источника
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService { IsGameRunning = false };
        string invalidSource = Path.Combine(Path.GetTempPath(), "aura_nonexistent_feed_" + Guid.NewGuid().ToString("N"));
        
        var service = new LauncherUpdateService(
            configService, 
            launchService, 
            overrideSource: invalidSource,
            isInstalledOverride: true);

        // Act
        var result = await service.CheckAndApplyAsync();

        // Assert
        Assert.Equal(LauncherUpdateStatus.Failed, result.Status);
        Assert.StartsWith("Ошибка обновления лаунчера", result.Message);
    }

    [Fact]
    public async Task CheckAndApplyAsync_WhenNetworkFails_ReturnsFailed()
    {
        // Arrange: передаем недоступный локальный endpoint
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService { IsGameRunning = false };
        string deadEndpoint = "http://127.0.0.1:59999/feed";

        var service = new LauncherUpdateService(
            configService,
            launchService,
            overrideSource: deadEndpoint,
            isInstalledOverride: true);

        // Act
        var result = await service.CheckAndApplyAsync();

        // Assert
        Assert.Equal(LauncherUpdateStatus.Failed, result.Status);
        Assert.StartsWith("Ошибка обновления лаунчера", result.Message);
    }

    [Fact]
    public void CurrentVersion_ReturnsValidSemanticVersion()
    {
        // Arrange
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService { IsGameRunning = false };
        var service = new LauncherUpdateService(configService, launchService);

        // Act
        string version = service.CurrentVersion;

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }
}
