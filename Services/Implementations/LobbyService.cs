using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class LobbyService : ILobbyService, IDisposable
{
    private readonly ILobbyApiClient _apiClient;
    private readonly ITunnelProvider _tunnelProvider;
    private CancellationTokenSource? _pollCts;

    public string? CurrentLobbyCode { get; private set; }
    public string? CurrentHostToken { get; private set; }
    public string? CurrentTunnelAddress { get; private set; }
    public string CurrentStatus { get; private set; } = "idle";
    public bool IsHost { get; private set; }
    public bool IsReadyToPlay => CurrentStatus.Equals("open", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(CurrentTunnelAddress);

    public event Action<string>? StatusChanged;
    public event Action<string>? TunnelAddressReady;

    public LobbyService(ILobbyApiClient apiClient, ITunnelProvider? tunnelProvider = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _tunnelProvider = tunnelProvider ?? new FakeTunnelProvider();
    }

    public async Task<string?> CreateLobbyAsHostAsync(string hostName, CancellationToken cancellationToken = default)
    {
        LeaveLobby();

        var response = await _apiClient.CreateLobbyAsync(hostName, cancellationToken);
        if (response == null) return null;

        CurrentLobbyCode = response.Code;
        CurrentHostToken = response.HostToken;
        CurrentStatus = response.Status;
        IsHost = true;

        StatusChanged?.Invoke(CurrentStatus);
        return CurrentLobbyCode;
    }

    public async Task<bool> HostOpenWorldAsync(string? customTunnelAddress = null, int localPort = 25565, CancellationToken cancellationToken = default)
    {
        if (!IsHost || string.IsNullOrWhiteSpace(CurrentLobbyCode) || string.IsNullOrWhiteSpace(CurrentHostToken))
        {
            return false;
        }

        string tunnelAddr;
        if (!string.IsNullOrWhiteSpace(customTunnelAddress))
        {
            tunnelAddr = customTunnelAddress;
        }
        else
        {
            var tunnelResult = await _tunnelProvider.StartAsync(localPort, cancellationToken);
            if (tunnelResult.Status != TunnelStatus.Active || string.IsNullOrWhiteSpace(tunnelResult.PublicAddress))
            {
                return false;
            }

            tunnelAddr = tunnelResult.PublicPort.HasValue 
                ? $"{tunnelResult.PublicAddress}:{tunnelResult.PublicPort.Value}" 
                : tunnelResult.PublicAddress;
        }

        var success = await _apiClient.OpenLobbyAsync(CurrentLobbyCode, CurrentHostToken, tunnelAddr, cancellationToken);
        if (success)
        {
            CurrentStatus = "open";
            CurrentTunnelAddress = tunnelAddr;
            StatusChanged?.Invoke(CurrentStatus);
            TunnelAddressReady?.Invoke(tunnelAddr);
            return true;
        }

        return false;
    }

    public async Task CloseLobbyAsHostAsync(CancellationToken cancellationToken = default)
    {
        if (IsHost && !string.IsNullOrWhiteSpace(CurrentLobbyCode) && !string.IsNullOrWhiteSpace(CurrentHostToken))
        {
            await _apiClient.CloseLobbyAsync(CurrentLobbyCode, CurrentHostToken, cancellationToken);
            await _tunnelProvider.StopAsync(cancellationToken);
        }
        LeaveLobby();
    }

    public async Task<bool> JoinLobbyAsGuestAsync(string code, string playerName, CancellationToken cancellationToken = default)
    {
        LeaveLobby();

        var cleanCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        var response = await _apiClient.JoinLobbyAsync(cleanCode, playerName, cancellationToken);
        if (response == null || !response.Success) return false;

        CurrentLobbyCode = response.Code;
        CurrentStatus = response.Status;
        CurrentTunnelAddress = response.TunnelAddress;
        IsHost = false;

        StatusChanged?.Invoke(CurrentStatus);
        if (!string.IsNullOrWhiteSpace(CurrentTunnelAddress))
        {
            TunnelAddressReady?.Invoke(CurrentTunnelAddress);
        }

        // Start background polling for guest
        StartGuestPolling();
        return true;
    }

    public async Task<LobbyStatusResponse?> RefreshGuestStatusAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(CurrentLobbyCode)) return null;

        var status = await _apiClient.GetStatusAsync(CurrentLobbyCode, cancellationToken);
        if (status != null)
        {
            bool statusChanged = !string.Equals(CurrentStatus, status.Status, StringComparison.OrdinalIgnoreCase);
            bool tunnelChanged = !string.Equals(CurrentTunnelAddress, status.TunnelAddress, StringComparison.OrdinalIgnoreCase);

            CurrentStatus = status.Status;
            CurrentTunnelAddress = status.TunnelAddress;

            if (statusChanged)
            {
                StatusChanged?.Invoke(CurrentStatus);
            }

            if (tunnelChanged && !string.IsNullOrWhiteSpace(CurrentTunnelAddress))
            {
                TunnelAddressReady?.Invoke(CurrentTunnelAddress);
            }
        }
        return status;
    }

    private void StartGuestPolling()
    {
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1500, ct);
                    if (ct.IsCancellationRequested) break;
                    await RefreshGuestStatusAsync(ct);
                    if (CurrentStatus.Equals("closed", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient errors
                }
            }
        }, ct);
    }

    public void LeaveLobby()
    {
        _pollCts?.Cancel();
        _pollCts = null;
        CurrentLobbyCode = null;
        CurrentHostToken = null;
        CurrentTunnelAddress = null;
        CurrentStatus = "idle";
        IsHost = false;
        StatusChanged?.Invoke(CurrentStatus);
    }

    public void Dispose()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
    }
}
