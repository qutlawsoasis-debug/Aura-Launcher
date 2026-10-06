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

        public event Action<string>? StatusChanged;
        public event Action<string>? TunnelAddressReady;
        public event Action<LobbyStatusResponse>? LobbyStatusUpdated;

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
}
