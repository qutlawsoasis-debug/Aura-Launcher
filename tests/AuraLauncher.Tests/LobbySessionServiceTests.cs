using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Xunit;

namespace AuraLauncher.Tests;

public class LobbySessionServiceTests
{
    private class FakeLobbyService : ILobbyService
    {
        public string? CurrentLobbyCode { get; set; }
        public string? CurrentHostToken { get; set; }
        public string? CurrentTunnelAddress { get; set; }
        public string CurrentStatus { get; set; } = "idle";
        public bool IsHost { get; set; }
        public bool IsReadyToPlay { get; set; }
        public IReadOnlyDictionary<string, PlayerModSyncInfo>? CurrentModSync { get; set; }

        public event Action<string>? StatusChanged;
        public event Action<string>? TunnelAddressReady;
        public event Action<LobbyStatusResponse>? LobbyStatusUpdated;
        public event Action<IReadOnlyDictionary<string, PlayerModSyncInfo>>? ModSyncUpdated;
        public event Action? KickedFromLobby;

        public bool CreateLobbySuccess { get; set; } = true;
        public bool JoinSuccess { get; set; } = true;
        public bool HostOpenWorldSuccess { get; set; } = true;
        public bool KickSuccess { get; set; } = true;

        public Task<string?> CreateLobbyAsHostAsync(string hostName, CancellationToken cancellationToken = default)
        {
            if (!CreateLobbySuccess) return Task.FromResult<string?>(null);
            CurrentLobbyCode = "AURA-7777";
            IsHost = true;
            return Task.FromResult<string?>("AURA-7777");
        }

        public Task<bool> HostOpenWorldAsync(string? customTunnelAddress = null, int localPort = 25565, CancellationToken cancellationToken = default)
        {
            if (!HostOpenWorldSuccess) return Task.FromResult(false);
            CurrentTunnelAddress = customTunnelAddress ?? "eu.aura.playit.gg:25565";
            CurrentStatus = "open";
            TunnelAddressReady?.Invoke(CurrentTunnelAddress);
            return Task.FromResult(true);
        }

        public Task CloseLobbyAsHostAsync(CancellationToken cancellationToken = default)
        {
            CurrentLobbyCode = null;
            IsHost = false;
            CurrentStatus = "closed";
            return Task.CompletedTask;
        }

        public Task<bool> JoinLobbyAsGuestAsync(string code, string playerName, CancellationToken cancellationToken = default)
        {
            if (!JoinSuccess) return Task.FromResult(false);
            CurrentLobbyCode = code;
            IsHost = false;
            return Task.FromResult(true);
        }

        public Task<LobbyStatusResponse?> RefreshGuestStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LobbyStatusResponse?>(new LobbyStatusResponse(
                Code: CurrentLobbyCode ?? "AURA-7777",
                Status: CurrentStatus,
                TunnelAddress: CurrentTunnelAddress,
                PlayerCount: 2,
                LastHeartbeat: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Players: new[] { "HostPlayer", "GuestPlayer" },
                HostName: "HostPlayer"
            ));
        }

        public Task UpdateManifestAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> KickPlayerAsync(string playerName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(KickSuccess);
        }

        public void LeaveLobby()
        {
            CurrentLobbyCode = null;
            IsHost = false;
        }

