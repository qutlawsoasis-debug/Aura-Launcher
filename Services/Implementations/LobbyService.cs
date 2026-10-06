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
            PlayitTunnelProvider.LogTunnel("[HOST-OPEN: WARN] HostOpenWorldAsync called but IsHost=false or lobby code/token empty.");
            return false;
        }

        try
        {
            bool isFakeTunnel = string.Equals(Environment.GetEnvironmentVariable("AURA_FAKE_TUNNEL"), "1", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(Environment.GetEnvironmentVariable("UseFakeTunnel"), "true", StringComparison.OrdinalIgnoreCase);

            // Условие (б): TCP-connect на 127.0.0.1:localPort успешен
            if (!isFakeTunnel)
            {
                bool tcpOk = await CheckLocalTcpPortAsync("127.0.0.1", localPort, timeoutMs: 3000, cancellationToken);
                if (!tcpOk)
                {
                    PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: ERROR] Local TCP connect to 127.0.0.1:{localPort} failed. World is not open or not accepting connections.");
                    return false;
                }
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
                    PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: ERROR] Tunnel StartAsync failed: status={tunnelResult.Status}, error={tunnelResult.ErrorMessage}");
                    return false;
                }

                // Условие (в): агент playit поднят
                if (!_tunnelProvider.IsProcessRunning && !isFakeTunnel)
                {
                    PlayitTunnelProvider.LogTunnel("[HOST-OPEN: ERROR] Playit agent process is not running.");
                    return false;
                }

                tunnelAddr = tunnelResult.PublicPort.HasValue 
                    ? $"{tunnelResult.PublicAddress}:{tunnelResult.PublicPort.Value}" 
                    : tunnelResult.PublicAddress;
            }

            PlayitTunnelProvider.LogTunnel($"[HOST-OPEN] Registering tunnel address '{tunnelAddr}' in lobby-api for code {CurrentLobbyCode}...");
            var success = await _apiClient.OpenLobbyAsync(CurrentLobbyCode, CurrentHostToken, tunnelAddr, cancellationToken);
            if (success)
            {
                CurrentStatus = "open";
                CurrentTunnelAddress = tunnelAddr;
                StatusChanged?.Invoke(CurrentStatus);
                TunnelAddressReady?.Invoke(tunnelAddr);
                PlayitTunnelProvider.LogTunnel($"[HOST-OPEN] Lobby {CurrentLobbyCode} opened successfully with tunnel {tunnelAddr}.");
                return true;
            }

            PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: ERROR] lobby-api OpenLobbyAsync returned false for code {CurrentLobbyCode}.");
            return false;
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[EXCEPTION] HostOpenWorldAsync: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    private static async Task<bool> CheckLocalTcpPortAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (completedTask == connectTask)
            {
                await connectTask.ConfigureAwait(false);
                return client.Connected;
            }
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: DEBUG] TCP check to {host}:{port} failed: {ex.Message}");
        }
        return false;
    }

    public async Task CloseLobbyAsHostAsync(CancellationToken cancellationToken = default)
    {
        if (IsHost && !string.IsNullOrWhiteSpace(CurrentLobbyCode) && !string.IsNullOrWhiteSpace(CurrentHostToken))
        {
            try
            {
                PlayitTunnelProvider.LogTunnel($"[LOBBY] Closing lobby {CurrentLobbyCode} as host via API...");
                await _apiClient.CloseLobbyAsync(CurrentLobbyCode, CurrentHostToken, cancellationToken);
                PlayitTunnelProvider.LogTunnel($"[LOBBY] Lobby {CurrentLobbyCode} closed via API.");
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] CloseLobbyAsync API call failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            }

            try
            {
                await _tunnelProvider.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] CloseLobbyAsHostAsync StopAsync: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            }
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
            try
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
                    catch (Exception loopEx)
                    {
                        // Transient poll error, log and continue
                        PlayitTunnelProvider.LogTunnel($"[GUEST-POLL: WARN] RefreshGuestStatusAsync transient failure: {loopEx.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal exit
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] GuestPolling: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
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
