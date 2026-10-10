using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.ViewModels;
using Xunit;

namespace AuraLauncher.Tests;

public class LobbyViewModelTests
{
    private class MockLobbyService : ILobbyService
    {
        public string? CurrentLobbyCode { get; set; }
        public string? CurrentHostToken { get; set; }
        public string? CurrentTunnelAddress { get; set; }
        public string CurrentStatus { get; set; } = "idle";
        public bool IsHost { get; set; }
        public bool IsReadyToPlay => CurrentStatus.Equals("open", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(CurrentTunnelAddress);

        public IReadOnlyDictionary<string, PlayerModSyncInfo>? CurrentModSync { get; set; }

        public event Action<string>? StatusChanged;
        public event Action<string>? TunnelAddressReady;
        public event Action<LobbyStatusResponse>? LobbyStatusUpdated;
        public event Action<IReadOnlyDictionary<string, PlayerModSyncInfo>>? ModSyncUpdated;
        public event Action? KickedFromLobby;

        public Task UpdateManifestAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> KickPlayerAsync(string playerName, CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<string?> CreateLobbyAsHostAsync(string hostName, CancellationToken cancellationToken = default)
        {
            CurrentLobbyCode = "TEST01";
            CurrentHostToken = "token-123";
            CurrentStatus = "waiting";
            IsHost = true;
            StatusChanged?.Invoke(CurrentStatus);
            return Task.FromResult<string?>("TEST01");
        }

        public Task<bool> HostOpenWorldAsync(string? customTunnelAddress = null, int localPort = 25565, CancellationToken cancellationToken = default)
        {
            CurrentStatus = "open";
            CurrentTunnelAddress = customTunnelAddress ?? $"127.0.0.1:{localPort}";
            StatusChanged?.Invoke(CurrentStatus);
            TunnelAddressReady?.Invoke(CurrentTunnelAddress);
            return Task.FromResult(true);
        }

        public Task CloseLobbyAsHostAsync(CancellationToken cancellationToken = default)
        {
            LeaveLobby();
            return Task.CompletedTask;
        }

        public Task<bool> JoinLobbyAsGuestAsync(string code, string playerName, CancellationToken cancellationToken = default)
        {
            if (code == "FAIL00") return Task.FromResult(false);
            CurrentLobbyCode = code;
            CurrentStatus = "waiting";
            IsHost = false;
            StatusChanged?.Invoke(CurrentStatus);
            return Task.FromResult(true);
        }

        public Task<LobbyStatusResponse?> RefreshGuestStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<LobbyStatusResponse?>(new LobbyStatusResponse(CurrentLobbyCode ?? "", CurrentStatus, CurrentTunnelAddress, 2, 0));
        }

        public void LeaveLobby()
        {
            CurrentLobbyCode = null;
            CurrentHostToken = null;
            CurrentTunnelAddress = null;
            CurrentStatus = "idle";
            IsHost = false;
            StatusChanged?.Invoke(CurrentStatus);
        }

        public void SimulateWorldOpen(string tunnelAddress)
        {
            CurrentStatus = "open";
            CurrentTunnelAddress = tunnelAddress;
            StatusChanged?.Invoke(CurrentStatus);
            TunnelAddressReady?.Invoke(tunnelAddress);
        }
    }

    [Fact]
    public async Task Host_CreateLobby_SetsLobbyCodeAndState()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService("C:\\AuraTest");
        configService.CurrentConfig.Nickname = "HostPlayer";
        var launchService = new TestLaunchService();

        var vm = new LobbyViewModel(mockLobby, launchService, configService);

        Assert.False(vm.IsInLobby);
        Assert.True(vm.CreateLobbyCommand.CanExecute(null));

        await vm.CreateLobbyCommand.ExecuteAsync(null);

        Assert.True(vm.IsInLobby);
        Assert.True(vm.IsLobbyCreated);
        Assert.Equal("TEST01", vm.LobbyCode);
        Assert.True(vm.IsHost);
    }

    [Fact]
    public async Task Guest_JoinAndOpenWorld_EnablesConnectCommand()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService("C:\\AuraTest");
        configService.CurrentConfig.Nickname = "GuestPlayer";
        var launchService = new TestLaunchService();

        var vm = new LobbyViewModel(mockLobby, launchService, configService);

        vm.GuestCodeInput = "TEST01";
        Assert.True(vm.JoinLobbyCommand.CanExecute(null));

        await vm.JoinLobbyCommand.ExecuteAsync(null);

        Assert.True(vm.IsInLobby);
        Assert.True(vm.IsGuestJoined);
        Assert.False(vm.CanGuestConnect);

        // Хост открывает мир
        mockLobby.SimulateWorldOpen("127.0.0.1:25565");

        Assert.True(vm.CanGuestConnect);
        Assert.True(vm.ConnectToGameCommand.CanExecute(null));