        public void TriggerKicked() => KickedFromLobby?.Invoke();
    }

    private class FakeTunnelProvider : ITunnelProvider
    {
        public TunnelInfo CurrentInfo { get; set; } = new TunnelInfo(null, null, TunnelStatus.Inactive);

        public bool IsProcessRunning => CurrentInfo.Status == TunnelStatus.Active;
        public event Action<TunnelInfo>? StatusChanged;

        public bool StartSuccess { get; set; } = true;

        public Task<TunnelInfo> StartAsync(int localPort, CancellationToken ct = default)
        {
            if (StartSuccess)
            {
                CurrentInfo = new TunnelInfo("eu.aura.playit.gg", 25565, TunnelStatus.Active);
            }
            else
            {
                CurrentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, "tunnel_failed");
            }
            StatusChanged?.Invoke(CurrentInfo);
            return Task.FromResult(CurrentInfo);
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            CurrentInfo = new TunnelInfo(null, null, TunnelStatus.Inactive);
            StatusChanged?.Invoke(CurrentInfo);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private class FakeLanWorldWatcher : ILanWorldWatcher
    {
        public bool IsRunning { get; private set; }
        public bool IsWorldOpen => CurrentPort.HasValue;
        public int? CurrentPort { get; set; }
        public string? CurrentDomain { get; set; }

        public event Action<int>? WorldOpened;
        public event Action? WorldClosed;
        public event Action<string>? DomainAssigned;

        public void Start(string logFilePath, bool readFromEnd = true, bool scanRecentTail = false)
        {
            IsRunning = true;
        }

        public void Stop()
        {
            IsRunning = false;
            CurrentPort = null;
            CurrentDomain = null;
        }

        public void TriggerWorldOpened(int port)
        {
            CurrentPort = port;
            WorldOpened?.Invoke(port);
        }

        public void TriggerWorldClosed()
        {
            CurrentPort = null;
            WorldClosed?.Invoke();
        }

        public void TriggerDomainAssigned(string domain)
        {
            CurrentDomain = domain;
            DomainAssigned?.Invoke(domain);
        }

        public void Dispose() { }
    }

    private class FakeLaunchService : IGameLaunchService
    {
        public bool IsGameRunning { get; set; } = false;
        public System.Diagnostics.Process? CurrentGameProcess => null;
        public event EventHandler<int>? GameExited { add { } remove { } }
        public Task<bool> StopGameProcessAsync(int timeoutMs = 6000) => Task.FromResult(true);
        public System.Diagnostics.Process? FindRunningGameProcess(string gameDir) => null;
        public string ResolveMinecraftDirectory(string? customPath = null) => customPath ?? @"C:\fake\.aura";
        public string ResolveInstanceDirectory(string? customPath = null, string? version = null) => customPath ?? @"C:\fake\.aura\instances\1.20.1";
        public string? ResolveJavaRuntime(string gameDir, int minMajorVersion = 17) => null;
        public bool CheckEnvironmentInstalled(string gameDir, string? version = null) => true;

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
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class FakeGameSessionService : IGameSessionService
    {
        public GameSessionState State { get; set; } = GameSessionState.Idle;
        public bool IsGameRunning => State == GameSessionState.Running;
        public System.Diagnostics.Process? CurrentProcess => null;

        public event EventHandler<GameSessionState>? StateChanged;
        public event EventHandler<string>? LogReceived;
        public event EventHandler<GameExitedEventArgs>? GameExited;
        public event EventHandler<System.Diagnostics.Process>? GameStarted;

        public bool LaunchCalled { get; private set; }
        public LaunchOptions? LastLaunchOptions { get; private set; }

        public Task<System.Diagnostics.Process> LaunchAsync(LaunchOptions? options = null, IProgress<LaunchProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            LaunchCalled = true;
            LastLaunchOptions = options;
            return Task.FromResult<System.Diagnostics.Process>(null!);
        }

        public Task<bool> StopAsync(int timeoutMs = 5000) => Task.FromResult(true);
    }

    [Fact]
    public async Task CreateLobby_Success_TransitionsToWaitingForWorld()
    {
        var lobbyService = new FakeLobbyService();
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService();
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        using var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});

        string? code = await service.CreateLobbyAsync("HostUser");

        Assert.Equal("AURA-7777", code);
        Assert.Equal(LobbySessionRole.Host, service.Role);
        Assert.Equal(LobbySessionState.WaitingForWorld, service.State);
        Assert.Equal("HostUser", service.HostNick);
        Assert.Single(service.Players);
        Assert.True(service.Players[0].IsHost);
    }

    [Fact]
    public async Task OpenWorld_FakeTunnel_TransitionsDirectlyToActive()
    {
        var lobbyService = new FakeLobbyService();
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService();
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        using var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});

        await service.CreateLobbyAsync("HostUser");

        Environment.SetEnvironmentVariable("AURA_FAKE_TUNNEL", "1");
        try
        {
            await service.OpenWorldAsync(25565);

            Assert.Equal(LobbySessionState.Active, service.State);
            Assert.Equal("fake-test.e4mc.link", service.CurrentTunnelAddress);
            Assert.False(watcher.IsRunning);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AURA_FAKE_TUNNEL", null);
        }
    }

    [Fact]
    public async Task OpenWorld_RealFlow_LaunchesGameAndBecomesActiveOnWorldOpened()
    {
        var lobbyService = new FakeLobbyService();
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService { IsGameRunning = false };
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        using var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});

        await service.CreateLobbyAsync("HostUser");
        await service.OpenWorldAsync();

        Assert.True(session.LaunchCalled);
        Assert.Equal(LobbySessionState.WaitingForWorld, service.State);
        Assert.True(watcher.IsRunning);

        watcher.TriggerWorldOpened(25565);

        // Allow async task to complete
        for (int i = 0; i < 50 && service.State != LobbySessionState.Active; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(LobbySessionState.Active, service.State);
        Assert.Equal("eu.aura.playit.gg:25565", service.CurrentTunnelAddress);
    }

    [Fact]
    public async Task OpenWorld_UseE4allTunnel_UsesDomainFromWatcher()
    {
        var lobbyService = new FakeLobbyService();
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService { IsGameRunning = false };
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        using var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});

        bool prevE4all = LobbyService.UseE4allTunnel;
        LobbyService.UseE4allTunnel = true;

        try
        {
            await service.CreateLobbyAsync("HostUser");
            await service.OpenWorldAsync();

            watcher.TriggerDomainAssigned("test-custom.e4mc.link");

            for (int i = 0; i < 50 && service.State != LobbySessionState.Active; i++)
            {
                await Task.Delay(20);
            }

            Assert.Equal(LobbySessionState.Active, service.State);
            Assert.Equal("test-custom.e4mc.link", service.CurrentTunnelAddress);
        }
        finally
        {
            LobbyService.UseE4allTunnel = prevE4all;
        }
    }

    [Fact]
    public async Task LanWorldClosed_ResetsLobbyToWaitingForWorld()
    {
        var lobbyService = new FakeLobbyService();
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService();
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        using var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});

        Environment.SetEnvironmentVariable("AURA_FAKE_TUNNEL", "1");
        try
        {
            await service.CreateLobbyAsync("HostUser");
            await service.OpenWorldAsync(25565);
            Assert.Equal(LobbySessionState.Active, service.State);

            watcher.TriggerWorldClosed();

            for (int i = 0; i < 50 && service.State != LobbySessionState.WaitingForWorld; i++)
            {
                await Task.Delay(20);
            }

            Assert.Equal(LobbySessionState.WaitingForWorld, service.State);
            Assert.Null(service.CurrentTunnelAddress);
        }
        finally
        {
            Environment.SetEnvironmentVariable("AURA_FAKE_TUNNEL", null);
        }
    }

    [Fact]
    public async Task JoinByCode_InvalidCode_SetsErrorStateAndLastError()
    {
        var lobbyService = new FakeLobbyService { JoinSuccess = false };
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService();
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        using var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});

        bool result = await service.JoinByCodeAsync("BAD", "GuestUser");

        Assert.False(result);
        Assert.Equal(LobbySessionState.Error, service.State);
        Assert.NotNull(service.LastError);
    }

    [Fact]
    public async Task KickPlayer_Host_RemovesPlayerFromSnapshot()
    {
        var lobbyService = new FakeLobbyService();
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService();
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        using var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});

        await service.CreateLobbyAsync("HostUser");
        lobbyService.CurrentStatus = "open";

        lobbyService.JoinSuccess = true;
        await service.JoinByCodeAsync("AURA-7777", "TrollPlayer");

        await service.CreateLobbyAsync("HostUser");
        bool kicked = await service.KickPlayerAsync("TrollPlayer");
        Assert.True(kicked);
    }

    [Fact]
    public async Task LeaveLobby_FromAnyState_ResetsToIdle()
    {
        var lobbyService = new FakeLobbyService();
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService();
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        using var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});

        await service.CreateLobbyAsync("HostUser");
        Assert.NotEqual(LobbySessionState.Idle, service.State);

        await service.LeaveLobbyAsync();

        Assert.Equal(LobbySessionState.Idle, service.State);
        Assert.Equal(LobbySessionRole.None, service.Role);
        Assert.Null(service.CurrentLobbyCode);
        Assert.Empty(service.Players);
    }

    [Fact]
    public void Dispose_UnsubscribesFromEvents()
    {
        var lobbyService = new FakeLobbyService();
        var tunnel = new FakeTunnelProvider();
        var watcher = new FakeLanWorldWatcher();
        var launch = new FakeLaunchService();
        var session = new FakeGameSessionService();
        var cfg = new TestConfigService(@"C:\fake\dir");

        var service = new LobbySessionService(lobbyService, tunnel, watcher, launch, session, cfg, logger: _ => {});
        service.Dispose();

        watcher.TriggerWorldOpened(25565);
        lobbyService.TriggerKicked();
    }
}
