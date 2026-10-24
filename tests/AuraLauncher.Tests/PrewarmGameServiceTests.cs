using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Core.WebBridge;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Xunit;

namespace AuraLauncher.Tests;

public class PrewarmGameServiceTests
{
    private class FakeConfigService : IConfigService
    {
        public event EventHandler<LauncherConfig>? ConfigChanged;

        public LauncherConfig CurrentConfig { get; set; } = new LauncherConfig
        {
            Nickname = "Player1",
            FastStartEnabled = true,
            GameDir = @"C:\FakeDir"
        };

        public Task<LauncherConfig> LoadConfigAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentConfig);
        public Task SaveConfigAsync(LauncherConfig config, CancellationToken cancellationToken = default)
        {
            CurrentConfig = config;
            return Task.CompletedTask;
        }
        public Task UpdateConfigAsync(Action<LauncherConfig> updateAction, CancellationToken cancellationToken = default)
        {
            updateAction(CurrentConfig);
            return Task.CompletedTask;
        }
    }

    private class MockLaunchService : IGameLaunchService
    {
        public bool LaunchCalled { get; private set; }
        public string? CapturedJvmArgs { get; private set; }
        public Process? ProcessToReturn { get; set; }

        public bool IsGameRunning => false;
        public Process? CurrentGameProcess => null;
        public event EventHandler<int>? GameExited { add { } remove { } }

        public string ResolveMinecraftDirectory(string? customPath = null) => customPath ?? @"C:\FakeDir";
        public string ResolveInstanceDirectory(string? customPath = null, string? version = null) => customPath ?? @"C:\FakeDir";
        public string? ResolveJavaRuntime(string gameDir, int minMajorVersion = 17) => @"C:\FakeDir\runtime\bin\java.exe";
        public bool CheckEnvironmentInstalled(string gameDir, string? version = null) => true;
        public Process? FindRunningGameProcess(string gameDir) => null;

        public Task EnsureInstalledAsync(
            string gameDir,
            IProgress<InstallProgressReport>? progress = null,
            Action<string>? onLogReceived = null,
            CancellationToken cancellationToken = default,
            string? version = null) => Task.CompletedTask;

        public Task<Process> LaunchGameAsync(
            LauncherConfig config,
            Action<string>? onLogReceived = null,
            Action<int, string>? onGameExited = null,
            IProgress<InstallProgressReport>? installProgress = null,
            CancellationToken cancellationToken = default)
        {
            LaunchCalled = true;
            CapturedJvmArgs = config.JvmArgs;
            // Return dummy or current process for testing
            var proc = ProcessToReturn ?? Process.GetCurrentProcess();
            return Task.FromResult(proc);
        }

        public Task<bool> StopGameProcessAsync(int timeoutMs = 6000) => Task.FromResult(true);
    }

    [Fact]
    public async Task StartPrewarmAsync_WhenDisabled_DoesNotLaunch()
    {
        var configService = new FakeConfigService();
        configService.CurrentConfig.FastStartEnabled = false;
        var launchService = new MockLaunchService();
        using var prewarmService = new PrewarmGameService(launchService, configService, _ => { });

        await prewarmService.StartPrewarmAsync();

        Assert.False(launchService.LaunchCalled);
        Assert.False(prewarmService.IsPrewarmedReady);
    }

    [Fact]
    public async Task StartPrewarmAsync_WhenEnabled_PassesPrewarmJvmArgs()
    {
        var configService = new FakeConfigService();
        configService.CurrentConfig.FastStartEnabled = true;
        var launchService = new MockLaunchService();
        using var prewarmService = new PrewarmGameService(launchService, configService, _ => { });

        await prewarmService.StartPrewarmAsync();

        Assert.True(launchService.LaunchCalled);
        Assert.NotNull(launchService.CapturedJvmArgs);
        Assert.Contains("-Daura.prewarm=true", launchService.CapturedJvmArgs);
        Assert.Contains("-Daura.prewarm.port=", launchService.CapturedJvmArgs);
        Assert.Contains("-Daura.prewarm.token=", launchService.CapturedJvmArgs);
    }

    [Fact]
    public async Task Prewarm_HandshakeAndActivation_Flow()
    {
        var configService = new FakeConfigService();
        configService.CurrentConfig.FastStartEnabled = true;
        var launchService = new MockLaunchService();
        using var prewarmService = new PrewarmGameService(launchService, configService, _ => { });

        await prewarmService.StartPrewarmAsync();

        // Extract port and token from captured args
        var args = launchService.CapturedJvmArgs!;
        var portStr = args.Split("-Daura.prewarm.port=")[1].Split(' ')[0];
        var token = args.Split("-Daura.prewarm.token=")[1].Split(' ')[0];
        int port = int.Parse(portStr);

        // Connect simulated mod client
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        using var netStream = client.GetStream();
        using var writer = new StreamWriter(netStream, Encoding.UTF8) { AutoFlush = true };
        using var reader = new StreamReader(netStream, Encoding.UTF8);

        // Send READY token
        await writer.WriteLineAsync($"READY {token}");

        // Wait up to 2 seconds for prewarmService to become ready
        for (int i = 0; i < 20 && !prewarmService.IsPrewarmedReady; i++)
        {
            await Task.Delay(100);
        }

        Assert.True(prewarmService.IsPrewarmedReady);

        // Activate with quickplay multiplayer
        var proc = await prewarmService.ActivateAsync(quickPlayMultiplayer: "play.auramc.ru");
        Assert.NotNull(proc);

        // Verify client received the command
        string? receivedCmd = await reader.ReadLineAsync();
        Assert.Equal("SHOW+JOIN play.auramc.ru", receivedCmd);
    }
}