        string? receivedAddress = null;
        vm.GuestConnectRequested += (s, addr) => receivedAddress = addr;

        await vm.ConnectToGameCommand.ExecuteAsync(null);

        Assert.Equal("127.0.0.1:25565", receivedAddress);
    }

    [Fact]
    public void GuestCodeInput_NormalizesToUpper_AndMax6Chars()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService();

        var vm = new LobbyViewModel(mockLobby, launchService, configService);

        vm.GuestCodeInput = "abc123456";
        Assert.Equal("ABC123", vm.GuestCodeInput);
    }

    private class MockLobbyApiClient : ILobbyApiClient
    {
        public System.Net.HttpStatusCode? DetailedStatusCode { get; set; } = System.Net.HttpStatusCode.OK;
        public LobbyStatusResponse? DetailedStatusResponse { get; set; }
        public string DetailedRawBody { get; set; } = "";

        public Task<LobbyCreateResponse?> CreateLobbyAsync(string hostName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default) => Task.FromResult<LobbyCreateResponse?>(null);
        public Task<LobbyJoinResponse?> JoinLobbyAsync(string code, string playerName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default) => Task.FromResult<LobbyJoinResponse?>(null);
        public Task<bool> UpdateManifestAsync(string code, string playerName, IReadOnlyList<ModManifestEntry> manifest, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<LobbyStatusResponse?> GetStatusAsync(string code, string? playerName = null, CancellationToken cancellationToken = default) => Task.FromResult<LobbyStatusResponse?>(null);
        public Task<(System.Net.HttpStatusCode? StatusCode, LobbyStatusResponse? Response, string RawBody)> GetStatusDetailedAsync(string code, string? playerName = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult((DetailedStatusCode, DetailedStatusResponse, DetailedRawBody));
        }
        public Task<bool> KickPlayerAsync(string code, string hostToken, string playerToKick, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> OpenLobbyAsync(string code, string hostToken, string tunnelAddress, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> HeartbeatAsync(string code, string hostToken, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CloseLobbyAsync(string code, string hostToken, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> LeaveLobbyAsync(string code, string playerName, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<TunnelConfigResponse?> GetTunnelConfigAsync(CancellationToken cancellationToken = default) => Task.FromResult<TunnelConfigResponse?>(null);
    }

    [Fact]
    public async Task Guest_Join_NotFound_ShowsNotFoundErrorMessage_AndClearsOnTyping()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService();
        var mockApi = new MockLobbyApiClient
        {
            DetailedStatusCode = System.Net.HttpStatusCode.NotFound,
            DetailedRawBody = "{\"error\":\"Lobby not found\"}"
        };

        var vm = new LobbyViewModel(mockLobby, launchService, configService, lobbyApiClient: mockApi);
        vm.GuestCodeInput = "111111";

        await vm.JoinLobbyCommand.ExecuteAsync(null);

        Assert.True(vm.HasJoinError);
        Assert.Contains("Лобби с кодом 111111 не найдено", vm.JoinErrorMessage);
        Assert.Equal("Войти", vm.JoinButtonText);

        // При вводе символа ошибка пропадает
        vm.GuestCodeInput = "111112";
        Assert.False(vm.HasJoinError);
        Assert.Empty(vm.JoinErrorMessage);
    }

    [Fact]
    public async Task Guest_Join_ClosedLobby_ShowsClosedErrorMessage()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService();
        var mockApi = new MockLobbyApiClient
        {
            DetailedStatusCode = System.Net.HttpStatusCode.OK,
            DetailedStatusResponse = new LobbyStatusResponse("222222", "closed", null, 1, 0)
        };

        var vm = new LobbyViewModel(mockLobby, launchService, configService, lobbyApiClient: mockApi);
        vm.GuestCodeInput = "222222";

        await vm.JoinLobbyCommand.ExecuteAsync(null);

        Assert.True(vm.HasJoinError);
        Assert.Contains("Это лобби закрыто", vm.JoinErrorMessage);
    }

    [Fact]
    public async Task Guest_Join_NetworkError_ShowsNetworkErrorMessage()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService("C:\\AuraTest");
        var launchService = new TestLaunchService();
        var mockApi = new MockLobbyApiClient
        {
            DetailedStatusCode = null
        };

        var vm = new LobbyViewModel(mockLobby, launchService, configService, lobbyApiClient: mockApi);
        vm.GuestCodeInput = "333333";

        await vm.JoinLobbyCommand.ExecuteAsync(null);

        Assert.True(vm.HasJoinError);
        Assert.Contains("Нет связи с сервером лобби", vm.JoinErrorMessage);
    }

    [Fact]
    public void LobbyStageLayout_PositionsHostInCenter_AndFriendsAlternatingRightAndLeft()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService(System.IO.Path.GetTempPath());
        var launchService = new TestLaunchService();
        var vm = new LobbyViewModel(mockLobby, launchService, configService);

        var host = new LobbyPlayerItem { Nickname = "HostPlayer", IsHost = true };
        vm.LobbyPlayers.Add(host);

        Assert.Equal(0, host.StageSlotIndex);
        Assert.Equal(0, host.StageOffsetX);
        Assert.Equal(1.0, host.StageScale);
        Assert.True(host.IsHost);
        Assert.False(host.IsGuest);

        var friend1 = new LobbyPlayerItem { Nickname = "FriendRight1", IsHost = false };
        vm.LobbyPlayers.Add(friend1);

        Assert.Equal(0, host.StageOffsetX);
        Assert.Equal(1, friend1.StageSlotIndex);
        Assert.True(friend1.StageOffsetX > 0);
        Assert.True(friend1.IsGuest);

        var friend2 = new LobbyPlayerItem { Nickname = "FriendLeft1", IsHost = false };
        vm.LobbyPlayers.Add(friend2);

        Assert.Equal(0, host.StageOffsetX);
        Assert.Equal(1, friend1.StageSlotIndex);
        Assert.Equal(-1, friend2.StageSlotIndex);
        Assert.Equal(-friend1.StageOffsetX, friend2.StageOffsetX);

        var friend3 = new LobbyPlayerItem { Nickname = "FriendRight2", IsHost = false };
        var friend4 = new LobbyPlayerItem { Nickname = "FriendLeft2", IsHost = false };
        vm.LobbyPlayers.Add(friend3);
        vm.LobbyPlayers.Add(friend4);

        Assert.Equal(2, friend3.StageSlotIndex);
        Assert.Equal(-2, friend4.StageSlotIndex);
        Assert.True(friend3.StageOffsetX > friend1.StageOffsetX);
        Assert.Equal(-friend3.StageOffsetX, friend4.StageOffsetX);
    }

    [Fact]
    public async Task KickPlayerAsync_WhenHost_RemovesPlayerImmediately()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService(System.IO.Path.GetTempPath());
        var launchService = new TestLaunchService();
        var vm = new LobbyViewModel(mockLobby, launchService, configService);

        await vm.CreateLobbyAsync();
        var guest = new LobbyPlayerItem { Nickname = "Vetements", IsHost = false };
        vm.LobbyPlayers.Add(guest);
        Assert.Equal(2, vm.LobbyPlayers.Count);

        bool kicked = await vm.KickPlayerAsync("Vetements");

        Assert.True(kicked);
        Assert.DoesNotContain(vm.LobbyPlayers, p => p.Nickname == "Vetements");
    }

    [Fact]
    public async Task KickPlayerAsync_WhenNotHost_ReturnsFalse()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService(System.IO.Path.GetTempPath());
        var launchService = new TestLaunchService();
        var vm = new LobbyViewModel(mockLobby, launchService, configService);

        var guest = new LobbyPlayerItem { Nickname = "Vetements", IsHost = false };
        vm.LobbyPlayers.Add(guest);

        bool kicked = await vm.KickPlayerAsync("Vetements");

        Assert.False(kicked);
        Assert.Contains(vm.LobbyPlayers, p => p.Nickname == "Vetements");
    }

    [Fact]
    public async Task Host_ActionHints_ReflectCurrentState()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService(System.IO.Path.GetTempPath());
        configService.CurrentConfig.Nickname = "HostPlayer";
        var launchService = new TestLaunchService();
        var vm = new LobbyViewModel(mockLobby, launchService, configService);

        await vm.CreateLobbyAsync();

        Assert.True(vm.IsHostHintVisible);
        Assert.False(vm.IsGuestHintVisible);
        Assert.Contains("Открыть мир", vm.HostActionHintText);

        launchService.IsGameRunning = true;
        vm.OnHostGameStarted();
        Assert.Contains("Открыть для сети", vm.HostActionHintText);
    }

    [Fact]
    public async Task Guest_ActionHints_ReflectWaitingAndOpenState()
    {
        var mockLobby = new MockLobbyService();
        var configService = new TestConfigService(System.IO.Path.GetTempPath());
        configService.CurrentConfig.Nickname = "GuestPlayer";
        var launchService = new TestLaunchService();
        var vm = new LobbyViewModel(mockLobby, launchService, configService);

        vm.GuestCodeInput = "TEST01";
        await vm.JoinLobbyCommand.ExecuteAsync(null);

        Assert.True(vm.IsGuestHintVisible);
        Assert.False(vm.IsHostHintVisible);
        Assert.Contains("Ожидание хоста", vm.GuestActionHintText);

        mockLobby.SimulateWorldOpen("aura-test.playit.gg:25565");

        Assert.True(vm.CanGuestConnect);
        Assert.Contains("Мир открыт", vm.GuestActionHintText);
    }
}
